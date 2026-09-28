using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    /// <summary>One authored 30x30 m visual prototype. Terrain trees and ForestSeeder are untouched.</summary>
    internal static class VegetationDiversityPrototype
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string RootName = "Vegetation Diversity Prototype (X -15..15, Z 60..90)";
        private const string Root = "Assets/VegetationDiversity";
        private const string Materials = Root + "/Materials";
        private const string Prefabs = Root + "/Prefabs";
        private const string Source = "Assets/ThirdParty/VegetationDiversity";
        private const string Prepared = Source + "/Prepared";
        private const string Fab = Source + "/Source/FabDesktopTrees60";
        private const string Palm = Source + "/Source/SketchfabPalmTrees/textures";
        private const string Banana = Source + "/Source/SketchfabBananaTree/textures";
        private const string Island = Source + "/PolyHaven/IslandTree02/textures";
        private const string Pachira = Source + "/PolyHaven/PachiraAquatica01/textures";
        private static readonly Rect Area = new Rect(-15f, 60f, 30f, 30f);

        private struct Placement
        {
            public string Prefab;
            public Vector2 Position;
            public float Height;
            public float Yaw;
            public float Separation;
            public Placement(string prefab, float x, float z, float height, float yaw, float separation = 3f)
            {
                Prefab = prefab; Position = new Vector2(x, z); Height = height;
                Yaw = yaw; Separation = separation;
            }
        }

        private static readonly Placement[] Trees =
        {
            new Placement("FabTree_1", -12f, 68f, 10f, 26f),
            new Placement("FabTree_1", -7f, 82f, 11f, 214f),
            new Placement("FabTree_5", -7f, 69f, 15f, 104f),
            new Placement("FabTree_5", 4f, 84f, 16f, 309f),
            new Placement("FabTree_10", 9f, 66f, 20f, 37f),
            new Placement("FabTree_10", -12f, 80f, 19f, 181f),
            new Placement("Palm_2", -14f, 76f, 7f, 52f),
            new Placement("Palm_2", 7f, 79f, 7.5f, 248f),
            new Placement("Palm_4", 3f, 75f, 8.5f, 132f),
            new Placement("Palm_4", 13f, 84f, 9f, 289f),
            new Placement("IslandTree02", -5f, 74f, 9f, 47f, 4f),
        };

        private static readonly Placement[] Understory =
        {
            new Placement("BananaTree", -9f, 76f, 3.3f, 24f, 1.5f),
            new Placement("BananaTree", 10f, 73f, 3.1f, 156f, 1.5f),
            new Placement("Pachira_B", 1f, 73f, 2.5f, 83f, 1.5f),
            new Placement("Pachira_B", 9f, 84f, 2.7f, 269f, 1.5f),
        };

        [MenuItem("Jurassic World/Vegetation Diversity/Prepare Assets")]
        public static void PrepareAssets()
        {
            EnsureFolder(Materials);
            EnsureFolder(Prefabs);
            Shader foliage = Shader.Find("Jurassic World/Vegetation Prototype Cutout");
            Shader standard = Shader.Find("Standard");
            if (foliage == null || standard == null) throw new InvalidOperationException("Vegetation shaders are missing.");

            // Fab: all ten source trees share these two texture sheets.
            MakeMaterial("Fab_Trunk", standard, Fab + "/T_Trees60_Trunk.png", null,
                Fab + "/T_Trees60_Trunk_normal.png", 0.12f);
            MakeMaterial("Fab_Leaf", foliage, Fab + "/T_Trees60_Leaf.png", null, null, 0.12f);

            for (int i = 1; i <= 4; i++)
            {
                MakeMaterial("Palm_Bark_" + i, standard, Palm + $"/Bark_{i}_Bake1_pbr_diffuse.jpg", null, null, 0.12f);
                int leaf = Mathf.Max(2, i);
                MakeMaterial("Palm_Leaf_" + i, foliage, Palm + $"/PalmLeaf_{leaf}.png", null, null, 0.12f);
            }

            MakeMaterial("Island_Trunk", standard, Island + "/island_tree_02_diff_1k.jpg", null,
                Island + "/island_tree_02_nor_gl_1k.exr", 0.1f);
            MakeMaterial("Island_Branches", foliage, Island + "/island_tree_02_branches_diff_1k.png", null,
                Island + "/island_tree_02_branches_nor_gl_1k.png", 0.1f);
            MakeMaterial("Island_Leaves", foliage, Island + "/island_tree_02_leaves_diff_1k.png",
                Island + "/island_tree_02_leaves_alpha_1k.png",
                Island + "/island_tree_02_leaves_nor_gl_1k.png", 0.08f);

            MakeMaterial("Pachira_Bark", standard, Pachira + "/pachira_aquatica_01_bark_diff_1k.png", null,
                Pachira + "/pachira_aquatica_01_bark_nor_gl_1k.png", 0.1f);
            MakeMaterial("Pachira_Leaves", foliage, Pachira + "/pachira_aquatica_01_leaves_diff_1k.png",
                Pachira + "/pachira_aquatica_01_leaves_alpha_1k.png",
                Pachira + "/pachira_aquatica_01_leaves_nor_gl_1k.png", 0.08f);

            string[] bananaIds = { "tiglajzdy", "sdbcdah", "slpmfhp", "sl0no0p", "pgfhf" };
            foreach (string id in bananaIds)
            {
                string albedo = Banana + $"/{id}_2K_Albedo.jpg";
                string opacity = Banana + $"/{id}_2K_Opacity.jpg";
                string normal = Banana + $"/{id}_2K_Normal.jpg";
                if (!File.Exists(opacity)) opacity = null;
                if (!File.Exists(normal)) normal = null;
                MakeMaterial("Banana_" + id, opacity == null ? standard : foliage, albedo, opacity, normal, 0.1f);
            }

            foreach (int number in new[] { 1, 5, 10 }) MakePrefab($"FabTree_{number}", number);
            MakePrefab("Palm_2", 2);
            MakePrefab("Palm_4", 4);
            MakePrefab("BananaTree", 0);
            MakePrefab("Pachira_B", 0);
            MakePrefab("IslandTree02", 0);
            AssetDatabase.SaveAssets();
            Debug.Log("VEG_ASSETS_READY: eight new prefabs prepared, including a three-level hero tree.");
        }

        [MenuItem("Jurassic World/Vegetation Diversity/Build Prototype")]
        public static void BuildPrototype()
        {
            PrepareAssets();
            var scene = EditorSceneManager.OpenScene(ScenePath);
            Terrain terrain = UnityEngine.Object.FindObjectOfType<Terrain>();
            if (terrain == null) throw new InvalidOperationException("Main scene has no terrain.");
            GameObject oldRoot = GameObject.Find(RootName);
            if (oldRoot != null) UnityEngine.Object.DestroyImmediate(oldRoot);
            var root = new GameObject(RootName);
            var treeGroup = new GameObject("New trees (old Terrain trees retained)");
            treeGroup.transform.SetParent(root.transform);
            var understoryGroup = new GameObject("Tropical understory");
            understoryGroup.transform.SetParent(root.transform);

            List<Vector2> existingTrees = TerrainTreePositions(terrain);
            int oldCount = existingTrees.Count(Area.Contains);
            var newTrees = new List<Vector2>();
            foreach (Placement item in Trees)
                Place(item, treeGroup.transform, terrain, existingTrees, newTrees, true);
            foreach (Placement item in Understory)
                Place(item, understoryGroup.transform, terrain, existingTrees, newTrees, false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"VEG_PROTOTYPE_BUILT: area X[-15,15] Z[60,90], oldTerrainTrees={oldCount}, newTrees={Trees.Length}, " +
                      $"oldShare={(100f * oldCount / (oldCount + Trees.Length)):F1}%, understory={Understory.Length}. " +
                      "ForestSeeder and TerrainData were not written.");
        }

        [MenuItem("Jurassic World/Vegetation Diversity/Validate Prototype")]
        public static void ValidatePrototype()
        {
            EditorSceneManager.OpenScene(ScenePath);
            Terrain terrain = UnityEngine.Object.FindObjectOfType<Terrain>();
            GameObject root = GameObject.Find(RootName);
            if (terrain == null || root == null) throw new InvalidOperationException("Prototype or terrain missing.");
            int oldCount = TerrainTreePositions(terrain).Count(Area.Contains);
            Transform treeGroup = root.transform.Find("New trees (old Terrain trees retained)");
            Transform understory = root.transform.Find("Tropical understory");
            if (oldCount != 15 || treeGroup == null || treeGroup.childCount != Trees.Length ||
                understory == null || understory.childCount != Understory.Length)
                throw new InvalidOperationException("Prototype count or original tree count drifted.");
            float oldShare = (float)oldCount / (oldCount + treeGroup.childCount);
            if (oldShare < 0.5f || oldShare > 0.6f)
                throw new InvalidOperationException($"Old tree share is {oldShare:P1}, outside 50-60%.");
            var species = new HashSet<string> { "tree01", "tree02", "tree03", "tree04" };
            long totalTriangles = 0;
            foreach (Transform child in treeGroup)
            {
                species.Add(child.name.Split('_')[0] + "_" + child.name.Split('_')[1]);
                if (!Area.Contains(new Vector2(child.position.x, child.position.z)))
                    throw new InvalidOperationException("Tree outside prototype area: " + child.name);
                float routeDistance = NearestRoute(new Vector2(child.position.x, child.position.z));
                if (routeDistance < 4.5f) throw new InvalidOperationException("Tree too close to route: " + child.name);
                foreach (MeshFilter filter in child.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh != null && filter.gameObject.name.IndexOf("LOD1", StringComparison.Ordinal) < 0 &&
                        filter.gameObject.name.IndexOf("LOD2", StringComparison.Ordinal) < 0)
                        for (int submesh = 0; submesh < filter.sharedMesh.subMeshCount; submesh++)
                            totalTriangles += (long)filter.sharedMesh.GetIndexCount(submesh) / 3;
                foreach (Renderer renderer in child.GetComponentsInChildren<Renderer>(true))
                    foreach (Material material in renderer.sharedMaterials)
                        if (material == null || material.GetTexture("_MainTex") == null)
                            throw new InvalidOperationException("Missing vegetation material or albedo: " + child.name);
            }
            if (species.Count < 8 || species.Count > 10)
                throw new InvalidOperationException($"Expected 8-10 tree types, got {species.Count}.");
            GameObject hero = PrefabAt("IslandTree02");
            LODGroup group = hero.GetComponent<LODGroup>();
            if (group == null || group.GetLODs().Length != 3)
                throw new InvalidOperationException("Hero tree must have three LOD levels.");
            Debug.Log($"VEG_PROTOTYPE_VALID: oldTrees={oldCount}, newTrees={treeGroup.childCount}, " +
                      $"understory={understory.childCount}, treeTypes={species.Count}, oldShare={oldShare:P1}, " +
                      $"newTreeLOD0Triangles={totalTriangles}.");
        }

        [MenuItem("Jurassic World/Vegetation Diversity/Measure Render Cost")]
        public static void MeasureRenderCost()
        {
            EditorSceneManager.OpenScene(ScenePath);
            GameObject root = GameObject.Find(RootName);
            Camera camera = Camera.main;
            if (root == null || camera == null) throw new InvalidOperationException("Prototype or camera missing.");
            RenderTexture target = RenderTexture.GetTemporary(1280, 720, 24);
            RenderTexture oldTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = target;
                root.SetActive(false);
                for (int i = 0; i < 20; i++) camera.Render();
                root.SetActive(true);
                for (int i = 0; i < 20; i++) camera.Render();
                var oldSamples = new List<double>();
                var newSamples = new List<double>();
                for (int pair = 0; pair < 4; pair++)
                {
                    if ((pair & 1) == 0)
                    {
                        root.SetActive(false); oldSamples.Add(TimeRenders(camera));
                        root.SetActive(true); newSamples.Add(TimeRenders(camera));
                    }
                    else
                    {
                        root.SetActive(true); newSamples.Add(TimeRenders(camera));
                        root.SetActive(false); oldSamples.Add(TimeRenders(camera));
                    }
                }
                root.SetActive(true);
                oldSamples.Sort();
                newSamples.Sort();
                double oldMs = (oldSamples[1] + oldSamples[2]) * 0.5;
                double newMs = (newSamples[1] + newSamples[2]) * 0.5;
                var textures = new HashSet<Texture>();
                var meshes = new HashSet<Mesh>();
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null) continue;
                        Texture albedo = material.GetTexture("_MainTex");
                        if (albedo != null) textures.Add(albedo);
                        if (material.HasProperty("_AlphaTex"))
                        {
                            Texture alpha = material.GetTexture("_AlphaTex");
                            if (alpha != null && alpha != Texture2D.whiteTexture) textures.Add(alpha);
                        }
                        Texture normal = material.GetTexture("_BumpMap");
                        if (normal != null) textures.Add(normal);
                    }
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh != null) meshes.Add(filter.sharedMesh);
                long textureBytes = textures.Sum(t => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t));
                long meshBytes = meshes.Sum(m => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m));
                UnityEngine.Debug.Log($"VEG_RENDER_COST: 1280x720 offscreen Camera.Render CPU baseline={oldMs:F2}ms " +
                    $"prototype={newMs:F2}ms delta={newMs - oldMs:+0.00;-0.00}ms, " +
                    $"uniqueTextureMemory={textureBytes / 1048576f:F1}MiB, " +
                    $"uniqueMeshMemory={meshBytes / 1048576f:F1}MiB. " +
                    "This is CPU render submission, not GPU frame time.");
            }
            finally
            {
                root.SetActive(true);
                camera.targetTexture = oldTarget;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static double TimeRenders(Camera camera)
        {
            for (int i = 0; i < 8; i++) camera.Render();
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 24; i++) camera.Render();
            watch.Stop();
            return watch.Elapsed.TotalMilliseconds / 24d;
        }

        private static void Place(Placement item, Transform parent, Terrain terrain,
            List<Vector2> existingTrees, List<Vector2> newTrees, bool tree)
        {
            GameObject prefab = PrefabAt(item.Prefab);
            Vector2 spot = FindSpot(item.Position, item.Separation, tree ? existingTrees : null,
                tree ? newTrees : null, tree ? 4.5f : 2.5f);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = item.Prefab + "_" + (parent.childCount).ToString("00");
            instance.transform.position = new Vector3(spot.x, 0f, spot.y);
            instance.transform.rotation = Quaternion.Euler(0f, item.Yaw, 0f);
            Bounds bounds = RenderBounds(instance);
            instance.transform.localScale *= item.Height / bounds.size.y;
            bounds = RenderBounds(instance);
            float ground = terrain.SampleHeight(new Vector3(spot.x, 0f, spot.y)) + terrain.transform.position.y;
            instance.transform.position += Vector3.up * (ground - bounds.min.y - 0.03f);
            if (tree) newTrees.Add(spot);
            Debug.Log($"VEG_PLACED {item.Prefab} x={spot.x:F2} z={spot.y:F2} height={item.Height:F1} " +
                      $"routeDistance={NearestRoute(spot):F1}");
        }

        private static Vector2 FindSpot(Vector2 desired, float separation,
            List<Vector2> existingTrees, List<Vector2> newTrees, float routeClearance)
        {
            for (float radius = 0f; radius <= 8f; radius += 0.5f)
                for (int step = 0; step < (radius == 0f ? 1 : 24); step++)
                {
                    float angle = step * Mathf.PI * 2f / 24f;
                    Vector2 p = desired + radius * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    if (!Area.Contains(p) || NearestRoute(p) < routeClearance) continue;
                    if (existingTrees != null && existingTrees.Any(t => Vector2.Distance(t, p) < separation)) continue;
                    if (newTrees != null && newTrees.Any(t => Vector2.Distance(t, p) < separation)) continue;
                    return p;
                }
            throw new InvalidOperationException("No safe vegetation spot near " + desired);
        }

        private static float NearestRoute(Vector2 point)
        {
            float distance = float.PositiveInfinity;
            for (int i = 0; i < MovementCorridor.RouteCount; i++)
                distance = Mathf.Min(distance, MovementCorridor.MinDistanceToRoute(point, i));
            return distance;
        }

        private static List<Vector2> TerrainTreePositions(Terrain terrain)
        {
            TerrainData data = terrain.terrainData;
            return data.treeInstances.Select(tree => new Vector2(
                terrain.transform.position.x + tree.position.x * data.size.x,
                terrain.transform.position.z + tree.position.z * data.size.z)).ToList();
        }

        private static Bounds RenderBounds(GameObject gameObject)
        {
            Renderer[] renderers = gameObject.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("No renderer: " + gameObject.name);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static Material MakeMaterial(string name, Shader shader, string albedo,
            string alpha, string normal, float smoothness)
        {
            string path = Materials + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_MainTex", RequireTexture(albedo, false));
            if (material.HasProperty("_AlphaTex"))
                material.SetTexture("_AlphaTex", alpha == null ? Texture2D.whiteTexture : RequireTexture(alpha, false));
            if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", 0.34f);
            if (normal != null)
            {
                material.SetTexture("_BumpMap", RequireTexture(normal, true));
                material.EnableKeyword("_NORMALMAP");
            }
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D RequireTexture(string path, bool normal)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new FileNotFoundException("Missing vegetation texture", path);
            bool changed = false;
            if (normal && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                changed = true;
            }
            // The prototype is viewed from the north forest edge, tens of metres away.
            if (importer.maxTextureSize > 1024)
            {
                importer.maxTextureSize = 1024;
                changed = true;
            }
            if (changed) importer.SaveAndReimport();
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) throw new InvalidOperationException("Texture import failed: " + path);
            return texture;
        }

        private static void MakePrefab(string name, int number)
        {
            string path = Prepared + "/" + name + (name == "IslandTree02" ? "_LODs" : "") + ".fbx";
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) throw new FileNotFoundException("Prepared FBX missing", path);
            var root = new GameObject(name);
            GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(model);
            body.transform.SetParent(root.transform, false);
            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                Material[] assigned = renderer.sharedMaterials;
                for (int i = 0; i < assigned.Length; i++)
                {
                    string sourceName = assigned[i] != null ? assigned[i].name.ToLowerInvariant() : string.Empty;
                    assigned[i] = ResolveMaterial(name, number, renderer.gameObject.name.ToLowerInvariant(), sourceName, i);
                }
                renderer.sharedMaterials = assigned;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            if (name == "IslandTree02")
            {
                Renderer[] lodRenderers = new Renderer[3];
                foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
                    for (int i = 0; i < 3; i++)
                        if (renderer.gameObject.name.Contains("LOD" + i)) lodRenderers[i] = renderer;
                if (lodRenderers.Any(r => r == null)) throw new InvalidOperationException("Hero LOD renderer missing.");
                LODGroup group = root.AddComponent<LODGroup>();
                group.SetLODs(new[]
                {
                    new LOD(0.38f, new[] { lodRenderers[0] }),
                    new LOD(0.14f, new[] { lodRenderers[1] }),
                    new LOD(0.025f, new[] { lodRenderers[2] }),
                });
                group.RecalculateBounds();
            }
            PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/" + name + ".prefab");
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static Material ResolveMaterial(string prefab, int number, string rendererName, string importedName, int slot)
        {
            string chosen;
            if (prefab.StartsWith("FabTree_")) chosen = slot == 0 ? "Fab_Trunk" : "Fab_Leaf";
            else if (prefab.StartsWith("Palm_")) chosen = rendererName.Contains("bark") ? "Palm_Bark_" + number : "Palm_Leaf_" + number;
            else if (prefab == "IslandTree02")
                chosen = importedName.Contains("leaves") || (importedName.Length == 0 && slot == 1) ? "Island_Leaves" :
                    importedName.Contains("branches") || (importedName.Length == 0 && slot == 2) ? "Island_Branches" : "Island_Trunk";
            else if (prefab.StartsWith("Pachira_"))
                chosen = rendererName.Contains("leaves") ? "Pachira_Leaves" : "Pachira_Bark";
            else if (prefab == "BananaTree")
            {
                string[] ids = { "tiglajzdy", "sdbcdah", "slpmfhp", "sl0no0p", "pgfhf" };
                chosen = "Banana_" + ids.FirstOrDefault(importedName.Contains);
                if (chosen == "Banana_") chosen = "Banana_" + ids[Mathf.Min(slot, ids.Length - 1)];
            }
            else throw new InvalidOperationException("Unknown prefab " + prefab);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(Materials + "/" + chosen + ".mat");
            if (material == null) throw new InvalidOperationException("Missing material " + chosen);
            return material;
        }

        private static GameObject PrefabAt(string name)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/" + name + ".prefab");
            if (prefab == null) throw new InvalidOperationException("Missing vegetation prefab: " + name);
            return prefab;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
