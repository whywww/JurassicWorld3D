using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class TRexBodyEnvelopeBake
    {
        internal static bool Included(Transform bone)
        {
            // Explicit anatomical roots keep all head/jaw descendants, but neither
            // tail nor limb descendants can re-enter through their hip/spine parent.
            for (var b = bone; b != null; b = b.parent)
                if (b.name.IndexOf("Tail", StringComparison.OrdinalIgnoreCase) >= 0
                    || b.name.IndexOf("Knee", StringComparison.OrdinalIgnoreCase) >= 0
                    || b.name.IndexOf("Foot", StringComparison.OrdinalIgnoreCase) >= 0
                    || b.name.IndexOf("Ankle", StringComparison.OrdinalIgnoreCase) >= 0
                    || b.name.IndexOf("Leg", StringComparison.OrdinalIgnoreCase) >= 0
                    || b.name.IndexOf("Shoulder", StringComparison.OrdinalIgnoreCase) >= 0
                    || b.name.IndexOf("Arm", StringComparison.OrdinalIgnoreCase) >= 0
                    || b.name.IndexOf("Elbow", StringComparison.OrdinalIgnoreCase) >= 0
                    || b.name.IndexOf("Hand", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            string name = bone.name;
            return name.StartsWith("jt_", StringComparison.Ordinal) &&
                (name.Contains("Spine") || name.Contains("Hips") || name.Contains("Hip_")
                || name.Contains("Neck") || name.Contains("Head") || name.Contains("Jaw")
                || name.Contains("Mouth") || name.Contains("Tongue") || name.Contains("Eye") || name.Contains("Teeth"));
        }

        internal static DinosaurCoreBody.Band[] Bake(DinosaurAgent agent, DinosaurInteractionProfile.Motion motion)
        {
            var animation = agent.GetComponentInChildren<Animation>(true);
            var transforms = animation.GetComponentsInChildren<Transform>(true);
            var positions = transforms.Select(t => t.localPosition).ToArray();
            var rotations = transforms.Select(t => t.localRotation).ToArray();
            var scales = transforms.Select(t => t.localScale).ToArray();
            var hints = transforms.Select(t => new SerializedObject(t).FindProperty("m_LocalEulerAnglesHint").vector3Value).ToArray();
            var skins = animation.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(s => s.sharedMesh != null && s.sharedMesh.boneWeights.Length == s.sharedMesh.vertexCount).ToArray();
            if (skins.Length == 0) throw new InvalidOperationException("No skinned T-Rex body.");
            var selected = skins.Select(s => s.bones.Select(Included).ToArray()).ToArray();
            var weights = skins.Select(s => s.sharedMesh.boneWeights).ToArray();
            var triangles = skins.Select(s => s.sharedMesh.triangles).ToArray();
            var meshes = skins.Select(s => new Mesh()).ToArray();
            var vertices = skins.Select(s => new List<Vector3>()).ToArray();
            var poses = new List<DinosaurInteractionProfile.PlayerPose>();
            var interval = new Dictionary<long, Bounds>();
            Quaternion inverse = Quaternion.Inverse(agent.transform.rotation * Quaternion.Euler(0f, agent.VisualYaw, 0f));
            int count = Mathf.CeilToInt(motion.clip.length * 60f);
            float intervalStart = 0f;
            bool owns = !AnimationMode.InAnimationMode();
            if (owns) AnimationMode.StartAnimationMode();
            try
            {
                for (int frame = 0; frame <= count; frame++)
                {
                    float time = motion.clip.length * frame / count;
                    var frameBands = new Dictionary<long, Bounds>();
                    AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(animation.gameObject, motion.clip, time); AnimationMode.EndSampling();
                    for (int s = 0; s < skins.Length; s++)
                    {
                        skins[s].BakeMesh(meshes[s]); meshes[s].GetVertices(vertices[s]);
                        var points = new Vector3[vertices[s].Count]; var influence = new float[points.Length]; var parts = new int[points.Length];
                        for (int v = 0; v < points.Length; v++)
                        {
                            points[v] = inverse * (skins[s].transform.TransformPoint(vertices[s][v]) - agent.transform.position);
                            var w = weights[s][v];
                            int[] bones = { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
                            float[] values = { w.weight0, w.weight1, w.weight2, w.weight3 };
                            float largest = -1f;
                            for (int i = 0; i < 4; i++) if (selected[s][bones[i]])
                            { influence[v] += values[i]; if (values[i] > largest) { largest = values[i]; parts[v] = 1 + s * 1000 + bones[i]; } }
                        }
                        // Clip each triangle at 50% anatomical influence. This retains the
                        // body/tail seam instead of leaving a hole by discarding mixed vertices.
                        for (int t = 0; t < triangles[s].Length; t += 3)
                        {
                            int a = triangles[s][t], b = triangles[s][t + 1], c = triangles[s][t + 2];
                            int part = influence[a] >= influence[b] && influence[a] >= influence[c] ? parts[a]
                                : influence[b] >= influence[c] ? parts[b] : parts[c];
                            int[] indices = { a, b, c };
                            var polygon = new List<Vector3>(4);
                            for (int edge = 0; edge < 3; edge++)
                            {
                                int u = indices[edge], v = indices[(edge + 1) % 3];
                                if (influence[u] >= .5f) polygon.Add(points[u]);
                                if ((influence[u] >= .5f) != (influence[v] >= .5f))
                                    polygon.Add(Vector3.Lerp(points[u], points[v], (.5f - influence[u]) / (influence[v] - influence[u])));
                            }
                            if (polygon.Count == 0) continue;
                            int bottom = Mathf.FloorToInt(polygon.Min(p => p.y) / .5f), top = Mathf.FloorToInt(polygon.Max(p => p.y) / .5f);
                            for (int layer = bottom; layer <= top; layer++)
                                foreach (var point in ClipHeight(ClipHeight(polygon, layer * .5f, true), (layer + 1) * .5f, false))
                                    Add(frameBands, point, part, layer);
                        }
                    }
                    foreach (var item in frameBands)
                    { if (interval.TryGetValue(item.Key, out var b)) { b.Encapsulate(item.Value); interval[item.Key] = b; } else interval[item.Key] = item.Value; }
                    if (frame % 3 == 0 && frame > 0 || frame == count)
                    {
                        var bands = interval.Select(item => { var b = item.Value; b.Expand(new Vector3(.1f, 0f, .1f));
                            return new DinosaurCoreBody.Band { layer = (int)item.Key, part = (int)(item.Key >> 32), bounds = b }; }).ToArray();
                        poses.Add(new DinosaurInteractionProfile.PlayerPose { time = intervalStart, bands = bands });
                        interval = frameBands; intervalStart = time;
                    }
                }
            }
            finally
            {
                if (owns) AnimationMode.StopAnimationMode();
                foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
                for (int i = 0; i < transforms.Length; i++)
                {
                    transforms[i].localPosition = positions[i]; transforms[i].localRotation = rotations[i]; transforms[i].localScale = scales[i];
                    var serialized = new SerializedObject(transforms[i]);
                    serialized.FindProperty("m_LocalEulerAnglesHint").vector3Value = hints[i]; serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            motion.obstaclePoses = poses.ToArray();
            if (poses.Count == 0 || poses.Any(p => p.bands.Length == 0)) throw new InvalidOperationException("Empty torso/head envelope: " + motion.sourceName);
            // Compatibility broad envelope; precise checks always use obstaclePoses.
            var union = new Dictionary<long, Bounds>();
            foreach (var pose in poses) foreach (var band in pose.bands)
            { long key = ((long)band.part << 32) | (uint)band.layer; if (union.TryGetValue(key, out var b)) { b.Encapsulate(band.bounds); union[key] = b; } else union[key] = band.bounds; }
            return union.Select(p => new DinosaurCoreBody.Band { layer = (int)p.Key, part = (int)(p.Key >> 32), bounds = p.Value }).ToArray();
        }

        private static List<Vector3> ClipHeight(List<Vector3> polygon, float y, bool above)
        {
            var result = new List<Vector3>();
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector3 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                bool insideA = above ? a.y >= y : a.y <= y, insideB = above ? b.y >= y : b.y <= y;
                if (insideA) result.Add(a);
                if (insideA != insideB) result.Add(Vector3.Lerp(a, b, (y - a.y) / (b.y - a.y)));
            }
            return result;
        }

        private static void Add(Dictionary<long, Bounds> bands, Vector3 p, int part, int layer)
        {
            long key = ((long)part << 32) | (uint)layer;
            if (bands.TryGetValue(key, out var b)) { b.Encapsulate(p); bands[key] = b; } else bands[key] = new Bounds(p, Vector3.zero);
        }

        [MenuItem("Jurassic World/Interaction/Rebuild T-Rex Body and Head Envelopes")]
        public static void Rebuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save the scene before rebuilding envelopes.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var agent = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex");
            var profile = (DinosaurInteractionProfile)new SerializedObject(agent).FindProperty("interactionProfile").objectReferenceValue;
            if (profile == null) throw new InvalidOperationException("Main has no interaction profile.");
            var skin = agent.GetComponentInChildren<Animation>(true).GetComponentsInChildren<SkinnedMeshRenderer>(true).First(s => s.sharedMesh.boneWeights.Length > 0);
            Debug.Log("BODY_HEAD_BONES: " + string.Join(", ", skin.bones.Select(b => b.name + "=" + Included(b))));
            var report = new StringBuilder("# T-Rex body/head envelopes\n\nTail and limbs excluded; original foot/root samples, clips and playback rates preserved. XZ margin: 0.05m per side.\n\n|Motion|Old whole-clip size|New whole-clip size|Largest pose size|\n|---|---|---|---|\n");
            foreach (var motion in profile.motions)
            {
                Bounds before = Union(motion.bodyBands);
                motion.bodyBands = Bake(agent, motion);
                Vector3 largest = Vector3.zero;
                foreach (var pose in motion.obstaclePoses) largest = Vector3.Max(largest, Union(pose.bands).size);
                report.AppendLine($"|{motion.sourceName}|{before.size:F3}|{Union(motion.bodyBands).size:F3}|{largest:F3}|");
            }
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();
            var body = (DinosaurCoreBody)new SerializedObject(agent).FindProperty("interactionBody").objectReferenceValue;
            body.bands = profile.Find(InteractionMotionRole.Walk).bodyBands;
            EditorUtility.SetDirty(body); EditorSceneManager.MarkSceneDirty(agent.gameObject.scene); EditorSceneManager.SaveScene(agent.gameObject.scene);
            Directory.CreateDirectory("Tools/VegetationDiversity/RainforestSample");
            File.WriteAllText("Tools/VegetationDiversity/RainforestSample/BodyHeadEnvelopes.md", report.ToString());
            Debug.Log("TREX_BODY_HEAD_ENVELOPES_BAKED: " + AssetDatabase.GetAssetPath(profile));
        }

        internal static Bounds Union(DinosaurCoreBody.Band[] bands)
        { if (bands.Length == 0) return default; var b = bands[0].bounds; foreach (var band in bands) b.Encapsulate(band.bounds); return b; }
    }
}
