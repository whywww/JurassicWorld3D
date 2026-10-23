using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    // Independent additions: never regenerate the accepted trees or paint TerrainData.
    public static class UnderstoryDetailSample
    {
        internal const string RootName = "Understory Detail Sample";
        internal const string Output = "Tools/VegetationDiversity/UnderstoryDetailSample";
        private const string Config = "Assets/Editor/UnderstoryDetailSample.json";
        private const string Generated = "Assets/Environment/Terrain/Prepared/UnderstoryDetailSample";
        private const string Natural = "Assets/Environment/Terrain/Prepared/GroundEcology/Natural/";
        [Serializable] private sealed class State
        {
            public int version = 1, seed = 733101;
            public bool enabled, generated;
            public Rect area;
            public string protectionMask;
            public float dryArea, coverageBefore, coverageAfter;
            public Entry[] entries = Array.Empty<Entry>();
            public Link[] links = Array.Empty<Link>();
        }
        [Serializable] private sealed class Entry
        {
            public string guid, kind;
            public Vector3 position, scale;
            public float yaw;
            public bool woody;
        }
        [Serializable] private sealed class Link { public Vector3 a, b; }
        [Serializable] private sealed class Protection { public string protectionMask; }
        private static bool busy;
        private static State Load() => File.Exists(Config) ? JsonUtility.FromJson<State>(File.ReadAllText(Config)) : new State();
        private static void Persist(State s) { File.WriteAllText(Config, JsonUtility.ToJson(s, true) + "\n"); AssetDatabase.ImportAsset(Config); }
        private static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
        private static Rect Horizontal(Bounds b) => Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
        private static float Ground(float x, float z) => Terrain.activeTerrain.SampleHeight(new Vector3(x, 0, z)) + Terrain.activeTerrain.transform.position.y;
        private static void RemoveOwned() { var root = GameObject.Find(RootName); if (root != null) Object.DestroyImmediate(root); TerrainTreeObstacleRegistry.Invalidate(); }
        private static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save Main and exit Play before changing the understory sample.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity"); Directory.CreateDirectory(Output);
        }
        private static void Save()
        {
            RainforestSample.RebuildNavigation(); FoliageInteractionPreparation.SeedConfigured();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene()); EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
        }
        private static void Checkpoint()
        {
            string dir = Output + "/Checkpoint"; Directory.CreateDirectory(dir);
            var settings = Object.FindObjectOfType<DinosaurInteractionSettings>();
            foreach (string path in new[] { "Assets/Scenes/Main.unity", Config, "Assets/Editor/CenterForestGapFill.json", "Assets/Editor/WaterPlantCleanup.json",
                AssetDatabase.GetAssetPath(Terrain.activeTerrain.terrainData), AssetDatabase.GetAssetPath(settings.navigationData) })
                if (File.Exists(path) && !File.Exists(dir + "/" + Path.GetFileName(path))) File.Copy(path, dir + "/" + Path.GetFileName(path));
        }
        private static bool Allowed(State s, Vector2 p)
        {
            if (!s.area.Contains(p) || p.x < -60 || p.x >= 60 || p.y < -50 || p.y >= 82) return false;
            int index = Mathf.FloorToInt(p.y + 50) * 120 + Mathf.FloorToInt(p.x + 60);
            return mask[index] == 3 && !WaterPlantCleanup.IsWet(new Vector3(p.x, 0, p.y));
        }
        private static byte[] mask;
        private static bool Footprint(State s, Bounds b)
        {
            for (float x = b.min.x; x < b.max.x + .25f; x += .25f)
            for (float z = b.min.z; z < b.max.z + .25f; z += .25f)
                if (!Allowed(s, new Vector2(Mathf.Min(x, b.max.x), Mathf.Min(z, b.max.z)))) return false;
            return true;
        }
        private static List<Bounds> ExistingPlants()
        {
            var roots = new HashSet<GameObject>();
            foreach (var r in Object.FindObjectsOfType<MeshRenderer>())
            {
                var root = PrefabUtility.GetNearestPrefabInstanceRoot(r.gameObject); if (root == null) continue;
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                if (path.Contains("Fern02") || Path.GetFileName(path).StartsWith("bush0", StringComparison.OrdinalIgnoreCase)) roots.Add(root);
            }
            return roots.Select(CentralForestDensification.BoundsOf).ToList();
        }
        private static List<Vector2> Samples(State s)
        {
            var list = new List<Vector2>();
            for (float x = s.area.xMin + .5f; x < s.area.xMax; x++) for (float z = s.area.yMin + .5f; z < s.area.yMax; z++)
                if (Allowed(s, new Vector2(x, z))) list.Add(new Vector2(x, z));
            return list;
        }
        private static float Coverage(List<Vector2> points, List<Bounds> plants) => points.Count == 0 ? 0 : points.Count(p => plants.Any(b => Horizontal(b).Contains(p))) / (float)points.Count;
        private static void Freeze(State s)
        {
            var protection = JsonUtility.FromJson<Protection>(File.ReadAllText("Assets/Editor/CenterForestGapFill.json"));
            s.protectionMask = protection.protectionMask; mask = Convert.FromBase64String(s.protectionMask);
            var plants = ExistingPlants(); float best = float.PositiveInfinity; bool found = false;
            var report = new StringBuilder("x,z,dry_plantable_m2,bbox_coverage,near_corridor\n");
            for (int x = -60; x <= 30; x += 10) for (int z = -50; z <= 52; z += 10)
            {
                s.area = new Rect(x, z, 30, 30); var points = Samples(s);
                bool near = false;
                for (int zz = Mathf.Max(-50, z - 10); zz < Mathf.Min(82, z + 40) && !near; zz++)
                for (int xx = Mathf.Max(-60, x - 10); xx < Mathf.Min(60, x + 40); xx++)
                    if (mask[(zz + 50) * 120 + xx + 60] == 2) { near = true; break; }
                float coverage = Coverage(points, plants); report.AppendLine($"{x},{z},{points.Count},{coverage:R},{near}");
                if (points.Count >= 300 && near && coverage < best)
                { best = coverage; selected = s.area; s.dryArea = points.Count; found = true; }
            }
            File.WriteAllText(Output + "/WindowCandidates.csv", report.ToString());
            if (!found) throw new InvalidOperationException("No 30m window has 300m2 of dry protected-mask capacity near a corridor.");
            s.area = selected; s.coverageBefore = best;
            s.links = CollectLinks(s);
        }
        private static Rect selected;
        private static Link[] CollectLinks(State s)
        {
            var settings = Object.FindObjectOfType<DinosaurInteractionSettings>(); var nav = NavMesh.AddNavMeshData(settings.navigationData);
            try
            {
                var filter = new NavMeshQueryFilter { agentTypeID = settings.navigationAgentTypeId, areaMask = 1 };
                var points = new List<Vector3>();
                for (int i = 0; i < MovementCorridor.RouteCount; i++)
                {
                    MovementCorridor.GetSampleAt(i, 0, out var a, out _); MovementCorridor.GetSampleAt(i, MovementCorridor.Length(i) * .5f, out var b, out _);
                    points.Add(new Vector3(a.x, Ground(a.x, a.y), a.y)); points.Add(new Vector3(b.x, Ground(b.x, b.y), b.y));
                }
                foreach (var a in Object.FindObjectsOfType<DinosaurAgent>()) points.Add(a.transform.position);
                Vector2 c = s.area.center;
                foreach (Vector2 p in new[] { c + Vector2.left * 18, c + Vector2.right * 18, c + Vector2.up * 18, c + Vector2.down * 18 })
                    points.Add(new Vector3(p.x, Ground(p.x, p.y), p.y));
                var links = new List<Link>();
                for (int i = 0; i < points.Count; i++)
                    if (NavMesh.SamplePosition(points[i], out var a, 8, filter) && NavMesh.SamplePosition(points[(i + 1) % points.Count], out var b, 8, filter))
                    { var path = new NavMeshPath(); if (NavMesh.CalculatePath(a.position, b.position, filter, path) && path.status == NavMeshPathStatus.PathComplete) links.Add(new Link { a = a.position, b = b.position }); }
                if (links.Count == 0) throw new InvalidOperationException("No baseline navigation connection could be frozen.");
                return links.ToArray();
            }
            finally { nav.Remove(); }
        }
        private static void VerifyLinks(State s)
        {
            var settings = Object.FindObjectOfType<DinosaurInteractionSettings>(); var nav = NavMesh.AddNavMeshData(settings.navigationData);
            try { foreach (var link in s.links) { var path = new NavMeshPath(); if (!NavMesh.CalculatePath(link.a, link.b, new NavMeshQueryFilter { agentTypeID = settings.navigationAgentTypeId, areaMask = 1 }, path) || path.status != NavMeshPathStatus.PathComplete) throw new InvalidOperationException("An existing route/sample/spawn connection was broken."); } }
            finally { nav.Remove(); }
        }
        private static string Guid(string name) => AssetDatabase.FindAssets(name + " t:Prefab").First(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == name);
        private static GameObject Create(Entry e, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(e.guid));
            if (prefab == null) throw new InvalidOperationException("Missing sample source GUID " + e.guid);
            var obj = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent); obj.name = e.kind + "_" + parent.childCount;
            obj.transform.SetPositionAndRotation(e.position, Quaternion.Euler(0, e.yaw, 0)); obj.transform.localScale = e.scale;
            if (e.kind == "Fern" || e.kind == "Bush")
            {
                obj.AddComponent<CenterGapGroundPlant>();
                if (e.woody) { var marker = obj.GetComponent<SceneTreeObstacle>() ?? obj.AddComponent<SceneTreeObstacle>(); marker.SetRadius(.05f); marker.includeHardBranches = true; }
                else foreach (var collider in obj.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
                var renderers=obj.GetComponentsInChildren<Renderer>();
                var lod=obj.GetComponent<LODGroup>();if(lod==null)lod=obj.AddComponent<LODGroup>();
                lod.SetLODs(new[]{new LOD(.02f,renderers)});
                var bounds=CentralForestDensification.BoundsOf(obj);
                lod.localReferencePoint=obj.transform.InverseTransformPoint(bounds.center);
                lod.size=Mathf.Max(.05f,bounds.size.y/Mathf.Max(.0001f,Mathf.Abs(obj.transform.lossyScale.y)));
                // The canopy still casts shadows; tiny added ground leaves do not add a second shadow draw.
                foreach(var renderer in renderers)renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return obj;
        }
        private sealed class Safety : IDisposable
        {
            private readonly GameObject helpers = new GameObject("Understory safety probes") { hideFlags = HideFlags.HideAndDontSave };
            private readonly Func<Vector2, float, bool> route = RouteObstacleClearance.CreateSolidCandidateProbe(Terrain.activeTerrain);
            private readonly List<RainforestSample.ReservedPose> poses;
            private readonly List<Bounds> solids;
            private readonly CenterForestGapFill.UnderstoryWoodClearance wood;
            internal Safety()
            {
                poses = TreeHeightAdjustment.Reservations(helpers.transform);
                wood = new CenterForestGapFill.UnderstoryWoodClearance(GameObject.Find(RootName).transform);
                solids = Object.FindObjectOfType<DinosaurInteractionSettings>().solidObstacles.ToList();
            }
            internal bool Fits(Bounds b)
            {
                float radius = new Vector2(b.extents.x, b.extents.z).magnitude;
                if (!route(XZ(b.center), radius) || solids.Any(old => old.Intersects(b)) || !wood.Clear(b)) return false;
                var section = new TreeTrunkSection { center = XZ(b.center), radius = radius, bottom = b.min.y, top = b.max.y };
                return !poses.Any(p => p.body.Intersects(p.position, p.forward, section));
            }
            internal bool RootFits(Vector2 p)
            {
                var b=new Bounds(new Vector3(p.x,Ground(p.x,p.y)+.04f,p.y),new Vector3(.12f,.08f,.12f));
                return !solids.Any(old=>old.Intersects(b))&&wood.Clear(b);
            }
            internal void Add(Bounds b) => solids.Add(b);
            internal int Count => solids.Count;
            internal void Reset(int count) => solids.RemoveRange(count, solids.Count - count);
            public void Dispose() { wood.Dispose(); Object.DestroyImmediate(helpers); }
        }
        private static bool MaterialsValid(GameObject obj) => obj.GetComponentsInChildren<MeshRenderer>().All(r => r.sharedMaterials.Length > 0 && r.sharedMaterials.All(m => m != null && m.shader != null && !m.shader.name.Contains("InternalError")));
        private static bool Woody(GameObject obj) => obj.GetComponentsInChildren<MeshRenderer>().Any(r => r.sharedMaterials.Any(RouteObstacleClearance.IsWood));
        private static void Generate(State s)
        {
            var random = new System.Random(s.seed); var entries = new List<Entry>(); var bounds = ExistingPlants();
            var root = new GameObject(RootName); var hard = new GameObject("Hard Props").transform; hard.SetParent(root.transform);
            var points = Samples(s); var sites = points.OrderBy(_ => random.Next()).ToArray();
            string[] ferns = new[] { "Fern02_A", "Fern02_B", "Fern02_C", "Fern02_D" }.Select(Guid).ToArray();
            string[] bushes = Enumerable.Range(1, 6).Select(i => Guid("bush0" + i)).ToArray();
            using (var safety = new Safety())
            {
                // Limited accents first, then let plants surround them. A failed candidate is discarded.
                foreach (var group in new[] { new { kind = "Log", names = new[] { "DeadwoodLogTrunk02", "AutumnGroundedLog" }, max = 3, size = 2.6f },
                    new { kind = "Stump", names = new[] { "DeadwoodStump01" }, max = 2, size = .65f },
                    new { kind = "Rock", names = Enumerable.Range(1,6).Select(i=>"RockMossSet01_Rock0"+i).ToArray(), max = 8, size = .65f } })
                {
                    int count = 0;
                    foreach (var p in sites.Take(400))
                    {
                        if (count >= group.max) break;
                        var e = new Entry { kind = group.kind, guid = Guid(group.names[random.Next(group.names.Length)]), yaw = (float)random.NextDouble() * 360, scale = Vector3.one, position = new Vector3(p.x, 0, p.y) };
                        var obj = Create(e, hard); var b = CentralForestDensification.BoundsOf(obj);
                        float length = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)); obj.transform.localScale *= group.size * Mathf.Lerp(.7f, 1.2f, (float)random.NextDouble()) / Mathf.Max(.001f, length);
                        CentralForestDensification.Ground(obj, Terrain.activeTerrain, p); b = CentralForestDensification.BoundsOf(obj);
                        if (!MaterialsValid(obj) || !Footprint(s, b) || !safety.Fits(b)) { Object.DestroyImmediate(obj); continue; }
                        e.position = obj.transform.position; e.scale = obj.transform.localScale; entries.Add(e); safety.Add(b); count++;
                    }
                }
                int clusters = 0;
                foreach (var center in sites)
                {
                    if (clusters >= 55 || entries.Count(e => e.kind == "Fern" || e.kind == "Bush") >= 360) break;
                    // Prefer holes; allow some infill within broad leaf bounds so the old AABB metric cannot suppress it all.
                    if (bounds.Count(b => Horizontal(b).Contains(center)) > 1 || random.NextDouble() < .15) continue;
                    int members = random.Next(4, 10), accepted = 0, first = entries.Count, groundStart = bounds.Count, solidStart = safety.Count;
                    var clusterObjects = new List<GameObject>();
                    for (int i = 0; i < members; i++)
                    {
                        float angle = (float)random.NextDouble() * Mathf.PI * 2, reach = Mathf.Sqrt((float)random.NextDouble()) * 1.7f;
                        Vector2 p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * reach;
                        if (!Allowed(s, p) || entries.Any(e => Vector2.Distance(XZ(e.position), p) < .32f)) continue;
                        bool fern = random.NextDouble() < .8;
                        var e = new Entry { kind = fern ? "Fern" : "Bush", guid = fern ? ferns[random.Next(ferns.Length)] : bushes[random.Next(bushes.Length)], yaw = (float)random.NextDouble() * 360, position = new Vector3(p.x, 0, p.y), scale = Vector3.one };
                        var obj = Create(e, root.transform); float h = fern ? Mathf.Lerp(.2f, .7f, (float)random.NextDouble()) : Mathf.Lerp(.4f, .9f, (float)random.NextDouble());
                        bool edge = false;
                        for(int dz=-2;dz<=2;dz++) for(int dx=-2;dx<=2;dx++)
                        { int xx=Mathf.FloorToInt(p.x+60)+dx, zz=Mathf.FloorToInt(p.y+50)+dz; if(xx>=0&&xx<120&&zz>=0&&zz<132&&mask[zz*120+xx]==2)edge=true; }
                        if(edge)h=fern?Mathf.Min(h,.35f):Mathf.Min(h,.45f);
                        obj.transform.localScale *= h / Mathf.Max(.001f, CentralForestDensification.BoundsOf(obj).size.y); CentralForestDensification.Ground(obj, Terrain.activeTerrain, p);
                        e.woody = Woody(obj); var b = CentralForestDensification.BoundsOf(obj);
                        if (!MaterialsValid(obj) || !Footprint(s, b) || !safety.RootFits(p) || (e.woody && !safety.Fits(b))) { Object.DestroyImmediate(obj); continue; }
                        if (e.woody) { var marker = obj.AddComponent<SceneTreeObstacle>(); marker.SetRadius(.05f); marker.includeHardBranches = true; safety.Add(b); }
                        e.position = obj.transform.position; e.scale = obj.transform.localScale; entries.Add(e); bounds.Add(b); clusterObjects.Add(obj); accepted++;
                    }
                    if (accepted >= 4) clusters++;
                    else { foreach(var obj in clusterObjects)Object.DestroyImmediate(obj); entries.RemoveRange(first,entries.Count-first);bounds.RemoveRange(groundStart,bounds.Count-groundStart);safety.Reset(solidStart); }
                }
            }
            // Small, irregular alpha-cutout patches; two shared materials, not large ground tiles.
            foreach (var center in sites.Where((_, i) => i % 3 == 0).Take(130))
            {
                if (entries.Where(e => e.kind == "Leaves" || e.kind == "Moss").Any(e => Vector2.Distance(XZ(e.position), center) < 1.25f)) continue;
                string kind = random.NextDouble() < .6 ? "Leaves" : "Moss";
                var e = new Entry { kind = kind, guid = AssetDatabase.AssetPathToGUID(Natural + kind + ".mat"), position = new Vector3(center.x, Ground(center.x, center.y), center.y), scale = Vector3.one * Mathf.Lerp(.35f, .85f, (float)random.NextDouble()), yaw = (float)random.NextDouble() * 360 };
                if (Footprint(s, new Bounds(e.position, new Vector3(e.scale.x * 2, .02f, e.scale.x * 2)))) entries.Add(e);
            }
            s.entries = entries.ToArray(); s.generated = s.enabled = true; s.coverageAfter = Coverage(points, bounds);
            File.WriteAllText(Output+"/Draft.json",JsonUtility.ToJson(s,true)); RemoveOwned();
        }
        private static void Surface(State s, Transform root, string kind)
        {
            var entries = s.entries.Where(e => e.kind == kind).ToArray(); if (entries.Length == 0) return;
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            foreach (var e in entries)
            {
                Vector2 tile = kind == "Moss" ? new Vector2(0, .5f) : new Vector2(.5f, .5f);
                int first = vertices.Count;
                for (int z = 0; z <= 2; z++) for (int x = 0; x <= 2; x++)
                {
                    Vector3 p = e.position + Quaternion.Euler(0, e.yaw, 0) * new Vector3((x - 1) * e.scale.x, 0, (z - 1) * e.scale.z);
                    p.y = Ground(p.x, p.z) + .014f; vertices.Add(p); uv.Add(tile + new Vector2(.012f + x * .238f, .012f + z * .238f));
                }
                for (int z = 0; z < 2; z++) for (int x = 0; x < 2; x++) { int a = first + z * 3 + x; triangles.AddRange(new[] { a, a + 4, a + 1, a, a + 3, a + 4 }); }
            }
            var mesh = new Mesh { name = "Understory " + kind }; mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            JurassicWorldSceneBuilder.EnsureFolder(Generated); string path = Generated + "/" + kind + ".asset"; var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved == null) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; } else { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); }
            var obj = new GameObject(kind); obj.transform.SetParent(root, false); obj.AddComponent<MeshFilter>().sharedMesh = saved;
            obj.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(entries[0].guid));
        }
        private static void Apply(State s, bool validate = true)
        {
            mask = Convert.FromBase64String(s.protectionMask); var root = new GameObject(RootName).transform; var hard = new GameObject("Hard Props").transform; hard.SetParent(root, false);
            using (var safety = new Safety())
            foreach (var e in s.entries)
            {
                if (e.kind == "Leaves" || e.kind == "Moss")
                { if (validate && !Footprint(s, new Bounds(e.position, new Vector3(e.scale.x * 2, .02f, e.scale.x * 2)))) throw new InvalidOperationException("Surface patch no longer safe."); continue; }
                bool solid = e.kind == "Log" || e.kind == "Stump" || e.kind == "Rock";
                var obj = Create(e, solid ? hard : root); var b = CentralForestDensification.BoundsOf(obj);
                if (validate && (!MaterialsValid(obj) || !Footprint(s, b) || (!solid&&!safety.RootFits(XZ(e.position))) || ((solid || e.woody) && !safety.Fits(b)))) throw new InvalidOperationException("Stored sample object no longer safe: " + e.kind + " " + e.position);
                if (solid || e.woody) safety.Add(b);
            }
            Surface(s, root, "Leaves"); Surface(s, root, "Moss");
        }
        private static string Signature() => string.Join("\n", GameObject.Find(RootName).GetComponentsInChildren<Transform>(true).Select(t => t.name + t.position.ToString("F6") + t.rotation.ToString("F6") + t.lossyScale.ToString("F6")));
        [MenuItem("Jurassic World/Environment/Understory Detail Sample/Build and Verify")]
        public static void BuildAndVerify() => Build(false);
        private static void Build(bool regenerate)
        {
            Open(); Checkpoint(); busy = true; var previous = Load(); bool existed = GameObject.Find(RootName) != null; var next = JsonUtility.FromJson<State>(JsonUtility.ToJson(previous));
            if(regenerate)next.generated=false;
            FoliageInteractionPreparation.RestoreCurrent(); RemoveOwned(); RainforestSample.RebuildNavigation(); RainforestSample.Snapshot snapshot;
            try
            {
                if (!next.generated) { Freeze(next); Capture(next, "before"); }
                snapshot = new RainforestSample.Snapshot();
                var terrainAudit = new TerrainAudit();
                var activeStates=Object.FindObjectsOfType<Transform>().ToDictionary(t=>t.gameObject,t=>t.gameObject.activeSelf);
                var dinosaurStates=Object.FindObjectsOfType<MonoBehaviour>().Where(b=>b is DinosaurAgent || b is DinosaurCoreBody || b is DinosaurFbxLoader).ToDictionary(b=>b,b=>EditorJsonUtility.ToJson(b));
                if(!next.generated)Generate(next);
                terrainAudit.Check("generation");
                mask = Convert.FromBase64String(next.protectionMask);
                Apply(next); snapshot.Check(); string first = Signature();
                RemoveOwned(); snapshot.Check(); Apply(next); snapshot.Check(); if (Signature() != first) throw new InvalidOperationException("Rebuild differs.");
                RemoveOwned(); snapshot.Check(); Apply(next); snapshot.Check(); if (Signature() != first) throw new InvalidOperationException("Record/Bake replay differs.");
                next.enabled = true; Persist(next); RainforestSample.RebuildNavigation(); VerifyLinks(next); snapshot.Check();
                if(activeStates.Any(p=>p.Key==null||p.Key.activeSelf!=p.Value)||dinosaurStates.Any(p=>p.Key==null||EditorJsonUtility.ToJson(p.Key)!=p.Value))throw new InvalidOperationException("An original object active state or dinosaur component changed.");
                Save(); Capture(next, "after"); WriteReport(next); Debug.Log("UNDERSTORY_DETAIL_SAMPLE_PASS");
                if(File.Exists(Output+"/Failure.md"))File.Move(Output+"/Failure.md",Output+"/Failure."+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".md");
            }
            catch (Exception e)
            {
                FoliageInteractionPreparation.RestoreCurrent(); RemoveOwned(); if (existed) Apply(previous, false); Persist(previous); Save();
                File.WriteAllText(Output + "/Failure.md", "Batch restored; no further additions.\n\n" + e); throw;
            }
            finally { busy = false; }
        }
        private sealed class TerrainAudit
        {
            private readonly TerrainData data=Terrain.activeTerrain.terrainData;
            private readonly TreeInstance[] trees=Terrain.activeTerrain.terrainData.treeInstances;
            private readonly float[] heights=Terrain.activeTerrain.terrainData.GetHeights(0,0,Terrain.activeTerrain.terrainData.heightmapResolution,Terrain.activeTerrain.terrainData.heightmapResolution).Cast<float>().ToArray();
            private readonly float[] paint=Terrain.activeTerrain.terrainData.GetAlphamaps(0,0,Terrain.activeTerrain.terrainData.alphamapWidth,Terrain.activeTerrain.terrainData.alphamapHeight).Cast<float>().ToArray();
            internal void Check(string stage)
            {
                var now=data.treeInstances;string tree="same";
                if(!trees.SequenceEqual(now))tree=trees.Length+"/"+now.Length+" first="+Enumerable.Range(0,Mathf.Min(trees.Length,now.Length)).FirstOrDefault(i=>!trees[i].Equals(now[i]));
                bool h=heights.SequenceEqual(data.GetHeights(0,0,data.heightmapResolution,data.heightmapResolution).Cast<float>()),p=paint.SequenceEqual(data.GetAlphamaps(0,0,data.alphamapWidth,data.alphamapHeight).Cast<float>());
                if(tree!="same"||!h||!p)throw new InvalidOperationException("Terrain audit "+stage+": trees="+tree+" heights="+h+" paint="+p);
            }
        }
        [MenuItem("Jurassic World/Environment/Understory Detail Sample/Rebuild")]
        public static void Rebuild() => BuildAndVerify();
        [MenuItem("Jurassic World/Environment/Understory Detail Sample/Undo")]
        public static void Undo()
        {
            Open(); busy = true; try { FoliageInteractionPreparation.RestoreCurrent(); RemoveOwned(); var s = Load(); s.enabled = false; Persist(s); Save(); WriteReport(s); } finally { busy = false; }
        }
        internal static void SeedConfigured()
        {
            if (busy || !File.Exists(Config)) return; var s = Load(); if (!s.enabled || GameObject.Find(RootName) != null) return;
            busy = true; try { Apply(s); RainforestSample.RebuildNavigation(); VerifyLinks(s); } catch { RemoveOwned(); RainforestSample.RebuildNavigation(); throw; } finally { busy = false; }
        }
        internal static Vector3[] Eyes(Rect area) { Vector2 c = area.center; return new[] { new Vector3(c.x, Ground(c.x, area.yMin - 3) + 1.7f, area.yMin - 3), new Vector3(c.x - 5, Ground(c.x - 5, c.y) + .9f, c.y), new Vector3(area.xMax + 3, Ground(area.xMax + 3, c.y) + 2, c.y), new Vector3(c.x, Ground(c.x,c.y) + 40, c.y - 5) }; }
        private static void Capture(State s, string prefix)
        {
            var source = Camera.main; if (source == null) return; var holder = new GameObject("Understory comparison camera"); var camera = holder.AddComponent<Camera>(); camera.CopyFrom(source); camera.enabled = false; camera.farClipPlane = 180;
            var target = new RenderTexture(1280, 720, 24); var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false); var prior = RenderTexture.active;
            var eyes = Eyes(s.area); Vector2 c = s.area.center; var aims = new[] { new Vector3(c.x,Ground(c.x,c.y)+.6f,c.y), new Vector3(c.x+5,Ground(c.x+5,c.y+7)+.35f,c.y+7), new Vector3(c.x,Ground(c.x,c.y)+.5f,c.y), new Vector3(c.x,Ground(c.x,c.y),c.y) };
            try { target.Create(); camera.targetTexture = target; for (int i=0;i<4;i++) { camera.transform.SetPositionAndRotation(eyes[i],Quaternion.LookRotation(aims[i]-eyes[i])); camera.Render(); RenderTexture.active=target; pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply(); File.WriteAllBytes(Output+"/"+prefix+"-"+i+".png",pixels.EncodeToPNG()); } }
            finally { RenderTexture.active=prior; Object.DestroyImmediate(pixels); target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(holder); }
        }
        private static void WriteReport(State s)
        {
            var r = new StringBuilder("# 中心林下细节样板\n\n");
            r.AppendLine($"Enabled: {s.enabled}; Seed: {s.seed}; X[{s.area.xMin},{s.area.xMax}), Z[{s.area.yMin},{s.area.yMax}); 可布置干地 {s.dryArea:F0}m²。\n");
            foreach(var g in s.entries.GroupBy(e=>e.kind)) r.AppendLine($"- {g.Key}: {g.Count()}");
            r.AppendLine($"\n植物包围盒覆盖率 {s.coverageBefore:P1} → {s.coverageAfter:P1}，不计表面装饰，不能替代近地视觉判断。\n\n现有植物/地形变换与数据快照、撤销、两次记录重建一致；{s.links.Length}对既有路线/出生/样板连接通过。每个新增对象完整占地通过冻结保护区及实际水域检查；腐木、苔石及木质灌木保持硬障碍。未执行破坏性完整场景Bake，仅验证相同重放入口。\n\n动态运动、让行及CPU/GPU性能由独立Play验收记录；构建通过不代表这些项目通过。历史运动失败不作修复。\n");
            File.WriteAllText(Output+"/Layout.md",r.ToString());
            File.WriteAllText(Output+"/Entries.csv","kind,guid,x,y,z,yaw,scaleX,scaleY,scaleZ,woody\n"+string.Join("\n",s.entries.Select(e=>FormattableString.Invariant($"{e.kind},{e.guid},{e.position.x:R},{e.position.y:R},{e.position.z:R},{e.yaw:R},{e.scale.x:R},{e.scale.y:R},{e.scale.z:R},{e.woody}"))));
        }
        public static void BuildAndExit() { try { BuildAndVerify(); EditorApplication.Exit(0); } catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); } }
        public static void RefreshDraftAndExit() { try { Build(true); EditorApplication.Exit(0); } catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); } }
        public static void VerifySavedAndExit()
        {
            try
            {
                Open();FoliageInteractionPreparation.RestoreCurrent();var s=Load();mask=Convert.FromBase64String(s.protectionMask);
                var root=GameObject.Find(RootName);if(root==null||!s.enabled)throw new InvalidOperationException("Saved sample missing.");
                var hard=root.transform.Find("Hard Props");
                var objects=root.GetComponentsInChildren<Transform>().Where(t=>(t.parent==root.transform||t.parent==hard)&&PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)).ToArray();
                var records=s.entries.Where(e=>e.kind!="Leaves"&&e.kind!="Moss").ToArray();
                if(objects.Length!=records.Length)throw new InvalidOperationException("Saved object count differs.");
                foreach(var e in records)
                {
                    var t=objects.SingleOrDefault(o=>(o.position-e.position).sqrMagnitude<.000001f);
                    if(t==null||Vector3.Distance(t.localScale,e.scale)>.00001f||Quaternion.Angle(t.rotation,Quaternion.Euler(0,e.yaw,0))>.01f
                        ||AssetDatabase.AssetPathToGUID(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject))!=e.guid||!Footprint(s,CentralForestDensification.BoundsOf(t.gameObject)))
                        throw new InvalidOperationException("Saved GUID, transform or footprint differs: "+e.kind+" "+e.position);
                }
                var assets=new StringBuilder("kind,guid,height_min_m,height_max_m,instances,colliders,material_names\n");
                foreach(var group in records.GroupBy(e=>new {e.kind,e.guid}))
                {
                    var plants=group.Select(e=>objects.Single(o=>(o.position-e.position).sqrMagnitude<.000001f).gameObject).ToArray();
                    float min=plants.Min(o=>CentralForestDensification.BoundsOf(o).size.y),max=plants.Max(o=>CentralForestDensification.BoundsOf(o).size.y);
                    if((group.Key.kind=="Fern"&&(min<.199f||max>.701f))||(group.Key.kind=="Bush"&&(min<.399f||max>.901f)))throw new InvalidOperationException("Actual plant height outside sample range.");
                    string materials=string.Join(";",plants[0].GetComponentsInChildren<MeshRenderer>().SelectMany(r=>r.sharedMaterials).Select(m=>m.name).Distinct());
                    assets.AppendLine(FormattableString.Invariant($"{group.Key.kind},{group.Key.guid},{min:R},{max:R},{plants.Length},{plants.Sum(p=>p.GetComponentsInChildren<Collider>().Length)},{materials}"));
                }
                File.WriteAllText(Output+"/AssetAudit.csv",assets.ToString());
                RemoveOwned();RainforestSample.RebuildNavigation();var snapshot=new RainforestSample.Snapshot();Apply(s);snapshot.Check();RainforestSample.RebuildNavigation();VerifyLinks(s);
                int patches=s.entries.Count(e=>e.kind=="Leaves"||e.kind=="Moss");
                File.WriteAllText(Output+"/SavedValidation.md",$"# 保存后独立复核\n\n重新加载Main后，{records.Length}个预制体的GUID、位置、朝向、尺寸、完整占地及实际水域检查通过；独立记录重放及{s.links.Length}对导航连接通过。落叶苔藓共{patches}处，以两个专属网格重放。未保存本次内存验证状态。\n");
                if(SessionState.GetBool("UnderstoryDetailSample.RunMotion",false))
                {
                    SessionState.SetBool("UnderstoryDetailSample.RunMotion",false);
                    EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");ForestPlayAcceptance.RunUnderstory();
                }
                else EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static void VerifyAndRunMotion() { SessionState.SetBool("UnderstoryDetailSample.RunMotion",true);VerifySavedAndExit(); }
    }
}
