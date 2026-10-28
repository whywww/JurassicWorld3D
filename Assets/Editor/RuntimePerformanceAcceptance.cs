using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using Crest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class RuntimePerformanceAcceptance
    {
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); Debug.Log("JW_PERF_PASS " + message); }

        [MenuItem("Jurassic World/Performance/Verify Runtime Budgets")]
        public static void Run()
        {
            string previous = SceneManager.GetActiveScene().path;
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save before acceptance.");
            int quality = QualitySettings.GetQualityLevel();
            int preference = PlayerPrefs.GetInt("JurassicWorld.GraphicsQuality", -1);
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                var controller = new GameObject("Quality regression fixture").AddComponent<GraphicsQualityController>();
                typeof(GraphicsQualityController).GetMethod("OnSceneLoaded", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(controller, new object[] { SceneManager.GetActiveScene(), LoadSceneMode.Single });
                var terrain = Terrain.activeTerrain;
                Require(terrain != null, "Main terrain exists");
                controller.ApplyTier(2);
                Vector4 high = TerrainValues(terrain);
                controller.ApplyTier(1);
                Vector4 medium = TerrainValues(terrain);
                for (int repeat = 0; repeat < 20; repeat++)
                {
                    controller.ApplyTier(0); controller.ApplyTier(2);
                    Require(TerrainValues(terrain) == high, "High tier is stable after Low/High switch " + repeat);
                    controller.ApplyTier(1);
                    Require(TerrainValues(terrain) == medium, "Medium tier does not accumulate scaling " + repeat);
                }
                Require(PlayerPrefs.GetInt("JurassicWorld.GraphicsQuality", -1) == preference, "session quality checks preserve saved selection");
                VerifyLowRendering(controller);
                VerifyThinning(controller);
                VerifySmallPlantLod();
                VerifyInteractionBudget();
                Directory.CreateDirectory("Logs/RuntimePerformance");
                File.WriteAllText("Logs/RuntimePerformance/budget-acceptance.txt",
                    "PASS: repeated quality switching; saved preference preserved; vegetation render state and distances unchanged; reflection and water materials restore; Low clears contact and skips sampling; Medium/High resume fresh contact; explicit interaction disable preserved.\n");
                Debug.Log("JW_PERF_BUDGET_ACCEPTANCE_PASS");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            { Debug.LogException(error); if (Application.isBatchMode) EditorApplication.Exit(1); else throw; }
            finally
            {
                QualitySettings.SetQualityLevel(quality, true);
                if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
            }
        }

        private static Vector4 TerrainValues(Terrain terrain) => new Vector4(terrain.detailObjectDistance,
            terrain.detailObjectDensity, terrain.treeDistance, terrain.treeMaximumFullLODCount);

        private static void VerifyLowRendering(GraphicsQualityController controller)
        {
            controller.ApplyTier(2);
            var renderers = Object.FindObjectsOfType<MeshRenderer>();
            bool[] enabled = new bool[renderers.Length], forcedOff = new bool[renderers.Length];
            var materials = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++) { enabled[i] = renderers[i].enabled; forcedOff[i] = renderers[i].forceRenderingOff; materials[i] = renderers[i].sharedMaterials; }
            int trunks = Object.FindObjectsOfType<SceneTreeObstacle>().Length;
            var camera = Camera.main;
            float[] distances = camera.layerCullDistances;
            var sorting = camera.opaqueSortMode;
            var ocean = Object.FindObjectOfType<OceanRenderer>();
            var reflection = camera.GetComponent<OceanPlanarReflection>();
            Require(ocean != null && reflection != null, "Main creek and planar reflection exist");
            Material original = ocean.OceanMaterial;
            bool reflectionEnabled = reflection.enabled, planarKeyword = original.IsKeywordEnabled("_PLANARREFLECTIONS_ON");
            controller.ApplyTier(0);
            Require(!reflection.enabled && !ocean.OceanMaterial.IsKeywordEnabled("_PLANARREFLECTIONS_ON"), "Low removes reflection camera work and shader sampling");
            Require(original.IsKeywordEnabled("_PLANARREFLECTIONS_ON") == planarKeyword, "original water material is untouched");
            int extraWater = LowQualityRendering.Stage >= LowQualityRendering.OptimizationStage.Water ? 1 : 0;
            Require(Object.FindObjectsOfType<MeshRenderer>().Length == renderers.Length + extraWater && Object.FindObjectsOfType<SceneTreeObstacle>().Length == trunks,
                "Low preserves vegetation population and hard tree count");
            bool unchanged = true;
            for (int i = 0; i < renderers.Length; i++)
            {
                // Authored atmosphere tiers already disable sun shafts; these are not vegetation.
                if (renderers[i].sharedMaterial != null && renderers[i].sharedMaterial.shader.name == "Jurassic World/Rainforest Soft Sun Shaft") continue;
                unchanged &= renderers[i].enabled == enabled[i] && renderers[i].forceRenderingOff == forcedOff[i];
            }
            Require(unchanged, "Low preserves vegetation visibility flags (authored sun shafts excluded)");
            float[] lowDistances = camera.layerCullDistances;
            for (int i = 0; i < distances.Length; i++) Require(distances[i] == lowDistances[i], "Low adds no camera layer distance reduction " + i);
            controller.ApplyTier(1);
            Require(ocean.OceanMaterial == original && reflection.enabled == reflectionEnabled, "Medium restores original creek material and reflection state");
            Require(ocean.enabled && camera.opaqueSortMode == sorting, "Medium restores Crest simulation and camera sorting");
            controller.ApplyTier(0); controller.ApplyTier(2);
            Require(ocean.OceanMaterial == original && reflection.enabled == reflectionEnabled, "High restores original creek material and reflection state");
            Require(ocean.enabled && camera.opaqueSortMode == sorting, "High restores Crest simulation and camera sorting");
            Require(renderers.Select((r,i) => r.sharedMaterials.SequenceEqual(materials[i])).All(equal=>equal), "High restores every original material binding");
        }

        private static void VerifyThinning(GraphicsQualityController controller)
        {
            if (LowQualityRendering.Stage < LowQualityRendering.OptimizationStage.Materials) return;
            const string requestedKey = "JurassicWorld.Performance.Interactive";
            const string reductionKey = "JurassicWorld.Performance.Argument.-jwLowVegetationReduction";
            const string stageKey = "JurassicWorld.Performance.Argument.-jwLowStage";
            bool requested = SessionState.GetBool(requestedKey,false);
            string oldReduction = SessionState.GetString(reductionKey,"0"), oldStage = SessionState.GetString(stageKey,"Materials");
            controller.ApplyTier(2);
            var renderers = Object.FindObjectsOfType<MeshRenderer>();
            var original = renderers.ToDictionary(r=>r,r=>r.forceRenderingOff);
            var protectedRenderers = Object.FindObjectsOfType<SceneTreeObstacle>().SelectMany(t=>t.GetComponentsInChildren<Renderer>(true)).Distinct().ToDictionary(r=>r,r=>r.forceRenderingOff);
            var hidden = new HashSet<MeshRenderer>();
            try
            {
                SessionState.SetBool(requestedKey,true); SessionState.SetString(stageKey,"Materials");
                foreach (int reduction in new[] {10,20,30})
                {
                    SessionState.SetString(reductionKey,reduction.ToString()); controller.ApplyTier(0);
                    var budget = controller.GetComponent<LowQualityRendering>();
                    Require(budget.SkippedSoftPlants == 0 && budget.HiddenSoftPlants == budget.EligibleSoftPlants * reduction / 100,
                        "exact complete soft plant reduction " + reduction);
                    var current = renderers.Where(r=>r.forceRenderingOff && !original[r]).ToHashSet();
                    Require(hidden.IsSubsetOf(current), "reductions are nested and deterministic " + reduction);
                    hidden=current;
                    Require(protectedRenderers.All(pair=>pair.Key.forceRenderingOff == pair.Value), "hard trees stay visible during reduction " + reduction);
                    controller.ApplyTier(2);
                    Require(original.All(pair=>pair.Key.forceRenderingOff == pair.Value), "High restores original visibility " + reduction);
                }
                SessionState.SetString(reductionKey,"30"); controller.ApplyTier(0);
                Require(hidden.SetEquals(renderers.Where(r=>r.forceRenderingOff && !original[r])), "reapplying reduction chooses identical plants");
                controller.ApplyTier(1);
                Require(original.All(pair=>pair.Key.forceRenderingOff == pair.Value), "Medium restores original visibility after thinning");
            }
            finally
            {
                controller.ApplyTier(2); SessionState.SetString(reductionKey,oldReduction); SessionState.SetString(stageKey,oldStage);
                SessionState.SetBool(requestedKey,requested);
            }
        }

        private static void VerifySmallPlantLod()
        {
            var plant = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var marker = plant.AddComponent<CenterGapGroundPlant>(); marker.ApplyQuality(1);
                var group = plant.GetComponent<LODGroup>();
                Require(group != null && group.GetLODs().Length == 1, "small marked ground plant receives a cull LOD");
                marker.ApplyQuality(0); marker.ApplyQuality(2); marker.ApplyQuality(1);
                Require(plant.GetComponents<LODGroup>().Length == 1 && plant.GetComponent<LODGroup>() == group,
                    "quality switching reuses the small plant LOD");
                var child = new GameObject("Authored parent LOD control"); child.transform.SetParent(plant.transform);
                child.AddComponent<CenterGapGroundPlant>().ApplyQuality(1);
                Require(child.GetComponent<LODGroup>() == null, "authored parent LOD remains authoritative");
            }
            finally { Object.DestroyImmediate(plant); }
            plant = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                plant.transform.localScale = Vector3.one * 5;
                plant.AddComponent<CenterGapGroundPlant>().ApplyQuality(1);
                Require(plant.GetComponent<LODGroup>() == null, "large plants are excluded from small-ground culling");
            }
            finally { Object.DestroyImmediate(plant); }
        }

        private static void VerifyInteractionBudget()
        {
            var holder = new GameObject("Foliage budget regression fixture");
            var previousSources = DinosaurFoliageInteractor.Sources.ToArray();
            var previousInstance = FoliageInteractionSystem.Instance;
            try
            {
                var camera = new GameObject("Budget camera").AddComponent<Camera>(); camera.transform.SetParent(holder.transform);
                camera.enabled = false;
                var system = holder.AddComponent<FoliageInteractionSystem>();
                typeof(FoliageInteractionSystem).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(system, null);
                var source = holder.AddComponent<DinosaurFoliageInteractor>();
                var near = MakeTree(holder.transform, Vector3.zero);
                var far = MakeTree(holder.transform, Vector3.right * 150);
                system.Register(near); system.Register(far); system.ApplyQuality(1, camera);
                DinosaurFoliageInteractor.Sources.Clear(); DinosaurFoliageInteractor.Sources.Add(source);
                source.contacts = new[] { Contact(Vector3.zero), Contact(Vector3.right * 150) };
                system.Step(1f / 60f);
                Require(near.displacement != null && system.ActiveTreeCount == 1, "near interaction remains active");
                Require(far.displacement == null, "distant interaction does not allocate a field");
                camera.transform.position = Vector3.right * 150;
                system.Step(1f / 60f);
                Require(far.displacement != null && system.ActiveTreeCount == 1, "camera relocation activates nearby interaction");
                foreach (var value in near.displacement) Require(value == Vector3.zero, "distant old field resets");
                system.ApplyQuality(0, camera);
                Require(system.interactionsEnabled && !system.ContactSamplingEnabled, "Low disables sampling without overwriting authored interaction switch");
                Require(system.ActiveTreeCount == 0, "Low clears active contact fields immediately");
                foreach (var value in far.displacement) Require(value == Vector3.zero, "Low restores resting plant geometry");
                source.capsules = new[] { new DinosaurFoliageInteractor.Capsule { bone = source.transform, end = Vector3.up, radius = 1f } };
                source.SamplePose(1f / 60f);
                Require(source.contacts.Length == 2, "Low skips pose sampling and contact allocation");
                system.ApplyQuality(2, camera);
                Require(system.ContactSamplingEnabled, "High restores contact sampling");
                source.SamplePose(1f / 60f);
                Require(source.contacts.Length == 1 && !source.contacts[0].valid, "resumed sampling starts with fresh pose history");
                source.SamplePose(1f / 60f);
                Require(source.contacts[0].valid, "second resumed pose can contact plants");
                source.contacts = new[] { Contact(Vector3.right * 150) };
                system.Step(1f / 60f);
                Require(system.ActiveTreeCount == 1, "High resumes plant bending");
                system.interactionsEnabled = false;
                system.ApplyQuality(0, camera); system.ApplyQuality(1, camera);
                Require(!system.interactionsEnabled && !system.ContactSamplingEnabled, "quality switch preserves an explicitly disabled interaction system");
            }
            finally
            {
                Object.DestroyImmediate(holder);
                typeof(FoliageInteractionSystem).GetProperty("Instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, previousInstance);
                DinosaurFoliageInteractor.Sources.Clear(); DinosaurFoliageInteractor.Sources.AddRange(previousSources);
            }
        }
        private static InteractiveFoliage MakeTree(Transform parent, Vector3 position)
        {
            var root = new GameObject("Budget tree"); root.transform.SetParent(parent); root.transform.position = position;
            var tree = root.AddComponent<InteractiveFoliage>(); tree.localBounds = new Bounds(Vector3.zero, Vector3.one * 4);
            tree.PrepareRuntime(); return tree;
        }
        private static FoliageContact Contact(Vector3 offset) => new FoliageContact
        {
            start = offset, end = offset + Vector3.up, previousStart = offset - Vector3.right * .1f,
            previousEnd = offset + Vector3.up - Vector3.right * .1f, radius = 1f, speed = 2f, valid = true
        };
    }
}
