using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace JurassicWorld.Editor
{
    /// <summary>Owned staged planting and reversible northwest conversion; perimeter prototypes remain stable.</summary>
    public static class CentralForestDensification
    {
        internal const string RootName = "Central Forest Densification";
        internal const string ScenePath = "Assets/Scenes/Main.unity";
        internal const string Output = "Tools/VegetationDiversity/CentralForest";
        private const string StatePath = "Assets/Editor/CentralForestStage.json";
        private const string PreviewName = "Poly Haven Five Tree Preview";
        private const int Seed = 731611;
        private static readonly Rect Core = Rect.MinMaxRect(-60f, -50f, 60f, 82f);
        [Serializable] private sealed class State { public int stage; public int seed = Seed; public int layoutVersion = 4; public int autumnBaseline; }
        internal static int AutumnBaseline;
        private sealed class Species
        {
            internal string Name, Path;
            internal float Height, Radius;
            internal bool Autumn;
            internal Species(string name, float height, float radius, bool autumn = false, bool starter = false)
            { Name = name; Height = height; Radius = radius; Autumn = autumn;
                Path = starter ? "Assets/ThirdParty/NatureStarterKit2/Nature/" + name + ".prefab" : "Assets/Environment/Vegetation/Trees/Prepared/Prefabs/" + name + ".prefab"; }
        }
        private static readonly Species[] SpeciesList =
        {
            new Species("tree01", 10f, .85f, false, true), new Species("tree02", 9f, .8f, false, true),
            new Species("tree03", 8f, .75f, false, true), new Species("tree04", 6f, .65f, false, true),
            new Species("Pachira_B", 6f, .6f), new Species("BananaTree", 3.6f, .4f),
            new Species("IslandTree02", 10f, 1.1f), new Species("IslandTree01", 5.03f, .7f),
            new Species("IslandTree03", 2.62f, .7f), new Species("TreeSmall02", 4.56f, .45f),
            new Species("JacarandaTree", 13.5f, .6f), new Species("FirTree01", 18.9f, .65f),
            new Species("FabTree_1", 10f, .8f, true), new Species("FabTree_5", 14f, .9f, true),
            new Species("FabTree_10", 18f, 1.1f, true),
        };
        private sealed class Stem
        {
            internal Vector2 Point;
            internal float Radius, Crown;
            internal bool Autumn;
            internal string Name;
        }
        private sealed class Patch { internal Vector2 Center; internal float Size, Autumn; internal int Species; }
        private sealed class Context
        {
            internal Terrain Terrain;
            internal Func<Vector2, float, bool> TrunkSafe, SolidSafe;
            internal Func<Transform, bool> TreeSafe;
            internal List<Bounds> Rocks;
            internal Vector2[] Spawns;
            internal List<Stem> Stems;
            internal List<Patch> Patches;
            internal List<Vector2> Plantable;
            internal List<Vector2> RouteEdges;
            internal float ReferenceDensity, ReferenceCover, PlantableArea, ActualPlantableArea;
            internal string Samples;
        }

        [MenuItem("Jurassic World/Environment/Central Forest/1 Build Stage One")]
        private static void BuildStageOneMenu() => JurassicMenuGuard.Run(BuildStageOne, true);

        [MenuItem("Jurassic World/Environment/Central Forest/1 Build Stage One", true)]
        private static bool BuildStageOneMenuEnabled() => JurassicMenuGuard.CanRun;

        public static void BuildStageOne() => BuildSaved(1);
        [MenuItem("Jurassic World/Environment/Central Forest/2 Append Stage Two (After Review)")]
        private static void AppendStageTwoMenu() => JurassicMenuGuard.Run(AppendStageTwo, true);

        [MenuItem("Jurassic World/Environment/Central Forest/2 Append Stage Two (After Review)", true)]
        private static bool AppendStageTwoMenuEnabled() => JurassicMenuGuard.CanRun;

        public static void AppendStageTwo()
        {
            if (LoadStage() < 1) throw new InvalidOperationException("Build and review stage one first.");
            BuildSaved(2);
        }
        [MenuItem("Jurassic World/Environment/Central Forest/3 Validate")]
        private static void ValidateMenu() => JurassicMenuGuard.Run(Validate, true);

        [MenuItem("Jurassic World/Environment/Central Forest/3 Validate", true)]
        private static bool ValidateMenuEnabled() => JurassicMenuGuard.CanRun;

        public static void Validate()
        {
            RequireEditMode();
            if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
            ValidateInScene();
        }
        [MenuItem("Jurassic World/Environment/Central Forest/4 Remove Densification")]
        private static void RemoveMenu() => JurassicMenuGuard.Run(Remove, true);

        [MenuItem("Jurassic World/Environment/Central Forest/4 Remove Densification", true)]
        private static bool RemoveMenuEnabled() => JurassicMenuGuard.CanRun;

        public static void Remove()
        {
            RequireEditMode();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
            GameObject root = GameObject.Find(RootName);
            RemoveOwnedRoot(root);
            SaveStage(0);
            TerrainTreeObstacleRegistry.Invalidate();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log("CENTRAL_FOREST_REMOVED: original vegetation retained; preview restored when available.");
        }
        private static void RemoveOwnedRoot(GameObject root)
        {
            if (root == null) return;
            NorthwestAutumnForest.Restore(root);
            Transform backup = root.transform.Find("Preview Backup");
            if (backup != null) { backup.SetParent(null); backup.name = PreviewName; backup.gameObject.SetActive(true); }
            UnityEngine.Object.DestroyImmediate(root);
            TerrainTreeObstacleRegistry.Invalidate();
        }
        private static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before changing forest stages.");
        }
        private static int LoadStage() => File.Exists(StatePath) ? JsonUtility.FromJson<State>(File.ReadAllText(StatePath)).stage : 0;
        private static void SaveStage(int stage)
        {
            if (AutumnBaseline <= 0 && File.Exists(StatePath)) AutumnBaseline = JsonUtility.FromJson<State>(File.ReadAllText(StatePath)).autumnBaseline;
            File.WriteAllText(StatePath, JsonUtility.ToJson(new State { stage = stage, autumnBaseline = AutumnBaseline }, true));
            AssetDatabase.ImportAsset(StatePath);
        }
        private static void BuildSaved(int stage)
        {
            RequireEditMode();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
            Directory.CreateDirectory(Output);
            const string checkpoint = "Tools/VegetationDiversity/Checkpoints/Main_BeforeCentralForest.unity";
            Directory.CreateDirectory("Tools/VegetationDiversity/Checkpoints");
            if (!File.Exists(checkpoint)) File.Copy(ScenePath, checkpoint);
            var state = File.Exists(StatePath) ? JsonUtility.FromJson<State>(File.ReadAllText(StatePath)) : new State();
            AutumnBaseline = state.layoutVersion >= 3 && state.autumnBaseline > 0 ? state.autumnBaseline :
                UnityEngine.Object.FindObjectsOfType<SceneTreeObstacle>().Count(t => t.name.StartsWith("FabTree") && NorthwestAutumnForest.SignedDistance(XZ(t.transform.position)) >= 0f);
            BuildInScene(stage);
            ValidateInScene();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            SaveStage(stage);
            AssetDatabase.SaveAssets();
        }
        // Called only after the base scene, dinosaurs and biome planting have been rebuilt.
        internal static void SeedConfiguredStage()
        {
            int stage = LoadStage();
            AutumnBaseline = JsonUtility.FromJson<State>(File.ReadAllText(StatePath)).autumnBaseline;
            if (stage > 0) { BuildInScene(stage); ValidateInScene(); }
        }
        private static void BuildInScene(int stage)
        {
            GameObject root = GameObject.Find(RootName);
            // Migrate the old count-quota layout once; repeat builds retain the revised layout.
            if (root != null && (root.GetComponentInChildren<ForestRegionBackup>() == null || root.GetComponentInChildren<ForestRegionBackup>().layoutVersion != 4))
            { RemoveOwnedRoot(root); root = null; }
            if (root != null && stage == 1 && root.transform.Find("Stage 2") != null)
                UnityEngine.Object.DestroyImmediate(root.transform.Find("Stage 2").gameObject);
            if (root != null && root.transform.Find("Stage " + stage) != null)
            { WriteReport(CreateContext(), root, stage); Debug.Log("CENTRAL_FOREST_REPEAT: existing stage retained, no duplicate planting."); return; }
            bool createdRoot = root == null;
            GameObject preview = GameObject.Find(PreviewName);
            if (createdRoot) root = new GameObject(RootName);
            if (preview != null) preview.SetActive(false);
            var added = new List<GameObject>();
            try
            {
                NorthwestAutumnForest.Apply(root);
                Context context = CreateContext(true);
                for (int phase = 1; phase <= stage; phase++)
                {
                    if (root.transform.Find("Stage " + phase) != null) continue;
                    var group = new GameObject("Stage " + phase);
                    group.transform.SetParent(root.transform, false); added.Add(group);
                    float fraction = phase == 1 ? .65f : .9f;
                    int target = Mathf.CeilToInt(context.ReferenceDensity * context.PlantableArea * fraction);
                    Plant(context, group.transform, target, phase, phase == 1);
                }
                if (preview != null)
                {
                    // Keep the exact original five poses for the Remove command; inactive objects are never registered.
                    preview.name = "Preview Backup"; preview.transform.SetParent(root.transform, true);
                }
                NorthwestAutumnForest.HalveAutumn(root);
                RouteObstacleClearance.BakeRuntimeProfiles(root);
                context.Stems = CollectStems(context.Terrain);
                WriteReport(context, root, stage);
                TerrainTreeObstacleRegistry.Invalidate();
            }
            catch
            {
                if (preview != null) { preview.transform.SetParent(null); preview.name = PreviewName; preview.SetActive(true); }
                foreach (var item in added) UnityEngine.Object.DestroyImmediate(item);
                if (createdRoot) { NorthwestAutumnForest.Restore(root); UnityEngine.Object.DestroyImmediate(root); }
                throw;
            }
        }
        private static Context CreateContext(bool includeRouteEdges = false)
        {
            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null) throw new InvalidOperationException("Main scene terrain missing.");
            var c = new Context { Terrain = terrain,
                TrunkSafe = RouteObstacleClearance.CreateTrunkCandidateProbe(terrain),
                SolidSafe = RouteObstacleClearance.CreateSolidCandidateProbe(terrain),
                TreeSafe = RouteObstacleClearance.CreateTreeCandidateProbe(terrain),
                Rocks = UnityEngine.Object.FindObjectsOfType<Renderer>().Where(r =>
                    r.name.IndexOf("rock", StringComparison.OrdinalIgnoreCase) >= 0 && r.GetComponentInParent<SceneTreeObstacle>() == null)
                    .Select(r => r.bounds).ToList(),
                Spawns = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>().Select(a => XZ(a.transform.position)).ToArray(),
                Stems = CollectStems(terrain), Plantable = new List<Vector2>(), Patches = MakePatches() };
            for (float x = Core.xMin + 1f; x < Core.xMax; x += 2f)
            for (float z = Core.yMin + 1f; z < Core.yMax; z += 2f)
            {
                var p = new Vector2(x, z);
                if (SiteSafe(c, p, .85f) && c.TrunkSafe(p, .85f)) c.Plantable.Add(p);
            }
            c.ActualPlantableArea = c.Plantable.Count * 4f;
            // Keep the approved stage-one density denominator fixed when the clearance policy changes.
            // Report the newly available area separately; it is not permission to add more trees.
            c.PlantableArea = 7084f;
            c.RouteEdges = includeRouteEdges ? MakeRouteEdges(c) : new List<Vector2>();
            if (c.PlantableArea <= 0f) throw new InvalidOperationException("No plantable centre area.");
            MeasureReference(c);
            return c;
        }
        private static List<Patch> MakePatches()
        {
            var random = new System.Random(Seed);
            var patches = new List<Patch>();
            for (int i = 0; i < 38; i++)
            {
                Vector2 p = new Vector2(Range(random, -58f, 58f), Range(random, -48f, 80f));
                float autumn = NorthwestAutumnForest.Probability(p);
                patches.Add(new Patch { Center = p, Size = Range(random, 9f, 19f), Autumn = autumn,
                    Species = random.Next(5) });
            }
            return patches;
        }
        private static List<Vector2> MakeRouteEdges(Context c)
        {
            var points = new List<Vector2>(); var cells = new HashSet<Vector2Int>();
            for (int route = 0; route < MovementCorridor.RouteCount; route++)
            for (int i = 0; i < MovementCorridor.SampleCount(route); i += 3)
            {
                var sample = MovementCorridor.GetSample(route, i);
                Vector2 right = new Vector2(sample.Tangent.y, -sample.Tangent.x);
                foreach (float side in new[] { -1f, 1f })
                {
                    float edge = -1f;
                    for (float distance = .25f; distance <= 16f; distance += .25f)
                    {
                        Vector2 p = sample.Position + right * distance * side;
                        if (c.TrunkSafe(p, .65f)) { edge = distance; break; }
                    }
                    if (edge < 0f) continue;
                    for (float band = 0f; band <= 5f; band += .5f)
                    {
                        Vector2 p = sample.Position + right * (edge + band) * side;
                        var cell = new Vector2Int(Mathf.RoundToInt(p.x * 2f), Mathf.RoundToInt(p.y * 2f));
                        if (Core.Contains(p) && SiteSafe(c, p, .65f) && c.TrunkSafe(p, .65f) && cells.Add(cell)) { points.Add(p); if (band <= 2f) { points.Add(p); points.Add(p); } }
                    }
                }
            }
            Debug.Log($"CENTRAL_FOREST_ROUTE_EDGES: {points.Count} candidates, 0-5m beyond the core envelope (0-2m preferred), six routes.");
            return points;
        }
        private static bool SiteSafe(Context c, Vector2 p, float radius)
        {
            // The local pass owns planting here once enabled; a full Bake must not first
            // fill its reserved action spaces with the older central planting pass.
            if (File.Exists(RainforestSample.EnabledPath) && RainforestSample.Area.Contains(p)) return false;
            if (RainforestExpansion.Owns(p)) return false;
            if (MainCreekIntegration.NearCreek(p.x, p.y, radius + 1.5f)) return false;
            Vector3 local = new Vector3(p.x, 0f, p.y) - c.Terrain.transform.position;
            if (c.Terrain.terrainData.GetSteepness(local.x / c.Terrain.terrainData.size.x,
                local.z / c.Terrain.terrainData.size.z) > 30f) return false;
            if (c.Spawns.Any(s => Vector2.Distance(s, p) < radius + 3f)) return false;
            if (Vector2.Distance(p, new Vector2(0f, -16f)) < radius + 2.5f) return false;
            foreach (Bounds b in c.Rocks)
                if (p.x > b.min.x - radius && p.x < b.max.x + radius && p.y > b.min.z - radius && p.y < b.max.z + radius) return false;
            return true;
        }
        private static void MeasureReference(Context c)
        {
            var centers = new List<Vector2>();
            foreach (float x in new[] { -82f, 82f }) foreach (float z in new[] { -64f, -32f, 0f, 32f, 64f }) centers.Add(new Vector2(x, z));
            foreach (float x in new[] { -32f, 0f, 32f }) centers.Add(new Vector2(x, -82f));
            var densities = new List<float>(); var covers = new List<float>(); var text = new StringBuilder();
            foreach (Vector2 center in centers)
            {
                Rect plot = new Rect(center - Vector2.one * 8f, Vector2.one * 16f);
                var points = new List<Vector2>();
                for (float x = plot.xMin + 1f; x < plot.xMax; x += 2f)
                for (float z = plot.yMin + 1f; z < plot.yMax; z += 2f)
                { var p = new Vector2(x, z); if (SiteSafe(c, p, .85f)) points.Add(p); }
                if (points.Count < 32) continue;
                int count = c.Stems.Count(s => plot.Contains(s.Point));
                float density = count / (points.Count * 4f);
                float cover = points.Count(p => c.Stems.Any(s => (s.Point - p).sqrMagnitude < s.Crown * s.Crown)) / (float)points.Count;
                densities.Add(density); covers.Add(cover);
                text.AppendLine($"| ({center.x:F0}, {center.y:F0}) | {count} | {points.Count * 4} | {density * 100:F2} | {cover:P0} |");
            }
            if (densities.Count < 5) throw new InvalidOperationException("Too few valid perimeter reference plots.");
            densities.Sort(); covers.Sort();
            c.ReferenceDensity = densities[densities.Count / 2]; c.ReferenceCover = covers[covers.Count / 2]; c.Samples = text.ToString();
            if (c.ReferenceDensity <= 0f) throw new InvalidOperationException("Reference forest density is zero.");
        }
        private static List<Stem> CollectStems(Terrain terrain)
        {
            var stems = new List<Stem>();
            foreach (TreeInstance tree in terrain.terrainData.treeInstances)
            {
                if (tree.widthScale <= 0f) continue;
                GameObject prefab = terrain.terrainData.treePrototypes[tree.prototypeIndex].prefab;
                Bounds b = BoundsOf(prefab);
                Vector3 position = terrain.transform.TransformPoint(Vector3.Scale(tree.position, terrain.terrainData.size));
                stems.Add(new Stem { Point = XZ(position), Radius = .7f * tree.widthScale,
                    Crown = Mathf.Max(b.extents.x, b.extents.z) * tree.widthScale, Name = prefab.name });
            }
            foreach (var tree in UnityEngine.Object.FindObjectsOfType<SceneTreeObstacle>())
            {
                Bounds b = BoundsOf(tree.gameObject);
                stems.Add(new Stem { Point = XZ(tree.transform.position), Radius = tree.Radius,
                    Crown = Mathf.Max(b.extents.x, b.extents.z), Autumn = tree.name.StartsWith("FabTree", StringComparison.Ordinal), Name = tree.name });
            }
            return stems;
        }
        private static int CoreCount(Context c) => c.Stems.Count(s => Core.Contains(s.Point));

        internal static void PlantLocalRepair(Transform parent)
        {
            Context c = CreateContext();
            var random = new System.Random(731649);
            var sites = new List<Vector2>();
            float area = 0f;
            for (float x = LocalForestRepair.Center.x - 35f; x < LocalForestRepair.Center.x + 35f; x += 2f)
            for (float z = LocalForestRepair.Center.y - 35f; z < LocalForestRepair.Center.y + 35f; z += 2f)
            {
                var p = new Vector2(x + 1f, z + 1f);
                if (LocalForestRepair.Weight(p) > 0f && SiteSafe(c, p, .85f) && c.TrunkSafe(p, .85f))
                { sites.Add(p); area += 4f * LocalForestRepair.Weight(p); }
            }
            float target = c.ReferenceDensity * area * .65f;
            float total = c.Stems.Sum(s => LocalForestRepair.Weight(s.Point));
            var patches = Enumerable.Range(0, 18).Select(i => LocalForestRepair.Center +
                new Vector2(Range(random, -29f, 29f), Range(random, -29f, 29f))).ToArray();
            int added = 0;
            for (int attempt = 0; attempt < 25000 && total < target && sites.Count > 0; attempt++)
            {
                Vector2 p = sites[random.Next(sites.Count)] + new Vector2(Range(random, -.9f, .9f), Range(random, -.9f, .9f));
                float weight = LocalForestRepair.Weight(p);
                if (random.NextDouble() > weight || Vector2.Distance(p, LocalForestRepair.Center) < 2.5f) continue;
                float cluster = patches.Min(q => Vector2.Distance(q, p));
                if (random.NextDouble() > Mathf.Lerp(.9f, .2f, Mathf.Clamp01(cluster / 12f))) continue;
                Species spec = SpeciesList[new[] { 0, 1, 2, 3, 4, 7, 9 }[random.Next(7)]];
                float scale = Range(random, .85f, 1.12f), radius = spec.Radius * scale;
                if (!SiteSafe(c, p, radius) || !c.TrunkSafe(p, radius) ||
                    c.Stems.Any(s => Vector2.Distance(s.Point, p) < s.Radius + radius + 1.1f)) continue;
                GameObject item = Instantiate(spec.Path, parent);
                item.name = "LocalTree_" + (added + 1).ToString("000");
                item.transform.localScale *= spec.Height * scale / BoundsOf(item).size.y;
                item.transform.rotation = Quaternion.Euler(0, Range(random, 0f, 360f), 0);
                var marker = item.GetComponent<SceneTreeObstacle>() ?? item.AddComponent<SceneTreeObstacle>();
                marker.SetRadius(radius); Ground(item, c.Terrain, p);
                if (!c.TreeSafe(item.transform)) { UnityEngine.Object.DestroyImmediate(item); continue; }
                c.Stems.Add(new Stem { Point = p, Radius = radius, Name = item.name });
                total += weight; added++;
            }
            var understory = new GameObject("Local Understory"); understory.transform.SetParent(parent, false);
            int lowCount = 0;
            for (int attempt = 0; attempt < 1600; attempt++)
            {
                Vector2 p = LocalForestRepair.Center + new Vector2(Range(random, -35f, 35f), Range(random, -35f, 35f));
                float weight = LocalForestRepair.Weight(p);
                if (random.NextDouble() > weight * .65f || weight <= 0f || !SiteSafe(c, p, .25f) ||
                    Vector2.Distance(p, LocalForestRepair.Center) < 1.5f) continue;
                if (c.Stems.Any(s => Vector2.Distance(s.Point, p) < s.Radius + .35f)) continue;
                if (understory.transform.Cast<Transform>().Any(t => Vector2.Distance(XZ(t.position), p) < 1.2f)) continue;
                bool fern = random.NextDouble() < .75;
                GameObject item = Instantiate(fern ? "Assets/Environment/Vegetation/Fern02/Prepared/Prefabs/Fern02_" + "ABCD"[random.Next(4)] + ".prefab" :
                    "Assets/ThirdParty/NatureStarterKit2/Nature/bush0" + random.Next(1, 7) + ".prefab", understory.transform);
                item.name = "Local" + (fern ? "Fern_" : "Shrub_") + (++lowCount).ToString("000");
                item.transform.localScale *= Range(random, .4f, .8f) / BoundsOf(item).size.y;
                item.transform.rotation = Quaternion.Euler(0f, Range(random, 0f, 360f), 0f);
                foreach (var collider in item.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
                foreach (var marker in item.GetComponentsInChildren<SceneTreeObstacle>()) UnityEngine.Object.DestroyImmediate(marker);
                Ground(item, c.Terrain, p);
            }
            RouteObstacleClearance.BakeRuntimeProfiles(parent.gameObject);
            TerrainTreeObstacleRegistry.Invalidate();
            Debug.Log($"LOCAL_FOREST_DENSITY: area={area:F0}m2 reference={c.ReferenceDensity*100:F2}/100m2 target={target:F1} weightedTotal={total:F1} added={added} understory={lowCount}");
            if (total + 1f < target) Debug.LogWarning("LOCAL_FOREST_DENSITY_SHORTFALL: retained all safety exclusions.");
        }
        private static int AutumnCount(Context c) => c.Stems.Count(s => Core.Contains(s.Point) && s.Autumn);
        private static void Plant(Context c, Transform parent, int target, int phase, bool featureTrees)
        {
            if (phase == 1) PlantRouteCanopy(c, parent, target);
            var random = new System.Random(Seed + phase * 101);
            GameObject candidate = null;
            int count = CoreCount(c), failures = 0, attempts = 0;
            var used = new Dictionary<string, int>();
            try
            {
                // Each reviewed new species gets a natural location, including the small fir group.
                var required = featureTrees ? new Queue<int>(new[] { 7, 8, 9, 10, 11 }) : new Queue<int>();
                while ((count < target || required.Count > 0) && attempts++ < 45000)
                {
                    Patch patch = c.Patches[random.Next(c.Patches.Count)];
                    float angle = Range(random, 0f, Mathf.PI * 2f);
                    float spread = Mathf.Sqrt((float)random.NextDouble()) * patch.Size;
                    Vector2 p = patch.Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * spread;
                    bool routeEdge = c.RouteEdges.Count > 0 && (attempts < 18000 || random.NextDouble() < .7);
                    if (routeEdge)
                    {
                        p = c.RouteEdges[random.Next(c.RouteEdges.Count)] + new Vector2(Range(random, -.45f, .45f), Range(random, -.45f, .45f));
                        patch = c.Patches.OrderBy(t => (t.Center - p).sqrMagnitude).First(); spread = Vector2.Distance(p, patch.Center);
                    }
                    if (!Core.Contains(p)) continue;
                    // Patch interiors are dense, fringes sparse; low-frequency noise creates persistent gaps.
                    float occupancy = Mathf.Lerp(.95f, .32f, spread / patch.Size) *
                        Mathf.Lerp(.35f, 1f, Mathf.PerlinNoise((p.x + 170f) * .047f, (p.y + 230f) * .047f));
                    if (!routeEdge && random.NextDouble() > occupancy) continue;
                    bool autumn = random.NextDouble() < NorthwestAutumnForest.Probability(p);
                    int speciesIndex;
                    if (required.Count > 0) speciesIndex = required.Peek();
                    else if (autumn)
                    { speciesIndex = 12 + (random.NextDouble() < .8 ? patch.Species % 3 : random.Next(3)); }
                    else
                    {
                        float roll = (float)random.NextDouble();
                        speciesIndex = roll < .72f ? patch.Species : roll < .84f ? 4 : roll < .91f ? 5 :
                            roll < .94f ? 6 : roll < .963f ? 7 : roll < .985f ? 9 : 8;
                        // Tall feature trees are occasional accents, never the bulk of the forest.
                        if (Vector2.Distance(p, new Vector2(42f, 60f)) < 16f && random.NextDouble() < .035) speciesIndex = 11;
                        else if (random.NextDouble() < .012) speciesIndex = 10;
                        if (routeEdge && speciesIndex >= 4 && speciesIndex != 10 && random.NextDouble() < .65)
                            speciesIndex = new[] { 0, 1, 2, 9 }[random.Next(4)];
                    }
                    Species spec = SpeciesList[speciesIndex];
                    float size = Range(random, .82f, 1.14f), radius = spec.Radius * size;
                    if (!SiteSafe(c, p, radius) || !c.TrunkSafe(p, radius)) continue;
                    if (c.Stems.Any(s => Vector2.Distance(s.Point, p) < s.Radius + radius + (routeEdge ? .8f : 1.1f))) continue;
                    // Dense tree groups still contain a few narrow, connected openings for walking.
                    if (candidate != null) UnityEngine.Object.DestroyImmediate(candidate);
                    candidate = Instantiate(spec.Path, parent);
                    candidate.name = spec.Name + "_" + (count + 1).ToString("0000");
                    Bounds b = BoundsOf(candidate);
                    candidate.transform.localScale *= spec.Height * size / b.size.y;
                    candidate.transform.rotation = Quaternion.Euler(0f, Range(random, 0f, 360f), 0f);
                    var marker = candidate.GetComponent<SceneTreeObstacle>() ?? candidate.AddComponent<SceneTreeObstacle>();
                    marker.SetRadius(radius);
                    Ground(candidate, c.Terrain, p);
                    if (!c.TreeSafe(candidate.transform)) { failures++; continue; }
                    Bounds crown = BoundsOf(candidate);
                    c.Stems.Add(new Stem { Point = p, Radius = radius, Crown = Mathf.Max(crown.extents.x, crown.extents.z), Autumn = spec.Autumn, Name = spec.Name });
                    used[spec.Name] = used.ContainsKey(spec.Name) ? used[spec.Name] + 1 : 1;
                    if (required.Count > 0) required.Dequeue();
                    candidate = null; count++;
                }
                if (required.Count > 0) throw new InvalidOperationException("Could not safely place all five reviewed tree species.");
                PlantUnderstory(c, parent, phase);
                PlantRouteGroundcover(c, parent, phase);
                Debug.Log($"CENTRAL_FOREST_PLANTED stage={phase} centreTrees={count} target={target} attempts={attempts} woodClearanceRejects={failures} species=" +
                    string.Join(", ", used.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value)));
                if (count < target) Debug.LogWarning($"CENTRAL_FOREST_DENSITY_SHORTFALL: {target - count} trees; safety exclusions were retained.");
            }
            finally { if (candidate != null) UnityEngine.Object.DestroyImmediate(candidate); }
        }
        private static void PlantRouteCanopy(Context c, Transform parent, int target)
        {
            var anchors = new GameObject("Route Canopy Anchors"); anchors.transform.SetParent(parent, false);
            int added = 0, gaps = 0;
            for (int route = 0; route < MovementCorridor.RouteCount; route++)
            foreach (float fraction in new[] { .18f, .5f, .82f })
            {
                MovementCorridor.GetSampleAt(route, MovementCorridor.Length(route) * fraction, out Vector2 point, out Vector2 forward);
                if (CoreCount(c) >= target || added >= 12 || c.Stems.Any(s => Vector2.Distance(s.Point, point) < s.Crown * .9f)) continue;
                Vector2 right = new Vector2(forward.y, -forward.x);
                bool placed = false;
                var candidates = c.RouteEdges.Where(p => Vector2.Distance(p, point) < 15f)
                    .OrderBy(p => Vector2.Distance(p, point)).Take(60).ToArray();
                foreach (Vector2 p in candidates)
                {
                    bool autumn = NorthwestAutumnForest.Probability(p) > .5f;
                    Species spec = SpeciesList[autumn ? 14 : 10];
                    float radius = autumn ? 1.1f : .85f;
                    if (!SiteSafe(c, p, radius) || !c.TrunkSafe(p, radius) || c.Stems.Any(s => Vector2.Distance(s.Point, p) < s.Radius + radius + .8f)) continue;
                    GameObject tree = Instantiate(spec.Path, anchors.transform);
                    tree.name = spec.Name + "_Canopy_" + route + "_" + Mathf.RoundToInt(fraction * 100f);
                    tree.transform.localScale *= spec.Height / BoundsOf(tree).size.y;
                    tree.transform.rotation = Quaternion.Euler(0f, (route * 83f + fraction * 193f) % 360f, 0f);
                    var marker = tree.GetComponent<SceneTreeObstacle>() ?? tree.AddComponent<SceneTreeObstacle>(); marker.SetRadius(radius);
                    Ground(tree, c.Terrain, p);
                    Bounds b = BoundsOf(tree); float crown = Mathf.Max(b.extents.x, b.extents.z);
                    if (Vector2.Distance(p, point) > crown || !c.TreeSafe(tree.transform)) { UnityEngine.Object.DestroyImmediate(tree); continue; }
                    c.Stems.Add(new Stem { Point = p, Radius = radius, Crown = crown, Autumn = spec.Autumn, Name = spec.Name });
                    added++; placed = true; break;
                }
                if (!placed) gaps++;
            }
            Debug.Log($"CENTRAL_FOREST_CANOPY_ANCHORS: {added} tall native-proportion trees reach over observation routes; {gaps} gaps retained after actual wood clearance tests.");
        }
        private static void PlantUnderstory(Context c, Transform parent, int phase)
        {
            var group = new GameObject("Understory"); group.transform.SetParent(parent, false);
            var random = new System.Random(Seed + phase * 719);
            var trees = parent.GetComponentsInChildren<SceneTreeObstacle>().ToArray();
            var points = new List<Vector2>(); int index = 0;
            foreach (var tree in trees)
            {
                for (int j = 0; j < 3; j++)
                {
                    float angle = Range(random, 0f, Mathf.PI * 2f), spread = Range(random, 1.4f, 4.5f);
                    Vector2 p = XZ(tree.transform.position) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * spread;
                    if (!Core.Contains(p) || !SiteSafe(c, p, .3f)) continue;
                    if (points.Any(other => Vector2.Distance(other, p) < 1.1f)) continue;
                    bool fern = random.NextDouble() < .68;
                    string path = fern ? "Assets/Environment/Vegetation/Fern02/Prepared/Prefabs/Fern02_" + "ABCD"[random.Next(4)] + ".prefab" :
                        "Assets/ThirdParty/NatureStarterKit2/Nature/bush0" + random.Next(1, 7) + ".prefab";
                    GameObject item = Instantiate(path, group.transform); item.name = (fern ? "Fern" : "Shrub") + "_" + (++index);
                    item.transform.rotation = Quaternion.Euler(0f, Range(random, 0f, 360f), 0f);
                    item.transform.localScale *= Range(random, fern ? .65f : .7f, fern ? 1.35f : 1.6f) / BoundsOf(item).size.y;
                    foreach (Collider collider in item.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
                    Ground(item, c.Terrain, p); points.Add(p);
                }
            }
            // Sparse deadwood sits well inside forest patches and cannot bridge the dinosaur corridor.
            for (int i = 0; i < Mathf.Min(12, trees.Length / 25); i++)
            {
                Vector2 p = XZ(trees[random.Next(trees.Length)].transform.position) + new Vector2(3f, 2f);
                if (!Core.Contains(p) || !SiteSafe(c, p, 2.5f) || !c.SolidSafe(p, 3f)) continue;
                if (c.Stems.Any(s => Vector2.Distance(s.Point, p) < s.Radius + 2.1f)) continue;
                GameObject item = Instantiate("Assets/Environment/RocksAndDeadwood/Deadwood/Prepared/Prefabs/" + (i % 3 == 0 ? "DeadwoodLogTrunk02" : "DeadwoodStump01") + ".prefab", group.transform);
                item.name = "Deadwood_" + i; item.transform.localScale *= .65f;
                item.transform.rotation = Quaternion.Euler(0f, Range(random, 0f, 360f), 0f);
                Ground(item, c.Terrain, p);
                // Bound the full solid prop, rather than only its origin, using a conservative ground circle.
                Bounds b = BoundsOf(item); float reach = new Vector2(b.extents.x, b.extents.z).magnitude + Vector2.Distance(p, XZ(b.center));
                if (!SiteSafe(c, p, reach) || !c.SolidSafe(p, reach)) UnityEngine.Object.DestroyImmediate(item);
            }
        }
        private static void PlantRouteGroundcover(Context c, Transform parent, int phase)
        {
            var group = new GameObject("Route Groundcover"); group.transform.SetParent(parent, false);
            var random = new System.Random(Seed + phase * 1919); var points = new List<Vector2>();
            // Reuse the existing grass texture with a small crossed tuft mesh, independent of Terrain details.
            var tuft = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Environment/Terrain/Prepared/GroundEcology/Surface/RouteGrassTuft.asset");
            if (tuft == null)
            {
                tuft = new Mesh { name = "Route grass tuft" };
                tuft.vertices = new[] { new Vector3(-.18f,0,0),new Vector3(.18f,0,0),new Vector3(.1f,1,0),new Vector3(-.1f,1,0),new Vector3(0,0,-.18f),new Vector3(0,0,.18f),new Vector3(0,1,.1f),new Vector3(0,1,-.1f) };
                tuft.uv = new[] { Vector2.zero,Vector2.right,Vector2.one,Vector2.up,Vector2.zero,Vector2.right,Vector2.one,Vector2.up };
                tuft.triangles = new[] { 0,2,1,0,3,2,4,6,5,4,7,6 }; tuft.RecalculateNormals(); tuft.RecalculateBounds();
                AssetDatabase.CreateAsset(tuft,"Assets/Environment/Terrain/Prepared/GroundEcology/Surface/RouteGrassTuft.asset");
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/Terrain/Prepared/GroundEcology/Surface/RouteGrassTuft.mat");
            if (mat == null)
            {
                mat = new Material(Shader.Find("Jurassic World/Foliage Cutout Double Sided"));
                mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ThirdParty/NatureStarterKit2/Textures/grass01.tga");
                mat.color = new Color(.65f,.8f,.5f); mat.SetFloat("_Cutoff",.35f);
                AssetDatabase.CreateAsset(mat,"Assets/Environment/Terrain/Prepared/GroundEcology/Surface/RouteGrassTuft.mat");
            }
            int count = 0;
            for (int route = 0; route < MovementCorridor.RouteCount; route++)
            for (float arc = 1f; arc < MovementCorridor.Length(route); arc += 2.2f)
            {
                MovementCorridor.GetSampleAt(route,arc,out Vector2 anchor,out Vector2 forward);
                if (Mathf.PerlinNoise((anchor.x+72f)*.16f,(anchor.y+153f)*.16f) < .48f || random.NextDouble() < .28) continue;
                Vector2 right = new Vector2(forward.y,-forward.x);
                for (int j = 0; j < random.Next(2,5); j++)
                {
                    Vector2 p = anchor + right * Range(random,-1.6f,1.6f) + forward * Range(random,-1f,1f);
                    if (!SiteSafe(c,p,.15f) || points.Any(q => Vector2.Distance(q,p)<.55f)) continue;
                    float choice = (float)random.NextDouble(); GameObject item;
                    if (choice < .6f)
                    {
                        item = new GameObject("RouteGrass_" + (++count)); item.transform.SetParent(group.transform,false);
                        item.AddComponent<MeshFilter>().sharedMesh = tuft; var renderer = item.AddComponent<MeshRenderer>(); renderer.sharedMaterial = mat; renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                        item.transform.localScale = new Vector3(Range(random,.7f,1.4f),Range(random,.15f,.5f),Range(random,.7f,1.4f));
                    }
                    else
                    {
                        bool fern = choice < .85f;
                        item = Instantiate(fern ? "Assets/Environment/Vegetation/Fern02/Prepared/Prefabs/Fern02_" + "ABCD"[random.Next(4)] + ".prefab" : "Assets/ThirdParty/NatureStarterKit2/Nature/bush0" + random.Next(1,7) + ".prefab",group.transform);
                        item.name = (fern ? "RouteFern_" : "RouteShrub_") + (++count);
                        item.transform.localScale *= Range(random,.4f,.8f)/BoundsOf(item).size.y;
                    }
                    item.transform.rotation = Quaternion.Euler(0f,Range(random,0,360),0);
                    foreach (var collider in item.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
                    foreach (var marker in item.GetComponentsInChildren<SceneTreeObstacle>()) UnityEngine.Object.DestroyImmediate(marker);
                    Ground(item,c.Terrain,p); points.Add(p);
                }
            }
            Debug.Log($"ROUTE_GROUNDCOVER_OK: {count} non-solid grass/fern/shrub instances on six routes; no trunk exclusion.");
        }
        internal static GameObject Instantiate(string path, Transform parent)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new FileNotFoundException("Existing vegetation prefab missing", path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            // Imported nested LODGroups must not also own renderers assigned to the prefab's root group.
            LODGroup rootGroup = instance.GetComponent<LODGroup>();
            if (rootGroup != null)
            {
                foreach (LODGroup nested in instance.GetComponentsInChildren<LODGroup>(true))
                    if (nested != rootGroup) UnityEngine.Object.DestroyImmediate(nested);
                LOD[] lods = rootGroup.GetLODs();
                if (lods.Length == 3)
                { lods[0].screenRelativeTransitionHeight = .55f; lods[1].screenRelativeTransitionHeight = .2f; lods[2].screenRelativeTransitionHeight = .018f; rootGroup.SetLODs(lods); }
            }
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            { renderer.shadowCastingMode = ShadowCastingMode.TwoSided; renderer.receiveShadows = true; }
            return instance;
        }
        internal static Bounds BoundsOf(GameObject root)
        {
            LODGroup group = root.GetComponent<LODGroup>();
            Renderer[] renderers = group != null && group.GetLODs().Length > 0 ? group.GetLODs()[0].renderers : root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("Vegetation renderer missing: " + root.name);
            Bounds b = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) b.Encapsulate(renderer.bounds); return b;
        }
        internal static void Ground(GameObject root, Terrain terrain, Vector2 p)
        {
            root.transform.position = new Vector3(p.x, 0f, p.y);
            float ground = terrain.SampleHeight(root.transform.position) + terrain.transform.position.y;
            root.transform.position += Vector3.up * (ground - BoundsOf(root).min.y - .02f);
        }
        private static float Range(System.Random random, float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
        private static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
        private static void WriteReport(Context c, GameObject root, int stage)
        {
            Directory.CreateDirectory(Output);
            int total = CoreCount(c), autumn = AutumnCount(c);
            float density = total / c.PlantableArea;
            float cover = c.Plantable.Count(p => c.Stems.Any(s => (s.Point - p).sqrMagnitude < s.Crown * s.Crown)) / (float)c.Plantable.Count;
            var text = new StringBuilder("# 中心密林密度报告\n\n");
            text.AppendLine($"阶段：{stage}；随机种子：{Seed}；范围：X[-60,60] Z[-50,82]。");
            text.AppendLine($"\n第一阶段固定标定面积：{c.PlantableArea:F0} m²（沿用已确认的净空口径以保持399棵总量）；新核心净空规则的可种植面积：{c.ActualPlantableArea:F0} m²。后者的实际密度为 {CoreCount(c)/c.ActualPlantableArea*100:F2} 棵/100m²，不用新增树填满放宽的路线空间。");
            text.AppendLine($"\n中心乔木：{total} 棵；秋叶树：{autumn} 棵（{autumn / (float)total:P1}，仅统计）。秋叶林按全地图西北约五分之一面积分区，主体秋叶保留修改前数量的一半。");
            Transform understory = root.transform.Find("Stage 1/Understory");
            Transform secondUnderstory = root.transform.Find("Stage 2/Understory");
            text.AppendLine($"\n林下新增实例：{(understory != null ? understory.childCount : 0) + (secondUnderstory != null ? secondUnderstory.childCount : 0)}（蕨类、灌木和少量倒木/树桩）；路线点缀 {root.transform.Find("Stage 1/Route Groundcover").childCount} 个（另计，无碰撞草/蕨/小灌木）。加密组包含替代原五树展示的实例；原展示保留为不激活的备份。");
            text.AppendLine($"\n外围参考中位密度：{c.ReferenceDensity * 100:F2} 棵/100m²；中心按原固定标定面积为 {density * 100:F2} 棵/100m²，即外围 **{density / c.ReferenceDensity:P1}**。按新可种植面积则为外围 **{total / c.ActualPlantableArea / c.ReferenceDensity:P1}**；未增加总棵数，因此新口径不满足60%～70%。");
            text.AppendLine($"\n树冠覆盖估计：外围 {c.ReferenceCover:P1}，中心 {cover:P1}。以 LOD0 树冠外接圆在同一 2m 网格估算，属于覆盖上界，不等同于叶片实际覆盖率。");
            float desired = stage == 1 ? .65f : .9f;
            int missing = Mathf.Max(0, Mathf.CeilToInt(c.ReferenceDensity * c.PlantableArea * desired) - total);
            text.AppendLine($"\n按原标定密度目标缺口：{missing} 棵。若按新可种植面积达到65%，还需 {Mathf.Max(0,Mathf.CeilToInt(c.ReferenceDensity*c.ActualPlantableArea*.65f)-total)} 棵；本次按“不增加中心总棵数”保持399棵，仅重排到路线附近。");
            text.AppendLine("\n## 外围 16m × 16m 样方\n\n| 中心 X,Z | 乔木 | 有效面积 m² | 棵/100m² | 树冠覆盖上界 |\n|---|---:|---:|---:|---:|");
            text.Append(c.Samples);
            text.AppendLine("\n## 新增树种\n\n| 树种 | 数量 |\n|---|---:|");
            foreach (var group in root.GetComponentsInChildren<SceneTreeObstacle>().GroupBy(t => t.name.Substring(0, t.name.LastIndexOf('_'))).OrderBy(g => g.Key))
                text.AppendLine($"| {group.Key} | {group.Count()} |");
            text.AppendLine("\n第一阶段供 Editor Play 人工确认。第二阶段仅通过独立菜单追加，不自动运行。\n\n操作入口：Jurassic World → Central Forest；Remove 撤销加密和西北区域转换，并恢复保留的五树预览及被替换的原树。\n\n外围原型区间、地形高度和溪流数据保持原值；原树等量区域转换有完整备份。见 NorthwestRegion.md 和 RouteEdges.md。");
            File.WriteAllText(Output + "/DensityReport.md", text.ToString());
            Debug.Log($"CENTRAL_FOREST_DENSITY stage={stage} total={total} autumn={autumn} share={autumn / (float)total:P1} plantable={c.PlantableArea:F0} reference={c.ReferenceDensity * 100:F2}/100m2 ratio={density / c.ReferenceDensity:P1} canopyUpperBound={cover:P1} deficit={missing}");
        }
        private static void ValidateInScene()
        {
            GameObject root = GameObject.Find(RootName);
            if (root == null) throw new InvalidOperationException("Central forest has not been built.");
            Terrain terrain = Terrain.activeTerrain;
            foreach (var tree in root.GetComponentsInChildren<SceneTreeObstacle>())
            {
                Vector2 p = XZ(tree.transform.position);
                bool regional = tree.GetComponentInParent<ForestRegionBackup>() != null;
                if ((!regional && !Core.Contains(p)) || MainCreekIntegration.NearCreek(p.x, p.y, tree.Radius + 1.5f)) throw new InvalidOperationException("Tree outside planting area or inside creek: " + tree.name);
                float ground = terrain.SampleHeight(tree.transform.position) + terrain.transform.position.y;
                if (Mathf.Abs(BoundsOf(tree.gameObject).min.y - ground) > .08f) throw new InvalidOperationException("Ungrounded tree: " + tree.name);
                foreach (Renderer renderer in tree.GetComponentsInChildren<Renderer>(true))
                    if (renderer.sharedMaterials.Any(m => m == null || m.shader == null || m.shader.name == "Hidden/InternalErrorShader"))
                        throw new InvalidOperationException("Missing vegetation material: " + tree.name);
                foreach (LODGroup lod in tree.GetComponentsInChildren<LODGroup>(true))
                    if (lod.GetLODs().Any(l => l.renderers.Length == 0 || l.renderers.Any(r => r == null))) throw new InvalidOperationException("Invalid LOD: " + tree.name);
            }
            Transform routeGroundcover = root.transform.Find("Stage 1/Route Groundcover");
            if (routeGroundcover == null || routeGroundcover.GetComponentsInChildren<Collider>().Length > 0 || routeGroundcover.GetComponentsInChildren<SceneTreeObstacle>().Length > 0) throw new InvalidOperationException("Route groundcover is not non-solid.");
            foreach (Transform item in routeGroundcover)
            {
                Bounds b = BoundsOf(item.gameObject); float y = terrain.SampleHeight(item.position) + terrain.transform.position.y;
                if (Mathf.Abs(b.min.y - y) > .08f || MainCreekIntegration.NearCreek(item.position.x,item.position.z,.15f+1.5f)) throw new InvalidOperationException("Route groundcover floats or enters water.");
                if (item.GetComponentsInChildren<Renderer>().Any(r => r.sharedMaterials.Any(m => m == null || m.shader == null))) throw new InvalidOperationException("Route groundcover material missing.");
            }
            TerrainTreeObstacleRegistry.Invalidate();
            NorthwestAutumnForest.ValidateAndReport(root);
            RouteObstacleClearance.Validate(terrain);
            WriteRouteReport(CreateContext());
            Debug.Log($"CENTRAL_FOREST_VALIDATION_OK: {root.GetComponentsInChildren<SceneTreeObstacle>().Length} new trees; ground, creek, materials, LOD references; trees use torso/hips +0m, rocks retain full-body clearance.");
        }
        private static void WriteRouteReport(Context c, string file = "RouteEdges.md")
        {
            var text = new StringBuilder("# 恐龙路线两侧林缘检查\n\n");
            text.AppendLine("六条路线各取18%、50%、82%弧长位置。距离以树干中心衡量；近树为前后12m内、横向同侧。两种眼高均检查附近可见树冠的包围关系，不生成截图。\n");
            text.AppendLine("| 路线 | 弧长比例 | X,Z | 左侧最近树干 m | 右侧最近树干 m | 玩家1.7m/恐龙眼高侧向树冠界框命中 | 上方树冠覆盖估计 | 最近树干距核心包络 m |\n|---|---:|---|---:|---:|---|---|---:|");
            var all = UnityEngine.Object.FindObjectsOfType<SceneTreeObstacle>().Select(t => BoundsOf(t.gameObject)).ToList();
            foreach (TreeInstance tree in c.Terrain.terrainData.treeInstances)
            {
                if (tree.widthScale <= 0f) continue;
                Bounds prefab = BoundsOf(c.Terrain.terrainData.treePrototypes[tree.prototypeIndex].prefab);
                Vector3 p = c.Terrain.transform.TransformPoint(Vector3.Scale(tree.position, c.Terrain.terrainData.size));
                all.Add(new Bounds(p + Vector3.Scale(prefab.center, new Vector3(tree.widthScale, tree.heightScale, tree.widthScale)),
                    Vector3.Scale(prefab.size, new Vector3(tree.widthScale, tree.heightScale, tree.widthScale))));
            }
            DinosaurAgent largest = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>().OrderByDescending(a => a.CollisionRadius).First();
            Renderer[] body = largest.GetComponentsInChildren<Renderer>();
            Bounds dinosaur = body[0].bounds; foreach (Renderer renderer in body.Skip(1)) dinosaur.Encapsulate(renderer.bounds);
            float dinosaurEye = Mathf.Max(1.7f, dinosaur.size.y * .6f);
            int twoSided = 0, crowns = 0, missing = 0;
            var cores = UnityEngine.Object.FindObjectsOfType<DinosaurCoreBody>();
            for (int route = 0; route < MovementCorridor.RouteCount; route++)
            foreach (float fraction in new[] { .18f, .5f, .82f })
            {
                MovementCorridor.GetSampleAt(route, MovementCorridor.Length(route) * fraction, out Vector2 p, out Vector2 forward);
                Vector2 right = new Vector2(forward.y, -forward.x);
                float leftDistance = float.PositiveInfinity, rightDistance = float.PositiveInfinity, coreGap = float.PositiveInfinity;
                foreach (Stem stem in c.Stems)
                {
                    Vector2 delta = stem.Point - p;
                    if (Mathf.Abs(Vector2.Dot(delta, forward)) > 12f) continue;
                    foreach (var core in cores) coreGap = Mathf.Min(coreGap, core.Gap(p, forward, new TreeTrunkSection { center = stem.Point, radius = stem.Radius, bottom = -100f, top = 100f }));
                    float side = Vector2.Dot(delta, right);
                    if (side < 0f) leftDistance = Mathf.Min(leftDistance, delta.magnitude);
                    else rightDistance = Mathf.Min(rightDistance, delta.magnitude);
                }
                if (leftDistance < 12f && rightDistance < 12f) twoSided++; else missing++;
                bool overhead = c.Stems.Any(s => (s.Point - p).sqrMagnitude < s.Crown * s.Crown);
                if (overhead) crowns++;
                var hits = new List<string>();
                foreach (float height in new[] { 1.7f, dinosaurEye })
                {
                    Vector3 origin = new Vector3(p.x, c.Terrain.SampleHeight(new Vector3(p.x, 0f, p.y)) + c.Terrain.transform.position.y + height, p.y);
                    int count = 0;
                    foreach (float sign in new[] { -1f, 1f })
                    foreach (float pitch in new[] { 0f, .25f, .55f })
                    {
                        Vector3 direction = new Vector3(right.x * sign, pitch, right.y * sign).normalized;
                        if (all.Any(b => b.IntersectRay(new Ray(origin, direction), out float distance) && distance < 18f)) count++;
                    }
                    hits.Add(count + "/6");
                }
                text.AppendLine($"| {route} | {fraction:P0} | ({p.x:F1}, {p.y:F1}) | {leftDistance:F1} | {rightDistance:F1} | {string.Join(" / ", hits)} | {(overhead ? "有" : "无")} | {coreGap:F2} |");
            }
            text.AppendLine($"\n18个观察段中，两侧12m内均有树干：{twoSided}/18；上方有树冠投影：{crowns}/18；两侧仍存在较大空缺：{missing}/18。该空缺保留安全净空，交汇处尤其保守。");
            text.AppendLine("\n核心距离按树干注册半径圆柱和所有启用恐龙包络取最小值，作为保守估计；实际分高度截面另外验收。树冠指标来自LOD0界框/外接圆，是空间层次估计而非叶片像素覆盖；实际视觉由Editor Play确认。树干经过躯干/髋部动画分高度扫掠及运行时同包络验证，无额外安全距离；树枝、头尾四肢和叶片不扩大植树禁区。\n");
            Directory.CreateDirectory(Output); File.WriteAllText(Output + "/" + file, text.ToString());
            Debug.Log($"CENTRAL_FOREST_ROUTE_OBSERVATIONS file={file} bilateralNearTrees={twoSided}/18 crownProjection={crowns}/18 retainedGaps={missing}/18 dinosaurEye={dinosaurEye:F2}m; no screenshots.");
        }
        // Batch verification: repeated stage one keeps all approved transforms and terrain instances.
        public static void VerifyIdempotence()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var root = GameObject.Find(RootName);
            var before = root.GetComponentsInChildren<Transform>().Select(t => t.name + "|" + t.position.ToString("F5") + "|" + t.rotation.ToString("F5") + "|" + t.localScale.ToString("F5")).ToArray();
            TreeInstance[] trees = Terrain.activeTerrain.terrainData.treeInstances;
            BuildInScene(1); BuildInScene(1);
            var after = root.GetComponentsInChildren<Transform>().Select(t => t.name + "|" + t.position.ToString("F5") + "|" + t.rotation.ToString("F5") + "|" + t.localScale.ToString("F5")).ToArray();
            if (!before.SequenceEqual(after) || !trees.SequenceEqual(Terrain.activeTerrain.terrainData.treeInstances)) throw new InvalidOperationException("Repeated forest build changed placement or TerrainData.");
            ValidateInScene();
            Debug.Log("CENTRAL_FOREST_IDEMPOTENCE_OK: two repeat builds, same transforms, no duplicates, unchanged terrain trees.");
        }
    }
}
