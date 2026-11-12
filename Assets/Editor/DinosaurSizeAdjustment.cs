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
    // One-time edit-mode migration. Runtime binders never change model scale.
    [InitializeOnLoad]
    public static class DinosaurSizeAdjustment
    {
        public const float Factor = 1.5f;
        const string Request = "Logs/dinosaur-size-request.txt";
        const string Done = "Assets/Editor/DinosaurSizeBaseline.json";
        [Serializable] class Entry { public string path, json, invariantHash; }
        [Serializable] class Baseline { public Entry[] entries; public bool complete, steeringScaled; }
        static DinosaurSizeAdjustment() => EditorApplication.update += Poll;
        static void Poll()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorSceneManager.GetActiveScene().isDirty) return;
            File.Delete(Request);
            try { Apply(); File.WriteAllText("Logs/dinosaur-size-result.txt", "PASS"); }
            catch (Exception e) { File.WriteAllText("Logs/dinosaur-size-result.txt", e.ToString()); Debug.LogException(e); }
        }
        [Serializable] class Metrics
        {
            public float walkSpeed = 0f, approachSpeed = 0f, moveAnimationGroundSpeed = 0f, collisionRadius = 0f, viewFocusHeight = 0f, groundSpeed = 0f, heightCorrection = 0f;
        }
        static string GaitHash(string path)
        {
            string text = string.Join("\n", File.ReadAllLines(path).Where(line =>
                !line.StartsWith("  groundSpeed:") && !line.StartsWith("  heightCorrection:")));
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
        static string Snapshot(Object obj)
        {
            var m = new Metrics(); var so = new SerializedObject(obj);
            foreach (var field in typeof(Metrics).GetFields())
            {
                var p = so.FindProperty(field.Name);
                if (p != null) field.SetValue(m, p.floatValue);
            }
            return "{\"MonoBehaviour\":" + JsonUtility.ToJson(m) + "}";
        }
        static void Multiply(Object obj, params string[] fields)
        {
            var so = new SerializedObject(obj);
            foreach (string field in fields) { var p = so.FindProperty(field); if (p != null && p.floatValue > 0f) p.floatValue *= Factor; }
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(obj);
        }
        static DinosaurCoreBody.Band[] Bands(DinosaurCoreBody.Band[] source)
        {
            var result = new List<DinosaurCoreBody.Band>();
            foreach (var band in source)
            {
                var b = new Bounds(band.bounds.center * Factor, band.bounds.size * Factor);
                // Spatial layers are fixed half-metre bins, not scale-independent identifiers.
                for (int layer = Mathf.FloorToInt(b.min.y / .5f); layer <= Mathf.FloorToInt(b.max.y / .5f); layer++)
                    result.Add(new DinosaurCoreBody.Band { layer = layer, bounds = b, part = band.part });
            }
            return result.ToArray();
        }
        internal static void ScaleSteering(DinosaurAgent agent)
            => Multiply(agent, "continuousMinTurnRadius", "continuousLookAheadDistance", "continuousProbeDistance");
        internal static DinosaurCoreBody.Band[] ScalePlayerBands(DinosaurCoreBody.Band[] source)
        {
            // Player layers encode bone identity as well as the original height bin.
            // Keep those grouping IDs: only obstacle bands use world-height layers.
            var result = new DinosaurCoreBody.Band[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                result[i] = source[i];
                result[i].bounds = new Bounds(source[i].bounds.center * Factor, source[i].bounds.size * Factor);
            }
            return result;
        }
        public static void RepairSteeringAndVerify()
        {
            var saved = JsonUtility.FromJson<Baseline>(File.ReadAllText(Done));
            if (!saved.steeringScaled)
            {
                foreach (string path in new[] { "Assets/Scenes/Main.unity", "Assets/Scenes/TRexFrontInteractionTest.unity" })
                {
                    var scene = EditorSceneManager.OpenScene(path);
                    foreach (var agent in Object.FindObjectsOfType<DinosaurAgent>(true)) ScaleSteering(agent);
                    EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                }
                saved.steeringScaled = true; File.WriteAllText(Done, JsonUtility.ToJson(saved, true));
            }
            DinosaurGaitAcceptance.RunMain();
        }
        public static void VerifyRepeatAndFlight()
        {
            FixFlight();
            Apply(); Apply();
            PteradactalAcceptance.IntegrateAndVerify();
        }
        static void FinishDerivedData()
        {
            TRexBodyEnvelopeBake.Rebuild();
            FixFlight();
        }
        public static void FixFlight()
        {
            var prefab = PrefabUtility.LoadPrefabContents(PteradactalSceneIntegration.PrefabPath);
            try
            {
                var animation = prefab.GetComponentInChildren<Animation>(true);
                var bounds = PteradactalSceneIntegration.SampleBounds(animation.gameObject, animation.clip);
                var pivot = animation.transform.parent;
                // SampleBounds returns pivot-local coordinates: scale the model inside
                // the pivot so both flight clearance and the visual see the enlarged size.
                if (pivot.localScale != Vector3.one || bounds.size.x < 6f)
                {
                    animation.transform.localScale *= Factor;
                    animation.transform.localPosition *= Factor;
                    pivot.localScale = Vector3.one;
                }
                PrefabUtility.SaveAsPrefabAsset(prefab, PteradactalSceneIntegration.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            PteradactalSceneIntegration.AddToMainScene();
        }
        [MenuItem("Jurassic World/Scene/Enlarge All Dinosaurs 1.5x")]
        public static void Apply()
        {
            if (File.Exists(Done))
            {
                var saved = JsonUtility.FromJson<Baseline>(File.ReadAllText(Done));
                if (!saved.complete) { FinishDerivedData(); saved.complete = true; File.WriteAllText(Done, JsonUtility.ToJson(saved, true)); }
                Debug.Log("DINOSAUR_SIZE_ALREADY_APPLIED"); return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Stop Play and save scene edits first.");
            var baseline = new List<Entry>();
            var profiles = new HashSet<string>();
            foreach (string path in new[] { "Assets/Scenes/Main.unity", "Assets/Scenes/TRexFrontInteractionTest.unity" })
            {
                var scene = EditorSceneManager.OpenScene(path);
                foreach (var agent in Object.FindObjectsOfType<DinosaurAgent>(true))
                {
                    var loader = agent.GetComponent<DinosaurFbxLoader>();
                    if (loader == null || loader.ModelRoot == null) continue;
                    baseline.Add(new Entry { path = path + "/" + agent.name, json = Snapshot(agent) });
                    baseline.Add(new Entry { path = path + "/" + agent.name + "/visual", json = EditorJsonUtility.ToJson(loader.ModelRoot.transform) });
                    Transform visual = loader.ModelRoot.transform;
                    visual.localScale *= Factor; visual.localPosition *= Factor;
                    ScaleSteering(agent);
                    Multiply(agent, "walkSpeed", "approachSpeed", "moveAnimationGroundSpeed", "collisionRadius", "viewFocusHeight");
                    Multiply(loader, "footContactHeight", "footRiseThreshold", "footLockDistanceLimit");
                    if (agent.GaitProfile != null) profiles.Add(AssetDatabase.GetAssetPath(agent.GaitProfile));
                    var so = new SerializedObject(agent);
                    foreach (string field in new[] { "interactionProfile", "interactionBody" })
                    { var p = so.FindProperty(field); if (p != null && p.objectReferenceValue != null) {
                        if (p.objectReferenceValue is DinosaurCoreBody body) { body.bands = Bands(body.bands); EditorUtility.SetDirty(body); }
                        else profiles.Add(AssetDatabase.GetAssetPath(p.objectReferenceValue));
                    } }
                }
                foreach (var interactor in Object.FindObjectsOfType<DinosaurFoliageInteractor>(true))
                {
                    for (int i = 0; i < interactor.capsules.Length; i++) interactor.capsules[i].radius *= Factor;
                    interactor.animationEnvelope = new Bounds(interactor.animationEnvelope.center * Factor, interactor.animationEnvelope.size * Factor);
                    EditorUtility.SetDirty(interactor);
                }
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            foreach (var assetPath in profiles)
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (asset == null) throw new InvalidOperationException("Missing asset: " + assetPath);
                baseline.Add(new Entry { path = AssetDatabase.GetAssetPath(asset), json = Snapshot(asset), invariantHash = asset is DinosaurGaitProfile ? GaitHash(assetPath) : null });
                if (asset is DinosaurGaitProfile gait) { gait.groundSpeed *= Factor; gait.heightCorrection *= Factor; }
                if (asset is DinosaurCoreBody body) body.bands = Bands(body.bands);
                if (asset is DinosaurInteractionProfile interaction)
                    foreach (var motion in interaction.motions)
                    {
                        motion.groundSpeed *= Factor;
                        var keys = motion.speed.keys;
                        for (int i = 0; i < keys.Length; i++) { keys[i].value *= Factor; keys[i].inTangent *= Factor; keys[i].outTangent *= Factor; }
                        motion.speed.keys = keys;
                        for (int i = 0; i < motion.footFrames.Length; i++)
                        {
                            var f = motion.footFrames[i]; f.leftSole *= Factor; f.rightSole *= Factor;
                            f.leftKnee *= Factor; f.rightKnee *= Factor; f.rootOffset *= Factor;
                            f.leftGroundHeight *= Factor; f.rightGroundHeight *= Factor; motion.footFrames[i] = f;
                        }
                        motion.bodyBands = Bands(motion.bodyBands);
                        for (int i = 0; i < motion.playerPoses.Length; i++)
                            motion.playerPoses[i].bands = ScalePlayerBands(motion.playerPoses[i].bands);
                        for (int i = 0; i < motion.obstaclePoses.Length; i++)
                            motion.obstaclePoses[i].bands = Bands(motion.obstaclePoses[i].bands);
                    }
                EditorUtility.SetDirty(asset);
            }
            AssetDatabase.SaveAssets();
            var savedBaseline = new Baseline { entries = baseline.ToArray(), steeringScaled = true };
            File.WriteAllText(Done, JsonUtility.ToJson(savedBaseline, true));
            FinishDerivedData();
            savedBaseline.complete = true;
            File.WriteAllText(Done, JsonUtility.ToJson(savedBaseline, true));
            AssetDatabase.Refresh();
            Debug.Log("DINOSAUR_SIZE_APPLIED");
        }
    }
}
