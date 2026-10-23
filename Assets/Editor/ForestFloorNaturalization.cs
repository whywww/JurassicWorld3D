using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    // Final surface-only pass: absolute targets, never multiply previously baked densities.
    public static class ForestFloorNaturalization
    {
        private const string Output = "Tools/VegetationDiversity/ForestFloor";
        private const string Generated = "Assets/Environment/Terrain/Prepared/ForestFloor";
        private const string RootName = "Natural Forest Floor";
        private static bool IsGrass(DetailPrototype p) => p.noiseSeed == 733107 ||
            (p.prototypeTexture != null && p.prototypeTexture.name == "grass01");
        private static float Noise(float x, float z, float scale, float offset)
            => Mathf.PerlinNoise(x * scale + offset, z * scale + offset * .71f);
        private static float Patch(float x, float z)
        {
            float warp = (Noise(x, z, .067f, 31f) - .5f) * 7f;
            return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.35f, .68f,
                .65f * Noise(x + warp, z - warp, .19f, 12f) + .35f * Noise(x, z, .63f, 48f)));
        }
        private static float Random01(int x, int z)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + z * 668265263 + 733107);
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h ^ (h >> 16)) / (float)uint.MaxValue;
            }
        }
        private static Vector3 Position(Terrain terrain, int x, int z, int width, int height)
            => terrain.transform.TransformPoint(new Vector3((x + .5f) / width * terrain.terrainData.size.x,
                0, (z + .5f) / height * terrain.terrainData.size.z));

        internal static void Apply(Terrain terrain)
        {
            if (terrain == null || terrain.terrainData.alphamapLayers < 4)
                throw new InvalidOperationException("Forest floor requires Main's existing four ground layers.");
            TerrainData data = terrain.terrainData;
            float[,,] map = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            float[,] trails = GroundEcologyPass.BuildTrailMask(data.alphamapWidth, data.alphamapHeight);
            for (int z = 0; z < data.alphamapHeight; z++)
            for (int x = 0; x < data.alphamapWidth; x++)
            {
                Vector3 p = Position(terrain, x, z, data.alphamapWidth, data.alphamapHeight);
                float available = 1f;
                for (int i = 4; i < data.alphamapLayers; i++) available -= map[z, x, i];
                available = Mathf.Clamp01(available);
                // Preserve creek/bank and sample-specific extra layers exactly.
                float patch = Patch(p.x, p.z);
                float soilSpot = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.61f, .76f,
                    Noise(p.x, p.z, .27f, 74f)));
                float litter = .12f + .20f * (1f - patch) + .06f * Noise(p.x, p.z, .09f, 23f);
                float trail = trails[x, z];
                litter *= (1f - .75f * soilSpot) * (1f - .65f * trail);
                float soil = Mathf.Max(.06f + .52f * soilSpot, .32f * trail);
                float dry = .04f;
                map[z, x, 0] = available * (1f - litter - soil - dry);
                map[z, x, 1] = available * soil;
                map[z, x, 2] = available * litter;
                map[z, x, 3] = available * dry;
            }
            data.SetAlphamaps(0, 0, map);

            DetailPrototype[] prototypes = data.detailPrototypes;
            // Assignment can clear maps, so snapshot and restore every unrelated layer.
            int res = data.detailResolution;
            int fernLayer = Array.FindIndex(prototypes, p => p.noiseSeed == 733109);
            int existingCount = prototypes.Length;
            if (fernLayer < 0)
            {
                fernLayer = prototypes.Length;
                Array.Resize(ref prototypes, prototypes.Length + 1);
            }
            prototypes[fernLayer] = new DetailPrototype
            {
                prototype = EnsureLowFernPrefab(),
                usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit,
                minWidth = .45f, maxWidth = .85f, minHeight = .35f, maxHeight = .65f,
                healthyColor = new Color(.26f, .40f, .16f), dryColor = new Color(.36f, .40f, .21f),
                noiseSeed = 733109, noiseSpread = .53f, positionJitter = 1, density = 1
            };
            if (prototypes[fernLayer].prototype == null) throw new InvalidOperationException("Existing Fern02_A asset missing.");
            var layers = new int[prototypes.Length][,];
            for (int i = 0; i < existingCount; i++) layers[i] = data.GetDetailLayer(0, 0, res, res, i);
            layers[fernLayer] = new int[res, res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                Vector3 p = Position(terrain, x, z, res, res);
                // Compact companions around the grass-clump rims, rather than a blanket.
                float patch = Patch(p.x, p.z);
                float rim = 1f - Mathf.Clamp01(Mathf.Abs(patch - .5f) * 2.8f);
                bool dry = !WaterPlantCleanup.IsWet(p);
                layers[fernLayer][z, x] = dry && Random01(x + 913, z - 271) < .006f * rim ? 1 : 0;
            }
            data.detailPrototypes = prototypes;
            for (int i = 0; i < layers.Length; i++) data.SetDetailLayer(0, 0, i, layers[i]);
            ScatterLitter(terrain);
            EditorUtility.SetDirty(data);
        }

        private static GameObject EnsureLowFernPrefab()
        {
            JurassicWorldSceneBuilder.EnsureFolder(Generated);
            string path = Generated + "/LowFern.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) return prefab;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Environment/Vegetation/Fern02/Prepared/Prefabs/Fern02_A.prefab");
            if (source == null) throw new InvalidOperationException("Existing Fern02_A asset missing.");
            var obj = Object.Instantiate(source);
            try
            {
                obj.name = "LowFern";
                foreach (var collider in obj.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
                return PrefabUtility.SaveAsPrefabAsset(obj, path);
            }
            finally { Object.DestroyImmediate(obj); }
        }

        private static void ScatterLitter(Terrain terrain)
        {
            JurassicWorldSceneBuilder.EnsureFolder(Generated);
            var root = GameObject.Find(RootName);
            if (root == null) root = new GameObject(RootName);
            for (int kind = 0; kind < 2; kind++)
            {
                var vertices = new System.Collections.Generic.List<Vector3>();
                var triangles = new System.Collections.Generic.List<int>();
                var uv = new System.Collections.Generic.List<Vector2>();
                var random = new System.Random(733111 + kind);
                for (int i = 0; i < 8000; i++)
                {
                    float x = (float)random.NextDouble() * terrain.terrainData.size.x + terrain.transform.position.x;
                    float z = (float)random.NextDouble() * terrain.terrainData.size.z + terrain.transform.position.z;
                    if (WaterPlantCleanup.IsWet(new Vector3(x, 0, z))) continue;
                    float patch = Patch(x, z);
                    if (random.NextDouble() > (kind == 0 ? .8f - .5f * patch : .25f + .35f * patch)) continue;
                    float angle = (float)random.NextDouble() * Mathf.PI * 2;
                    float radius = kind == 0 ? .09f + (float)random.NextDouble() * .15f : .22f + (float)random.NextDouble() * .5f;
                    Vector3 a = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                    Vector3 b = new Vector3(-a.z, 0, a.x) * .75f;
                    int n = vertices.Count;
                    foreach (Vector3 offset in new[] { -a - b, a - b, a + b, -a + b })
                    {
                        Vector3 p = new Vector3(x, 0, z) + offset;
                        p.y = terrain.SampleHeight(p) + terrain.transform.position.y + .012f;
                        vertices.Add(p);
                    }
                    // Existing forest-floor atlas: upper left moss, other quadrants leaves.
                    int tile = kind == 1 ? 2 : new[] { 0, 1, 3 }[random.Next(3)];
                    Vector2 origin = new Vector2(tile % 2 * .5f, tile / 2 * .5f);
                    uv.Add(origin); uv.Add(origin + new Vector2(.5f, 0)); uv.Add(origin + new Vector2(.5f, .5f)); uv.Add(origin + new Vector2(0, .5f));
                    triangles.AddRange(new[] { n, n + 2, n + 1, n, n + 3, n + 2 });
                }
                string name = kind == 0 ? "Leaves" : "Moss";
                string path = Generated + "/" + name + ".asset";
                var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                bool createMesh = saved == null;
                if (createMesh) saved = new Mesh { name = "Forest floor " + name };
                else saved.Clear();
                saved.SetVertices(vertices); saved.SetUVs(0, uv); saved.SetTriangles(triangles, 0); saved.RecalculateNormals(); saved.RecalculateBounds();
                if (createMesh) AssetDatabase.CreateAsset(saved, path);
                EditorUtility.SetDirty(saved);
                var child = root.transform.Find(name);
                if (child == null)
                {
                    var obj = new GameObject(name); child = obj.transform; child.SetParent(root.transform, false);
                    obj.AddComponent<MeshFilter>(); obj.AddComponent<MeshRenderer>();
                }
                child.GetComponent<MeshFilter>().sharedMesh = saved;
                var renderer = child.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/Terrain/Prepared/GroundEcology/Natural/" + name + ".mat");
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        [MenuItem("Jurassic World/Environment/Ground/Naturalize Forest Floor")]
        public static void Rebuild() => JurassicMenuGuard.Run(BuildAndVerify, true);

        private static string Digest(TerrainData data)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                foreach (float value in data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight))
                    writer.Write(Mathf.RoundToInt(value * 255));
                for (int i = 0; i < data.detailPrototypes.Length; i++)
                {
                    writer.Write(JsonUtility.ToJson(data.detailPrototypes[i]));
                    foreach (int value in data.GetDetailLayer(0, 0, data.detailResolution, data.detailResolution, i)) writer.Write(value);
                }
                writer.Flush();
                using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(stream.ToArray()));
            }
        }
        private static string PreservedSurface(TerrainData data)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                var map = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
                for (int z = 0; z < data.alphamapHeight; z++)
                for (int x = 0; x < data.alphamapWidth; x++)
                for (int i = 4; i < data.alphamapLayers; i++) writer.Write(Mathf.RoundToInt(map[z, x, i] * 255));
                for (int i = 0; i < data.detailPrototypes.Length; i++)
                    if (!IsGrass(data.detailPrototypes[i]) && data.detailPrototypes[i].noiseSeed != 733109)
                        foreach (int v in data.GetDetailLayer(0, 0, data.detailResolution, data.detailResolution, i)) writer.Write(v);
                writer.Flush();
                using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(stream.ToArray()));
            }
        }
        private static string Structure(Terrain terrain)
        {
            string trees = JsonUtility.ToJson(new TreeSnapshot { trees = terrain.terrainData.treeInstances });
            return trees + string.Join("\n", Object.FindObjectsOfType<Transform>(true)
                .Where(t => !t.name.StartsWith("RouteGrass_", StringComparison.Ordinal) && t.root.name != RootName)
                .OrderBy(t => t.GetInstanceID()).Select(t => t.GetInstanceID() + EditorJsonUtility.ToJson(t)));
        }
        [Serializable] private sealed class TreeSnapshot { public TreeInstance[] trees; }

        public static void BuildAndVerify()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the scene and exit Play Mode before naturalizing the floor.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null) throw new InvalidOperationException("Main terrain missing.");
            Directory.CreateDirectory(Output + "/Checkpoint");
            foreach (string path in new[] { "Assets/Scenes/Main.unity", AssetDatabase.GetAssetPath(terrain.terrainData),
                "Assets/Environment/Terrain/Prepared/GroundEcology/Surface/RouteGrassTuft.mat" })
                if (!File.Exists(Output + "/Checkpoint/" + Path.GetFileName(path)))
                    File.Copy(path, Output + "/Checkpoint/" + Path.GetFileName(path));
            string structure = Structure(terrain);
            string preserved = PreservedSurface(terrain.terrainData);
            float[,] heights = terrain.terrainData.GetHeights(0, 0, terrain.terrainData.heightmapResolution, terrain.terrainData.heightmapResolution);
            if (!File.Exists(Output + "/before-0.png")) Capture(terrain, "before");
            long before = GrassCount(terrain.terrainData);
            Apply(terrain);
            string first = Digest(terrain.terrainData);
            Apply(terrain);
            if (first != Digest(terrain.terrainData)) throw new InvalidOperationException("Floor pass is not idempotent.");
            if (structure != Structure(terrain)) throw new InvalidOperationException("Unrelated scene layout changed.");
            if (preserved != PreservedSurface(terrain.terrainData)) throw new InvalidOperationException("Existing creek layers or unrelated vegetation maps changed.");
            if (!heights.Cast<float>().SequenceEqual(terrain.terrainData.GetHeights(0, 0, heights.GetLength(1), heights.GetLength(0)).Cast<float>()))
                throw new InvalidOperationException("Terrain heights changed.");
            long after = GrassCount(terrain.terrainData);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            terrain = Terrain.activeTerrain;
            if (first != Digest(terrain.terrainData)) throw new InvalidOperationException("Saved floor differs from generated data.");
            int links = VerifyNavigation();
            Capture(terrain, "after");
            File.WriteAllText(Output + "/Validation.md", $"# Forest floor validation\n\nPrevious saved grass density sum: {before} → {after}. Original screenshot baseline: 272871.\n\nTwo applications identical; saved/reloaded terrain identical. Existing tree instances, unrelated transforms and heights preserved. {links} route connections complete. Added one low fern detail layer and two combined litter/moss meshes; no colliders added.\n\nImages and synchronous camera render timings are recorded for three matching views; these timings are not Play-mode CPU/GPU frame performance.\n");
            Debug.Log("FOREST_FLOOR_PASS: " + before + " -> " + after + "; links=" + links);
        }
        private static long GrassCount(TerrainData data)
        {
            long count = 0;
            for (int i = 0; i < data.detailPrototypes.Length; i++)
                if (IsGrass(data.detailPrototypes[i]))
                    foreach (int v in data.GetDetailLayer(0, 0, data.detailResolution, data.detailResolution, i)) count += v;
            return count;
        }
        private static int VerifyNavigation()
        {
            var settings = Object.FindObjectOfType<DinosaurInteractionSettings>();
            if (settings == null || settings.navigationData == null) throw new InvalidOperationException("Navigation missing.");
            var instance = NavMesh.AddNavMeshData(settings.navigationData);
            try
            {
                var filter = new NavMeshQueryFilter { agentTypeID = settings.navigationAgentTypeId, areaMask = 1 };
                for (int r = 0; r < MovementCorridor.RouteCount; r++)
                {
                    var start = MovementCorridor.GetSample(r, 0).Position;
                    var end = MovementCorridor.GetSample(r, MovementCorridor.SampleCount(r) - 1).Position;
                    var path = new NavMeshPath();
                    if (!NavMesh.SamplePosition(new Vector3(start.x, 0, start.y), out var a, 12f, filter) ||
                        !NavMesh.SamplePosition(new Vector3(end.x, 0, end.y), out var b, 12f, filter) ||
                        !NavMesh.CalculatePath(a.position, b.position, filter, path) || path.status != NavMeshPathStatus.PathComplete)
                        throw new InvalidOperationException("Route disconnected: " + r);
                }
                return MovementCorridor.RouteCount;
            }
            finally { instance.Remove(); }
        }
        private static void Capture(Terrain terrain, string prefix)
        {
            var source = Camera.main;
            if (source == null) throw new InvalidOperationException("Main camera missing.");
            var holder = new GameObject("Floor verification camera");
            var camera = holder.AddComponent<Camera>(); camera.CopyFrom(source); camera.enabled = false;
            var target = new RenderTexture(1280, 720, 24);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var prior = RenderTexture.active;
            Vector3[] positions = { new Vector3(43.2f, 0, -15), new Vector3(0, 0, 11), new Vector3(45, 0, 65) };
            float[] yaw = { 284, 35, 210 };
            var timings = new System.Text.StringBuilder();
            try
            {
                target.Create(); camera.targetTexture = target;
                for (int i = 0; i < positions.Length; i++)
                {
                    var p = positions[i]; p.y = terrain.SampleHeight(p) + terrain.transform.position.y + 1.7f;
                    camera.transform.SetPositionAndRotation(p, Quaternion.Euler(10, yaw[i], 0));
                    camera.Render();
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    for (int frame = 0; frame < 5; frame++) camera.Render();
                    watch.Stop(); timings.AppendLine($"view {i}: {watch.Elapsed.TotalMilliseconds / 5:F2} ms Camera.Render");
                    RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
                    File.WriteAllBytes(Output + "/" + prefix + "-" + i + ".png", pixels.EncodeToPNG());
                }
                File.WriteAllText(Output + "/" + prefix + "-render.txt", timings.ToString());
            }
            finally { RenderTexture.active = prior; Object.DestroyImmediate(pixels); target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(holder); }
        }
        public static void BuildAndExit()
        {
            try { BuildAndVerify(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        // One-time rollback of the grass layer only. The ordinary floor pass no longer paints grass.
        public static void RestoreOriginalGrassAndExit()
        {
            const string temporary = Generated + "/GrassRestoreBaseline.asset";
            bool createdBaseline = false;
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                    throw new InvalidOperationException("Save the scene and exit Play before restoring grass.");
                if (File.Exists(temporary)) throw new InvalidOperationException("Temporary baseline already exists.");
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                Terrain terrain = Terrain.activeTerrain;
                TerrainData data = terrain.terrainData;
                string preserved = ExceptGrassDigest(data), structure = Structure(terrain);
                var settings = Object.FindObjectOfType<DinosaurInteractionSettings>();
                string navPath = AssetDatabase.GetAssetPath(settings.navigationData);
                byte[] navigation = File.ReadAllBytes(navPath);
                File.Copy(Output + "/Checkpoint/MainInteractionTerrain.asset", temporary);
                createdBaseline = true;
                AssetDatabase.ImportAsset(temporary, ImportAssetOptions.ForceSynchronousImport);
                var baseline = AssetDatabase.LoadAssetAtPath<TerrainData>(temporary);
                int[] sources = Enumerable.Range(0, baseline.detailPrototypes.Length).Where(i => IsGrass(baseline.detailPrototypes[i])).ToArray();
                int[] targets = Enumerable.Range(0, data.detailPrototypes.Length).Where(i => IsGrass(data.detailPrototypes[i])).ToArray();
                if (sources.Length == 0 || !sources.SequenceEqual(targets) || baseline.detailResolution != data.detailResolution)
                    throw new InvalidOperationException("Grass layers or resolutions do not match the original snapshot.");
                int res = data.detailResolution;
                var originals = sources.ToDictionary(i => i, i => baseline.GetDetailLayer(0, 0, res, res, i));
                long originalSum = originals.Values.Sum(map => map.Cast<int>().Sum(v => (long)v));
                if (originalSum != 272871) throw new InvalidOperationException("Unexpected original grass snapshot: " + originalSum);
                long before = GrassCount(data);
                var prototypes = data.detailPrototypes;
                var layers = new int[prototypes.Length][,];
                for (int i = 0; i < layers.Length; i++) layers[i] = data.GetDetailLayer(0, 0, res, res, i);
                foreach (int index in sources)
                {
                    layers[index] = originals[index];
                    prototypes[index] = baseline.detailPrototypes[index];
                }
                data.detailPrototypes = prototypes;
                if (data.detailResolution != res) throw new InvalidOperationException("Detail resolution changed.");
                for (int i = 0; i < layers.Length; i++) data.SetDetailLayer(0, 0, i, layers[i]);
                if (preserved != ExceptGrassDigest(data) || structure != Structure(terrain))
                    throw new InvalidOperationException("Grass restoration changed unrelated data.");
                EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                terrain = Terrain.activeTerrain; data = terrain.terrainData;
                bool allMatch = sources.All(index => data.GetDetailLayer(0, 0, res, res, index).Cast<int>().SequenceEqual(originals[index].Cast<int>()));
                if (!allMatch || GrassCount(data) != originalSum)
                    throw new InvalidOperationException("Saved grass differs from the original snapshot.");
                if (preserved != ExceptGrassDigest(data) || !navigation.SequenceEqual(File.ReadAllBytes(navPath)))
                    throw new InvalidOperationException("Saved non-grass data or navigation changed.");
                int links = VerifyNavigation();
                Capture(terrain, "after");
                File.WriteAllText(Output + "/GrassRestore.md", $"# 原始草层恢复\n\n草密度总量：{before} → {originalSum}。保存并重新加载后，{sources.Length} 个草图层的全部 {res * res * sources.Length} 个单元与首次修改前快照逐格一致；两个图层分别恢复自己的原始分布，草原型同时恢复为快照中的贴片草。\n\n地表所有混合图层、全部其他植被密度与原型、树实例和地形高度的摘要保持一致，原场景对象布局未改动；导航资产逐字节未变，{links} 条路线连通。\n\n新增地表步骤已移除草层重绘和聚簇规则，后续烘焙沿用原有草层生成逻辑。土壤、落叶和苔藓调整保留。\n");
                Debug.Log("ORIGINAL_GRASS_RESTORED: " + before + " -> " + originalSum + "; every cell matches; links=" + links);
                AssetDatabase.DeleteAsset(temporary);
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (createdBaseline && File.Exists(temporary)) AssetDatabase.DeleteAsset(temporary);
                EditorApplication.Exit(1);
            }
        }

        private static string ExceptGrassDigest(TerrainData data)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                foreach (float v in data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight)) writer.Write(v);
                foreach (float v in data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution)) writer.Write(v);
                writer.Write(JsonUtility.ToJson(new TreeSnapshot { trees = data.treeInstances }));
                for (int i = 0; i < data.detailPrototypes.Length; i++)
                {
                    if (IsGrass(data.detailPrototypes[i])) continue;
                    writer.Write(JsonUtility.ToJson(data.detailPrototypes[i]));
                    foreach (int v in data.GetDetailLayer(0, 0, data.detailResolution, data.detailResolution, i)) writer.Write(v);
                }
                writer.Flush();
                using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(stream.ToArray()));
            }
        }
    }
}
