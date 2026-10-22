using System;
using System.Collections.Generic;
using System.IO;
using Crest;
using Crest.Spline;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JurassicWorld.Editor
{
    /// <summary>Adds the approved Crest creek to Main without modifying the source TerrainData.</summary>
    public static class MainCreekIntegration
    {
        const string MainScene = "Assets/Scenes/Main.unity";
        const string BackupScene = "Assets/Environment/Creek/MainBeforeCreek.unity";
        const string SourceTerrain = "Assets/Environment/Terrain/Prepared/JurassicTerrainData.asset";
        const string CreekTerrain = "Assets/Environment/Creek/Prepared/MainCreekTerrain.asset";
        const string WaterMaterial = "Assets/Environment/Creek/Prepared/CrestCreekWater.mat";
        const string WaveSpectrum = "Assets/Environment/Creek/Prepared/CreekRippleSpectrum.asset";
        const string WetLayer = "Assets/Environment/Creek/Prepared/JungleCreekBank.terrainlayer";
        const string RootName = "Main East to North Creek";
        const float StartZ = -25f;
        const float EndZ = 86f;
        const float WaterY = -0.06f;
        const float TerrainOffset = 0.8f;

        static readonly Vector2[] NorthControls =
        {
            new Vector2(-2f, 0f), // X is calculated from the approved 23 m creek below.
            new Vector2(16f, 62f),
            new Vector2(38f, 66f),
            new Vector2(58f, 65f),
            new Vector2(74f, 63f),
            new Vector2(86f, 57f),
        };

        static float DemoCenter(float z)
        {
            float t = Mathf.Clamp01((z - StartZ) / 23f);
            return 59f + 2.3f * Mathf.Sin(t * Mathf.PI * 1.65f) + 0.45f * Mathf.Sin(t * Mathf.PI * 4.2f);
        }

        static float Center(float z)
        {
            if (z <= -2f) return DemoCenter(z);
            for (int i = 0; i < NorthControls.Length - 1; i++)
            {
                float a = NorthControls[i].x;
                float b = NorthControls[i + 1].x;
                if (z > b && i < NorthControls.Length - 2) continue;
                float p0 = i == 0 ? DemoCenter(-2f) : NorthControls[i].y;
                float p1 = NorthControls[i + 1].y;
                float m0 = i == 0
                    ? (DemoCenter(-2f) - DemoCenter(-2.1f)) / 0.1f
                    : (p1 - NorthControls[i - 1].y) / (b - NorthControls[i - 1].x);
                float m1 = i + 2 < NorthControls.Length
                    ? (NorthControls[i + 2].y - p0) / (NorthControls[i + 2].x - a)
                    : (p1 - p0) / (b - a);
                float t = Mathf.Clamp01((z - a) / (b - a));
                float t2 = t * t;
                float t3 = t2 * t;
                return (2f * t3 - 3f * t2 + 1f) * p0 + (t3 - 2f * t2 + t) * (b - a) * m0
                    + (-2f * t3 + 3f * t2) * p1 + (t3 - t2) * (b - a) * m1;
            }
            return 57f;
        }

        static float HalfWidth(float z)
        {
            float taper = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(Mathf.Min(z - StartZ, EndZ - z) / 2.5f));
            return (1.18f + 0.14f * Mathf.Sin(z * 0.31f)) * Mathf.Lerp(0.12f, 1f, taper);
        }

        static float Distance(float x, float z)
        {
            float best = float.MaxValue;
            for (float sampleZ = Mathf.Max(StartZ, z - 4f); sampleZ <= Mathf.Min(EndZ, z + 4f); sampleZ += 0.4f)
            {
                float dx = x - Center(sampleZ);
                float dz = z - sampleZ;
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
            }
            return best;
        }

        internal static bool NearCreek(float x, float z, float margin)
        {
            if (x < 50f - margin || x > 70f + margin || z < StartZ - margin || z > EndZ + margin)
                return false;
            return Distance(x, z) < margin;
        }

        static float GroundY(Terrain terrain, Vector3 point) =>
            terrain.SampleHeight(point) + terrain.transform.position.y;

        [MenuItem("Jurassic World/Environment/Creek/Integrate East to North Creek")]
        private static void IntegrateMenu() => JurassicMenuGuard.Run(Integrate, true);

        [MenuItem("Jurassic World/Environment/Creek/Integrate East to North Creek", true)]
        private static bool IntegrateMenuEnabled() => JurassicMenuGuard.CanRun;

        public static void Integrate()
        {
            if (!File.Exists(MainScene) || !File.Exists(SourceTerrain) || !File.Exists(WaterMaterial) ||
                !File.Exists(WaveSpectrum) || !File.Exists(WetLayer))
                throw new FileNotFoundException("Main scene, terrain, or approved Crest assets are missing.");

            Scene scene = EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);
            if (GameObject.Find(RootName) != null)
                throw new InvalidOperationException("Main already contains the creek; refusing to carve terrain twice.");
            if (File.Exists(BackupScene) || File.Exists(CreekTerrain))
                throw new InvalidOperationException("Creek backup or terrain already exists; refusing to overwrite it.");

            Terrain terrain = UnityEngine.Object.FindObjectOfType<Terrain>();
            TerrainData source = AssetDatabase.LoadAssetAtPath<TerrainData>(SourceTerrain);
            if (terrain == null || source == null || terrain.terrainData != source)
                throw new InvalidDataException("Main must still use the untouched source TerrainData.");
            if (UnityEngine.Object.FindObjectOfType<OceanRenderer>() != null)
                throw new InvalidOperationException("Main already has an OceanRenderer.");

            if (!AssetDatabase.CopyAsset(MainScene, BackupScene))
                throw new IOException("Could not preserve Main before creek integration.");
            if (!AssetDatabase.CopyAsset(SourceTerrain, CreekTerrain))
                throw new IOException("Could not copy the source TerrainData.");
            TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(CreekTerrain);
            if (data == null || data == source) throw new InvalidDataException("Creek TerrainData was not isolated.");
            terrain.terrainData = data;
            TerrainCollider collider = terrain.GetComponent<TerrainCollider>();
            if (collider != null) collider.terrainData = data;

            Sculpt(terrain, data);
            PaintBanks(terrain, data);
            ClearTerrainPlants(terrain, data);
            ClearScenePlants();

            var root = new GameObject(RootName);
            CreateCrest(root.transform);
            DressBanks(root.transform, terrain);
            CreateGroundViews(root.transform, terrain);

            EditorUtility.SetDirty(data);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("MAIN_CREEK_INTEGRATED: East-to-north Crest creek; backup and source terrain preserved.");
        }

        static void Sculpt(Terrain terrain, TerrainData data)
        {
            int n = data.heightmapResolution;
            float[,] heights = data.GetHeights(0, 0, n, n);
            for (int zi = 0; zi < n; zi++)
            {
                float z = terrain.transform.position.z + zi * data.size.z / (n - 1f);
                for (int xi = 0; xi < n; xi++)
                {
                    float x = terrain.transform.position.x + xi * data.size.x / (n - 1f);
                    float depth = 0f;
                    if (NearCreek(x, z, 3.2f))
                    {
                        float d = Distance(x, z);
                        float w = HalfWidth(Mathf.Clamp(z, StartZ, EndZ));
                        if (d < 0.55f) depth = 0.79f;
                        else if (d < w)
                            depth = Mathf.Lerp(0.79f, 0.13f, Mathf.SmoothStep(0f, 1f, (d - 0.55f) / Mathf.Max(0.1f, w - 0.55f)));
                        else if (d < w + 1.9f)
                            depth = Mathf.Lerp(0.13f, 0f, Mathf.SmoothStep(0f, 1f, (d - w) / 1.9f));
                        depth *= Mathf.SmoothStep(0f, 1f,
                            Mathf.Clamp01(Mathf.Min(z - StartZ, EndZ - z) / 2.5f));
                    }
                    heights[zi, xi] = Mathf.Clamp01(heights[zi, xi] + (TerrainOffset - depth) / data.size.y);
                }
            }
            terrain.transform.position += Vector3.down * TerrainOffset;
            data.SetHeights(0, 0, heights);
            terrain.Flush();
        }

        static void PaintBanks(Terrain terrain, TerrainData data)
        {
            TerrainLayer wet = AssetDatabase.LoadAssetAtPath<TerrainLayer>(WetLayer);
            TerrainLayer[] priorLayers = data.terrainLayers;
            int layerCount = priorLayers.Length;
            float[,,] prior = data.GetAlphamaps(0, 0, data.alphamapResolution, data.alphamapResolution);
            var layers = new TerrainLayer[layerCount + 1];
            Array.Copy(priorLayers, layers, layerCount);
            layers[layerCount] = wet;
            data.terrainLayers = layers;
            int n = data.alphamapResolution;
            var result = new float[n, n, layerCount + 1];
            for (int zi = 0; zi < n; zi++)
            {
                float z = terrain.transform.position.z + (zi + 0.5f) * data.size.z / n;
                for (int xi = 0; xi < n; xi++)
                {
                    float x = terrain.transform.position.x + (xi + 0.5f) * data.size.x / n;
                    float blend = 0f;
                    if (NearCreek(x, z, 4.2f))
                    {
                        float d = Distance(x, z);
                        blend = 0.86f * (1f - Mathf.SmoothStep(1.0f, 4.1f, d));
                        blend *= Mathf.SmoothStep(0f, 1f,
                            Mathf.Clamp01(Mathf.Min(z - StartZ, EndZ - z) / 2.5f));
                    }
                    for (int layer = 0; layer < layerCount; layer++)
                        result[zi, xi, layer] = prior[zi, xi, layer] * (1f - blend);
                    result[zi, xi, layerCount] = blend;
                }
            }
            data.SetAlphamaps(0, 0, result);
        }

        static void ClearTerrainPlants(Terrain terrain, TerrainData data)
        {
            var trees = new List<TreeInstance>();
            foreach (TreeInstance tree in data.treeInstances)
            {
                float x = terrain.transform.position.x + tree.position.x * data.size.x;
                float z = terrain.transform.position.z + tree.position.z * data.size.z;
                if (!NearCreek(x, z, 4.2f)) trees.Add(tree);
            }
            data.SetTreeInstances(trees.ToArray(), true);
            int n = data.detailResolution;
            for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
            {
                int[,] details = data.GetDetailLayer(0, 0, n, n, layer);
                for (int zi = 0; zi < n; zi++)
                {
                    float z = terrain.transform.position.z + (zi + 0.5f) * data.size.z / n;
                    for (int xi = 0; xi < n; xi++)
                    {
                        float x = terrain.transform.position.x + (xi + 0.5f) * data.size.x / n;
                        if (NearCreek(x, z, 2.7f)) details[zi, xi] = 0;
                    }
                }
                data.SetDetailLayer(0, 0, layer, details);
            }
        }

        static void ClearScenePlants()
        {
            var removed = new HashSet<GameObject>();
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (renderer.GetComponentInParent<DinosaurAgent>() != null) continue;
                GameObject instance = PrefabUtility.GetOutermostPrefabInstanceRoot(renderer.gameObject);
                if (instance == null) continue;
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(instance);
                string path = source != null ? AssetDatabase.GetAssetPath(source) : "";
                if (!path.StartsWith("Assets/Environment/Vegetation/") &&
                    !path.StartsWith("Assets/Environment/RocksAndDeadwood/") &&
                    !path.StartsWith("Assets/ThirdParty/NatureStarterKit2/")) continue;
                Vector3 p = instance.transform.position;
                if (NearCreek(p.x, p.z, 4.2f)) removed.Add(instance);
            }
            foreach (GameObject item in removed) UnityEngine.Object.DestroyImmediate(item);
        }

        static void CreateCrest(Transform parent)
        {
            Material water = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterial);
            OceanWaveSpectrum spectrum = AssetDatabase.LoadAssetAtPath<OceanWaveSpectrum>(WaveSpectrum);
            Camera camera = UnityEngine.Object.FindObjectOfType<Camera>();
            if (water == null || spectrum == null || camera == null)
                throw new InvalidDataException("Approved Crest material, spectrum, or Main camera is missing.");

            var oceanObject = new GameObject("Crest Creek Water");
            oceanObject.transform.SetParent(parent, true);
            oceanObject.transform.position = new Vector3(0f, WaterY, 0f);
            OceanRenderer ocean = oceanObject.AddComponent<OceanRenderer>();
            ocean.OceanMaterial = water;
            ocean.ViewCamera = camera;
            ocean._globalWindSpeed = 0.6f;
            ocean._defaultClippingState = OceanRenderer.DefaultClippingState.EverythingClipped;
            ocean._waterBodyCulling = false;
            var settings = new SerializedObject(ocean);
            settings.FindProperty("_createClipSurfaceData").boolValue = true;
            settings.FindProperty("_createFlowSim").boolValue = true;
            settings.FindProperty("_createFoamSim").boolValue = false;
            settings.FindProperty("_minScale").floatValue = 2f;
            settings.FindProperty("_maxScale").floatValue = 32f;
            settings.FindProperty("_lodDataResolution").intValue = 256;
            settings.FindProperty("_lodCount").intValue = 5;
            settings.ApplyModifiedPropertiesWithoutUndo();

            ShapeGerstner waves = oceanObject.AddComponent<ShapeGerstner>();
            waves._spectrum = spectrum;
            waves._componentsPerOctave = 4;
            waves._overrideGlobalWindSpeed = true;
            waves._windSpeed = 10f;
            waves._overrideGlobalWindDirection = true;
            waves._waveDirectionHeadingAngle = 90f;
            waves._respectShallowWaterAttenuation = 0f;
            waves._weight = 0.09f;

            OceanPlanarReflection reflection = camera.GetComponent<OceanPlanarReflection>();
            if (reflection == null) reflection = camera.gameObject.AddComponent<OceanPlanarReflection>();
            var reflectionSettings = new SerializedObject(reflection);
            reflectionSettings.FindProperty("_textureSize").intValue = 512;
            reflectionSettings.FindProperty("RefreshPerFrames").intValue = 2;
            reflectionSettings.ApplyModifiedPropertiesWithoutUndo();

            for (float z = StartZ + 4f; z < EndZ; z += 8f)
            {
                float centerZ = Mathf.Min(EndZ - 2f, z);
                var section = new GameObject("Creek Water Section");
                section.transform.SetParent(oceanObject.transform, true);
                section.transform.position = new Vector3(Center(centerZ), WaterY, centerZ);
                section.transform.rotation = Quaternion.identity;
                section.transform.localScale = new Vector3(12f, 1f, 13f);
                section.AddComponent<WaterBody>();
            }

            var flowObject = new GameObject("Downstream Flow");
            flowObject.transform.SetParent(oceanObject.transform, true);
            flowObject.transform.position = Vector3.zero;
            Spline flow = flowObject.AddComponent<Spline>();
            flow.Radius = 1.5f;
            flow.Subdivisions = 3;
            flowObject.AddComponent<RegisterFlowInput>();
            int count = Mathf.CeilToInt((EndZ - StartZ) / 8f);
            for (int i = 0; i <= count; i++)
            {
                float z = Mathf.Lerp(StartZ, EndZ, i / (float)count);
                var point = new GameObject("Flow Point " + i);
                point.transform.SetParent(flowObject.transform, true);
                point.transform.position = new Vector3(Center(z), WaterY, z);
                point.AddComponent<SplinePoint>();
                point.AddComponent<SplinePointData>();
                point.AddComponent<SplinePointDataFlow>().FlowVelocity = 0.34f;
            }
            flow.UpdateSpline();
        }

        static void DressBanks(Transform parent, Terrain terrain)
        {
            string[] paths =
            {
                "Assets/Environment/RocksAndDeadwood/RockMossSet01/Prepared/Prefabs/RockMossSet01_Rock01.prefab",
                "Assets/Environment/RocksAndDeadwood/RockMossSet01/Prepared/Prefabs/RockMossSet01_Rock03.prefab",
                "Assets/Environment/RocksAndDeadwood/RockMossSet01/Prepared/Prefabs/RockMossSet01_Rock05.prefab",
                "Assets/Environment/Vegetation/Fern02/Prepared/Prefabs/Fern02_A.prefab"
            };
            var random = new System.Random(20260929);
            for (int i = 0; i < 74; i++)
            {
                float z = Mathf.Lerp(StartZ + 2f, EndZ - 2f, (i + 0.5f) / 74f);
                float side = i % 2 == 0 ? -1f : 1f;
                float x = Center(z) + side * (HalfWidth(z) + 0.4f + (float)random.NextDouble() * 0.8f);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i % paths.Length]);
                if (prefab == null) throw new FileNotFoundException("Missing creek bank prefab", paths[i % paths.Length]);
                GameObject item = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                item.name = i % paths.Length == 3 ? "Creek Fern" : "Creek Mossy Rock";
                item.transform.SetParent(parent, true);
                Vector3 p = new Vector3(x, 0f, z);
                p.y = GroundY(terrain, p) - (i % paths.Length == 3 ? 0f : 0.07f);
                item.transform.position = p;
                item.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                item.transform.localScale *= i % paths.Length == 3 ? 0.7f : 0.4f + (float)random.NextDouble() * 0.35f;
            }
            GameObject logPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Environment/RocksAndDeadwood/Deadwood/Prepared/Prefabs/DeadwoodLogTrunk02.prefab");
            if (logPrefab == null) throw new FileNotFoundException("Missing creek log prefab.");
            foreach (float z in new[] { -9.5f, 23f, 51f, 76f })
            {
                GameObject log = (GameObject)PrefabUtility.InstantiatePrefab(logPrefab);
                log.name = "Creek Fallen Log";
                log.transform.SetParent(parent, true);
                float x = Center(z) + HalfWidth(z) + 2.2f;
                log.transform.position = new Vector3(x, GroundY(terrain, new Vector3(x, 0f, z)), z);
                log.transform.rotation = Quaternion.Euler(0f, 18f + z * 0.7f, 0f);
            }
        }

        static void CreateGroundViews(Transform parent, Terrain terrain)
        {
            float[] positions = { -18f, 35f, 78f };
            for (int i = 0; i < positions.Length; i++)
            {
                float z = positions[i];
                var marker = new GameObject("Creek Ground View " + (i + 1));
                marker.transform.SetParent(parent, true);
                Vector3 p = new Vector3(Center(z) + 3.2f, 0f, z);
                p.y = GroundY(terrain, p) + 1.7f;
                Vector3 target = new Vector3(Center(Mathf.Min(EndZ, z + 9f)), WaterY, Mathf.Min(EndZ, z + 9f));
                marker.transform.SetPositionAndRotation(p, Quaternion.LookRotation(target - p));
            }
        }

        [MenuItem("Jurassic World/Environment/Creek/Place Main Camera at Creek")]
        private static void PlaceMainCameraAtCreekMenu() => JurassicMenuGuard.Run(PlaceMainCameraAtCreek, true);

        [MenuItem("Jurassic World/Environment/Creek/Place Main Camera at Creek", true)]
        private static bool PlaceMainCameraAtCreekMenuEnabled() => JurassicMenuGuard.CanRun;

        public static void PlaceMainCameraAtCreek()
        {
            Scene scene = EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);
            GameObject marker = GameObject.Find("Creek Ground View 1");
            Camera camera = Camera.main;
            if (marker == null || camera == null)
                throw new InvalidDataException("Main creek ground view or Main Camera is missing.");

            camera.transform.SetPositionAndRotation(marker.transform.position, marker.transform.rotation);
            var controller = camera.GetComponent<SmoothExplorerCamera>();
            if (controller == null)
                throw new InvalidDataException("Main Camera explorer controller is missing.");
            var settings = new SerializedObject(controller);
            settings.FindProperty("initialPitch").floatValue = camera.transform.eulerAngles.x;
            settings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(camera.transform);
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("MAIN_CREEK_CAMERA_READY: Main Camera starts at Creek Ground View 1.");
        }

        [MenuItem("Jurassic World/Scene/Build Windows Player")]
        private static void BuildPlayableMenu() => JurassicMenuGuard.Run(BuildPlayable, true);

        [MenuItem("Jurassic World/Scene/Build Windows Player", true)]
        private static bool BuildPlayableMenuEnabled() => JurassicMenuGuard.CanRun;

        public static void BuildPlayable()
        {
            Directory.CreateDirectory("Builds/JurassicWorld");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { MainScene },
                locationPathName = "Builds/JurassicWorld/JurassicWorld.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Playable Main build failed: " + report.summary.result);
            Debug.Log("MAIN_CREEK_PLAYABLE_BUILT: " + options.locationPathName);
        }

    }
}
