using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class BushHeightAcceptance
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string Output = "Logs/BushHeight";
        private const string PlayKey = "JurassicWorld.BushHeightPlayCheck";
        private static int frames;
        [Serializable] private sealed class PlayPlant { public string path; public Bounds bounds; }
        [Serializable] private sealed class PlayPlants { public PlayPlant[] plants; }

        static BushHeightAcceptance()
        {
            if (SessionState.GetBool(PlayKey, false)) EditorApplication.update += PlayTick;
        }

        // Run with a graphics device, without -quit; the callback exits after Play checks.
        public static void CheckPlay() => StartPlay(false);

        public static void CheckFernPlay() => StartPlay(true);

        private static void StartPlay(bool fern)
        {
            EditorSceneManager.OpenScene(ScenePath);
            string output = fern ? "Logs/FernHeight" : Output;
            Directory.CreateDirectory(output);
            CenterForestGapFill.ValidateBushReplay();
            var plants = (fern ? FernHeightAdjustment.SceneFerns() : BushHeightAdjustment.SceneBushes()).Where(b => b.activeInHierarchy)
                .Select(b => new PlayPlant { path = HierarchyPath(b.transform), bounds = BushHeightAdjustment.MeshBounds(b, true) }).ToArray();
            Require(plants.Length > 0, "No active bushes found before Play.");
            SessionState.SetString(PlayKey + ".Plants", JsonUtility.ToJson(new PlayPlants { plants = plants }));
            SessionState.SetString(PlayKey + ".Output", output);
            SessionState.SetString(PlayKey + ".Kind", fern ? "ferns" : "bushes");
            SessionState.SetFloat(PlayKey + ".Limit", fern ? FernHeightAdjustment.MaximumHeight : BushHeightAdjustment.MaximumHeight);
            SessionState.SetBool(PlayKey, true);
            EditorApplication.isPlaying = true;
        }

        private static void PlayTick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            if (++frames < 60) return;
            EditorApplication.update -= PlayTick;
            SessionState.SetBool(PlayKey, false);
            try
            {
                Require(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null, "Graphics device required.");
                float limit = SessionState.GetFloat(PlayKey + ".Limit", BushHeightAdjustment.MaximumHeight);
                string output = SessionState.GetString(PlayKey + ".Output", Output);
                string kind = SessionState.GetString(PlayKey + ".Kind", "bushes");
                // Prefab connections are unavailable in Play; retain the edit-mode source selection.
                var plants = JsonUtility.FromJson<PlayPlants>(SessionState.GetString(PlayKey + ".Plants", "")).plants;
                var bushes = plants.Select(p => GameObject.Find(p.path)).ToArray();
                Require(bushes.Length > 0 && bushes.All(b => b != null), "Runtime bush selection missing.");
                // Static batching replaces MeshFilter.sharedMesh with the whole batch mesh.
                // Transform the original per-plant geometry, rather than measuring that batch.
                for (int i = 0; i < plants.Length; i++)
                    Require(RuntimeHeight(bushes[i].transform, plants[i].bounds) <= limit + .0002f, "Runtime plant height exceeded: " + bushes[i].name);
                // Sample the initial view, main route, and three close approaches to tall bushes.
                var positions = new[] { new Vector2(0, -16), new Vector2(0, 0) }.Concat(bushes
                    .Where(b => Mathf.Abs(b.transform.position.x) < 65 && Mathf.Abs(b.transform.position.z) < 65)
                    .OrderByDescending(b => RuntimeHeight(b.transform, plants[Array.IndexOf(bushes, b)].bounds))
                    .GroupBy(b => new Vector2Int(Mathf.RoundToInt(b.transform.position.x / 15), Mathf.RoundToInt(b.transform.position.z / 15)))
                    .Take(3).Select(g => new Vector2(g.First().transform.position.x, g.First().transform.position.z - 2f))).ToArray();
                var cameraObject = new GameObject("Bush height visual probe");
                try
                {
                    var camera = cameraObject.AddComponent<Camera>();
                    camera.CopyFrom(Camera.main); camera.enabled = false;
                    for (int i = 0; i < positions.Length; i++)
                    {
                        // Two samples per approach also check forward movement through the view.
                        for (int step = 0; step < 2; step++)
                        {
                            Vector2 p = positions[i] + Vector2.up * (step * .5f);
                            Terrain terrain = Terrain.activeTerrain;
                            float ground = terrain.SampleHeight(new Vector3(p.x, 0, p.y)) + terrain.transform.position.y;
                            camera.transform.SetPositionAndRotation(new Vector3(p.x, ground + 1.7f, p.y), Quaternion.identity);
                            Capture(camera, output + "/view-" + i + "-" + step + ".png");
                        }
                    }
                }
                finally { Object.DestroyImmediate(cameraObject); }
                File.WriteAllText(output + "/Play.md", $"PASS: {frames} Play frames; {bushes.Length} active {kind} <= {limit}m. Captured {positions.Length * 2} eye-height views at 1.7m, including forward movement.\n");
                Debug.Log(kind == "ferns" ? "FERN_HEIGHT_PLAY_OK" : "BUSH_HEIGHT_PLAY_OK");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        private static string HierarchyPath(Transform t) => t.parent == null ? "/" + t.name : HierarchyPath(t.parent) + "/" + t.name;

        private static float RuntimeHeight(Transform root, Bounds local)
        {
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
            {
                float height = root.TransformPoint(new Vector3(x == 0 ? local.min.x : local.max.x,
                    y == 0 ? local.min.y : local.max.y, z == 0 ? local.min.z : local.max.z)).y;
                min = Mathf.Min(min, height); max = Mathf.Max(max, height);
            }
            return max - min;
        }

        private static void Capture(Camera camera, string path)
        {
            var target = RenderTexture.GetTemporary(1280, 720, 24);
            var previous = RenderTexture.active;
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(texture);
            }
        }

        // Targeted migration, not a scene bake. Also usable with -executeMethod.
        public static void ApplyMain()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            var active = EditorSceneManager.GetActiveScene();
            if (active.isDirty) throw new InvalidOperationException("Save the current scene before adjusting Main.");
            EditorSceneManager.OpenScene(ScenePath);
            Directory.CreateDirectory(Output);
            var bushes = BushHeightAdjustment.SceneBushes();
            var targets = bushes.Select(b => b.transform).ToHashSet();
            var untouched = EditorSceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Where(t => !targets.Contains(t))
                .Select(t => (t, t.localPosition, t.localRotation, t.localScale)).ToArray();
            var before = bushes.Select(b => (root: b, bounds: BushHeightAdjustment.MeshBounds(b),
                position: b.transform.position, rotation: b.transform.rotation, scale: b.transform.localScale)).ToArray();
            var terrains = Object.FindObjectsOfType<Terrain>();
            var densities = terrains.Select(t => t.terrainData.detailPrototypes.Select((p, i) =>
                t.terrainData.GetDetailLayer(0, 0, t.terrainData.detailWidth, t.terrainData.detailHeight, i).Cast<int>().ToArray()).ToArray()).ToArray();
            var trees = terrains.Select(t => t.terrainData.treeInstances).ToArray();
            var heights = terrains.Select(t => t.terrainData.GetHeights(0, 0,
                t.terrainData.heightmapResolution, t.terrainData.heightmapResolution).Cast<float>().ToArray()).ToArray();
            var paint = terrains.Select(t => t.terrainData.GetAlphamaps(0, 0,
                t.terrainData.alphamapWidth, t.terrainData.alphamapHeight).Cast<float>().ToArray()).ToArray();
            var report = new StringBuilder("# Bush height acceptance\n\n");
            report.AppendLine($"Bush instances (including inactive): {bushes.Length}; before maximum: {before.Max(b => b.bounds.size.y):F4} m.");
            int changed = BushHeightAdjustment.ApplyCurrent();
            foreach (var b in before)
            {
                Bounds after = BushHeightAdjustment.MeshBounds(b.root);
                Require(after.size.y <= BushHeightAdjustment.MaximumHeight + .0001f, "Height exceeded: " + b.root.name);
                Require(Mathf.Abs(after.min.y - b.bounds.min.y) < .0002f, "Bush base moved: " + b.root.name);
                Require(b.root.transform.position.x == b.position.x && b.root.transform.position.z == b.position.z
                    && b.root.transform.rotation == b.rotation, "Bush placement changed: " + b.root.name);
                if (b.bounds.size.y <= BushHeightAdjustment.MaximumHeight + .0001f)
                    Require(b.root.transform.position == b.position && b.root.transform.localScale == b.scale, "Short bush changed.");
            }
            foreach (var item in untouched)
                Require(item.t.localPosition == item.localPosition && item.t.localRotation == item.localRotation
                    && item.t.localScale == item.localScale, "Unrelated local transform changed: " + item.t.name);
            Require(BushHeightAdjustment.ApplyCurrent() == 0, "Second adjustment changed the scene.");
            for (int t = 0; t < terrains.Length; t++)
            {
                TerrainData data = terrains[t].terrainData;
                Require(trees[t].SequenceEqual(data.treeInstances), "Terrain trees changed.");
                Require(heights[t].SequenceEqual(data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution).Cast<float>()), "Terrain heights changed.");
                Require(paint[t].SequenceEqual(data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight).Cast<float>()), "Terrain paint changed.");
                for (int i = 0; i < data.detailPrototypes.Length; i++)
                {
                    Require(densities[t][i].SequenceEqual(data.GetDetailLayer(0, 0, data.detailWidth, data.detailHeight, i).Cast<int>()), "Detail density changed.");
                    DetailPrototype p = data.detailPrototypes[i];
                    if (!BushHeightAdjustment.IsBush(p.prototype)) continue;
                    float meshHeight = BushHeightAdjustment.MeshBounds(p.prototype).size.y;
                    Require(meshHeight * p.maxHeight <= BushHeightAdjustment.MaximumHeight + .0001f, "Terrain bush height exceeded.");
                    float actualMax = 0f; int instances = 0;
                    for (int x = 0; x < data.detailPatchCount; x++) for (int z = 0; z < data.detailPatchCount; z++)
                    {
                        var transforms = data.ComputeDetailInstanceTransforms(x, z, i, 1f, out _);
                        foreach (var instance in transforms) { instances++; actualMax = Mathf.Max(actualMax, meshHeight * instance.scaleY); }
                    }
                    Require(actualMax <= BushHeightAdjustment.MaximumHeight + .0001f, "Computed detail instances exceeded the height limit.");
                    report.AppendLine($"Terrain layer {i} ({p.prototype.name}): {instances} instances; computed maximum {actualMax:F4} m.");
                }
            }
            TestScaledParent();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(ScenePath);
            Require(BushHeightAdjustment.ApplyCurrent() == 0, "Saved scene is not stable after reload.");
            report.AppendLine($"\nChanged instances/detail layers: {changed}; after maximum: {BushHeightAdjustment.SceneBushes().Max(b => BushHeightAdjustment.MeshBounds(b).size.y):F4} m.");
            report.AppendLine("PASS: base height, XZ, orientation, shorter bushes, unrelated transforms, terrain trees/heights/paint/detail density, repeated pass, reload, scaled-parent case.");
            File.WriteAllText(Output + "/Acceptance.md", report.ToString());
            Debug.Log("BUSH_HEIGHT_ACCEPTANCE_OK: " + report);
        }

        private static void TestScaledParent()
        {
            var parent = new GameObject("Bush height test parent");
            try
            {
                parent.transform.SetPositionAndRotation(new Vector3(10, 5, 12), Quaternion.Euler(0, 35, 0));
                parent.transform.localScale = new Vector3(2, 3, 2);
                var bush = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/ThirdParty/NatureStarterKit2/Nature/bush01.prefab"), parent.transform);
                bush.transform.localScale *= 10f;
                Bounds before = BushHeightAdjustment.MeshBounds(bush);
                Require(BushHeightAdjustment.Clamp(bush), "Oversized test bush unchanged.");
                Bounds after = BushHeightAdjustment.MeshBounds(bush);
                Require(after.size.y <= BushHeightAdjustment.MaximumHeight + .0001f && Mathf.Abs(after.min.y - before.min.y) < .0002f, "Scaled-parent case failed.");
                Require(!BushHeightAdjustment.Clamp(bush), "Scaled-parent adjustment not stable.");
            }
            finally { Object.DestroyImmediate(parent); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
