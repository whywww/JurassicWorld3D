using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace JurassicWorld.Editor
{
    public static class LocalForestRepair
    {
        internal static readonly Vector2 Center = new Vector2(12.5f, -37.7f);
        internal const string RootName = "Southeast Local Forest Repair";
        private const string BaselinePath = "Assets/Terrain/GroundEcology/LocalForestRepairBaseline.asset";
        private const string EnabledPath = "Assets/Editor/LocalForestRepairEnabled.json";
        private const string Output = "Tools/VegetationDiversity/LocalForestRepair";
        internal static TerrainData Baseline => AssetDatabase.LoadAssetAtPath<TerrainData>(BaselinePath);

        internal static float Weight(Vector2 p)
        {
            float t = Mathf.Clamp01((35f - Vector2.Distance(p, Center)) / 6f);
            return t * t * (3f - 2f * t);
        }

        [MenuItem("Jurassic World/Environment/Southeast Forest/Build and Verify")]
        private static void BuildAndVerifyMenu() => JurassicMenuGuard.Run(BuildAndVerify, true);

        [MenuItem("Jurassic World/Environment/Southeast Forest/Build and Verify", true)]
        private static bool BuildAndVerifyMenuEnabled() => JurassicMenuGuard.CanRun;

        public static void BuildAndVerify()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the scene and exit Play Mode before repairing forest.");
            EditorSceneManager.OpenScene(CentralForestDensification.ScenePath);
            Terrain terrain = Terrain.activeTerrain;
            Directory.CreateDirectory(Output);
            const string checkpoint = "Tools/VegetationDiversity/Checkpoints/Main_BeforeLocalForestRepair.unity";
            if (!File.Exists(checkpoint)) File.Copy(CentralForestDensification.ScenePath, checkpoint);
            if (Baseline == null && !AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(terrain.terrainData), BaselinePath))
                throw new InvalidOperationException("Could not save terrain checkpoint.");
            var original = new Snapshot(terrain);
            Capture(terrain, "before");
            Diagnose(terrain);
            Apply(terrain);
            Validate(terrain);
            original.CheckUnchanged(terrain);
            var first = new Snapshot(terrain);
            string layout = Layout();
            Apply(terrain);
            first.CheckExact(terrain);
            if (layout != Layout()) throw new InvalidOperationException("Repeated repair changed planting.");
            // Exercise the exact post-Bake hook in memory, removing only this owned group.
            File.WriteAllText(EnabledPath, "{\"enabled\":true}\n");
            AssetDatabase.ImportAsset(EnabledPath);
            UnityEngine.Object.DestroyImmediate(GameObject.Find(RootName));
            SeedConfigured();
            first.CheckExact(terrain);
            if (layout != Layout()) throw new InvalidOperationException("Bake hook changed local planting.");
            original.CheckUnchanged(terrain);
            Capture(terrain, "after");
            EditorUtility.SetDirty(terrain.terrainData);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            File.WriteAllText(Output + "/Validation.md", "# 局部森林修复\n\n中心 (12.5,-37.7)，半径35m，边缘6m渐变。\n\n" +
                "区域外原有草层、全部非自有草层、地形高度、Terrain树数组、溪流图层及原有对象姿态验证通过。\n\n" +
                "重复生成两次及移除自有节点后通过Bake恢复钩子重建：草、纹理和植物布局一致。\n\n" +
                "四向眼高图及同机位编辑态CPU渲染提交耗时见before/after文件；该计时不是Play帧率。\n");
            Debug.Log("LOCAL_FOREST_REPAIR_OK: saved, outside region preserved, repeat + Bake hook identical.");
        }

        internal static void SeedConfigured()
        {
            if (File.Exists(EnabledPath))
            {
                if (Baseline == null) throw new InvalidOperationException("Local forest baseline missing.");
                Apply(Terrain.activeTerrain);
                Validate(Terrain.activeTerrain);
            }
        }

        private static void Apply(Terrain terrain)
        {
            GameObject old = GameObject.Find(RootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            TerrainTreeObstacleRegistry.Invalidate();
            PaintGrass(terrain);
            GroundEcologyPass.RepaintLocalRegion(terrain);
            var root = new GameObject(RootName);
            try { CentralForestDensification.PlantLocalRepair(root.transform); }
            catch { UnityEngine.Object.DestroyImmediate(root); throw; }
        }

        private static int GrassIndex(TerrainData data)
        {
            Texture2D texture = NatureStarterKitLocator.FindGroundTexture("grass01");
            for (int i = 0; i < data.detailPrototypes.Length; i++)
                if (data.detailPrototypes[i].prototypeTexture == texture) return i;
            throw new InvalidOperationException("Owned grass01 detail layer missing.");
        }

        private static void PaintGrass(Terrain terrain)
        {
            TerrainData data = terrain.terrainData;
            int index = GrassIndex(data), res = data.detailResolution;
            if (res != Baseline.detailResolution) throw new InvalidOperationException("Grass checkpoint resolution mismatch.");
            DetailPrototype[] prototypes = data.detailPrototypes;
            int local = Array.FindIndex(prototypes, p => p.prototypeTexture == prototypes[index].prototypeTexture &&
                Mathf.Approximately(p.minHeight, .15f) && Mathf.Approximately(p.maxHeight, .45f));
            if (local < 0)
            {
                var layers = Enumerable.Range(0, prototypes.Length).Select(i => data.GetDetailLayer(0, 0, res, res, i)).ToArray();
                var extended = prototypes.ToList();
                var grass = new DetailPrototype(prototypes[index]) { minHeight = .15f, maxHeight = .45f };
                extended.Add(grass); local = extended.Count - 1;
                data.detailPrototypes = extended.ToArray();
                for (int i = 0; i < layers.Length; i++) data.SetDetailLayer(0, 0, i, layers[i]);
            }
            int[,] previous = Baseline.GetDetailLayer(0, 0, res, res, GrassIndex(Baseline));
            int[,] original = data.GetDetailLayer(0, 0, res, res, index);
            var low = new int[res, res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                Vector2 p = DetailPoint(terrain, x, z);
                float weight = Weight(p);
                if (weight <= 0f) continue;
                original[z, x] = Mathf.RoundToInt(previous[z, x] * (1f - weight));
                if (MainCreekIntegration.NearCreek(p.x, p.y, 1.5f)) continue;
                // Fresh absolute density: old empty route cells are not inputs to this field.
                float noise = Mathf.PerlinNoise(p.x * .085f + 47f, p.y * .085f + 91f);
                float value = Mathf.Lerp(2f, 4f, noise) * weight;
                low[z, x] = Mathf.RoundToInt(value);
            }
            data.SetDetailLayer(0, 0, index, original);
            data.SetDetailLayer(0, 0, local, low);
        }

        private static Vector2 DetailPoint(Terrain terrain, int x, int z)
        {
            TerrainData data = terrain.terrainData;
            Vector3 p = terrain.transform.TransformPoint(new Vector3((x + .5f) / data.detailResolution * data.size.x,
                0f, (z + .5f) / data.detailResolution * data.size.z));
            return new Vector2(p.x, p.z);
        }

        private static void Diagnose(Terrain terrain)
        {
            TerrainData data = terrain.terrainData;
            int[,] grass = data.GetDetailLayer(0, 0, data.detailResolution, data.detailResolution, GrassIndex(data));
            int near = 0, away = 0;
            for (int z = 0; z < data.detailResolution; z++)
            for (int x = 0; x < data.detailResolution; x++)
            {
                Vector2 p = DetailPoint(terrain, x, z);
                if (Weight(p) <= 0f || grass[z, x] != 0) continue;
                if (Enumerable.Range(0, MovementCorridor.RouteCount).Min(r => MovementCorridor.MinDistanceToRoute(p, r)) < 4.7f) near++;
                else away++;
            }
            Debug.Log($"LOCAL_GRASS_BEFORE: emptyCellsNearCurrentRoutes={near} emptyCellsAwayFromRoutes={away}");
        }

        private static string Layout() => string.Join("\n", GameObject.Find(RootName).GetComponentsInChildren<Transform>()
            .Select(t => t.name + "|" + t.position.ToString("F5") + "|" + t.rotation.ToString("F5") + "|" + t.localScale.ToString("F5")));

        private static void Validate(Terrain terrain)
        {
            var root = GameObject.Find(RootName);
            foreach (var tree in root.GetComponentsInChildren<SceneTreeObstacle>())
            {
                Vector3 p = tree.transform.position;
                if (Weight(new Vector2(p.x, p.z)) <= 0f) throw new InvalidOperationException("Local tree outside region.");
            }
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
                if (renderer.sharedMaterials.Any(m => m == null || m.shader == null || !m.shader.isSupported))
                    throw new InvalidOperationException("Local vegetation material invalid.");
            Transform low = root.transform.Find("Local Understory");
            if (low.GetComponentsInChildren<Collider>().Length != 0 || low.GetComponentsInChildren<SceneTreeObstacle>().Length != 0)
                throw new InvalidOperationException("Local groundcover has solid obstacles.");
            foreach (Transform item in root.transform.Cast<Transform>().Concat(low.Cast<Transform>()))
            {
                if (item == low) continue;
                Bounds bounds = CentralForestDensification.BoundsOf(item.gameObject);
                float ground = terrain.SampleHeight(item.position) + terrain.transform.position.y;
                if (Mathf.Abs(bounds.min.y - ground) > .08f || MainCreekIntegration.NearCreek(item.position.x, item.position.z, 1.5f))
                    throw new InvalidOperationException("Local vegetation floats or enters creek.");
            }
            RouteObstacleClearance.Validate(terrain);
        }

        private static void Capture(Terrain terrain, string label)
        {
            Camera camera = Camera.main;
            Vector3 position = camera.transform.position; Quaternion rotation = camera.transform.rotation;
            RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
            var target = new RenderTexture(1280, 720, 24); target.Create();
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var timings = new List<string>();
            try
            {
                camera.targetTexture = target;
                camera.transform.position = new Vector3(Center.x, terrain.SampleHeight(new Vector3(Center.x, 0f, Center.y)) + terrain.transform.position.y + 1.7f, Center.y);
                foreach (int heading in new[] { 0, 78, 180, 270 })
                {
                    camera.transform.rotation = Quaternion.Euler(0f, heading, 0f);
                    for (int i = 0; i < 8; i++) camera.Render();
                    var times = new List<double>();
                    for (int i = 0; i < 16; i++) { var watch = Stopwatch.StartNew(); camera.Render(); times.Add(watch.Elapsed.TotalMilliseconds); }
                    times.Sort(); timings.Add($"{heading} degrees: CPU Camera.Render median {times[times.Count/2]:F2}ms");
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0f, 0f, 1280, 720), 0, 0); image.Apply();
                    File.WriteAllBytes($"{Output}/{label}-{heading}.png", image.EncodeToPNG());
                }
                File.WriteAllLines(Output + "/" + label + "-render-cost.txt", timings);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation); camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive; target.Release();
                UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private sealed class Snapshot
        {
            private readonly int[][,] details;
            private readonly float[,,] paint;
            private readonly float[,] heights;
            private readonly TreeInstance[] trees;
            private readonly Dictionary<Transform, string> poses;
            internal Snapshot(Terrain terrain)
            {
                TerrainData data = terrain.terrainData;
                details = Enumerable.Range(0, data.detailPrototypes.Length).Select(i => data.GetDetailLayer(0, 0, data.detailResolution, data.detailResolution, i)).ToArray();
                paint = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
                heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
                trees = data.treeInstances;
                poses = EditorSceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                    .Where(t => t.root.name != RootName).ToDictionary(t => t, Pose);
            }
            private static string Pose(Transform t) => t.position.ToString("F6") + t.rotation.ToString("F6") + t.lossyScale.ToString("F6");
            internal void CheckUnchanged(Terrain terrain) => Compare(terrain, false);
            internal void CheckExact(Terrain terrain) => Compare(terrain, true);
            private void Compare(Terrain terrain, bool exact)
            {
                TerrainData data = terrain.terrainData;
                TreeInstance[] currentTrees = data.treeInstances;
                if (trees.Length != currentTrees.Length) throw new InvalidOperationException("Repair changed Terrain tree count.");
                for (int i = 0; i < trees.Length; i++)
                {
                    TreeInstance a = trees[i], b = currentTrees[i];
                    if (a.position != b.position || a.rotation != b.rotation || a.widthScale != b.widthScale ||
                        a.heightScale != b.heightScale || a.prototypeIndex != b.prototypeIndex || !a.color.Equals(b.color) || !a.lightmapColor.Equals(b.lightmapColor))
                        throw new InvalidOperationException($"Repair changed Terrain tree {i}: position {a.position:F6}/{b.position:F6} scale {a.widthScale}/{b.widthScale} prototype {a.prototypeIndex}/{b.prototypeIndex}.");
                }
                if (!heights.Cast<float>().SequenceEqual(data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution).Cast<float>()))
                    throw new InvalidOperationException("Repair altered terrain heights.");
                foreach (var pose in poses) if (pose.Key == null || Pose(pose.Key) != pose.Value) throw new InvalidOperationException("Repair changed original object pose.");
                int grass = GrassIndex(data);
                for (int layer = 0; layer < details.Length; layer++)
                {
                    int[,] current = data.GetDetailLayer(0, 0, data.detailResolution, data.detailResolution, layer);
                    for (int z = 0; z < data.detailResolution; z++)
                    for (int x = 0; x < data.detailResolution; x++)
                        if ((exact || layer != grass || Weight(DetailPoint(terrain, x, z)) <= 0f) && current[z, x] != details[layer][z, x])
                            throw new InvalidOperationException("Repair changed protected grass/detail cell.");
                }
                float[,,] currentPaint = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
                for (int z = 0; z < data.alphamapHeight; z++)
                for (int x = 0; x < data.alphamapWidth; x++)
                {
                    Vector3 p = terrain.transform.TransformPoint(new Vector3(x / (float)(data.alphamapWidth-1) * data.size.x, 0f, z / (float)(data.alphamapHeight-1) * data.size.z));
                    for (int layer = 0; layer < data.alphamapLayers; layer++)
                        if ((exact || layer >= 4 || Weight(new Vector2(p.x, p.z)) <= 0f) && Math.Abs(currentPaint[z,x,layer]-paint[z,x,layer]) > .00001f)
                            throw new InvalidOperationException($"Repair changed protected ground/creek layer: ({x},{z}) layer={layer} {paint[z,x,layer]:F8}->{currentPaint[z,x,layer]:F8} exact={exact}.");
                }
            }
        }
    }
}
