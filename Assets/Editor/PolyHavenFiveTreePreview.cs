using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace JurassicWorld.Editor
{
    /// <summary>Five authored comparison trees; deliberately independent of Bake and scene-wide seeding.</summary>
    public static class PolyHavenFiveTreePreview
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string RootName = "Poly Haven Five Tree Preview";
        private const string Source = "Assets/ThirdParty/VegetationDiversity/PolyHaven/";
        private const string Prepared = "Assets/ThirdParty/VegetationDiversity/Prepared/";
        private const string Prefabs = "Assets/VegetationDiversity/Prefabs/";
        private const string Materials = "Assets/VegetationDiversity/Materials/";
        private const string Output = "Tools/VegetationDiversity/FiveTreePreview";
        private static readonly Rect Area = Rect.MinMaxRect(-20f, -10f, 20f, 30f);

        private sealed class TreeSpec
        {
            internal string Id, Name, Label;
            internal Vector2 Desired;
            internal float Radius, Yaw, Scale;
            internal TreeSpec(string id, string name, string label, float x, float z, float radius, float yaw, float scale = 1f)
            { Id = id; Name = name; Label = label; Desired = new Vector2(x, z); Radius = radius; Yaw = yaw; Scale = scale; }
        }

        private static readonly TreeSpec[] Trees =
        {
            new TreeSpec("tree_small_02", "TreeSmall02", "Tree Small 02", -14f, -2f, .45f, 25f),
            new TreeSpec("island_tree_03", "IslandTree03", "Island Tree 03", -2f, -4f, .7f, 145f),
            new TreeSpec("island_tree_01", "IslandTree01", "Island Tree 01", 13f, 2f, .7f, 30f),
            new TreeSpec("jacaranda_tree", "JacarandaTree", "Jacaranda Tree", -10f, 21f, .8f, 15f, .65f),
            new TreeSpec("fir_tree_01", "FirTree01", "Fir Tree 01", 14f, 24f, .65f, 70f),
        };

        [MenuItem("Jurassic World/Vegetation Diversity/Five Tree Preview/Prepare Assets")]
        public static void PrepareAssets()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (TreeSpec spec in Trees)
            {
                string folder = Source + spec.Name + "/textures/";
                foreach (string file in Directory.GetFiles(folder))
                {
                    if (file.EndsWith(".meta") || !file.Contains("_nor_gl_")) continue;
                    string path = file.Replace('\\', '/');
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    if (importer.textureType != TextureImporterType.NormalMap || importer.maxTextureSize != 1024)
                    {
                        importer.textureType = TextureImporterType.NormalMap;
                        importer.maxTextureSize = 1024;
                        importer.SaveAndReimport();
                    }
                }
                string modelPath = Prepared + spec.Name + "_LODs.fbx";
                var modelImporter = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                if (modelImporter == null) throw new FileNotFoundException("Prepared model missing", modelPath);
                if (!modelImporter.isReadable || modelImporter.importCameras || modelImporter.importLights)
                {
                    modelImporter.isReadable = true;
                    modelImporter.importCameras = false;
                    modelImporter.importLights = false;
                    modelImporter.SaveAndReimport();
                }
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                var root = new GameObject(spec.Name);
                try
                {
                    var body = (GameObject)PrefabUtility.InstantiatePrefab(model);
                    body.transform.SetParent(root.transform, false);
                    foreach (LODGroup group in body.GetComponentsInChildren<LODGroup>(true)) UnityEngine.Object.DestroyImmediate(group);
                    foreach (Collider collider in body.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                    foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.sharedMaterials = renderer.sharedMaterials.Select(m => MakeMaterial(spec, m.name)).ToArray();
                        renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                        renderer.receiveShadows = true;
                    }
                    var lods = new List<LOD>();
                    float[] thresholds = { .35f, .12f, .015f };
                    for (int level = 0; level < 3; level++)
                    {
                        Renderer[] renderers = body.GetComponentsInChildren<Renderer>(true)
                            .Where(r => r.name.EndsWith("_LOD" + level, StringComparison.Ordinal)).ToArray();
                        if (renderers.Length == 0) throw new InvalidOperationException("Missing LOD " + spec.Name + " " + level);
                        lods.Add(new LOD(thresholds[level], renderers));
                    }
                    var lodGroup = root.AddComponent<LODGroup>();
                    lodGroup.SetLODs(lods.ToArray());
                    lodGroup.RecalculateBounds();
                    PrefabUtility.SaveAsPrefabAsset(root, Prefabs + spec.Name + ".prefab");
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
                Debug.Log("FIVE_TREE_ASSETS_READY: 5 native-scale prefabs, three LODs each.");
        }

        private static Material MakeMaterial(TreeSpec spec, string importedName)
        {
            string stem = importedName.Replace(" (Instance)", "");
            if (spec.Name == "TreeSmall02")
                stem = stem.Replace("_branches", "_branch").Replace("_trunk", "");
            string folder = Source + spec.Name + "/textures/";
            bool foliage = stem.Contains("leaves") || stem.Contains("branch") || stem.Contains("twig");
            string albedo = TexturePath(folder, stem, "diff");
            if (albedo == null && spec.Name == "FirTree01")
            {
                // The official FBX shares the bark atlas with trunk and dead-branch materials.
                stem = spec.Id + "_bark";
                albedo = TexturePath(folder, stem, "diff");
                foliage = false;
            }
            if (albedo == null) throw new FileNotFoundException("No albedo for " + importedName);
            string path = Materials + "Preview_" + importedName + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(foliage ? "Jurassic World/Vegetation Prototype Cutout" : "Standard");
            if (shader == null) throw new InvalidOperationException("Vegetation shader missing.");
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            // The existing route checker recognizes solid wood by material name.
            material.name = "Preview_" + importedName + (foliage ? "_Foliage" : "_Trunk");
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(albedo));
            string alpha = TexturePath(folder, stem, "alpha");
            if (foliage)
            {
                material.SetTexture("_AlphaTex", alpha == null ? Texture2D.whiteTexture : AssetDatabase.LoadAssetAtPath<Texture2D>(alpha));
                material.SetFloat("_Cutoff", .34f);
                material.SetFloat("_Smoothness", .1f);
            }
            else material.SetFloat("_Glossiness", .12f);
            string normal = TexturePath(folder, stem, "nor_gl");
            if (normal != null)
            {
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
                material.EnableKeyword("_NORMALMAP");
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static string TexturePath(string folder, string stem, string channel)
        {
            return Directory.GetFiles(folder, stem + "_" + channel + "_1k.*")
                .Where(p => !p.EndsWith(".meta")).Select(p => p.Replace('\\', '/')).FirstOrDefault();
        }

        [MenuItem("Jurassic World/Vegetation Diversity/Five Tree Preview/Build and Capture")]
        public static void BuildAndCapture()
        {
            Build();
            Capture();
        }

        [MenuItem("Jurassic World/Vegetation Diversity/Five Tree Preview/Build Central Preview")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            PrepareAssets();
            var scene = EditorSceneManager.OpenScene(ScenePath);
            Directory.CreateDirectory(Output);
            string checkpoint = "Tools/VegetationDiversity/Checkpoints/Main_BeforeFiveTreePreview.unity";
            if (!File.Exists(checkpoint)) File.Copy(ScenePath, checkpoint);
            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null) throw new InvalidOperationException("Main terrain missing.");
            GameObject old = GameObject.Find(RootName);
            bool oldActive = old != null && old.activeSelf;
            if (old != null) old.SetActive(false);
            var root = new GameObject(RootName);
            try
            {
                var instances = new List<GameObject>();
                foreach (TreeSpec spec in Trees)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + spec.Name + ".prefab");
                    var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                    tree.name = spec.Label;
                    tree.transform.localScale = Vector3.one * spec.Scale;
                    tree.transform.rotation = Quaternion.Euler(0f, spec.Yaw, 0f);
                    tree.AddComponent<SceneTreeObstacle>().SetRadius(spec.Radius);
                    Ground(tree, terrain, Vector2.zero);
                    instances.Add(tree);
                }
                var routeSafe = RouteObstacleClearance.CreatePlacementProbe(terrain, root.transform);
                var existing = UnityEngine.Object.FindObjectsOfType<SceneTreeObstacle>()
                    .Where(t => !t.transform.IsChildOf(root.transform)).ToArray();
                var placed = new List<GameObject>();
                foreach (int index in Enumerable.Range(0, Trees.Length).OrderByDescending(i => BoundsOf(instances[i]).size.x))
                {
                    GameObject tree = instances[index];
                    TreeSpec spec = Trees[index];
                    bool found = false;
                    int tested = 0, routeCount = 0, oldTreeCount = 0, rockCount = 0, crownCount = 0;
                    foreach (Vector2 candidate in Candidates(spec.Desired))
                    {
                        tested++;
                        if (!routeSafe(tree.transform, candidate) || MainCreekIntegration.NearCreek(candidate.x, candidate.y, 5f)) continue;
                        routeCount++;
                        if (existing.Any(t => Vector2.Distance(candidate, XZ(t.transform.position)) < spec.Radius + t.Radius + 2f)) continue;
                        if (TerrainTreeNear(terrain, candidate, spec.Radius + 2.5f)) continue;
                        oldTreeCount++;
                        if (RockNear(candidate, spec.Radius + 1f)) continue;
                        rockCount++;
                        Ground(tree, terrain, candidate);
                        Bounds bounds = BoundsOf(tree);
                        // Keep crowns separated in plan view, allowing a modest natural overlap.
                        if (placed.Any(other => CrownOverlap(bounds, BoundsOf(other)))) continue;
                        crownCount++;
                        placed.Add(tree); found = true; break;
                    }
                    Debug.Log($"FIVE_TREE_CANDIDATES {spec.Label}: tested={tested}, routeSafe={routeCount}, treesSafe={oldTreeCount}, rocksSafe={rockCount}, crownsSafe={crownCount}");
                    if (!found) throw new InvalidOperationException("No central clearing position for " + spec.Label);
                    Debug.Log($"FIVE_TREE_PLACED {spec.Label}: {tree.transform.position}, height={BoundsOf(tree).size.y:F2}m");
                }
                TerrainTreeObstacleRegistry.Invalidate();
                ValidateRoot(root, terrain);
                if (old != null) UnityEngine.Object.DestroyImmediate(old);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Selection.activeGameObject = root;
                SceneView.lastActiveSceneView?.FrameSelected();
                Debug.Log("FIVE_TREE_PREVIEW_BUILT: only five comparison trees added to Main.");
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(root);
                if (old != null) old.SetActive(oldActive);
                TerrainTreeObstacleRegistry.Invalidate();
                throw;
            }
        }

        private static IEnumerable<Vector2> Candidates(Vector2 desired)
        {
            var values = new List<Vector2>();
            for (float x = Area.xMin + 1f; x < Area.xMax; x += 1f)
                for (float z = Area.yMin + 1f; z < Area.yMax; z += 1f) values.Add(new Vector2(x, z));
            return values.OrderBy(p => (p - desired).sqrMagnitude);
        }

        private static bool CrownOverlap(Bounds a, Bounds b)
        {
            return Mathf.Abs(a.center.x - b.center.x) < (a.extents.x + b.extents.x) * .75f + 1f
                && Mathf.Abs(a.center.z - b.center.z) < (a.extents.z + b.extents.z) * .75f + 1f;
        }

        private static bool TerrainTreeNear(Terrain terrain, Vector2 point, float radius)
        {
            foreach (TreeInstance tree in terrain.terrainData.treeInstances)
            {
                if (tree.widthScale <= 0f) continue;
                Vector3 position = terrain.transform.TransformPoint(Vector3.Scale(tree.position, terrain.terrainData.size));
                if (Vector2.Distance(XZ(position), point) < radius + .7f * tree.widthScale) return true;
            }
            return false;
        }

        private static bool RockNear(Vector2 point, float radius)
        {
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (renderer.name.IndexOf("rock", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Bounds b = renderer.bounds;
                if (point.x >= b.min.x - radius && point.x <= b.max.x + radius
                    && point.y >= b.min.z - radius && point.y <= b.max.z + radius) return true;
            }
            return false;
        }

        private static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
        private static void Ground(GameObject tree, Terrain terrain, Vector2 point)
        {
            tree.transform.position = new Vector3(point.x, 0f, point.y);
            float ground = terrain.SampleHeight(tree.transform.position) + terrain.transform.position.y;
            tree.transform.position += Vector3.up * (ground - BoundsOf(tree).min.y - .02f);
        }

        private static Bounds BoundsOf(GameObject tree)
        {
            Renderer[] renderers = tree.GetComponent<LODGroup>().GetLODs()[0].renderers;
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static Bounds RendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static List<Bounds> ViewObstacles(GameObject preview, GameObject subject)
        {
            var obstacles = UnityEngine.Object.FindObjectsOfType<SceneTreeObstacle>()
                .Where(t => subject != null ? t.gameObject != subject : !t.transform.IsChildOf(preview.transform))
                .Select(t => RendererBounds(t.gameObject)).ToList();
            Terrain terrain = Terrain.activeTerrain;
            foreach (TreeInstance tree in terrain.terrainData.treeInstances)
            {
                if (tree.widthScale <= 0f) continue;
                GameObject prefab = terrain.terrainData.treePrototypes[tree.prototypeIndex].prefab;
                Bounds b = RendererBounds(prefab);
                Vector3 position = terrain.transform.TransformPoint(Vector3.Scale(tree.position, terrain.terrainData.size));
                Matrix4x4 matrix = Matrix4x4.TRS(position, Quaternion.Euler(0f, tree.rotation * Mathf.Rad2Deg, 0f),
                    new Vector3(tree.widthScale, tree.heightScale, tree.widthScale));
                Bounds world = new Bounds(matrix.MultiplyPoint3x4(b.center), Vector3.zero);
                for (int corner = 0; corner < 8; corner++)
                    world.Encapsulate(matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f))));
                obstacles.Add(world);
            }
            foreach (DinosaurAgent dinosaur in UnityEngine.Object.FindObjectsOfType<DinosaurAgent>())
                obstacles.Add(RendererBounds(dinosaur.gameObject));
            return obstacles;
        }

        private static Vector3 ClearView(GameObject preview, GameObject subject, Bounds bounds, float distance, bool eyeLevel)
        {
            List<Bounds> obstacles = ViewObstacles(preview, subject);
            GameObject[] subjects = subject != null ? new[] { subject } : Trees.Select(t => preview.transform.Find(t.Label).gameObject).ToArray();
            var points = new List<Vector3>();
            foreach (GameObject tree in subjects)
            {
                Bounds b = BoundsOf(tree);
                points.Add(b.center);
                points.Add(new Vector3(b.center.x, b.min.y + b.size.y * .15f, b.center.z));
                points.Add(new Vector3(b.center.x, b.max.y - b.size.y * .15f, b.center.z));
            }
            Vector3 best = Vector3.zero;
            float bestScore = float.PositiveInfinity;
            for (int step = 0; step < 48; step++)
            {
                float angle = step * Mathf.PI * 2f / 48f;
                Vector3 eye = bounds.center + new Vector3(Mathf.Sin(angle), 0f, -Mathf.Cos(angle)) * distance;
                eye.y = eyeLevel ? Terrain.activeTerrain.SampleHeight(eye) + Terrain.activeTerrain.transform.position.y + 1.7f
                    : bounds.center.y + distance * .48f;
                float score = step * .002f;
                foreach (Vector3 point in points)
                {
                    Vector3 direction = point - eye;
                    var ray = new Ray(eye, direction.normalized);
                    foreach (Bounds obstacle in obstacles)
                        if (obstacle.IntersectRay(ray, out float hit) && hit < direction.magnitude - .5f) score += 1f;
                }
                if (score < bestScore) { bestScore = score; best = eye; }
            }
            return best;
        }

        [MenuItem("Jurassic World/Vegetation Diversity/Five Tree Preview/Validate")]
        public static void Validate()
        {
            var root = GameObject.Find(RootName);
            if (root == null) throw new InvalidOperationException("Five tree preview missing.");
            ValidateRoot(root, Terrain.activeTerrain);
        }

        private static void ValidateRoot(GameObject root, Terrain terrain)
        {
            if (root.transform.childCount != 5) throw new InvalidOperationException("Expected exactly five trees.");
            var routeSafe = RouteObstacleClearance.CreatePlacementProbe(terrain, root.transform);
            var lines = new List<string> { "# Five Tree Preview", "", "Five CC0 trees, central clearing only. Distribution requires visual approval. Prefabs retain native scale; Jacaranda's scene instance uses 65% scale to fit the clearing, while the other four use 100%.", "",
                "| Tree | X | Z | Height (m) | Scene scale | LOD triangles |", "|---|---:|---:|---:|---:|---|" };
            foreach (TreeSpec spec in Trees)
            {
                Transform tree = root.transform.Find(spec.Label);
                if (tree == null || !Area.Contains(XZ(tree.position))) throw new InvalidOperationException("Preview tree outside central area.");
                if (!routeSafe(tree, XZ(tree.position))) throw new InvalidOperationException("Dinosaur model envelope intersects " + spec.Label);
                if (MainCreekIntegration.NearCreek(tree.position.x, tree.position.z, 5f)) throw new InvalidOperationException("Tree near creek.");
                float ground = terrain.SampleHeight(tree.position) + terrain.transform.position.y;
                Bounds bounds = BoundsOf(tree.gameObject);
                if (Mathf.Abs(bounds.min.y - ground + .02f) > .05f) throw new InvalidOperationException("Tree not grounded: " + spec.Label);
                LOD[] lods = tree.GetComponent<LODGroup>().GetLODs();
                if (lods.Length != 3 || lods.Any(l => l.renderers.Length == 0)) throw new InvalidOperationException("Missing LODs.");
                var counts = new List<long>();
                foreach (LOD lod in lods)
                {
                    long triangles = 0;
                    long trunkTriangles = 0;
                    foreach (Renderer renderer in lod.renderers)
                    {
                        if (renderer.sharedMaterials.Any(m => m == null || m.mainTexture == null || m.shader.name.Contains("Error")))
                            throw new InvalidOperationException("Missing material/texture: " + spec.Label);
                        Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                        for (int i = 0; i < mesh.subMeshCount; i++)
                        {
                            long count = mesh.GetIndexCount(i) / 3;
                            triangles += count;
                            if (renderer.sharedMaterials[i].shader.name == "Standard") trunkTriangles += count;
                        }
                    }
                    if (trunkTriangles == 0) throw new InvalidOperationException("Trunk disappeared from LOD: " + spec.Label);
                    counts.Add(triangles);
                }
                if (counts[0] > 185000 || counts[1] >= counts[0] || counts[2] >= counts[1]) throw new InvalidOperationException("LOD budget failed.");
                lines.Add($"| {spec.Label} | {tree.position.x:F1} | {tree.position.z:F1} | {bounds.size.y:F2} | {spec.Scale:F2} | {string.Join(" / ", counts)} |");
            }
            lines.Add("\nValidation: three LODs, non-null textures, grounding, creek exclusion, and directional dinosaur body clearance passed.");
            Directory.CreateDirectory(Output);
            File.WriteAllLines(Output + "/README.md", lines);
            Debug.Log("FIVE_TREE_PREVIEW_VALIDATED: five trees; materials, ground, LOD budgets and all six dinosaur routes passed.");
        }

        [MenuItem("Jurassic World/Vegetation Diversity/Five Tree Preview/Capture Views")]
        public static void Capture()
        {
            Validate();
            GameObject root = GameObject.Find(RootName);
            Bounds all = BoundsOf(root.transform.GetChild(0).gameObject);
            foreach (Transform child in root.transform) all.Encapsulate(BoundsOf(child.gameObject));
            var holder = new GameObject("Five tree capture camera") { hideFlags = HideFlags.HideAndDontSave };
            Camera camera = holder.AddComponent<Camera>();
            if (Camera.main != null) camera.CopyFrom(Camera.main);
            camera.enabled = false;
            camera.farClipPlane = 250f;
            camera.fieldOfView = 60f;
            camera.cullingMask = ~0;
            camera.aspect = 16f / 9f;
            var target = new RenderTexture(1920, 1080, 24);
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            RenderTexture active = RenderTexture.active;
            var markers = UnityEngine.Object.FindObjectsOfType<MovementCorridorDebug>();
            bool[] visibility = markers.Select(m => m.ShowMarkers).ToArray();
            foreach (var marker in markers) marker.SetShowMarkers(false);
            try
            {
                camera.targetTexture = target;
                Vector3 focus = all.center;
                float distance = Mathf.Max(all.size.x / camera.aspect, all.size.y) * 1.25f + all.size.z * .6f;
                Vector3 overview = ClearView(root, null, all, distance, false);
                Shot(camera, texture, target, "01-overview", overview, focus);
                Vector3 eye = ClearView(root, null, all, distance, true);
                camera.fieldOfView = 72f;
                Shot(camera, texture, target, "02-eye-level", eye, focus);
                camera.fieldOfView = 55f;
                foreach (Transform child in root.transform)
                {
                    Bounds bounds = BoundsOf(child.gameObject);
                    float spacing = Mathf.Max(bounds.size.y, bounds.size.x / camera.aspect) * 1.15f + bounds.extents.z;
                    Shot(camera, texture, target, "03-" + child.name.Replace(' ', '-'),
                        ClearView(root, child.gameObject, bounds, spacing, false), bounds.center);
                }
                // Explicitly render each forced LOD at the same viewpoint for visual inspection.
                for (int level = 0; level < 3; level++)
                {
                    foreach (Transform child in root.transform) child.GetComponent<LODGroup>().ForceLOD(level);
                    Shot(camera, texture, target, "04-LOD" + level, overview, focus);
                }
                Debug.Log("FIVE_TREE_CAPTURED: " + Path.GetFullPath(Output));
            }
            finally
            {
                foreach (Transform child in root.transform) child.GetComponent<LODGroup>().ForceLOD(-1);
                for (int i = 0; i < markers.Length; i++) markers[i].SetShowMarkers(visibility[i]);
                RenderTexture.active = active;
                camera.targetTexture = null;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(holder);
            }
        }

        private static void Shot(Camera camera, Texture2D texture, RenderTexture target, string name, Vector3 eye, Vector3 focus)
        {
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", texture.EncodeToPNG());
        }
    }
}
