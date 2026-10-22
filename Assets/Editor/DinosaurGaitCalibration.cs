using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class DinosaurGaitCalibration
    {
        internal static readonly string[] Species = { "Sauropelta", "Pachycephalosaurus", "Baurusuchus" };
        internal static string[] FootNames(string species)
        {
            switch (species)
            {
                case "Sauropelta": return new[] { "Bone.L.002", "Bone.R.002", "Bone.L.005", "Bone.R.005" };
                case "Pachycephalosaurus": return new[] { "Foot.L", "Foot.R" };
                case "Baurusuchus": return new[] { "Bone.L.006", "Bone.R.006", "Bone.L.019", "Bone.R.019" };
                default: throw new ArgumentException(species);
            }
        }
        [MenuItem("Jurassic World/Scene/Repair Three Animal Gaits")]
        public static void Repair()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorSceneManager.GetActiveScene().isDirty,
                "Stop Play and save scene edits before gait calibration.");
            if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/Main.unity")
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            foreach (string species in Species)
                Apply(Object.FindObjectsOfType<DinosaurAgent>(true).Single(a => a.name == species));
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        }
        internal static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        internal static void Set(Object target, string property, Object value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(property).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        internal static void Number(Object target, string property, float value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(property).floatValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        internal static DinosaurGaitProfile Apply(DinosaurAgent agent)
        {
            if (BaurusuchusWalkingClip.IsWalkingClip(agent)) return BaurusuchusWalkingClip.ApplyProfile(agent);
            float previousHeight = agent.GaitProfile != null ? agent.GaitProfile.heightCorrection : 0f;
            // Sampling a clone prevents animation poses or IK rotations leaking into saved scene transforms.
            var clone = Object.Instantiate(agent.gameObject);
            clone.name = agent.name;
            DinosaurGaitProfile baked;
            try { baked = Bake(clone.GetComponent<DinosaurAgent>()); }
            finally { Object.DestroyImmediate(clone); }
            string path = JurassicWorldSceneBuilder.GetDinosaurPreparedFolder(agent.name) + "/Gait.asset";
            var profile = AssetDatabase.LoadAssetAtPath<DinosaurGaitProfile>(path);
            if (profile == null) { AssetDatabase.CreateAsset(baked, path); profile = baked; }
            else { EditorUtility.CopySerialized(baked, profile); Object.DestroyImmediate(baked); EditorUtility.SetDirty(profile); }
            Set(agent, "gaitProfile", profile);
            Number(agent, "walkSpeed", WalkingSpeed(agent.name, profile.groundSpeed));
            Number(agent, "moveAnimationGroundSpeed", profile.groundSpeed);
            Number(agent, "approachSpeed", Mathf.Max(profile.groundSpeed * 1.35f, profile.groundSpeed + .5f));
            Number(agent, "moveClipLoopStart", profile.loopStart);
            Number(agent, "moveClipLoopEnd", profile.loopEnd);
            agent.GetComponent<DinosaurFbxLoader>().ModelRoot.transform.position += Vector3.up * (profile.heightCorrection - previousHeight);
            var loader = agent.GetComponent<DinosaurFbxLoader>();
            var data = new SerializedObject(loader);
            data.FindProperty("enableFootLock").boolValue = true;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(agent.gameObject.scene);
            Debug.Log($"GAIT_CALIBRATED {agent.name} native={profile.groundSpeed:F4} feet={profile.feet.Length} loop={profile.loopStart:F3}..{profile.loopEnd:F3}");
            return profile;
        }
        internal static float WalkingSpeed(string species, float nativeSpeed)
        {
            return nativeSpeed * (species == "Sauropelta" ? 2f : 1f);
        }

        internal static DinosaurGaitProfile Bake(DinosaurAgent agent)
        {
            if (agent.GaitProfile != null)
                agent.GetComponent<DinosaurFbxLoader>().ModelRoot.transform.position -= Vector3.up * agent.GaitProfile.heightCorrection;
            var animation = agent.GetComponentInChildren<Animation>(true);
            var clip = animation.Cast<AnimationState>().Single(s => s.name == (agent.name == "Baurusuchus" ? "Run" : "Walk")).clip;
            var profile = ScriptableObject.CreateInstance<DinosaurGaitProfile>();
            profile.name = agent.name + " Gait"; profile.clip = clip;
            var previousGait = agent.GaitProfile != null ? agent.GaitProfile
                : AssetDatabase.LoadAssetAtPath<DinosaurGaitProfile>(JurassicWorldSceneBuilder.GetDinosaurPreparedFolder(agent.name) + "/Gait.asset");
            profile.turnSpeed = previousGait != null ? previousGait.turnSpeed : agent.name == "Sauropelta" ? 10f : 2f;
            var feet = FootNames(agent.name).Select(n => animation.GetComponentsInChildren<Transform>(true).Single(t => t.name == n)).ToArray();
            profile.feet = new DinosaurGaitProfile.Foot[feet.Length];
            var lowestTime = new float[feet.Length];
            var lowestBone = Enumerable.Repeat(float.MaxValue, feet.Length).ToArray();
            var knees = new Transform[feet.Length]; var hipsBones = new Transform[feet.Length];
            for (int i = 0; i <= 120; i++)
            {
                clip.SampleAnimation(animation.gameObject, clip.length * i / 120f);
                for (int f = 0; f < feet.Length; f++)
                    if (feet[f].position.y < lowestBone[f])
                    { lowestBone[f] = feet[f].position.y; lowestTime[f] = clip.length * i / 120f; }
            }
            for (int f = 0; f < feet.Length; f++)
            {
                clip.SampleAnimation(animation.gameObject, lowestTime[f]);
                knees[f] = agent.name == "Pachycephalosaurus" ? feet[f].parent.parent : feet[f].parent;
                hipsBones[f] = knees[f].parent;
                profile.feet[f] = new DinosaurGaitProfile.Foot {
                    hipPath = AnimationUtility.CalculateTransformPath(hipsBones[f], animation.transform),
                    kneePath = AnimationUtility.CalculateTransformPath(knees[f], animation.transform),
                    path = AnimationUtility.CalculateTransformPath(feet[f], animation.transform),
                    soleOffset = SoleOffset(animation, feet[f]) };
                Debug.Log($"GAIT_LEG {agent.name} {feet[f].name}: {hipsBones[f].name}/{knees[f].name}/{feet[f].name} sole={profile.feet[f].soleOffset} scale={feet[f].lossyScale}");
            }
            int steps = Mathf.Clamp(Mathf.CeilToInt(clip.length * 120f), 120, 2400);
            float dt = clip.length / steps;
            var positions = new Vector3[feet.Length][];
            var hips = new Vector3[feet.Length][];
            var ankles = new Vector3[feet.Length][];
            var lengths = new float[feet.Length][];
            for (int f = 0; f < feet.Length; f++)
            {
                positions[f] = new Vector3[steps + 1]; hips[f] = new Vector3[steps + 1];
                ankles[f] = new Vector3[steps + 1]; lengths[f] = new float[steps + 1];
            }
            for (int i = 0; i <= steps; i++)
            {
                clip.SampleAnimation(animation.gameObject, i * dt);
                for (int f = 0; f < feet.Length; f++)
                {
                    positions[f][i] = agent.transform.InverseTransformPoint(feet[f].TransformPoint(profile.feet[f].soleOffset));
                    hips[f][i] = agent.transform.InverseTransformPoint(hipsBones[f].position);
                    ankles[f][i] = agent.transform.InverseTransformPoint(feet[f].position);
                    lengths[f][i] = Vector3.Distance(hipsBones[f].position, knees[f].position)
                        + Vector3.Distance(knees[f].position, feet[f].position);
                }
            }
            var speeds = new List<float>();
            for (int f = 0; f < feet.Length; f++)
            {
                float low = positions[f].Min(p => p.y), high = positions[f].Max(p => p.y);
                float band = Mathf.Clamp((high - low) * .2f, .015f, .045f);
                var contact = new float[steps + 1];
                var backwardSpeeds = new float[steps + 1];
                int count = 0;
                for (int i = 0; i <= steps; i++)
                {
                    int a = Mathf.Max(0, i - 1), b = Mathf.Min(steps, i + 1);
                    Vector3 velocity = (positions[f][b] - positions[f][a]) / ((b - a) * dt);
                    Vector3 forward = Quaternion.Euler(0f, agent.VisualYaw, 0f) * Vector3.forward;
                    float backward = -Vector3.Dot(velocity, forward);
                    bool support = positions[f][i].y <= low + band && backward > .01f;
                    contact[i] = support ? 1f : 0f;
                    backwardSpeeds[i] = backward;
                }
                // Reject one-frame toe jitter and bridge short gaps within the same planted step.
                SmoothContact(contact, dt, false, .05f);
                SmoothContact(contact, dt, true, .08f);
                for (int i = 0; i < steps; i++)
                    if (contact[i] >= .5f) { count++; if (backwardSpeeds[i] > .01f) speeds.Add(backwardSpeeds[i]); }
                profile.feet[f].contact = contact;
                Directory.CreateDirectory("Reports/ThreeAnimalGaits");
                File.WriteAllLines("Reports/ThreeAnimalGaits/" + agent.name + "-" + f + "-track.csv",
                    new[] { "time,x,y,z,contact" }.Concat(positions[f].Select((p, i) =>
                        FormattableString.Invariant($"{i * dt:F5},{p.x:F5},{p.y:F5},{p.z:F5},{contact[i]}"))));
                Debug.Log($"GAIT_TRACK {agent.name}/{feet[f].name} rangeX={positions[f].Max(p => p.x)-positions[f].Min(p => p.x):F4} rangeZ={positions[f].Max(p => p.z)-positions[f].Min(p => p.z):F4} low={low:F4} high={high:F4}");
                Require(count > 3, "No measurable stance for " + agent.name + "/" + feet[f].name);
                Debug.Log($"GAIT_STANCE {agent.name}/{feet[f].name} duty={(float)count / steps:F3} soleY={low:F3}..{high:F3}");
            }
            speeds.Sort(); profile.groundSpeed = speeds[speeds.Count / 2];
            Require(profile.groundSpeed > .02f && profile.groundSpeed < 15f, "Invalid gait speed: " + agent.name);
            float drop = 0f;
            Vector3 forwardAxis = Quaternion.Euler(0f, agent.VisualYaw, 0f) * Vector3.forward;
            // Include curved travel, turning in place, and resuming at arbitrary gait phases.
            // A straight-only envelope misses the outside leg after a turn begins mid-stance.
            bool continuous = new SerializedObject(agent).FindProperty("roamingMode").enumValueIndex
                == (int)DinosaurAgent.RoamingMode.ContinuousFree;
            float turnTimeScale = continuous ? profile.groundSpeed / Mathf.Max(.01f, agent.WalkSpeed) : 1f;
            // Continuous walkers turn only while translating. Animation samples
            // use clip time, so account for playback speed when integrating yaw.
            for (int mode = 0; mode < (continuous ? 2 : 3); mode++)
            for (int sign = -1; sign <= 1; sign += 2)
            for (int entry = 0; entry < 4; entry++)
            for (int f = 0; f < feet.Length; f++)
            {
                bool planted = false;
                Vector3 anchor = Vector3.zero, offset = Vector3.zero, travel = Vector3.zero;
                for (int tick = 0; tick < 2 * steps; tick++)
                {
                    int i = (tick + entry * steps / 4) % steps;
                    Quaternion turn = Quaternion.Euler(0f, mode == 0 ? 0f : tick * dt * profile.turnSpeed * sign * turnTimeScale, 0f);
                    if (mode != 2) travel += turn * forwardAxis * (dt * profile.groundSpeed);
                    bool support = profile.feet[f].contact[i] >= .5f;
                    if (support && !planted)
                    {
                        anchor = turn * positions[f][i] + travel; anchor.y = 0f;
                        offset = turn * (positions[f][i] - ankles[f][i]);
                    }
                    planted = support;
                    if (!support) continue;
                    Vector3 delta = turn * hips[f][i] + travel - (anchor - offset);
                    float horizontal = delta.x * delta.x + delta.z * delta.z;
                    float length = lengths[f][i] - .005f;
                    Require(horizontal < length * length, "Stance exceeds leg reach horizontally: " + agent.name);
                    drop = Mathf.Max(drop, delta.y - Mathf.Sqrt(length * length - horizontal));
                }
            }
            profile.heightCorrection = drop > .001f ? -(drop + .015f) : 0f;
            Require(drop < .15f, "Excessive gait height correction: " + agent.name + " " + drop);
            Debug.Log($"GAIT_SPEED {agent.name} native={profile.groundSpeed:F4} heightCorrection={profile.heightCorrection:F4}");
            var tracks = positions.Select(track => track.Select(p => new Vector2(p.x, p.z)).ToList()).ToArray();
            JurassicWorldSceneBuilder.FindBestLoopWindow(tracks, clip.length, steps,
                out float loopStart, out float loopEnd, out float seamBefore, out float seamAfter);
            profile.loopStart = loopStart >= 0f ? loopStart : 0f;
            profile.loopEnd = loopEnd > loopStart && loopEnd > 0f ? loopEnd : clip.length;
            Debug.Log($"GAIT_LOOP {agent.name} window={profile.loopStart:F4}..{profile.loopEnd:F4} seam={seamBefore:F5}->{seamAfter:F5}");
            clip.SampleAnimation(animation.gameObject, 0f);
            return profile;
        }
        private static void SmoothContact(float[] contact, float dt, bool removeSupport, float duration)
        {
            int count = contact.Length - 1;
            var source = (float[])contact.Clone();
            bool target = removeSupport;
            for (int start = 0; start < count; start++)
            {
                if ((source[start] >= .5f) != target || (source[(start + count - 1) % count] >= .5f) == target) continue;
                int length = 0;
                while (length < count && (source[(start + length) % count] >= .5f) == target) length++;
                if (length * dt >= duration) continue;
                for (int i = 0; i < length; i++) contact[(start + i) % count] = target ? 0f : 1f;
            }
            contact[count] = contact[0];
        }
        private static Vector3 SoleOffset(Animation animation, Transform foot)
        {
            var points = new List<Vector3>();
            var mesh = new Mesh();
            try
            {
                foreach (var skin in animation.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var bones = skin.bones; var weights = skin.sharedMesh.boneWeights;
                    var indices = new HashSet<int>();
                    for (int b = 0; b < bones.Length; b++)
                        if (bones[b] == foot || bones[b] != null && bones[b].IsChildOf(foot)) indices.Add(b);
                    if (indices.Count == 0) continue;
                    SauropeltaMeshSampling.Bake(skin, mesh);
                    var vertices = mesh.vertices;
                    for (int v = 0; v < weights.Length; v++)
                    {
                        var w = weights[v];
                        float influence = (indices.Contains(w.boneIndex0) ? w.weight0 : 0f)
                            + (indices.Contains(w.boneIndex1) ? w.weight1 : 0f)
                            + (indices.Contains(w.boneIndex2) ? w.weight2 : 0f)
                            + (indices.Contains(w.boneIndex3) ? w.weight3 : 0f);
                        if (influence >= .5f) points.Add(skin.transform.TransformPoint(vertices[v]));
                    }
                }
            }
            finally { Object.DestroyImmediate(mesh); }
            Require(points.Count > 0, "Missing sole vertices for " + foot.name);
            float lowest = points.Min(p => p.y);
            var bottom = points.Where(p => p.y <= lowest + .012f).ToArray();
            Vector3 centre = bottom.Aggregate(Vector3.zero, (a, b) => a + b) / bottom.Length;
            centre.y = lowest;
            return foot.InverseTransformPoint(centre);
        }
    }
}
