using System;
using System.IO;
using System.Reflection;
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
                VerifySmallPlantLod();
                VerifyInteractionBudget();
                Directory.CreateDirectory("Logs/RuntimePerformance");
                File.WriteAllText("Logs/RuntimePerformance/budget-acceptance.txt",
                    "PASS: repeated quality switching; saved preference preserved; near foliage bends; distant foliage remains unallocated; camera relocation restores interaction; distant active fields reset.\n");
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
            try
            {
                var camera = new GameObject("Budget camera").AddComponent<Camera>(); camera.transform.SetParent(holder.transform);
                camera.enabled = false;
                var system = holder.AddComponent<FoliageInteractionSystem>();
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
            }
            finally
            {
                Object.DestroyImmediate(holder);
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
