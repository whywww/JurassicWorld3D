using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    /// <summary>Reference-based thinning of NSK trees, independent of earlier local withdrawals.</summary>
    public static class NskTreeDensity
    {
        const string Config = "Assets/Editor/NskTreeDensity.json";
        const string Output = "Tools/VegetationDiversity/NskTreeDensity";
        const string ScenePath = "Assets/Scenes/Main.unity";
        static readonly Vector2 Reference = new Vector2(14.8f, -4.4f);
        static readonly Vector2 Example = new Vector2(53.5f, -21.5f);
        static bool busy;
        [Serializable] sealed class State
        {
            public bool enabled;
            public int target5, target10;
            public Entry[] withdrawn = Array.Empty<Entry>();
        }
        [Serializable] sealed class Entry
        {
            public string key, prefab, species, objectId, name;
            public Vector3 position;
            public int terrainIndex = -1;
            public int terrainPrototype;
            public Vector3 terrainPosition;
            public float terrainWidth, terrainHeight, terrainRotation;
            public Color32 terrainColor, terrainLightmapColor;
            public TreeInstance TerrainInstance(int prototype) => new TreeInstance { prototypeIndex = prototype,
                position = terrainPosition, widthScale = terrainWidth, heightScale = terrainHeight,
                rotation = terrainRotation, color = terrainColor, lightmapColor = terrainLightmapColor };
        }
        sealed class Tree
        {
            public Entry entry;
            public GameObject scene;
            public Vector2 point;
            public int index = -1;
        }
        sealed class Window
        {
            public Vector2 center;
            public float radius;
            public int count, target;
            public readonly List<int> members = new List<int>();
        }
        static State Load() => File.Exists(Config) ? JsonUtility.FromJson<State>(File.ReadAllText(Config)) : new State();
        static void Persist(State s) { File.WriteAllText(Config, JsonUtility.ToJson(s, true) + "\n"); AssetDatabase.ImportAsset(Config); }
        static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
        static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        static string Key(Entry e) => e.prefab + "|" + e.name + "|" + F(e.position.x) + "|" + F(e.position.y) + "|" + F(e.position.z);
        static bool Inside(Vector2 a, Vector2 b, float radius) => (a - b).sqrMagnitude <= radius * radius + .00001f;

        // Prepared lighting/bark prefabs still reference the original NSK meshes.
        // Resolve those sources rather than accepting any prefab named tree01.
        static string Species(GameObject prefab)
        {
            if (prefab == null) return null;
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                string path = AssetDatabase.GetAssetPath(filter.sharedMesh).Replace('\\', '/');
                for (int i = 1; i <= 4; i++)
                    if (path.Contains("NatureStarterKit2/Nature/tree0" + i + ".")) return "tree0" + i;
            }
            return null;
        }
        static List<Tree> Collect()
        {
            var rows = new List<Tree>();
            var seen = new HashSet<GameObject>();
            var species = new Dictionary<string, string>();
            foreach (var t in Object.FindObjectsOfType<Transform>())
            {
                if (!t.gameObject.scene.IsValid()) continue;
                var root = PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject);
                if (root == null || !seen.Add(root)) continue;
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                if (!species.TryGetValue(path, out var kind))
                    species[path] = kind = Species(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                if (kind == null) continue;
                var e = new Entry { prefab = path, species = kind, name = root.name, position = root.transform.position,
                    objectId = GlobalObjectId.GetGlobalObjectIdSlow(root).ToString() };
                e.key = Key(e);
                rows.Add(new Tree { entry = e, scene = root, point = XZ(e.position) });
            }
            var terrain = Terrain.activeTerrain;
            var data = terrain.terrainData;
            var trees = data.treeInstances;
            for (int i = 0; i < trees.Length; i++)
            {
                var tree = trees[i];
                if (tree.widthScale <= 0 || tree.heightScale <= 0) continue;
                var prefab = data.treePrototypes[tree.prototypeIndex].prefab;
                string path = AssetDatabase.GetAssetPath(prefab);
                if (!species.TryGetValue(path, out var kind)) species[path] = kind = Species(prefab);
                if (kind == null) continue;
                var e = new Entry { prefab = path, species = kind, name = "Terrain", terrainIndex = i,
                    terrainPrototype = tree.prototypeIndex,
                    terrainPosition = tree.position, terrainWidth = tree.widthScale, terrainHeight = tree.heightScale,
                    terrainRotation = tree.rotation, terrainColor = tree.color, terrainLightmapColor = tree.lightmapColor,
                    position = terrain.transform.position + Vector3.Scale(tree.position, data.size) };
                e.key = Key(e);
                rows.Add(new Tree { entry = e, index = i, point = XZ(e.position) });
            }
            if (rows.GroupBy(r => r.entry.key).Any(g => g.Count() > 1))
                throw new InvalidOperationException("Ambiguous NSK tree identities; no thinning performed.");
            return rows.OrderBy(r => r.entry.key, StringComparer.Ordinal).ToList();
        }
        static int Count(List<Tree> rows, Vector2 p, float radius) => rows.Count(t => Inside(t.point, p, radius));
        static string Signature(List<Tree> rows) => string.Join("\n", rows.Select(t => t.entry.key).OrderBy(k => k, StringComparer.Ordinal));

        static List<Window> Windows(List<Tree> rows, State state)
        {
            var centers = new HashSet<Vector2>(rows.Select(t => t.point)) { Reference, Example };
            if (rows.Count == 0) throw new InvalidOperationException("No NSK trees found.");
            // The grid is for measuring density only; no tree is moved onto it.
            for (float x = Mathf.Floor(rows.Min(t => t.point.x) / 2.5f) * 2.5f; x <= rows.Max(t => t.point.x); x += 2.5f)
                for (float z = Mathf.Floor(rows.Min(t => t.point.y) / 2.5f) * 2.5f; z <= rows.Max(t => t.point.y); z += 2.5f)
                    centers.Add(new Vector2(x, z));
            var result = new List<Window>();
            foreach (var center in centers.OrderBy(p => p.x).ThenBy(p => p.y))
                foreach (float radius in new[] { 5f, 10f })
                {
                    var w = new Window { center = center, radius = radius, target = radius == 5 ? state.target5 : state.target10 };
                    for (int i = 0; i < rows.Count; i++) if (Inside(center, rows[i].point, radius)) w.members.Add(i);
                    w.count = w.members.Count;
                    result.Add(w);
                }
            return result;
        }
        static void Apply(State state)
        {
            var rows = Collect();
            var entries = state.withdrawn.ToDictionary(e => e.key, StringComparer.Ordinal);
            var removed = new HashSet<int>();
            for (int i = 0; i < rows.Count; i++) if (entries.ContainsKey(rows[i].entry.key)) removed.Add(i);
            var windows = Windows(rows, state);
            var membership = Enumerable.Range(0, rows.Count).Select(_ => new List<Window>()).ToArray();
            foreach (var w in windows)
            {
                foreach (int i in w.members) membership[i].Add(w);
                w.count -= w.members.Count(removed.Contains);
            }
            // Trees rooted in an already suitable neighborhood stay untouched. The
            // reference disk is protected even when neighboring dense windows overlap it.
            var eligible = rows.Select(t => !Inside(t.point, Reference, 10) &&
                (Count(rows, t.point, 10) > state.target10 || Count(rows, t.point, 5) > state.target5)).ToArray();
            var nearest = rows.Select((t, i) => rows.Where((other, j) => j != i)
                .Select(other => (t.point - other.point).sqrMagnitude).DefaultIfEmpty(float.MaxValue).Min()).ToArray();
            while (true)
            {
                int best = -1;
                double bestScore = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (removed.Contains(i) || !eligible[i]) continue;
                    double score = membership[i].Where(w => w.count > w.target)
                        .Sum(w => (w.count - w.target) / (double)Math.Max(1, w.target));
                    if (score == 0) continue;
                    // Prefer close trunks when their density benefit is similar.
                    score += .01 / Math.Max(.01, nearest[i]);
                    if (score > bestScore) { best = i; bestScore = score; }
                }
                if (best < 0) break;
                removed.Add(best);
                entries[rows[best].entry.key] = rows[best].entry;
                foreach (var w in membership[best]) w.count--;
            }
            foreach (int i in removed)
                if (rows[i].scene != null)
                {
                    rows[i].scene.SetActive(false);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(rows[i].scene);
                    EditorUtility.SetDirty(rows[i].scene);
                }
            var indices = new HashSet<int>(removed.Where(i => rows[i].index >= 0).Select(i => rows[i].index));
            if (indices.Count > 0)
            {
                var data = Terrain.activeTerrain.terrainData;
                data.SetTreeInstances(data.treeInstances.Where((t, i) => !indices.Contains(i)).ToArray(), false);
                EditorUtility.SetDirty(data);
            }
            state.withdrawn = entries.Values.OrderBy(e => e.key, StringComparer.Ordinal).ToArray();
            TerrainTreeObstacleRegistry.Invalidate();
        }

        static void Restore(State state)
        {
            var data = Terrain.activeTerrain.terrainData;
            var trees = data.treeInstances.ToList();
            var objects = Object.FindObjectsOfType<Transform>(true).Where(t => t.gameObject.scene.IsValid())
                .Select(t => t.gameObject).ToArray();
            foreach (var e in state.withdrawn.Where(e => e.terrainIndex < 0))
            {
                GameObject obj = null;
                if (GlobalObjectId.TryParse(e.objectId, out var id)) obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
                if (obj == null) obj = objects.FirstOrDefault(o => o.name == e.name && o.transform.position == e.position &&
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(o) == e.prefab);
                if (obj == null) throw new InvalidOperationException("Cannot restore missing scene tree: " + e.key);
                obj.SetActive(true); PrefabUtility.RecordPrefabInstancePropertyModifications(obj); EditorUtility.SetDirty(obj);
            }
            foreach (var e in state.withdrawn.Where(e => e.terrainIndex >= 0).OrderBy(e => e.terrainIndex))
            {
                int prototype = e.terrainPrototype;
                if (prototype < 0 || prototype >= data.treePrototypes.Length || AssetDatabase.GetAssetPath(data.treePrototypes[prototype].prefab) != e.prefab)
                    prototype = Array.FindIndex(data.treePrototypes, p => AssetDatabase.GetAssetPath(p.prefab) == e.prefab);
                if (prototype < 0) throw new InvalidOperationException("Missing Terrain prototype: " + e.prefab);
                var tree = e.TerrainInstance(prototype);
                if (trees.Any(t => t.prototypeIndex == prototype && t.position == tree.position)) continue;
                trees.Insert(Math.Min(e.terrainIndex, trees.Count), tree);
            }
            data.SetTreeInstances(trees.ToArray(), false); EditorUtility.SetDirty(data);
            TerrainTreeObstacleRegistry.Invalidate();
        }
        internal static void SeedConfigured()
        {
            if (busy) return;
            var state = Load(); if (!state.enabled) return;
            FoliageInteractionPreparation.RestoreCurrent();
            Apply(state); Persist(state);
        }
        static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the scene and stop Play first.");
            if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
        }
        static void Finish(bool refreshGeometry = true)
        {
            if (refreshGeometry) TreeHeightAdjustment.RefreshGeometry();
            if (File.Exists(FoliageInteractionPreparation.EnabledPath)) FoliageInteractionPreparation.PrepareCurrent();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
        }
        [MenuItem("Jurassic World/Environment/NSK Tree Density/Apply and Verify")]
        public static void BuildAndVerify()
        {
            Open(); Directory.CreateDirectory(Output);
            // Back up current authored files, including any pre-existing user changes.
            string terrainPath = AssetDatabase.GetAssetPath(Terrain.activeTerrain.terrainData);
            var files = new[] { ScenePath, terrainPath, Config };
            var backups = files.ToDictionary(p => p, p => File.Exists(p) ? File.ReadAllBytes(p) : null);
            busy = true;
            try
            {
                FoliageInteractionPreparation.RestoreCurrent();
                var before = Collect(); var state = Load();
                var snapshot = new ProtectedSnapshot();
                bool firstApplication = !state.enabled;
                var data = Terrain.activeTerrain.terrainData;
                var originalInstances = data.treeInstances;
                string originalTerrain = FoliageInteractionPreparation.TreeSignature(originalInstances, data.treePrototypes);
                if (!state.enabled)
                {
                    state.target5 = Count(before, Reference, 5); state.target10 = Count(before, Reference, 10);
                    if (state.target5 == 0 || state.target10 == 0) throw new InvalidOperationException("Reference area has no NSK trees.");
                    state.enabled = true;
                }
                Apply(state);
                var after = Collect(); string signature = Signature(after);
                var originalKeys = new HashSet<string>(before.Select(t => t.entry.key));
                snapshot.Check(); CheckPreserved(before, after, state);
                Apply(state);
                if (Signature(Collect()) != signature) throw new InvalidOperationException("Repeated application removed additional trees.");
                Persist(state); state = Load();
                Restore(state);
                if (firstApplication && originalTerrain != FoliageInteractionPreparation.TreeSignature(data.treeInstances, data.treePrototypes))
                {
                    var current = data.treeInstances;
                    int different = Enumerable.Range(0, Math.Min(current.Length, originalInstances.Length)).FirstOrDefault(i =>
                        FoliageInteractionPreparation.TreeSignature(new[] { current[i] }, data.treePrototypes) !=
                        FoliageInteractionPreparation.TreeSignature(new[] { originalInstances[i] }, data.treePrototypes));
                    throw new InvalidOperationException("Serialized undo failed: counts=" + originalInstances.Length + "/" + current.Length +
                        "; first difference=" + different + "; original=" + Describe(originalInstances[different]) + "; restored=" + Describe(current[different]));
                }
                var restored = Collect();
                var expected = new HashSet<string>(originalKeys.Concat(state.withdrawn.Select(e => e.key)));
                if (!expected.SetEquals(restored.Select(t => t.entry.key))) throw new InvalidOperationException("Undo failed to restore population.");
                Apply(state);
                if (Signature(Collect()) != signature) throw new InvalidOperationException("Reapplication after restoration differs.");
                Persist(state);
                // Exercise the actual regeneration hook, using restored authored trees.
                Restore(state); busy = false;
                SeedConfigured(); busy = true;
                if (Signature(Collect()) != signature) throw new InvalidOperationException("Regeneration hook changed the result.");
                snapshot.Check(); CheckPreserved(before, Collect(), state);
                WriteReport(before, after, state);
                Finish();
                FoliageInteractionPreparation.RestoreCurrent();
                snapshot.Check(); CheckPreserved(before, Collect(), state);
                if (Signature(Collect()) != signature) throw new InvalidOperationException("Geometry/foliage refresh changed trees.");
                Finish(false);
                File.WriteAllText(Output + "/Validation.md", "PASS: protected reference disk; retained tree transforms; other scene objects; terrain height/paint/details; dinosaur state; earlier withdrawal policies.\nPASS: idempotence; undo/reapply; regeneration hook; geometry/navigation/foliage round trip.\n");
                Debug.Log("NSK_TREE_DENSITY_PASS before=" + before.Count + " after=" + after.Count + " removed=" + state.withdrawn.Length);
            }
            catch
            {
                foreach (var pair in backups)
                    if (pair.Value != null) File.WriteAllBytes(pair.Key, pair.Value);
                    else if (File.Exists(pair.Key)) File.Delete(pair.Key);
                AssetDatabase.Refresh(); EditorSceneManager.OpenScene(ScenePath);
                throw;
            }
            finally { busy = false; }
        }
        static string Describe(TreeInstance t) => $"prototype={t.prototypeIndex}, position={F(t.position.x)}/{F(t.position.y)}/{F(t.position.z)}, scale={F(t.widthScale)}/{F(t.heightScale)}, rotation={F(t.rotation)}, color={t.color}, light={t.lightmapColor}";
        [MenuItem("Jurassic World/Environment/NSK Tree Density/Undo This Thinning")]
        public static void Undo()
        {
            Open(); var state = Load(); busy = true;
            try
            {
                FoliageInteractionPreparation.RestoreCurrent(); Restore(state);
                state.enabled = false; state.withdrawn = Array.Empty<Entry>(); Persist(state); Finish();
            }
            finally { busy = false; }
        }
        static void CheckPreserved(List<Tree> before, List<Tree> after, State state)
        {
            var removed = new HashSet<string>(state.withdrawn.Select(e => e.key));
            if (Signature(before.Where(t => !removed.Contains(t.entry.key)).ToList()) != Signature(after))
                throw new InvalidOperationException("Unselected NSK population changed.");
            if (state.withdrawn.Any(e => Inside(XZ(e.position), Reference, 10)))
                throw new InvalidOperationException("Reference disk was thinned.");
        }
        sealed class ProtectedSnapshot
        {
            readonly TerrainData data = Terrain.activeTerrain.terrainData;
            readonly float[] heights, paint;
            readonly int[][] details;
            readonly Dictionary<Transform, string> transforms;
            readonly Dictionary<string, string> policies;
            readonly HashSet<GameObject> targets;
            readonly string otherTerrain;
            static string Pose(Transform t) => JsonUtility.ToJson(new PoseData { p = t.localPosition, q = t.localRotation, s = t.localScale });
            [Serializable] sealed class PoseData { public Vector3 p, s; public Quaternion q; }
            public ProtectedSnapshot()
            {
                targets = new HashSet<GameObject>(Collect().Where(t => t.scene != null).Select(t => t.scene));
                transforms = Object.FindObjectsOfType<Transform>(true).Where(t => t.gameObject.scene.IsValid())
                    .ToDictionary(t => t, t => Pose(t) + (targets.Contains(t.gameObject) ? "" : "|" + t.gameObject.activeSelf));
                heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution).Cast<float>().ToArray();
                paint = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight).Cast<float>().ToArray();
                details = Enumerable.Range(0, data.detailPrototypes.Length).Select(i => data.GetDetailLayer(0, 0, data.detailResolution, data.detailResolution, i).Cast<int>().ToArray()).ToArray();
                policies = new[] { "Assets/Editor/LocalTreeThinning.json", "Assets/Editor/TreeOverlapWithdrawals.json", "Assets/Editor/TreeHeightPolicy.json" }
                    .ToDictionary(p => p, File.ReadAllText);
                otherTerrain = OtherTerrain();
            }
            string OtherTerrain() => FoliageInteractionPreparation.TreeSignature(data.treeInstances.Where(t => Species(data.treePrototypes[t.prototypeIndex].prefab) == null).ToArray(), data.treePrototypes);
            public void Check()
            {
                foreach (var pair in transforms)
                    if (pair.Key == null || pair.Value != Pose(pair.Key) + (targets.Contains(pair.Key.gameObject) ? "" : "|" + pair.Key.gameObject.activeSelf))
                        throw new InvalidOperationException("Existing object pose/state changed: " + pair.Key);
                if (!heights.SequenceEqual(data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution).Cast<float>()) ||
                    !paint.SequenceEqual(data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight).Cast<float>()) || otherTerrain != OtherTerrain())
                    throw new InvalidOperationException("Terrain or other tree species changed.");
                for (int i = 0; i < details.Length; i++)
                    if (!details[i].SequenceEqual(data.GetDetailLayer(0, 0, data.detailResolution, data.detailResolution, i).Cast<int>()))
                        throw new InvalidOperationException("Understory changed.");
                foreach (var pair in policies) if (File.ReadAllText(pair.Key) != pair.Value) throw new InvalidOperationException("Earlier policy changed: " + pair.Key);
            }
        }
        static void WriteReport(List<Tree> before, List<Tree> after, State state)
        {
            var report = new StringBuilder("# NSK tree01–04 全场疏树\n\n参考点(14.8,-4.4)，保护半径10m。树根数量目标：5m=" + state.target5 + "，10m=" + state.target10 + "。\n\n|位置|半径|调整前|调整后|\n|---|---:|---:|---:|\n");
            foreach (var point in new[] { Reference, Example }) foreach (float radius in new[] { 5f, 10f })
                report.AppendLine($"|({F(point.x)},{F(point.y)})|{radius}m|{Count(before, point, radius)}|{Count(after, point, radius)}|");
            report.AppendLine("\n|树种|本次调整前|调整后|累计撤下|\n|---|---:|---:|---:|");
            for (int i = 1; i <= 4; i++)
            {
                string s = "tree0" + i;
                report.AppendLine($"|{s}|{before.Count(t => t.entry.species == s)}|{after.Count(t => t.entry.species == s)}|{state.withdrawn.Count(e => e.species == s)}|");
            }
            report.AppendLine("\n全场 NSK：" + before.Count + " → " + after.Count + "。累计撤下：" + state.withdrawn.Length + "。\n\n2.5m采样网格与每棵原始树根共同检测5m/10m邻域；优先撤下对过密邻域贡献最大且树干紧邻的树。原先两个尺度均不超标的树和参考圈内树受到保护；因此与保护树重叠的检测窗口可能仍高于目标。网格只用于测量，不移动或缩放树木。\n");
            var residual = Windows(before, state).Where(w => Count(after, w.center, w.radius) > w.target).ToArray();
            report.AppendLine("保护约束下仍超目标的采样窗口：" + residual.Length + "。详见 ResidualWindows.csv。");
            File.WriteAllText(Output + "/Summary.md", report.ToString());
            File.WriteAllText(Output + "/ResidualWindows.csv", "x,z,radius,target,remaining\n" + string.Join("\n", residual.Select(w => $"{F(w.center.x)},{F(w.center.y)},{w.radius},{w.target},{Count(after, w.center, w.radius)}")));
            File.WriteAllText(Output + "/Trees.csv", "species,kind,prefab,name,x,y,z\n" + string.Join("\n", state.withdrawn.Select(e => string.Join(",", e.species, e.terrainIndex < 0 ? "Scene" : "Terrain", e.prefab, e.name, F(e.position.x), F(e.position.y), F(e.position.z)))));
        }
    }
}
