using System;
using System.Collections.Generic;
using System.Linq;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    /// <summary>Scene-wide west/autumn and east/tropical vegetation pass.</summary>
    internal static class Phase2VegetationSeeder
    {
        internal const string RootName = "Phase 2 Biome Vegetation";
        private const string OldRootName = "Vegetation Diversity Prototype (X -15..15, Z 60..90)";
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string BananaPrefabPath = "Assets/VegetationDiversity/Prefabs/BananaTree.prefab";
        private const string BananaGroveName = "Banana Grove Tropical";
        private const int ExpectedGroups = 30;
        private const int ExpectedBananaTrees = 12;
        private const float AvoidanceMargin = 1f;
        private static readonly Rect PlayArea = Rect.MinMaxRect(-74f, -73f, 74f, 85f);
        private static readonly Rect BananaGroveArea = Rect.MinMaxRect(50f, -65f, 70f, -35f);

        private enum Biome { Autumn, Transition, Tropical }
        private struct GroupSite
        {
            public readonly Biome Biome;
            public readonly Vector2 Point;
            public GroupSite(Biome biome, float x, float z)
            { Biome = biome; Point = new Vector2(x, z); }
        }

        // Broad, irregular coverage across the full playable area. First 15 sites formed the reviewed checkpoint;
        // the remaining sites complete the denser full layout while retaining biome separation.
        private static readonly GroupSite[] Sites =
        {
            new GroupSite(Biome.Autumn, -59f, -56f), new GroupSite(Biome.Autumn, -37f, -43f),
            new GroupSite(Biome.Autumn, -55f, -13f), new GroupSite(Biome.Autumn, -27f, 6f),
            new GroupSite(Biome.Autumn, -49f, 38f), new GroupSite(Biome.Autumn, -28f, 68f),
            new GroupSite(Biome.Transition, -8f, -53f), new GroupSite(Biome.Transition, 7f, -4f),
            new GroupSite(Biome.Transition, 4f, 73f),
            new GroupSite(Biome.Tropical, 56f, -55f), new GroupSite(Biome.Tropical, 36f, -40f),
            new GroupSite(Biome.Tropical, 56f, -9f), new GroupSite(Biome.Tropical, 28f, 13f),
            new GroupSite(Biome.Tropical, 49f, 41f), new GroupSite(Biome.Tropical, 28f, 68f),
            new GroupSite(Biome.Autumn, -27f, -60f), new GroupSite(Biome.Autumn, -55f, -38f),
            new GroupSite(Biome.Autumn, -68f, -8f), new GroupSite(Biome.Autumn, -41f, 18f),
            new GroupSite(Biome.Autumn, -65f, 48f), new GroupSite(Biome.Autumn, -24f, 50f),
            new GroupSite(Biome.Transition, 0f, -62f), new GroupSite(Biome.Transition, -2f, 27f),
            new GroupSite(Biome.Transition, 1f, 52f),
            new GroupSite(Biome.Tropical, 27f, -60f), new GroupSite(Biome.Tropical, 55f, -35f),
            new GroupSite(Biome.Tropical, 68f, -2f), new GroupSite(Biome.Tropical, 43f, 18f),
            new GroupSite(Biome.Tropical, 67f, 50f), new GroupSite(Biome.Tropical, 27f, 50f),
        };

        private struct TreeSpec
        {
            public readonly string Path;
            public readonly float Height;
            public readonly float TrunkRadius;
            public TreeSpec(string path, float height, float radius)
            { Path = path; Height = height; TrunkRadius = radius; }
        }

        private static readonly TreeSpec[] AutumnTrees =
        {
            new TreeSpec("Assets/VegetationDiversity/Prefabs/FabTree_1.prefab", 10f, 0.8f),
            new TreeSpec("Assets/VegetationDiversity/Prefabs/FabTree_5.prefab", 14f, 0.9f),
            new TreeSpec("Assets/VegetationDiversity/Prefabs/FabTree_10.prefab", 18f, 1.1f),
        };
        private static readonly TreeSpec[] TransitionTrees =
        {
            new TreeSpec("Assets/NatureStarterKit2/Nature/tree01.prefab", 10f, 0.85f),
            new TreeSpec("Assets/NatureStarterKit2/Nature/tree02.prefab", 9f, 0.85f),
            new TreeSpec("Assets/NatureStarterKit2/Nature/tree03.prefab", 8f, 0.8f),
        };
        private static readonly TreeSpec[] TropicalTrees =
        {
            new TreeSpec(BananaPrefabPath, 3.2f, 0.38f),
            new TreeSpec(BananaPrefabPath, 3.6f, 0.40f),
            new TreeSpec("Assets/VegetationDiversity/Prefabs/Pachira_B.prefab", 5f, 0.6f),
            new TreeSpec("Assets/VegetationDiversity/Prefabs/IslandTree02.prefab", 10f, 1.1f),
        };

        [MenuItem("Jurassic World/Vegetation Diversity/Build Phase 2 Full Layout")]
        public static void BuildFullLayout()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            Seed();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        internal static void Seed()
        {
            Terrain terrain = UnityEngine.Object.FindObjectOfType<Terrain>();
            if (terrain == null) throw new InvalidOperationException("Phase 2 requires the scene Terrain.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(BananaPrefabPath) == null)
                throw new InvalidOperationException("Phase 2 requires the prepared BananaTree prefab.");
            GameObject old = GameObject.Find(OldRootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            old = GameObject.Find(RootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old);

            var root = new GameObject(RootName);
            var terrainTrees = TerrainTreePositions(terrain);
            var newTrees = new List<Vector2>();
            var existingSceneTrees = UnityEngine.Object.FindObjectsOfType<SceneTreeObstacle>()
                .Select(tree => new Vector2(tree.transform.position.x, tree.transform.position.z)).ToList();
            var bananaSpecs = new List<TreeSpec>();
            GameObject[] ferns = UnderstoryAssetLocator.FindFernPrefabs();
            GameObject[] bushes = NatureStarterKitLocator.FindDetailMeshes()
                .Where(p => p.name.IndexOf("bush", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(p => p.name, StringComparer.Ordinal).ToArray();
            if (ferns.Length == 0 || bushes.Length == 0)
                throw new InvalidOperationException("Phase 2 requires fern and bush prefabs.");

            for (int i = 0; i < Sites.Length; i++)
            {
                GroupSite site = Sites[i];
                var group = new GameObject($"{i + 1:00}_{site.Biome}");
                group.transform.SetParent(root.transform);
                TreeSpec[] options = site.Biome == Biome.Autumn ? AutumnTrees :
                    site.Biome == Biome.Tropical ? TropicalTrees : TransitionTrees;
                for (int j = 0; j < 2; j++)
                {
                    TreeSpec spec = options[(i + j) % options.Length];
                    if (spec.Path == BananaPrefabPath)
                    {
                        bananaSpecs.Add(spec);
                        continue;
                    }
                    Vector2 desired = site.Point + (j == 0 ? new Vector2(-2f, 1.5f) : new Vector2(2.5f, -1.5f));
                    PlaceTree(spec, desired, site.Biome, i * 51f + j * 107f,
                        terrain, terrainTrees, newTrees, group.transform);
                }
                for (int j = 0; j < 6; j++)
                {
                    float angle = (j * 137.5f + i * 39f) * Mathf.Deg2Rad;
                    float distance = 2.5f + (j % 3) * 1.5f;
                    Vector2 desired = site.Point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                    GameObject prefab = j % 2 == 0 ? ferns[(i + j) % ferns.Length] : bushes[(i + j) % bushes.Length];
                    PlaceUnderstory(prefab, desired, terrain, group.transform, j);
                }
            }
            if (bananaSpecs.Count != ExpectedBananaTrees)
                throw new InvalidOperationException("Banana tree slot count drifted.");
            var grove = new GameObject(BananaGroveName);
            grove.transform.SetParent(root.transform);
            for (int index = 0; index < bananaSpecs.Count; index++)
            {
                int row = index / 4;
                int column = index % 4;
                Vector2 desired = new Vector2(54f + column * 4f + (row % 2 == 0 ? 0f : 1f), -57f + row * 6f);
                TreeSpec spec = bananaSpecs[index];
                Vector2 spot = FindGroveSpot(desired, spec.TrunkRadius, terrainTrees, existingSceneTrees, newTrees);
                PlaceTreeAtSpot(spec, spot, index * 137f, terrain, newTrees, grove.transform);
            }
            if (root.transform.childCount != ExpectedGroups + 1)
                throw new InvalidOperationException("Phase 2 group count drifted.");
            MoveCentralRocksOffRoutes(terrain);
            RealignRightRockIsland(terrain);
            RegisterExistingIslandSaplings();
            ValidateRoutes(root);
            Debug.Log($"PHASE2_FULL_BUILT groups={ExpectedGroups} grove={ExpectedBananaTrees} trees={newTrees.Count} understory={ExpectedGroups * 6} " +
                      "west=autumn center=green east=tropical; TerrainData and ForestSeeder unchanged.");
        }

        private static void PlaceTree(TreeSpec spec, Vector2 desired, Biome biome, float yaw,
            Terrain terrain, List<Vector2> oldTrees, List<Vector2> newTrees, Transform parent)
        {
            Vector2 spot = FindTreeSpot(desired, biome, spec.TrunkRadius, oldTrees, newTrees);
            PlaceTreeAtSpot(spec, spot, yaw, terrain, newTrees, parent);
        }

        private static void PlaceTreeAtSpot(TreeSpec spec, Vector2 spot, float yaw,
            Terrain terrain, List<Vector2> newTrees, Transform parent)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.Path);
            if (prefab == null) throw new InvalidOperationException("Missing tree prefab: " + spec.Path);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = prefab.name + "_SceneTree";
            instance.transform.SetPositionAndRotation(new Vector3(spot.x, 0f, spot.y), Quaternion.Euler(0f, yaw, 0f));
            Bounds bounds = RenderBounds(instance);
            instance.transform.localScale *= spec.Height / bounds.size.y;
            Ground(instance, terrain, spot);
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            SceneTreeObstacle marker = instance.AddComponent<SceneTreeObstacle>();
            marker.SetRadius(spec.TrunkRadius);
            newTrees.Add(spot);
        }

        private static Vector2 FindTreeSpot(Vector2 desired, Biome biome, float radius,
            List<Vector2> oldTrees, List<Vector2> newTrees)
        {
            for (float spread = 0f; spread <= 15f; spread += 0.75f)
                for (int step = 0; step < (spread == 0f ? 1 : 32); step++)
                {
                    float angle = step * Mathf.PI * 2f / 32f;
                    Vector2 spot = desired + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * spread;
                    if (!PlayArea.Contains(spot) || !InBiome(spot.x, biome)) continue;
                    if (Vector2.Distance(spot, new Vector2(0f, -16f)) < 12f) continue;
                    if (oldTrees.Any(t => Vector2.Distance(t, spot) < radius + 2f)) continue;
                    if (newTrees.Any(t => Vector2.Distance(t, spot) < radius + 2f)) continue;
                    if (DistanceToRoutes(spot) <= radius + DinosaurAgent.TreeAvoidRadiusCap + AvoidanceMargin) continue;
                    return spot;
                }
            throw new InvalidOperationException($"No route-safe {biome} tree position near {desired}.");
        }

        private static Vector2 FindGroveSpot(Vector2 desired, float radius, List<Vector2> terrainTrees,
            List<Vector2> sceneTrees, List<Vector2> newTrees)
        {
            for (float spread = 0f; spread <= 2f; spread += 0.5f)
            for (int step = 0; step < (spread == 0f ? 1 : 32); step++)
            {
                float angle = step * Mathf.PI * 2f / 32f;
                Vector2 spot = desired + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * spread;
                if (!BananaGroveArea.Contains(spot) || MainCreekIntegration.NearCreek(spot.x, spot.y, 6f)) continue;
                if (terrainTrees.Any(tree => Vector2.Distance(tree, spot) < radius + 2f)) continue;
                if (sceneTrees.Any(tree => Vector2.Distance(tree, spot) < radius + 2f)) continue;
                if (newTrees.Any(tree => Vector2.Distance(tree, spot) < radius + 2f)) continue;
                if (DistanceToRoutes(spot) <= radius + DinosaurAgent.TreeAvoidRadiusCap + AvoidanceMargin) continue;
                return spot;
            }
            throw new InvalidOperationException($"No safe banana grove position within 2m of {desired}.");
        }

        private static bool InBiome(float x, Biome biome) =>
            biome == Biome.Autumn ? x <= -20f : biome == Biome.Tropical ? x >= 20f : x > -14f && x < 14f;

        private static void PlaceUnderstory(GameObject prefab, Vector2 desired, Terrain terrain, Transform parent, int index)
        {
            Vector2 spot = desired;
            if (!PlayArea.Contains(spot)) spot = new Vector2(
                Mathf.Clamp(spot.x, PlayArea.xMin, PlayArea.xMax),
                Mathf.Clamp(spot.y, PlayArea.yMin, PlayArea.yMax));
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = prefab.name + "_" + index;
            instance.transform.SetPositionAndRotation(new Vector3(spot.x, 0f, spot.y),
                Quaternion.Euler(0f, (index * 67f + parent.GetSiblingIndex() * 23f) % 360f, 0f));
            Bounds bounds = RenderBounds(instance);
            instance.transform.localScale *= (index % 2 == 0 ? 1.9f : 2.6f) / bounds.size.y;
            Ground(instance, terrain, spot);
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
        }

        private static void Ground(GameObject instance, Terrain terrain, Vector2 spot)
        {
            float y = terrain.SampleHeight(new Vector3(spot.x, 0f, spot.y)) + terrain.transform.position.y;
            Bounds bounds = RenderBounds(instance);
            instance.transform.position += Vector3.up * (y - bounds.min.y - 0.03f);
        }

        private static Bounds RenderBounds(GameObject instance)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("Empty vegetation prefab: " + instance.name);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static List<Vector2> TerrainTreePositions(Terrain terrain)
        {
            TerrainData data = terrain.terrainData;
            return data.treeInstances.Where(t => t.widthScale > 0f).Select(t => new Vector2(
                terrain.transform.position.x + t.position.x * data.size.x,
                terrain.transform.position.z + t.position.z * data.size.z)).ToList();
        }

        private static float DistanceToRoutes(Vector2 point)
        {
            float minimum = float.PositiveInfinity;
            for (int route = 0; route < MovementCorridor.RouteCount; route++)
                minimum = Mathf.Min(minimum, MovementCorridor.MinDistanceToRoute(point, route));
            return minimum;
        }

        [MenuItem("Jurassic World/Vegetation Diversity/Validate Phase 2 Full Layout")]
        public static void ValidateFullLayout()
        {
            EditorSceneManager.OpenScene(ScenePath);
            GameObject root = GameObject.Find(RootName);
            if (root == null) throw new InvalidOperationException("Phase 2 scene root missing.");
            ValidateRoutes(root);
            Transform grove = root.transform.Find(BananaGroveName);
            if (root.transform.childCount != ExpectedGroups + 1 || grove == null)
                throw new InvalidOperationException("Group or banana grove count mismatch.");
            TerrainTreeObstacleRegistry.Invalidate();
            TerrainTreeObstacleRegistry.EnsureInitialized();
            int routeSamples = 0;
            for (int route = 0; route < MovementCorridor.RouteCount; route++)
            for (int sample = 0; sample < MovementCorridor.SampleCount(route); sample++)
            {
                Vector2 point = MovementCorridor.GetSample(route, sample).Position;
                if (TerrainTreeObstacleRegistry.IsBlocked(point, DinosaurAgent.TreeAvoidRadiusCap))
                    throw new InvalidOperationException($"Tree blocks route {MovementCorridor.GetRouteName(route)} at sample {sample}.");
                routeSamples++;
            }
            foreach (SceneTreeObstacle tree in UnityEngine.Object.FindObjectsOfType<SceneTreeObstacle>())
                if (!TerrainTreeObstacleRegistry.IsBlocked(
                        new Vector2(tree.transform.position.x, tree.transform.position.z), 0.05f))
                    throw new InvalidOperationException("Scene tree is missing from runtime avoidance: " + tree.name);
            GameObject bananaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BananaPrefabPath);
            Renderer[] bananaRenderers = bananaPrefab != null ? bananaPrefab.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            if (bananaRenderers.Length == 0 || bananaRenderers.Any(renderer => renderer.sharedMaterials.Any(material => material == null)))
                throw new InvalidOperationException("BananaTree prefab or its materials are missing.");
            SceneTreeObstacle[] sceneTrees = root.GetComponentsInChildren<SceneTreeObstacle>();
            int bananaCount = sceneTrees.Count(tree => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(tree.gameObject) == BananaPrefabPath);
            SceneTreeObstacle[] groveTrees = grove.GetComponentsInChildren<SceneTreeObstacle>();
            if (bananaCount != ExpectedBananaTrees || groveTrees.Length != ExpectedBananaTrees)
                throw new InvalidOperationException($"Expected {ExpectedBananaTrees} banana trees, found {bananaCount}.");
            foreach (SceneTreeObstacle tree in groveTrees)
            {
                Vector2 point = new Vector2(tree.transform.position.x, tree.transform.position.z);
                if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(tree.gameObject) != BananaPrefabPath ||
                    !BananaGroveArea.Contains(point) || MainCreekIntegration.NearCreek(point.x, point.y, 6f))
                    throw new InvalidOperationException("Banana tree is outside the safe grove: " + tree.name);
                if (!groveTrees.Any(other => other != tree &&
                    Vector2.Distance(point, new Vector2(other.transform.position.x, other.transform.position.z)) < 8f))
                    throw new InvalidOperationException("A banana tree is isolated from the grove: " + tree.name);
            }
            if (sceneTrees.Any(tree => tree.name.StartsWith("CoconutPalm_", StringComparison.Ordinal)))
                throw new InvalidOperationException("A generated coconut palm remains in the scene.");
            foreach (string fab in new[] { "FabTree_1", "FabTree_5", "FabTree_10" })
                AssertThreeLods(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VegetationDiversity/Prefabs/" + fab + ".prefab"), fab, false);
            Debug.Log($"PHASE2_FULL_VALID groups={root.transform.childCount} trees={root.GetComponentsInChildren<SceneTreeObstacle>().Length} " +
                      $"routeSamples={routeSamples} obstacleCount={TerrainTreeObstacleRegistry.ObstacleCount}.");
        }

        private static void AssertThreeLods(GameObject prefab, string name, bool multipleRenderers)
        {
            LODGroup group = prefab != null ? prefab.GetComponent<LODGroup>() : null;
            LOD[] lods = group != null ? group.GetLODs() : Array.Empty<LOD>();
            if (lods.Length != 3 || lods.Any(level => level.renderers.Length < (multipleRenderers ? 2 : 1)))
                throw new InvalidOperationException("Three-level LOD setup is incomplete: " + name);
            long[] triangles = lods.Select(level => level.renderers.Sum(renderer =>
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) return 0L;
                long count = 0;
                for (int i = 0; i < filter.sharedMesh.subMeshCount; i++)
                    count += (long)filter.sharedMesh.GetIndexCount(i) / 3;
                return count;
            })).ToArray();
            if (!(triangles[0] > triangles[1] && triangles[1] > triangles[2]))
                throw new InvalidOperationException($"LOD triangle counts do not decrease for {name}: {string.Join("/", triangles)}");
        }

        [MenuItem("Jurassic World/Vegetation Diversity/Measure Phase 2 Render Cost")]
        public static void MeasureFullRenderCost()
        {
            EditorSceneManager.OpenScene(ScenePath);
            GameObject root = GameObject.Find(RootName);
            Camera camera = Camera.main;
            if (root == null || camera == null) throw new InvalidOperationException("Phase 2 root or main camera missing.");
            RenderTexture target = RenderTexture.GetTemporary(1280, 720, 24);
            RenderTexture prior = camera.targetTexture;
            try
            {
                camera.targetTexture = target;
                root.SetActive(false);
                for (int i = 0; i < 12; i++) camera.Render();
                double baseline = Median(new[] { RenderSample(camera), RenderSample(camera), RenderSample(camera) });
                root.SetActive(true);
                for (int i = 0; i < 12; i++) camera.Render();
                double full = Median(new[] { RenderSample(camera), RenderSample(camera), RenderSample(camera) });
                var meshes = new HashSet<Mesh>();
                var textures = new HashSet<Texture>();
                long sceneTriangles = 0;
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh != null && meshes.Add(filter.sharedMesh))
                        for (int submesh = 0; submesh < filter.sharedMesh.subMeshCount; submesh++)
                            sceneTriangles += (long)filter.sharedMesh.GetIndexCount(submesh) / 3;
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null) continue;
                        Texture albedo = material.GetTexture("_MainTex");
                        if (albedo != null) textures.Add(albedo);
                        Texture alpha = material.GetTexture("_AlphaTex");
                        if (alpha != null && alpha != Texture2D.whiteTexture) textures.Add(alpha);
                        Texture normal = material.GetTexture("_BumpMap");
                        if (normal != null) textures.Add(normal);
                    }
                long meshBytes = meshes.Sum(m => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m));
                long textureBytes = textures.Sum(t => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t));
                Debug.Log($"PHASE2_RENDER_COST 1280x720 baseline={baseline:F2}ms full={full:F2}ms delta={full - baseline:+0.00;-0.00}ms " +
                    $"groups={root.transform.childCount} renderers={root.GetComponentsInChildren<Renderer>(true).Length} " +
                    $"uniqueMeshLODTriangles={sceneTriangles} meshMemory={meshBytes / 1048576f:F1}MiB textures={textureBytes / 1048576f:F1}MiB. " +
                    "CPU Camera.Render only; GPU frame time is not measured.");
            }
            finally
            {
                root.SetActive(true);
                camera.targetTexture = prior;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static double RenderSample(Camera camera)
        {
            for (int i = 0; i < 4; i++) camera.Render();
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 16; i++) camera.Render();
            watch.Stop();
            return watch.Elapsed.TotalMilliseconds / 16d;
        }

        private static double Median(double[] values)
        {
            Array.Sort(values);
            return values[values.Length / 2];
        }

        private static void ValidateRoutes(GameObject root)
        {
            SceneTreeObstacle[] trees = root.GetComponentsInChildren<SceneTreeObstacle>();
            if (trees.Length != ExpectedGroups * 2) throw new InvalidOperationException("Tree count mismatch.");
            foreach (SceneTreeObstacle tree in trees)
            {
                Vector2 xz = new Vector2(tree.transform.position.x, tree.transform.position.z);
                float clearance = DistanceToRoutes(xz) - tree.Radius - DinosaurAgent.TreeAvoidRadiusCap;
                if (clearance <= AvoidanceMargin)
                    throw new InvalidOperationException($"Tree invades dinosaur route: {tree.name}, clearance={clearance:F2}m");
                string group = tree.transform.parent.name;
                if (group.Contains("Autumn") && xz.x > -20f || group.Contains("Tropical") && xz.x < 20f)
                    throw new InvalidOperationException("Tree is outside its biome: " + tree.name);
            }
            GameObject existingIslands = GameObject.Find(VegetationIslandSeeder.RootName);
            if (existingIslands != null)
                foreach (Transform sapling in existingIslands.GetComponentsInChildren<Transform>())
                {
                    if (!sapling.name.StartsWith("Sapling_", StringComparison.Ordinal)) continue;
                    Vector2 xz = new Vector2(sapling.position.x, sapling.position.z);
                    if (DistanceToRoutes(xz) <= 0.7f + DinosaurAgent.TreeAvoidRadiusCap + AvoidanceMargin)
                        throw new InvalidOperationException("Existing island sapling invades dinosaur route: " + sapling.name);
                }
            foreach (GameObject rock in UnityEngine.Object.FindObjectsOfType<GameObject>())
            {
                if (!rock.name.StartsWith("Rock_", StringComparison.Ordinal) ||
                    rock.GetComponentsInChildren<Renderer>(true).Length == 0) continue;
                Bounds bounds = RenderBounds(rock);
                Vector2 xz = new Vector2(bounds.center.x, bounds.center.z);
                float footprint = Mathf.Max(bounds.extents.x, bounds.extents.z);
                if (DistanceToRoutes(xz) <= footprint + DinosaurAgent.TreeAvoidRadiusCap + AvoidanceMargin)
                    throw new InvalidOperationException("Rock footprint invades dinosaur route: " + rock.name);
            }
        }

        private static void MoveCentralRocksOffRoutes(Terrain terrain)
        {
            foreach (GameObject rock in UnityEngine.Object.FindObjectsOfType<GameObject>())
            {
                if (!rock.name.StartsWith("Rock_Large_", StringComparison.Ordinal)) continue;
                Bounds bounds = RenderBounds(rock);
                Vector2 origin = new Vector2(bounds.center.x, bounds.center.z);
                float footprint = Mathf.Max(bounds.extents.x, bounds.extents.z);
                float required = footprint + DinosaurAgent.TreeAvoidRadiusCap + AvoidanceMargin;
                if (DistanceToRoutes(origin) > required) continue;
                bool found = false;
                for (float spread = 0.5f; spread <= 18f && !found; spread += 0.5f)
                    for (int step = 0; step < 48; step++)
                    {
                        float angle = step * Mathf.PI * 2f / 48f;
                        Vector2 point = origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * spread;
                        if (!PlayArea.Contains(point) || DistanceToRoutes(point) <= required) continue;
                        if (Vector2.Distance(point, new Vector2(0f, -16f)) < 8f) continue;
                        float before = terrain.SampleHeight(rock.transform.position) + terrain.transform.position.y;
                        float after = terrain.SampleHeight(new Vector3(point.x, 0f, point.y)) + terrain.transform.position.y;
                        rock.transform.position += new Vector3(point.x - origin.x, after - before, point.y - origin.y);
                        Debug.Log($"PHASE2_ROCK_MOVED {rock.name} from={origin} to={point} footprint={footprint:F2}m");
                        found = true;
                        break;
                    }
                if (!found) throw new InvalidOperationException("No route-safe rock position near " + rock.name);
            }
        }

        private static void RealignRightRockIsland(Terrain terrain)
        {
            GameObject rock = GameObject.Find("Rock_Large_00");
            GameObject island = GameObject.Find("C_RightRock");
            if (rock == null || island == null) return;
            Vector3 position = rock.transform.position + new Vector3(4.5f, 0f, 0.5f);
            position.y = terrain.SampleHeight(position) + terrain.transform.position.y;
            island.transform.position = position;
        }

        private static void RegisterExistingIslandSaplings()
        {
            GameObject islands = GameObject.Find(VegetationIslandSeeder.RootName);
            if (islands == null) return;
            foreach (Transform sapling in islands.GetComponentsInChildren<Transform>())
            {
                if (!sapling.name.StartsWith("Sapling_", StringComparison.Ordinal)) continue;
                SceneTreeObstacle marker = sapling.GetComponent<SceneTreeObstacle>();
                if (marker == null) marker = sapling.gameObject.AddComponent<SceneTreeObstacle>();
                marker.SetRadius(0.7f);
            }
        }
    }
}
