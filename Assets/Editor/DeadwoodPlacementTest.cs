using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    public static class DeadwoodPlacementTest
    {
        private const string Folder = "Assets/DeadwoodPlacementTest";
        private const string ScenePath = Folder + "/DeadwoodPlacementTest.unity";
        private const string TerrainPath = Folder + "/DeadwoodTerrain.asset";
        private const string PlayerPath = "Builds/DeadwoodPlacementTest/DeadwoodPlacementTest.exe";
        private const string LogPath = AutumnGroundedLogAssetBuilder.PrefabPath;

        [MenuItem("Jurassic World/Build Isolated Deadwood Placement Test")]
        public static void Build()
        {
            AutumnGroundedLogAssetBuilder.EnsureAssets();
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "DeadwoodPlacementTest");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath);
            if (data == null)
            {
                data = new TerrainData();
                AssetDatabase.CreateAsset(data, TerrainPath);
            }
            data.heightmapResolution = 65;
            data.size = new Vector3(54f, 2f, 36f);
            var heights = new float[65, 65];
            for (int z = 0; z < 65; z++)
            for (int x = 0; x < 65; x++)
            {
                float wx = -27f + x * 54f / 64f;
                float wz = -18f + z * 36f / 64f;
                float rise = Mathf.Max(0f, wx + 8f) * 0.016f;
                heights[z, x] = (rise + 0.018f * Mathf.Sin(wz * 0.32f)) / 2f;
            }
            data.SetHeights(0, 0, heights);
            TerrainLayer grass = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/Terrain/Layers/GrassLayer.terrainlayer");
            if (grass != null) data.terrainLayers = new[] { grass };
            EditorUtility.SetDirty(data);
            GameObject ground = Terrain.CreateTerrainGameObject(data);
            ground.name = "Flat and gentle slope test terrain";
            ground.transform.position = new Vector3(-27f, 0f, -18f);
            Terrain terrain = ground.GetComponent<Terrain>();
            terrain.drawTreesAndFoliage = true;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LogPath);
            if (prefab == null) throw new FileNotFoundException("Missing deadwood prefab", LogPath);
            float[] xPositions = { -15f, 0f, 15f };
            float[] yaws = { 0f, 35f, 90f };
            float[] scales = { 1f, 1.1f, 0.9f };
            for (int i = 0; i < 3; i++)
            {
                GameObject log = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                log.name = "Test log " + (i + 1);
                log.transform.position = new Vector3(xPositions[i], 0f, 2f);
                log.transform.rotation = Quaternion.Euler(0f, yaws[i], 0f);
                log.transform.localScale = Vector3.one * scales[i];
                DeadVegetationPass.LogContact contact = DeadVegetationPass.GroundFallenLog(log, terrain);
                Debug.Log($"DEADWOOD_TEST_CONTACT log={i + 1} gap={contact.HighestGap:F3}m burial={contact.DeepestBurial:F3}m");
                if (contact.HighestGap > 0.10f || contact.DeepestBurial > 0.15f)
                    throw new InvalidOperationException("Deadwood contact failed for test log " + (i + 1));
            }

            var lightObject = new GameObject("Test sun");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            var cameraObject = new GameObject("Test camera");
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<DeadwoodTestCamera>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Path.GetDirectoryName(PlayerPath));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = PlayerPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Deadwood test player build failed: " + report.summary.result);
            Debug.Log("DEADWOOD_TEST_PLAYER_READY: " + PlayerPath);
        }
    }
}
