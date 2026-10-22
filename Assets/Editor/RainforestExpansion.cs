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
    /// <summary>Four independently owned additions around the reviewed sample. Never relocates old vegetation.</summary>
    public static class RainforestExpansion
    {
        internal const string RootName = "Rainforest Sample Expansion";
        internal const string Output = "Tools/VegetationDiversity/RainforestExpansion";
        private const string ConfigPath = "Assets/Editor/RainforestExpansion.json";
        private static State ownershipState;
        private static DateTime ownershipTime;
        [Serializable] private sealed class State
        {
            public float referenceDensity;
            public string referenceSamples;
            public Zone[] zones = {
                new Zone { name = "West", seed = 731717, area = Rect.MinMaxRect(-40,-10,-25,45) },
                new Zone { name = "East", seed = 731718, area = Rect.MinMaxRect(25,-10,40,45) },
                new Zone { name = "South", seed = 731719, area = Rect.MinMaxRect(-40,-25,40,-10) },
                new Zone { name = "North", seed = 731720, area = Rect.MinMaxRect(-40,45,40,60) }
            };
        }
        [Serializable] private sealed class Zone
        {
            public string name;
            public int seed;
            public Rect area;
            public bool enabled, generated;
            public float plantable, corridor, clearings, unavailable;
            public int original, retained, target;
            public RainforestTreeSizing.Tree[] trees = Array.Empty<RainforestTreeSizing.Tree>();
            public Connection[] connections = Array.Empty<Connection>();
            public string status;
            public OriginalTree[] originals = Array.Empty<OriginalTree>();
        }
        [Serializable] private sealed class OriginalTree
        {
            public string path, name;
            public Vector3 position, scale;
            public Quaternion rotation;
            public float radius;
            public bool hardBranches;
        }
        [Serializable] private sealed class Connection { public string agent; public Vector3[] corners; }
        private sealed class Stem { internal Vector2 p; internal float radius; }
        private static State Load() => File.Exists(ConfigPath) ? JsonUtility.FromJson<State>(File.ReadAllText(ConfigPath)) : new State();
        private static void Save(State state)
        {
            File.WriteAllText(ConfigPath, JsonUtility.ToJson(state, true) + "\n"); AssetDatabase.ImportAsset(ConfigPath);
        }
        internal static bool Owns(Vector2 p)
        {
            if (!File.Exists(ConfigPath)) return false;
            var written = File.GetLastWriteTimeUtc(ConfigPath);
            if (ownershipState == null || written != ownershipTime) { ownershipState = Load(); ownershipTime = written; }
            return ownershipState.zones.Any(z => z.enabled && z.area.Contains(p));
        }

        [MenuItem("Jurassic World/Environment/Rainforest Expansion/Build and Verify Ring")]
        public static void BuildAndVerify() { Build(null); VerifyBakeReplay(); }
        [MenuItem("Jurassic World/Environment/Rainforest Expansion/Rebuild/West")]
        public static void RebuildWest() => Build("West");
        [MenuItem("Jurassic World/Environment/Rainforest Expansion/Rebuild/East")]
        public static void RebuildEast() => Build("East");
        [MenuItem("Jurassic World/Environment/Rainforest Expansion/Rebuild/South")]
        public static void RebuildSouth() => Build("South");
        [MenuItem("Jurassic World/Environment/Rainforest Expansion/Rebuild/North")]
        public static void RebuildNorth() => Build("North");
        [MenuItem("Jurassic World/Environment/Rainforest Expansion/Remove/West")]
        public static void RemoveWest() => Remove("West");
        [MenuItem("Jurassic World/Environment/Rainforest Expansion/Remove/East")]
        public static void RemoveEast() => Remove("East");
        [MenuItem("Jurassic World/Environment/Rainforest Expansion/Remove/South")]
        public static void RemoveSouth() => Remove("South");
        [MenuItem("Jurassic World/Environment/Rainforest Expansion/Remove/North")]
        public static void RemoveNorth() => Remove("North");
        [MenuItem("Jurassic World/Environment/Rainforest Expansion/Remove All Expansion")]
        public static void RemoveAll() => Remove(null);

        private static void OpenClean()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the scene and exit Play before changing expansion zones.");
            EditorSceneManager.OpenScene(CentralForestDensification.ScenePath);
            if (GameObject.Find(RainforestSample.RootName) == null) throw new InvalidOperationException("Reviewed rainforest sample is required.");
        }
        private static void PrepareAndSave()
        {
            RainforestSample.RebuildNavigation(); FoliageInteractionPreparation.SeedConfigured();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
        }
        private static void Checkpoint()
        {
            string directory = Output + "/Checkpoint"; Directory.CreateDirectory(directory);
            foreach (string source in new[] { CentralForestDensification.ScenePath, RainforestSample.EnabledPath,
                "Assets/Editor/RainforestSamplePlanting.json", "Assets/Editor/CentralForestStage.json", ConfigPath })
                if (File.Exists(source) && !File.Exists(directory + "/" + Path.GetFileName(source)))
                    File.Copy(source, directory + "/" + Path.GetFileName(source));
        }
        private static Transform Parent()
        {
            var root = GameObject.Find(RootName) ?? new GameObject(RootName); return root.transform;
        }
        private static void Delete(string name)
        {
            var root = GameObject.Find(RootName);
            if (root != null) { var zone = root.transform.Find(name); if (zone != null) Object.DestroyImmediate(zone.gameObject); }
            TerrainTreeObstacleRegistry.Invalidate();
        }
        private static string Signature(Zone zone) => string.Join("\n", Parent().Find(zone.name).GetComponentsInChildren<Transform>(true)
            .Select(t => t.name + t.position.ToString("F6") + t.rotation.ToString("F6") + t.lossyScale.ToString("F6")));

        private static void Build(string selected)
        {
            OpenClean(); Checkpoint(); FoliageInteractionPreparation.RestoreCurrent();
            State state = Load();
            if (state.referenceDensity <= 0) FreezeReference(state);
            CaptureOriginalTrees(state);
            string sample = RainforestSample.Signature();
            foreach (Zone zone in state.zones.Where(z => selected == null || z.name == selected))
            {
                string previous = JsonUtility.ToJson(zone); bool existed = Parent().Find(zone.name) != null;
                Delete(zone.name);
                var snapshot = new RainforestSample.Snapshot();
                try
                {
                    Apply(zone, state); snapshot.Check(); VerifyConnections(zone);
                    string first = Signature(zone); Delete(zone.name); snapshot.Check();
                    Apply(zone, state); snapshot.Check(); VerifyConnections(zone);
                    if (first != Signature(zone)) throw new InvalidOperationException(zone.name + " repeat build differs.");
                    Delete(zone.name); snapshot.Check();
                    // Same replay used by the scene Bake: no fresh random selection or new reference samples.
                    zone.enabled = true; Apply(zone, state); snapshot.Check(); VerifyConnections(zone);
                    if (first != Signature(zone) || sample != RainforestSample.Signature())
                        throw new InvalidOperationException("Bake replay or reviewed sample changed.");
                    Save(state); PrepareAndSave(); WriteReport(state);
                    FoliageInteractionPreparation.RestoreCurrent();
                    Debug.Log("RAINFOREST_EXPANSION_ZONE_PASS " + zone.name);
                }
                catch (Exception error)
                {
                    Delete(zone.name); JsonUtility.FromJsonOverwrite(previous, zone);
                    if (existed) Apply(zone, state);
                    Save(state); PrepareAndSave(); WriteReport(state);
                    File.WriteAllText(Output + "/Failure.txt", zone.name + " rolled back; later zones not attempted.\n" + error);
                    throw;
                }
            }
            PrepareAndSave(); WriteReport(state);
            Debug.Log("RAINFOREST_EXPANSION_BUILD_OK: repeats, undo, replay, existing poses and Terrain unchanged.");
        }
        private static void Remove(string selected)
        {
            OpenClean(); FoliageInteractionPreparation.RestoreCurrent(); State state = Load();
            foreach (var zone in state.zones.Where(z => selected == null || z.name == selected)) { Delete(zone.name); zone.enabled = false; }
            if (state.zones.All(z => !z.enabled)) { var root = GameObject.Find(RootName); if (root != null) Object.DestroyImmediate(root); }
            Save(state); PrepareAndSave(); WriteReport(state);
        }
        internal static void SeedConfigured()
        {
            if (!File.Exists(ConfigPath)) return;
            State state = Load(); foreach (var zone in state.zones.Where(z => z.enabled)) { Delete(zone.name); Apply(zone, state); VerifyConnections(zone); }
        }
        private static void CaptureOriginalTrees(State state)
        {
            var ownRoot = GameObject.Find(RootName);
            foreach(var zone in state.zones.Where(z=>z.originals==null || z.originals.Length==0))
            {
                zone.originals=Object.FindObjectsOfType<SceneTreeObstacle>()
                    .Where(t=>zone.area.Contains(XZ(t.transform.position)) && (ownRoot==null || !t.transform.IsChildOf(ownRoot.transform)))
                    .Select(t=>new OriginalTree { path=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject), name=t.name,
                        position=t.transform.position, rotation=t.transform.rotation, scale=t.transform.lossyScale, radius=t.Radius, hardBranches=t.includeHardBranches }).ToArray();
                if(zone.originals.Any(t=>string.IsNullOrEmpty(t.path))) throw new InvalidOperationException("Cannot preserve a non-prefab original tree through Bake: "+zone.name);
            }
        }
        // Restore the old trees before the central seeder computes its remaining quota.
        // These objects deliberately live outside the removable expansion root.
        internal static void RestoreConfiguredOriginals()
        {
            if(!File.Exists(ConfigPath))return;
            var state=Load(); var existing=Object.FindObjectsOfType<SceneTreeObstacle>().ToList();
            Transform root=null;
            foreach(var tree in state.zones.SelectMany(z=>z.originals ?? Array.Empty<OriginalTree>()))
            {
                if(existing.Any(t=>(t.transform.position-tree.position).sqrMagnitude<1e-6f))continue;
                if(root==null)root=(GameObject.Find("Preserved Rainforest Baseline")??new GameObject("Preserved Rainforest Baseline")).transform;
                var item=CentralForestDensification.Instantiate(tree.path,root);item.name=tree.name;
                item.transform.SetPositionAndRotation(tree.position,tree.rotation);item.transform.localScale=tree.scale;
                foreach(var collider in item.GetComponentsInChildren<Collider>())Object.DestroyImmediate(collider);
                var marker=item.GetComponent<SceneTreeObstacle>()??item.AddComponent<SceneTreeObstacle>();
                marker.SetRadius(tree.radius);marker.includeHardBranches=tree.hardBranches;existing.Add(marker);
            }
            if(root!=null)RouteObstacleClearance.BakeRuntimeProfiles(root.gameObject);
            TerrainTreeObstacleRegistry.Invalidate();
        }
        public static void VerifyBakeReplay()
        {
            OpenClean(); FoliageInteractionPreparation.RestoreCurrent();
            var state=Load(); var signatures=state.zones.Where(z=>z.enabled).ToDictionary(z=>z.name,Signature);
            string sample=RainforestSample.Signature();
            try
            {
                foreach(var zone in state.zones)Delete(zone.name);
                var baseline=new RainforestSample.Snapshot();
                SeedConfigured(); baseline.Check();
                foreach(var zone in state.zones.Where(z=>z.enabled))
                    if(signatures[zone.name]!=Signature(zone))throw new InvalidOperationException("Full expansion Bake replay differs: "+zone.name);
                // In-memory reconstruction of originals which the older central seeder now excludes.
                // Discard this test scene afterwards; never replace the user's original objects.
                var originals=state.zones.SelectMany(z=>z.originals ?? Array.Empty<OriginalTree>()).ToArray();
                foreach(var original in originals)
                {
                    var marker=Object.FindObjectsOfType<SceneTreeObstacle>().Single(t=>(t.transform.position-original.position).sqrMagnitude<1e-6f);
                    Object.DestroyImmediate(marker.gameObject);
                }
                RestoreConfiguredOriginals();
                int count=Object.FindObjectsOfType<SceneTreeObstacle>().Length;RestoreConfiguredOriginals();
                if(count!=Object.FindObjectsOfType<SceneTreeObstacle>().Length)throw new InvalidOperationException("Original tree restoration duplicated objects.");
                foreach(var original in originals)
                {
                    var marker=Object.FindObjectsOfType<SceneTreeObstacle>().Single(t=>(t.transform.position-original.position).sqrMagnitude<1e-6f);
                    if(marker.name!=original.name || Vector3.Distance(marker.transform.lossyScale,original.scale)>.0001f
                        || Quaternion.Angle(marker.transform.rotation,original.rotation)>.001f || Mathf.Abs(marker.Radius-original.radius)>.0001f)
                        throw new InvalidOperationException("Original tree Bake restore changed geometry.");
                }
                if(sample!=RainforestSample.Signature())throw new InvalidOperationException("Bake replay changed the reviewed sample.");
                File.WriteAllText(Output+"/BakeReplay.md",$"# Bake重放检查\n\nPASS：四区独立撤销后通过场景Bake同一入口重放，种植签名一致、原有对象与Terrain不变。\n\nPASS：{originals.Length}棵既有实体树在临时场景中移除后，按持久记录恢复位置、朝向、尺寸及树干半径；重复恢复不增加对象。测试场景已丢弃，保存的Main中原对象未被替换。\n\n未执行重新生成整个地形和场景的完整Bake；这里验证的是Bake使用的扩展重放与原树恢复入口。\n");
                Debug.Log("RAINFOREST_EXPANSION_BAKE_REPLAY_PASS");
            }
            finally { EditorSceneManager.OpenScene(CentralForestDensification.ScenePath); }
        }
        public static void CleanupBaselineCheck()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before deleting test artifacts.");
            const string scene="Assets/Scenes/RainforestExpansionBaseline.unity", nav="Assets/Navigation/RainforestExpansionBaselineNav.asset";
            if(EditorSceneManager.GetActiveScene().path==scene)EditorSceneManager.OpenScene(CentralForestDensification.ScenePath);
            string guid=AssetDatabase.AssetPathToGUID(nav);
            if(!string.IsNullOrEmpty(guid) && File.ReadAllText(CentralForestDensification.ScenePath).Contains(guid))
                throw new InvalidOperationException("Main still references the test navigation asset; refusing deletion.");
            if(File.Exists(scene))AssetDatabase.DeleteAsset(scene);
            if(File.Exists(nav))AssetDatabase.DeleteAsset(nav);
            Debug.Log("RAINFOREST_EXPANSION_BASELINE_ARTIFACTS_REMOVED");
        }

        private static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
        private static IEnumerable<Vector2> Grid(Rect area)
        {
            for (float x = area.xMin + .5f; x < area.xMax; x++)
                for (float z = area.yMin + .5f; z < area.yMax; z++) yield return new Vector2(x,z);
        }
        private static List<Stem> Stems()
        {
            var terrain = Terrain.activeTerrain; var result = new List<Stem>();
            foreach (var tree in terrain.terrainData.treeInstances)
            {
                if (tree.widthScale <= 0) continue;
                result.Add(new Stem { p = XZ(terrain.transform.position + Vector3.Scale(tree.position, terrain.terrainData.size)), radius = .7f * tree.widthScale });
            }
            result.AddRange(Object.FindObjectsOfType<SceneTreeObstacle>().Select(t => new Stem { p = XZ(t.transform.position), radius = t.Radius }));
            return result;
        }
        private static void FreezeReference(State state)
        {
            var stems = Stems(); var counts = new List<int>(); var report = new StringBuilder();
            foreach (float x in new[] { -82f, 82f }) foreach (float z in new[] { -64f, -32f, 0f, 32f, 64f })
            {
                int count = stems.Count(s => new Rect(x-8,z-8,16,16).Contains(s.p)); counts.Add(count);
                report.AppendLine($"- ({x}, {z}): {count} trees / 256m²");
            }
            counts.Sort(); state.referenceDensity = (counts[4] + counts[5]) * .5f / 256f;
            state.referenceSamples = report.ToString();
        }
        private static DinosaurCoreBody Body(DinosaurAgent agent) => new SerializedObject(agent).FindProperty("interactionBody").objectReferenceValue as DinosaurCoreBody
            ?? agent.GetComponent<DinosaurCoreBody>();
        internal static List<RainforestSample.ReservedPose> Along(Vector3[] corners, DinosaurCoreBody body)
        {
            var result = new List<RainforestSample.ReservedPose>();
            for (int i = 1; i < corners.Length; i++)
            {
                Vector2 direction = XZ(corners[i] - corners[i-1]).normalized;
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(corners[i],corners[i-1]) / .25f));
                for (int j = 0; j <= steps; j++) result.Add(new RainforestSample.ReservedPose
                { position = XZ(Vector3.Lerp(corners[i-1],corners[i],j/(float)steps)), forward = direction, body = body });
                if (i < corners.Length - 1)
                {
                    float angle = Vector2.SignedAngle(direction, XZ(corners[i+1]-corners[i]).normalized);
                    int turns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(angle)/2f));
                    for (int j=1;j<=turns;j++)
                    {
                        float radians = angle*j/turns*Mathf.Deg2Rad;
                        var f = new Vector2(direction.x*Mathf.Cos(radians)-direction.y*Mathf.Sin(radians), direction.x*Mathf.Sin(radians)+direction.y*Mathf.Cos(radians));
                        result.Add(new RainforestSample.ReservedPose { position = XZ(corners[i]), forward = f, body = body });
                    }
                }
            }
            return result;
        }
        private static NavMeshQueryFilter Filter(DinosaurInteractionSettings settings) => new NavMeshQueryFilter { agentTypeID = settings.navigationAgentTypeId, areaMask = 1 };
        private static bool Fits(List<RainforestSample.ReservedPose> poses, DinosaurInteractionSettings settings)
            => poses.All(p => !settings.PoseBlocked(p.position,p.forward,p.body));
        internal static List<RainforestSample.ReservedPose> HeightReservations()
        {
            var result=new List<RainforestSample.ReservedPose>();
            if(!File.Exists(ConfigPath))return result;
            var agents=Object.FindObjectsOfType<DinosaurAgent>();
            foreach(var connection in Load().zones.Where(z=>z.enabled).SelectMany(z=>z.connections))
            {
                var agent=agents.SingleOrDefault(a=>a.name==connection.agent);if(agent!=null)result.AddRange(Along(connection.corners,Body(agent)));
            }
            return result;
        }
        private static Connection[] FindConnections(Zone zone, DinosaurAgent[] agents, DinosaurInteractionSettings settings, RainforestSampleLayout layout)
        {
            var result = new List<Connection>();
            foreach (var agent in agents)
            {
                Connection best = null; float length = float.MaxValue;
                foreach (var point in Grid(zone.area).OrderBy(p => (p-zone.area.center).sqrMagnitude).Where((p,i) => i%9==0).Take(50))
                {
                    if (!NavMesh.SamplePosition(new Vector3(point.x,agent.MovementHeight,point.y),out var end,1f,Filter(settings)) || !zone.area.Contains(XZ(end.position))) continue;
                    foreach (Vector3 origin in layout.clearingPositions)
                    {
                        var path = new NavMeshPath();
                        if (!NavMesh.CalculatePath(origin,end.position,Filter(settings),path) || path.status != NavMeshPathStatus.PathComplete) continue;
                        var poses = Along(path.corners,Body(agent)); if (!Fits(poses,settings)) continue;
                        float distance = 0; for(int i=1;i<path.corners.Length;i++) distance += Vector3.Distance(path.corners[i-1],path.corners[i]);
                        if(distance < length) { best = new Connection { agent = agent.name, corners = path.corners }; length = distance; }
                    }
                    if (best != null) break;
                }
                if(best == null) throw new InvalidOperationException(zone.name + ": no existing full-body connector for " + agent.name);
                result.Add(best);
            }
            return result.ToArray();
        }

        private static void Apply(Zone zone, State state)
        {
            var terrain = Terrain.activeTerrain; var settings = Object.FindObjectOfType<DinosaurInteractionSettings>();
            RainforestSample.RebuildNavigation(); var nav = NavMesh.AddNavMeshData(settings.navigationData);
            var group = new GameObject(zone.name); group.transform.SetParent(Parent(),false);
            var helpers = new GameObject("Expansion clearance probes") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var agents = Object.FindObjectsOfType<DinosaurAgent>();
                var rex = agents.Single(a=>a.name=="Tyrannosaurus Rex");
                var profile = new SerializedObject(rex).FindProperty("interactionProfile").objectReferenceValue as DinosaurInteractionProfile;
                var layout = Object.FindObjectOfType<RainforestSampleLayout>();
                var clearings = new List<RainforestSample.ReservedPose>();
                for(int i=0;i<layout.clearingPositions.Length;i++) clearings.AddRange(RainforestSample.PerformancePoses(profile,layout.clearingPositions[i],layout.clearingRotations[i],rex.VisualYaw,helpers.transform));
                var walkBody = new GameObject("Sample walking reservation").AddComponent<DinosaurCoreBody>(); walkBody.transform.SetParent(helpers.transform,false);
                walkBody.bands = profile.Find(InteractionMotionRole.Walk).bodyBands;
                var reserved = Along(layout.corridor,walkBody);
                foreach (var agent in agents)
                {
                    var forward = agent.transform.rotation*Quaternion.Euler(0,agent.VisualYaw,0)*Vector3.forward;
                    reserved.AddRange(Along(new[] { agent.transform.position, agent.transform.position+forward*6f },Body(agent)));
                }
                if (!zone.generated) zone.connections = FindConnections(zone,agents,settings,layout);
                foreach(var connection in zone.connections) reserved.AddRange(Along(connection.corners,Body(agents.Single(a=>a.name==connection.agent))));
                foreach(var connection in state.zones.Where(z=>z.enabled && z!=zone).SelectMany(z=>z.connections))
                    reserved.AddRange(Along(connection.corners,Body(agents.Single(a=>a.name==connection.agent))));
                var trunkSafe = RouteObstacleClearance.CreateTrunkCandidateProbe(terrain);
                var treeSafe = RouteObstacleClearance.CreateTreeCandidateProbe(terrain, agents.SingleOrDefault(a=>a.name=="Stegosaurus"));
                Func<Vector2,bool> siteSafe = p => !MainCreekIntegration.NearCreek(p.x,p.y,2f)
                    && !settings.solidObstacles.Any(b=>p.x>b.min.x-1 && p.x<b.max.x+1 && p.y>b.min.z-1 && p.y<b.max.z+1);
                Func<Vector2,int> category = p => !siteSafe(p) ? 0 : RainforestSample.BlockedBy(clearings,p,.85f) ? 1
                    : RainforestSample.BlockedBy(reserved,p,.85f) || !trunkSafe(p,.85f) ? 2 : 3;
                var stems = Stems();
                var sites = new List<Vector2>();
                if(!zone.generated)
                {
                    zone.unavailable=zone.clearings=zone.corridor=0;
                    foreach(var point in Grid(zone.area))
                    {
                        switch(category(point)) { case 0:zone.unavailable++;break;case 1:zone.clearings++;break;case 2:zone.corridor++;break;default:sites.Add(point);break; }
                    }
                    zone.plantable=sites.Count; zone.original=stems.Count(s=>zone.area.Contains(s.p));
                    zone.retained=stems.Count(s=>zone.area.Contains(s.p) && category(s.p)==3);
                    zone.target=Mathf.RoundToInt(state.referenceDensity*zone.plantable*.65f);
                }
                var trees = new GameObject("Trees"); trees.transform.SetParent(group.transform,false);
                var random = new System.Random(zone.seed); var accepted = new List<RainforestTreeSizing.Tree>();
                var candidates = zone.generated ? zone.trees : sites.SelectMany(p=>new[] {p+new Vector2(-.25f,-.25f),p+new Vector2(.25f,.25f)})
                    .OrderBy(_=>random.Next()).Select(p=>new RainforestTreeSizing.Tree { position=p,
                        path=random.Next(3)==0 ? "Assets/VegetationDiversity/Prefabs/Pachira_B.prefab" : "Assets/NatureStarterKit2/Nature/tree0"+(1+random.Next(4))+".prefab",
                        yaw=(float)random.NextDouble()*360f, variation=Mathf.Lerp(.82f,1.14f,(float)random.NextDouble()) }).ToArray();
                foreach (var candidate in candidates)
                {
                    if (!zone.generated && accepted.Count+zone.retained>=zone.target) break;
                    Vector2 p=candidate.position;
                    if (!zone.area.Contains(p) || category(p)!=3 || stems.Any(s=>Vector2.Distance(s.p,p)<s.radius+1.6f))
                    { if(zone.generated) throw new InvalidOperationException("Stored tree site is no longer safe: "+zone.name+" "+p); continue; }
                    var tree=CentralForestDensification.Instantiate(candidate.path,trees.transform); tree.name="ExpansionTree_"+accepted.Count;
                    tree.transform.rotation=Quaternion.Euler(0,candidate.yaw,0);
                    tree.transform.localScale*=RainforestTreeSizing.BaseHeight(candidate.path)*candidate.variation/CentralForestDensification.BoundsOf(tree).size.y;
                    CentralForestDensification.Ground(tree,terrain,p);
                    foreach(var collider in tree.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
                    var marker=tree.GetComponent<SceneTreeObstacle>()??tree.AddComponent<SceneTreeObstacle>(); marker.SetRadius(.85f); marker.includeHardBranches=true;
                    bool safe=treeSafe(tree.transform);
                    if(safe) { safe=!marker.trunkSections.Any(s=>RainforestSample.BlockedBy(clearings,s.center+p,s.radius,s.bottom,s.top)
                        || RainforestSample.BlockedBy(reserved,s.center+p,s.radius,s.bottom,s.top)); }
                    if(!safe) { Object.DestroyImmediate(tree); if(zone.generated) throw new InvalidOperationException("Stored tree wood is no longer safe: "+zone.name); continue; }
                    accepted.Add(candidate); stems.Add(new Stem {p=p,radius=.85f});
                }
                if(!zone.generated) { zone.trees=accepted.ToArray(); zone.generated=true; }
                PlantUnderstory(zone,group.transform,terrain);
                RouteObstacleClearance.BakeRuntimeProfiles(group); TerrainTreeObstacleRegistry.Invalidate();
                if(!Fits(clearings,settings) || !Fits(reserved,settings)) throw new InvalidOperationException(zone.name+" obstructed an existing reservation.");
                float ratio=(zone.retained+zone.trees.Length)/Mathf.Max(1,zone.plantable)/state.referenceDensity;
                zone.status=zone.retained>state.referenceDensity*zone.plantable*.70f ? "Existing density above 70%; no trees added"
                    : ratio>=.60f && ratio<=.70f ? "Density PASS" : "Safe capacity shortfall; exclusion areas unchanged";
            }
            catch { Object.DestroyImmediate(group); throw; }
            finally { Object.DestroyImmediate(helpers); nav.Remove(); TerrainTreeObstacleRegistry.Invalidate(); }
            RainforestSample.RebuildNavigation();
        }
        private static void PlantUnderstory(Zone zone, Transform parent, Terrain terrain)
        {
            var root=new GameObject("Understory");root.transform.SetParent(parent,false); var random=new System.Random(zone.seed+100);
            int count=0;
            foreach(var stem in Stems().Where(s=>zone.area.Contains(s.p))) for(int j=0;j<3;j++)
            {
                float a=(float)random.NextDouble()*Mathf.PI*2, d=Mathf.Lerp(1.5f,3.5f,(float)random.NextDouble());
                Vector2 p=stem.p+new Vector2(Mathf.Sin(a),Mathf.Cos(a))*d;
                if(!zone.area.Contains(p)||MainCreekIntegration.NearCreek(p.x,p.y,.5f))continue;
                string path=random.Next(4)<3 ? "Assets/Vegetation/Ferns/Fern02_"+"ABCD"[random.Next(4)]+".prefab"
                    : "Assets/NatureStarterKit2/Nature/bush0"+(1+random.Next(6))+".prefab";
                var item=CentralForestDensification.Instantiate(path,root.transform);item.name="ExpansionUnderstory_"+count++;
                item.transform.rotation=Quaternion.Euler(0,(float)random.NextDouble()*360,0);
                item.transform.localScale*=Mathf.Lerp(.4f,.8f,(float)random.NextDouble())/CentralForestDensification.BoundsOf(item).size.y;
                foreach(var collider in item.GetComponentsInChildren<Collider>())Object.DestroyImmediate(collider);
                foreach(var marker in item.GetComponentsInChildren<SceneTreeObstacle>())Object.DestroyImmediate(marker);
                CentralForestDensification.Ground(item,terrain,p);
            }
        }
        private static void VerifyConnections(Zone zone)
        {
            var settings=Object.FindObjectOfType<DinosaurInteractionSettings>(); var nav=NavMesh.AddNavMeshData(settings.navigationData);
            try
            {
                foreach(var connection in zone.connections)
                {
                    var agent=Object.FindObjectsOfType<DinosaurAgent>().Single(a=>a.name==connection.agent);
                    if(!Fits(Along(connection.corners,Body(agent)),settings))throw new InvalidOperationException("Connector body clearance failed: "+zone.name);
                    for(int i=1;i<connection.corners.Length;i++)
                    {
                        var path=new NavMeshPath();
                        if(!NavMesh.CalculatePath(connection.corners[i-1],connection.corners[i],Filter(settings),path)||path.status!=NavMeshPathStatus.PathComplete)
                            throw new InvalidOperationException("Connector navigation disconnected: "+zone.name);
                    }
                }
            }
            finally { nav.Remove(); }
        }
        private static void WriteReport(State state)
        {
            Directory.CreateDirectory(Output);
            var report=new StringBuilder("# 样板相邻圈密度与构建验收\n\n范围4050m²；原样板2750m²不计入本轮。面积按1m网格互斥分类，边界半开；外围样方在首次构建前冻结。\n\n");
            report.AppendLine($"外围中位密度：{state.referenceDensity*100:F3}棵/100m²。\n\n|分区|启用|种子|总面积|不可种植|既有林隙覆盖|通道/路线/出生|可种植|原有总树|可种植原树|新增|目标|密度比|状态|\n|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");
            foreach(var z in state.zones) report.AppendLine($"|{z.name}|{z.enabled}|{z.seed}|{z.area.width*z.area.height:F0}|{z.unavailable:F0}|{z.clearings:F0}|{z.corridor:F0}|{z.plantable:F0}|{z.original}|{z.retained}|{z.trees.Length}|{z.target}|{(z.retained+z.trees.Length)/Mathf.Max(1,z.plantable)/state.referenceDensity:P1}|{z.status}|");
            report.AppendLine("\n## 冻结外围样方\n\n"+state.referenceSamples);
            report.AppendLine("\n构建检查：每区两次生成签名一致、移除后原对象和Terrain数据不变、Bake相同重放入口一致；每只启用恐龙的新增连接检查完整身体平移及2°角度采样，并复查导航连通。林隙复用样板，未新开林隙。此报告不替代普通Play、路线、互动和性能报告。\n");
            File.WriteAllText(Output+"/DensityReport.md",report.ToString());
        }
    }
}
