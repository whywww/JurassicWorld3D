using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// <summary>Owned additions only. Frozen protection mask and GUID-based replay survive scene Bake.</summary>
    public static class CenterForestGapFill
    {
        internal const string RootName="Center Forest Gap Fill", Output="Tools/VegetationDiversity/CenterGapFill";
        private const string Config="Assets/Editor/CenterForestGapFill.json";
        private static readonly Rect Area=Rect.MinMaxRect(-60,-50,60,82);
        private const int Width=120,Depth=132;
        private const float Reference=.076171875f;
        [Serializable] private sealed class State
        {
            public int version=1;
            public float referenceDensity=Reference;
            public string protectionMask;
            public Link[] links=Array.Empty<Link>();
            public Zone[] zones={new Zone("Southwest",732101,-60,-50,0,16),new Zone("Southeast",732102,0,-50,60,16),
                new Zone("Northwest",732103,-60,16,0,82),new Zone("Northeast",732104,0,16,60,82)};
        }
        [Serializable] private sealed class Link { public Vector3 a,b; }
        [Serializable] private sealed class Zone
        {
            public string name,status;
            public int seed;
            public Rect area;
            public bool enabled,generated;
            public Plant[] plants=Array.Empty<Plant>();
            public Plot[] plots=Array.Empty<Plot>();
            public float plantable,protectedArea,coverageBefore,coverageAfter;
            public Zone(string n,int s,float x,float z,float xx,float zz) {name=n;seed=s;area=Rect.MinMaxRect(x,z,xx,zz);}
        }
        [Serializable] private sealed class Plant
        {
            public string guid;
            public bool tree;
            public bool waterExcluded;
            public Vector3 position,scale;
            public float yaw,height;
        }
        [Serializable] private sealed class Plot
        {
            public Rect area;
            public int original,target,added,attempted;
            public float plantable;
        }
        private sealed class Stem { internal Vector2 p;internal float radius; }
        private sealed class ExistingWood
        {
            internal SceneTreeObstacle marker;
            internal GameObject prefab;
            internal Vector3 position,scale;
            internal Quaternion rotation;
            internal float radius;
            internal Rect bounds;
            internal bool measured;
        }
        private sealed class Context : IDisposable
        {
            internal readonly GameObject helpers=new GameObject("Gap fill probes") {hideFlags=HideFlags.HideAndDontSave};
            internal readonly Terrain terrain=Terrain.activeTerrain;
            internal readonly DinosaurInteractionSettings settings=Object.FindObjectOfType<DinosaurInteractionSettings>();
            internal readonly List<Stem> stems=new List<Stem>();
            internal readonly List<Bounds> ground=new List<Bounds>();
            internal readonly Dictionary<Vector2Int,List<TreeTrunkSection>> wood=new Dictionary<Vector2Int,List<TreeTrunkSection>>();
            internal readonly Dictionary<string,GameObject> previews=new Dictionary<string,GameObject>();
            internal string rejection;
            private int originalStems,originalGround;
            private readonly List<ExistingWood> existing=new List<ExistingWood>();
            private readonly Dictionary<GameObject,GameObject> terrainProbes=new Dictionary<GameObject,GameObject>();
            private readonly Dictionary<Vector2Int,List<TreeTrunkSection>> addedWood=new Dictionary<Vector2Int,List<TreeTrunkSection>>();
            internal readonly Func<Transform,HashSet<int>> route;
            internal readonly List<RainforestSample.ReservedPose> poses;
            internal readonly Func<Vector2,float,bool> trunk;
            internal Context(Transform ignore=null)
            {
                route=RouteObstacleClearance.CreateHeightConflictProbe(terrain);
                trunk=RouteObstacleClearance.CreateTrunkCandidateProbe(terrain);
                poses=TreeHeightAdjustment.Reservations(helpers.transform);
                foreach(var marker in Object.FindObjectsOfType<SceneTreeObstacle>())
                {
                    if(ignore!=null && marker.transform.IsChildOf(ignore))continue;
                    stems.Add(new Stem {p=XZ(marker.transform.position),radius=marker.Radius});
                    if(!Area.Overlaps(Horizontal(CentralForestDensification.BoundsOf(marker.gameObject))))continue;
                    existing.Add(new ExistingWood {marker=marker,bounds=Horizontal(CentralForestDensification.BoundsOf(marker.gameObject))});
                }
                var data=terrain.terrainData;
                foreach(var t in data.treeInstances)
                {
                    if(t.widthScale<=0)continue;
                    Vector3 p=terrain.transform.position+Vector3.Scale(t.position,data.size);
                    stems.Add(new Stem {p=XZ(p),radius=.7f*t.widthScale});
                    if(p.x<Area.xMin-15 || p.x>Area.xMax+15 || p.z<Area.yMin-15 || p.z>Area.yMax+15)continue;
                    var prefab=data.treePrototypes[t.prototypeIndex].prefab;
                    float reach=CentralForestDensification.BoundsOf(prefab).extents.magnitude*Mathf.Max(t.widthScale,t.heightScale);
                    existing.Add(new ExistingWood {prefab=prefab,position=p,rotation=Quaternion.Euler(0,t.rotation*Mathf.Rad2Deg,0),
                        scale=Vector3.Scale(prefab.transform.localScale,new Vector3(t.widthScale,t.heightScale,t.widthScale)),radius=.7f*t.widthScale,
                        bounds=new Rect(p.x-reach,p.z-reach,reach*2,reach*2)});
                }
                var seen=new HashSet<GameObject>();
                foreach(var renderer in Object.FindObjectsOfType<MeshRenderer>())
                {
                    var root=PrefabUtility.GetNearestPrefabInstanceRoot(renderer.gameObject);if(root==null || !seen.Add(root))continue;
                    string path=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                    if(path.Contains("Fern02") || System.IO.Path.GetFileName(path).StartsWith("bush0",StringComparison.OrdinalIgnoreCase))
                        ground.Add(CentralForestDensification.BoundsOf(root));
                }
                originalStems=stems.Count;originalGround=ground.Count;
            }
            internal void Reset()
            {
                stems.RemoveRange(originalStems,stems.Count-originalStems);ground.RemoveRange(originalGround,ground.Count-originalGround);
                addedWood.Clear();
            }
            private void EnsureExistingWood(Bounds bounds)
            {
                // Wood sections circumscribe clipped 1m cells. Include both
                // circumscribed radii, not just the visible mesh AABB, in broad phase.
                bounds.Expand(4f);Rect area=Horizontal(bounds);
                foreach(var item in existing.Where(e=>!e.measured && e.bounds.Overlaps(area)))
                {
                    var marker=item.marker;
                    if(marker==null)
                    {
                        if(!terrainProbes.TryGetValue(item.prefab,out var obj))
                        {obj=(GameObject)PrefabUtility.InstantiatePrefab(item.prefab,helpers.transform);terrainProbes.Add(item.prefab,obj);if(obj.GetComponent<SceneTreeObstacle>()==null)obj.AddComponent<SceneTreeObstacle>();}
                        var probe=terrainProbes[item.prefab];probe.transform.SetPositionAndRotation(item.position,item.rotation);probe.transform.localScale=item.scale;
                        marker=probe.GetComponent<SceneTreeObstacle>();marker.SetRadius(item.radius);
                    }
                    bool flag=marker.includeHardBranches;var sections=marker.trunkSections;
                    try {marker.includeHardBranches=true;route(marker.transform);AddWood(marker.transform,true);item.measured=true;}
                    finally {marker.includeHardBranches=flag;marker.trunkSections=sections;}
                }
            }
            internal void AddWood(Transform tree,bool original=false)
            {
                var destination=original?wood:addedWood;
                foreach(var s in tree.GetComponent<SceneTreeObstacle>().trunkSections)
                {
                    var section=s;section.center+=XZ(tree.position);
                    Vector2Int a=Cell(section.center-Vector2.one*section.radius),b=Cell(section.center+Vector2.one*section.radius);
                    for(int x=a.x;x<=b.x;x++)for(int z=a.y;z<=b.y;z++)
                    {var key=new Vector2Int(x,z);if(!destination.TryGetValue(key,out var list))destination.Add(key,list=new List<TreeTrunkSection>());list.Add(section);}
                }
            }
            internal bool WoodClear(Transform tree)
            {
                foreach(var s in tree.GetComponent<SceneTreeObstacle>().trunkSections)
                {
                    Vector2 p=s.center+XZ(tree.position);Vector2Int a=Cell(p-Vector2.one*s.radius),b=Cell(p+Vector2.one*s.radius);
                    for(int x=a.x;x<=b.x;x++)for(int z=a.y;z<=b.y;z++)
                        foreach(var source in new[]{wood,addedWood})
                        if(source.TryGetValue(new Vector2Int(x,z),out var list))foreach(var old in list)
                            if(s.top>old.bottom && s.bottom<old.top && Vector2.Distance(p,old.center)<s.radius+old.radius+.04f)
                            {rejection=(source==wood?"original wood":"new wood")+" gap="+(Vector2.Distance(p,old.center)-s.radius-old.radius).ToString("R")+" at "+p+" / "+old.center;return false;}
                }
                return true;
            }
            internal bool SafeTree(GameObject obj)
            {
                var marker=obj.GetComponent<SceneTreeObstacle>();marker.includeHardBranches=true;
                if(route(obj.transform).Count>0){rejection="route";return false;}
                EnsureExistingWood(CentralForestDensification.BoundsOf(obj));
                if(!WoodClear(obj.transform))return false;
                foreach(var s in marker.trunkSections)
                {
                    var world=s;world.center+=XZ(obj.transform.position);
                    if(poses.Any(p=>p.body.Intersects(p.position,p.forward,world))){rejection="reserved body";return false;}
                    Vector3 center=new Vector3(world.center.x,(world.bottom+world.top)*.5f,world.center.y);
                    var box=new Bounds(center,new Vector3(world.radius*2,world.top-world.bottom,world.radius*2));
                    if(settings.solidObstacles.Any(b=>b.Intersects(box))){rejection="solid";return false;}
                }
                return true;
            }
            public void Dispose() {Object.DestroyImmediate(helpers);}
        }
        private static Vector2 XZ(Vector3 p)=>new Vector2(p.x,p.z);
        private static Vector2Int Cell(Vector2 p)=>new Vector2Int(Mathf.FloorToInt(p.x/4),Mathf.FloorToInt(p.y/4));
        private static Rect Horizontal(Bounds b)=>Rect.MinMaxRect(b.min.x,b.min.z,b.max.x,b.max.z);
        private static State Load()=>File.Exists(Config)?JsonUtility.FromJson<State>(File.ReadAllText(Config)):new State();
        private static void Persist(State s) {File.WriteAllText(Config,JsonUtility.ToJson(s,true)+"\n");AssetDatabase.ImportAsset(Config);}
        private static Transform Parent()=>(GameObject.Find(RootName)??new GameObject(RootName)).transform;
        private static void Delete(Zone z) {var root=GameObject.Find(RootName);if(root!=null){var child=root.transform.Find(z.name);if(child!=null)Object.DestroyImmediate(child.gameObject);}}
        private static void Open()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Save scene and stop Play first.");
            EditorSceneManager.OpenScene(CentralForestDensification.ScenePath);Directory.CreateDirectory(Output);
        }
        private static void SaveScene()
        {
            RainforestSample.RebuildNavigation();FoliageInteractionPreparation.SeedConfigured();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());AssetDatabase.SaveAssets();
        }
        private static void Checkpoint()
        {
            string folder=Output+"/Checkpoint";Directory.CreateDirectory(folder);
            foreach(string path in new[]{CentralForestDensification.ScenePath,"Assets/Editor/RainforestSamplePlanting.json","Assets/Editor/RainforestExpansion.json","Assets/Editor/TreeHeightPolicy.json","Assets/Editor/TreeOverlapWithdrawals.json",Config,AssetDatabase.GetAssetPath(Terrain.activeTerrain.terrainData)})
                if(File.Exists(path) && !File.Exists(folder+"/"+Path.GetFileName(path)))File.Copy(path,folder+"/"+Path.GetFileName(path));
            string nav=AssetDatabase.GetAssetPath(Object.FindObjectOfType<DinosaurInteractionSettings>().navigationData);
            if(!File.Exists(folder+"/Navigation_Before.asset"))File.Copy(nav,folder+"/Navigation_Before.asset");
        }
        private static byte Category(State state,Vector2 p)
        {
            if(!Area.Contains(p))return 0;
            return Convert.FromBase64String(state.protectionMask)[Mathf.FloorToInt(p.y-Area.yMin)*Width+Mathf.FloorToInt(p.x-Area.xMin)];
        }
        private static byte[] mask;
        private static bool Plantable(Vector2 p)=>Area.Contains(p) && mask[Mathf.FloorToInt(p.y-Area.yMin)*Width+Mathf.FloorToInt(p.x-Area.xMin)]==3;
        private static bool GroundFits(Bounds bounds)
        {
            for(float x=bounds.min.x;x<=bounds.max.x+.25f;x+=.25f)for(float z=bounds.min.z;z<=bounds.max.z+.25f;z+=.25f)
                if(!Plantable(new Vector2(Mathf.Min(x,bounds.max.x),Mathf.Min(z,bounds.max.z))))return false;
            return true;
        }
        private static void Freeze(State state,Context c)
        {
            mask=new byte[Width*Depth];var layout=Object.FindObjectOfType<RainforestSampleLayout>();
            int clearingCount=0;
            if(layout!=null)
            {
                // Performance reservations precede the walk/spawn reservations in the shared builder.
                var agent=Object.FindObjectsOfType<DinosaurAgent>().Single(a=>a.name=="Tyrannosaurus Rex");
                var profile=new SerializedObject(agent).FindProperty("interactionProfile").objectReferenceValue as DinosaurInteractionProfile;
                var temp=new GameObject("Frozen clearing probes");
                try {for(int i=0;i<layout.clearingPositions.Length;i++)clearingCount+=RainforestSample.PerformancePoses(profile,layout.clearingPositions[i],layout.clearingRotations[i],agent.VisualYaw,temp.transform).Count;}
                finally {Object.DestroyImmediate(temp);}
            }
            var clear=c.poses.Take(clearingCount).ToList();var walk=c.poses.Skip(clearingCount).ToList();
            for(int z=0;z<Depth;z++)for(int x=0;x<Width;x++)
            {
                Vector2 p=new Vector2(Area.xMin+x+.5f,Area.yMin+z+.5f);
                byte value=MainCreekIntegration.NearCreek(p.x,p.y,2f)||c.settings.solidObstacles.Any(b=>Horizontal(b).Contains(p))?(byte)0
                    :RainforestSample.BlockedBy(clear,p,.5f)?(byte)1
                    :RainforestSample.BlockedBy(walk,p,.5f)||!c.trunk(p,.5f)?(byte)2:(byte)3;
                mask[z*Width+x]=value;
            }
            state.protectionMask=Convert.ToBase64String(mask);
            var links=new List<Link>();var handle=NavMesh.AddNavMeshData(c.settings.navigationData);
            try
            {
                for(int route=0;route<MovementCorridor.RouteCount;route++)
                {
                    MovementCorridor.GetSampleAt(route,0,out Vector2 a,out _);MovementCorridor.GetSampleAt(route,MovementCorridor.Length(route)*.5f,out Vector2 b,out _);
                    var path=new NavMeshPath();var filter=new NavMeshQueryFilter {agentTypeID=c.settings.navigationAgentTypeId,areaMask=1};
                    if(NavMesh.SamplePosition(new Vector3(a.x,0,a.y),out var aa,10,filter) && NavMesh.SamplePosition(new Vector3(b.x,0,b.y),out var bb,10,filter)
                        && NavMesh.CalculatePath(aa.position,bb.position,filter,path) && path.status==NavMeshPathStatus.PathComplete)links.Add(new Link {a=aa.position,b=bb.position});
                }
            }
            finally {handle.Remove();}
            state.links=links.ToArray();
        }
        private static void VerifyLinks(State state)
        {
            var settings=Object.FindObjectOfType<DinosaurInteractionSettings>();var handle=NavMesh.AddNavMeshData(settings.navigationData);
            try
            {
                var filter=new NavMeshQueryFilter {agentTypeID=settings.navigationAgentTypeId,areaMask=1};
                foreach(var link in state.links)
                {var path=new NavMeshPath();if(!NavMesh.CalculatePath(link.a,link.b,filter,path)||path.status!=NavMeshPathStatus.PathComplete)throw new InvalidOperationException("Previously connected route became blocked.");}
            }
            finally {handle.Remove();}
        }
        private static string[] TreeGuids()=>new[]{"tree01","tree02","tree03","tree04","Pachira_B"}.Select(name=>AssetDatabase.FindAssets(name+" t:Prefab")
            .First(g=>Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g))==name)).ToArray();
        private static string[] GroundGuids()=>new[]{"Fern02_A","Fern02_B","Fern02_C","Fern02_D","bush01","bush02","bush03","bush04","bush05","bush06"}
            .Select(name=>AssetDatabase.FindAssets(name+" t:Prefab").First(g=>Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g))==name)).ToArray();
        private static GameObject Create(Plant p,Transform parent)
        {
            string path=AssetDatabase.GUIDToAssetPath(p.guid);if(string.IsNullOrEmpty(path))throw new InvalidOperationException("Missing plant GUID "+p.guid);
            var obj=CentralForestDensification.Instantiate(path,parent);obj.name=(p.tree?"GapTree_":"GapGround_")+parent.childCount;
            obj.transform.SetPositionAndRotation(p.position,Quaternion.Euler(0,p.yaw,0));obj.transform.localScale=p.scale;
            if(p.tree){var m=obj.GetComponent<SceneTreeObstacle>()??obj.AddComponent<SceneTreeObstacle>();m.SetRadius(.35f*p.height/6);m.includeHardBranches=true;}
            else obj.AddComponent<CenterGapGroundPlant>();
            if(p.waterExcluded && WaterPlantCleanup.Enabled)obj.SetActive(false);
            foreach(var col in obj.GetComponentsInChildren<Collider>())Object.DestroyImmediate(col);
            return obj;
        }
        private static Plant Candidate(string guid,bool tree,Vector2 p,float yaw,float height,Context c,out GameObject obj)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            var result=new Plant {guid=guid,tree=tree,position=new Vector3(p.x,0,p.y),yaw=yaw,height=height,scale=prefab.transform.localScale};
            if(!c.previews.TryGetValue(guid,out obj)){obj=Create(result,c.helpers.transform);c.previews.Add(guid,obj);}
            obj.transform.SetPositionAndRotation(result.position,Quaternion.Euler(0,yaw,0));obj.transform.localScale=result.scale;
            if(tree)obj.GetComponent<SceneTreeObstacle>().SetRadius(.35f*height/6);
            obj.transform.localScale*=height/CentralForestDensification.BoundsOf(obj).size.y;
            CentralForestDensification.Ground(obj,Terrain.activeTerrain,p);result.scale=obj.transform.localScale;result.position=obj.transform.position;
            return result;
        }
        private static IEnumerable<Vector2> Sites(Rect area,float spacing,System.Random random)
        {
            var sites=new List<Vector2>();
            for(float x=area.xMin+spacing*.5f;x<area.xMax;x+=spacing)for(float z=area.yMin+spacing*.5f;z<area.yMax;z+=spacing)
                sites.Add(new Vector2(x+((float)random.NextDouble()-.5f)*spacing*.6f,z+((float)random.NextDouble()-.5f)*spacing*.6f));
            return sites;
        }
        private static float Nearest(Vector2 p,List<Stem> stems)=>stems.Count==0?100:stems.Min(s=>Vector2.Distance(p,s.p)-s.radius);
        private static bool Covered(Vector2 p,List<Bounds> bounds)=>bounds.Any(b=>Horizontal(b).Contains(p));
        private static List<Vector2> GroundSamples(Rect area)
        {
            var points=new List<Vector2>();for(float x=area.xMin+.25f;x<area.xMax;x+=.5f)for(float z=area.yMin+.25f;z<area.yMax;z+=.5f)
                if(Plantable(new Vector2(x,z)))points.Add(new Vector2(x,z));return points;
        }
        private static string Signature(Zone z)=>string.Join("\n",Parent().Find(z.name).GetComponentsInChildren<Transform>(true)
            .Select(t=>t.name+t.position.ToString("F6")+t.rotation.ToString("F6")+t.lossyScale.ToString("F6")));
        private static void Apply(Zone zone,Context c)
        {
            var parent=new GameObject(zone.name).transform;parent.SetParent(Parent(),false);
            if(zone.generated)
            {
                foreach(var plant in zone.plants)
                {
                    var obj=Create(plant,parent);
                    if(plant.waterExcluded && WaterPlantCleanup.Enabled)continue;
                    if(plant.tree)
                    {
                        if(!Plantable(XZ(plant.position)) || Nearest(XZ(plant.position),c.stems)<.15f || !c.SafeTree(obj))throw new InvalidOperationException("Stored tree no longer safe: "+zone.name+" "+plant.position+" "+c.rejection);
                        c.stems.Add(new Stem {p=XZ(plant.position),radius=obj.GetComponent<SceneTreeObstacle>().Radius});c.AddWood(obj.transform);
                    }
                    else if(!GroundFits(CentralForestDensification.BoundsOf(obj)))throw new InvalidOperationException("Stored ground plant invades protection.");
                }
                return;
            }
            var random=new System.Random(zone.seed);var trees=TreeGuids();var planted=new List<Plant>();var plots=new List<Plot>();
            for(float z=zone.area.yMin;z<zone.area.yMax;z+=10)for(float x=zone.area.xMin;x<zone.area.xMax;x+=10)
            {
                var plot=new Plot {area=Rect.MinMaxRect(x,z,Mathf.Min(x+10,zone.area.xMax),Mathf.Min(z+10,zone.area.yMax))};
                for(float xx=plot.area.xMin+.5f;xx<plot.area.xMax;xx++)for(float zz=plot.area.yMin+.5f;zz<plot.area.yMax;zz++)if(Plantable(new Vector2(xx,zz)))plot.plantable++;
                plot.original=c.stems.Count(s=>plot.area.Contains(s.p)&&Plantable(s.p));plot.target=Mathf.RoundToInt(plot.plantable*Reference*.65f);plots.Add(plot);
            }
            foreach(var plot in plots.OrderBy(p=>p.original/(p.plantable+1)))
            {
                File.WriteAllText("Logs/center-gap-progress.txt",zone.name+" plot "+plot.area+" new trees="+planted.Count(t=>t.tree));
                var sites=Sites(plot.area,1.5f,random).Where(Plantable).OrderByDescending(p=>Nearest(p,c.stems)).ToList();
                while(plot.original+plot.added<plot.target && sites.Count>0)
                {
                    Vector2 p=sites.OrderByDescending(v=>Nearest(v,c.stems)).First();sites.Remove(p);plot.attempted++;
                    if(Nearest(p,c.stems)<1f || MainCreekIntegration.NearCreek(p.x,p.y,2f) || WaterPlantCleanup.IsWet(new Vector3(p.x,0,p.y)))continue;
                    bool accepted=false;int start=random.Next(trees.Length);float yaw=(float)random.NextDouble()*360;
                    for(int species=0;species<trees.Length && !accepted;species++)
                    {
                        string guid=trees[(start+species)%trees.Length];string name=Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
                        float h=(name=="tree01"?7.8f:name=="tree02"?7.2f:name=="tree03"?6.8f:5.8f)*Mathf.Lerp(.9f,1.03f,(float)random.NextDouble());h=Mathf.Clamp(h,5,8);
                        // Main range first; 8-10m exceptions are limited to 20% of accepted trees.
                        var heights=new[]{h,5f,6f,7f,8f}.Concat(planted.Count(t=>t.tree&&t.height>8)<Mathf.FloorToInt((planted.Count(t=>t.tree)+1)*.2f)?new[]{9f,10f}:Array.Empty<float>());
                        foreach(float height in heights)
                        {
                            var plant=Candidate(guid,true,p,yaw,height,c,out var obj);
                            if(c.SafeTree(obj))
                            {
                                var actual=Create(plant,parent);
                                // Measure in the final hierarchy too: clipping a wood mesh
                                // into world cells can differ at floating-point cell seams.
                                if(!c.SafeTree(actual)){Object.DestroyImmediate(actual);continue;}
                                planted.Add(plant);c.stems.Add(new Stem {p=p,radius=actual.GetComponent<SceneTreeObstacle>().Radius});c.AddWood(actual.transform);plot.added++;accepted=true;break;
                            }
                        }
                    }
                }
            }
            zone.plots=plots.ToArray();zone.plantable=plots.Sum(p=>p.plantable);zone.protectedArea=zone.area.width*zone.area.height-zone.plantable;
            var samples=GroundSamples(zone.area);zone.coverageBefore=samples.Count==0?0:samples.Count(p=>Covered(p,c.ground))/(float)samples.Count;
            var groundGuids=GroundGuids();int attempts=0;float coverage=zone.coverageBefore;
            var clusters=Sites(zone.area,2f,random).Where(Plantable).OrderBy(p=>Covered(p,c.ground)?1:0).ToList();
            foreach(Vector2 center in clusters)
            {
                if(attempts%50==0)File.WriteAllText("Logs/center-gap-progress.txt",zone.name+" understory "+coverage.ToString("P1")+" attempts="+attempts);
                if(coverage>=.45f || attempts>6000)break;
                if(Covered(center,c.ground))continue;int count=random.Next(4,10);
                for(int i=0;i<count;i++)
                {
                    attempts++;float angle=(float)random.NextDouble()*Mathf.PI*2;float spread=Mathf.Lerp(.2f,1.4f,(float)random.NextDouble());Vector2 p=center+new Vector2(Mathf.Sin(angle),Mathf.Cos(angle))*spread;
                    if(!zone.area.Contains(p)||!Plantable(p)||Nearest(p,c.stems)<.25f||WaterPlantCleanup.IsWet(new Vector3(p.x,0,p.y)))continue;
                    string guid=groundGuids[random.NextDouble()<.75?random.Next(4):random.Next(4,groundGuids.Length)];
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                    // Only leaf-only shrubs enter the flexible ground layer; wood-bearing
                    // resources are not made passable by deleting their collision metadata.
                    if(prefab.GetComponentsInChildren<MeshRenderer>().SelectMany(r=>r.sharedMaterials).Any(RouteObstacleClearance.IsWood))continue;
                    bool fern=AssetDatabase.GUIDToAssetPath(guid).Contains("Fern02");float height=fern?Mathf.Lerp(.3f,.8f,(float)random.NextDouble()):Mathf.Lerp(.5f,1.1f,(float)random.NextDouble());
                    var plant=Candidate(guid,false,p,(float)random.NextDouble()*360,height,c,out var obj);var bounds=CentralForestDensification.BoundsOf(obj);
                    if(!GroundFits(bounds)||planted.Any(t=>Vector2.Distance(XZ(t.position),p)<.15f))continue;
                    Create(plant,parent);c.ground.Add(bounds);planted.Add(plant);
                }
                coverage=samples.Count==0?0:samples.Count(p=>Covered(p,c.ground))/(float)samples.Count;
            }
            zone.coverageAfter=coverage;zone.plants=planted.ToArray();zone.generated=zone.enabled=true;
            File.WriteAllText(Output+"/Draft-"+zone.name+".json",JsonUtility.ToJson(zone,true));
            zone.status=plots.Sum(p=>Mathf.Max(0,p.target-p.original-p.added))>0?"Safe capacity shortfall (preserved)":"Local quotas met";
        }
        [MenuItem("Jurassic World/Environment/Center Gap Fill/Build and Verify")]
        public static void BuildAndVerify()=>Build(null);
        [MenuItem("Jurassic World/Environment/Center Gap Fill/Rebuild/Southwest")] public static void Southwest()=>Build("Southwest");
        [MenuItem("Jurassic World/Environment/Center Gap Fill/Rebuild/Southeast")] public static void Southeast()=>Build("Southeast");
        [MenuItem("Jurassic World/Environment/Center Gap Fill/Rebuild/Northwest")] public static void Northwest()=>Build("Northwest");
        [MenuItem("Jurassic World/Environment/Center Gap Fill/Rebuild/Northeast")] public static void Northeast()=>Build("Northeast");
        [MenuItem("Jurassic World/Environment/Center Gap Fill/Undo/Southwest")] public static void UndoSouthwest()=>Remove("Southwest");
        [MenuItem("Jurassic World/Environment/Center Gap Fill/Undo/Southeast")] public static void UndoSoutheast()=>Remove("Southeast");
        [MenuItem("Jurassic World/Environment/Center Gap Fill/Undo/Northwest")] public static void UndoNorthwest()=>Remove("Northwest");
        [MenuItem("Jurassic World/Environment/Center Gap Fill/Undo/Northeast")] public static void UndoNortheast()=>Remove("Northeast");
        [MenuItem("Jurassic World/Environment/Center Gap Fill/Undo All")] public static void UndoAll()=>Remove(null);
        private static void Build(string selected)
        {
            Open();Checkpoint();FoliageInteractionPreparation.RestoreCurrent();State state=Load();
            if(string.IsNullOrEmpty(state.protectionMask)){using(var c=new Context())Freeze(state,c);Persist(state);}
            mask=Convert.FromBase64String(state.protectionMask);Capture("before");
            foreach(var zone in state.zones.Where(z=>selected==null||z.name==selected))
            {
                string old=JsonUtility.ToJson(zone);bool existed=Parent().Find(zone.name)!=null;Delete(zone);var snapshot=new RainforestSample.Snapshot();
                try
                {
                    using(var c=new Context())
                    {
                        Apply(zone,c);snapshot.Check();string first=Signature(zone);
                        Delete(zone);snapshot.Check();c.Reset();Apply(zone,c);snapshot.Check();
                        if(first!=Signature(zone))throw new InvalidOperationException("Replay transforms differ.");
                        Delete(zone);snapshot.Check();c.Reset();Apply(zone,c);snapshot.Check();
                        if(first!=Signature(zone))throw new InvalidOperationException("Bake replay transforms differ.");
                    }
                    ValidateZone(zone);zone.enabled=true;Persist(state);SaveScene();VerifyLinks(state);WriteReport(state);FoliageInteractionPreparation.RestoreCurrent();
                    Debug.Log("CENTER_GAP_ZONE_PASS "+zone.name);
                }
                catch(Exception error)
                {
                    FoliageInteractionPreparation.RestoreCurrent();Delete(zone);JsonUtility.FromJsonOverwrite(old,zone);
                    if(existed)using(var c=new Context())Apply(zone,c);
                    Persist(state);SaveScene();WriteReport(state);File.WriteAllText(Output+"/Failure.md",zone.name+" rolled back; later batches stopped.\n\n"+error);throw;
                }
            }
            Persist(state);SaveScene();Capture("after");WriteReport(state);Debug.Log("CENTER_GAP_FILL_PASS");
        }
        private static void ValidateZone(Zone z)
        {
            var trees=z.plants.Where(p=>p.tree).ToArray();
            if(trees.Any(p=>p.height>10.01f||p.height<4.99f)||trees.Count(p=>p.height<=8.01f)<trees.Length*.8f)throw new InvalidOperationException("New tree height contract failed.");
            for(int i=0;i<z.plants.Length;i++)for(int j=i+1;j<z.plants.Length;j++)if(Vector2.Distance(XZ(z.plants[i].position),XZ(z.plants[j].position))<.05f)throw new InvalidOperationException("Duplicate new roots.");
        }
        private static void Remove(string selected)
        {
            Open();FoliageInteractionPreparation.RestoreCurrent();var state=Load();
            foreach(var z in state.zones.Where(z=>selected==null||z.name==selected)){Delete(z);z.enabled=false;}
            Persist(state);SaveScene();WriteReport(state);
        }
        internal static void SeedConfigured()
        {
            if(!File.Exists(Config))return;var state=Load();mask=Convert.FromBase64String(state.protectionMask);
            foreach(var z in state.zones.Where(z=>z.enabled))
            {Delete(z);try {using(var c=new Context())Apply(z,c);ValidateZone(z);}catch {Delete(z);throw;}}
        }
        internal static void MarkWaterExclusions()
        {
            if(!File.Exists(Config))return;var state=Load();bool changed=false;
            foreach(var p in state.zones.SelectMany(z=>z.plants))
                if(!p.waterExcluded && WaterPlantCleanup.IsWet(p.position)){p.waterExcluded=true;changed=true;}
            if(changed)Persist(state);
        }
        public static void VerifySaved()
        {
            Open();FoliageInteractionPreparation.RestoreCurrent();var state=Load();mask=Convert.FromBase64String(state.protectionMask);
            var heights=new List<float>();int groundCount=0;var root=Parent();
            using(var c=new Context(root))
            foreach(var z in state.zones.Where(z=>z.enabled))
            {
                var parent=root.Find(z.name);if(parent==null || parent.childCount!=z.plants.Length)throw new InvalidOperationException("Saved population differs: "+z.name);
                ValidateZone(z);
                for(int i=0;i<z.plants.Length;i++)
                {
                    var p=z.plants[i];var obj=parent.GetChild(i).gameObject;
                    if(p.waterExcluded && WaterPlantCleanup.Enabled)continue;
                    if(Vector3.Distance(obj.transform.position,p.position)>.0001f || Vector3.Distance(obj.transform.localScale,p.scale)>.0001f
                        || AssetDatabase.AssetPathToGUID(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(obj))!=p.guid)throw new InvalidOperationException("Saved GUID/pose differs.");
                    float h=CentralForestDensification.BoundsOf(obj).size.y;
                    if(p.tree)
                    {
                        if(h>10.02f||h<4.98f||!c.SafeTree(obj))throw new InvalidOperationException("Saved tree clearance/height: "+obj.transform.position+" "+c.rejection);
                        heights.Add(h);c.AddWood(obj.transform);c.stems.Add(new Stem {p=XZ(obj.transform.position),radius=obj.GetComponent<SceneTreeObstacle>().Radius});
                    }
                    else {if(!GroundFits(CentralForestDensification.BoundsOf(obj)))throw new InvalidOperationException("Saved ground protection failed.");groundCount++;}
                }
            }
            if(heights.Count(h=>h<=8.02f)<heights.Count*.8f)throw new InvalidOperationException("Saved main-height share below80%.");
            SaveScene();VerifyLinks(state);Capture("after");WriteReport(state);
            var ordered=heights.OrderBy(h=>h).ToArray();
            File.WriteAllText(Output+"/Validation.md",$"# 保存后独立复核\n\n重新加载Main后，GUID、绝对位置与尺寸、数量、硬木间距、路线/预留身体净空、林下保护范围和6条既有导航连接通过。新增乔木 {heights.Count}，矮植 {groundCount}；实际LOD0树高最小/中位/最大 {ordered.FirstOrDefault():F3}/{(ordered.Length==0?0:ordered[ordered.Length/2]):F3}/{ordered.LastOrDefault():F3}m；5–8m占比 {(heights.Count==0?1:heights.Count(h=>h<=8.02f)/(float)heights.Count):P1}。\n\n本轮不是全场景树高调整，所有原树保持尺寸。运动与性能另行判定。\n");
            if(File.Exists(Output+"/Failure.md"))File.Move(Output+"/Failure.md",Output+"/Failure.BeforeReplayFix.md");
            Debug.Log("CENTER_GAP_SAVED_VALIDATED");
        }
        private static void WriteReport(State state)
        {
            var report=new StringBuilder("# 中心密林补空\n\n范围 X[-60,60] Z[-50,82]；冻结参考7.6171875棵/100m²，目标65%。原有树不移动、不缩放；本轮未开启统一树高。\n\n|分区|启用|可种植m²|保护m²|原树（可种区）|局部目标|新增树|新增矮植|缺口|林下前→后|状态|\n|---|---|---:|---:|---:|---:|---:|---:|---:|---|---|\n");
            var csv=new StringBuilder("zone,x,z,plantable,original,target,added,shortfall,attempts\n");
            foreach(var z in state.zones)
            {
                int original=z.plots.Sum(p=>p.original),target=z.plots.Sum(p=>p.target),gap=z.plots.Sum(p=>Mathf.Max(0,p.target-p.original-p.added));
                report.AppendLine($"|{z.name}|{z.enabled}|{z.plantable:F0}|{z.protectedArea:F0}|{original}|{target}|{z.plants.Count(p=>p.tree)}|{z.plants.Count(p=>!p.tree)}|{gap}|{z.coverageBefore:P1} → {z.coverageAfter:P1}|{z.status}|");
                foreach(var p in z.plots)csv.AppendLine(string.Join(",",z.name,p.area.x,p.area.y,p.plantable,p.original,p.target,p.added,Mathf.Max(0,p.target-p.original-p.added),p.attempted));
            }
            report.AppendLine("\n林下覆盖为0.5m网格LOD0水平包围盒估算，不计草地纹理，不代表精确叶片覆盖。原已过密网格不追加，过密不抵消其他网格缺口。安全容量不足保留已通过植物，不扩大保护区。\n\n每批完成新增硬木/保护区检查、原对象/地形快照检查、撤销、两次GUID重放，以及原有可达路线复核。运动和动态性能需另行运行，不能由构建结果推断通过。\n");
            File.WriteAllText(Output+"/Layout.md",report.ToString());File.WriteAllText(Output+"/Plots.csv",csv.ToString());
            var areas=mask.GroupBy(v=>v).ToDictionary(g=>g.Key,g=>g.Count());
            File.WriteAllText(Output+"/Protection.md",$"# 冻结保护面积\n\n1m网格，互斥分类。溪流/岩石不可种植 {areas.GetValueOrDefault((byte)0)}m²；互动林隙 {areas.GetValueOrDefault((byte)1)}m²；路线/通道/出生 {areas.GetValueOrDefault((byte)2)}m²；可种植 {areas.GetValueOrDefault((byte)3)}m²。总计15840m²。原先可达导航检查对 {state.links.Length}。\n");
        }
        internal static void Capture(string prefix)
        {
            if(prefix=="before"&&File.Exists(Output+"/before-3.png"))return;
            var source=Camera.main;if(source==null)return;var holder=new GameObject("Gap fill comparison camera");var camera=holder.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;
            var target=new RenderTexture(1280,720,24);var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            var eyes=new[]{new Vector3(0,1.7f,-13),new Vector3(-6,1.7f,14),new Vector3(29,1.7f,22),new Vector3(0,105,-42)};
            var aims=new[]{new Vector3(-5,4,18),new Vector3(9,4,30),new Vector3(0,4,20),new Vector3(0,0,18)};
            try {target.Create();camera.targetTexture=target;for(int i=0;i<4;i++){camera.transform.SetPositionAndRotation(eyes[i],Quaternion.LookRotation(aims[i]-eyes[i]));camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();File.WriteAllBytes(Output+"/"+prefix+"-"+i+".png",pixels.EncodeToPNG());}}
            finally {RenderTexture.active=previous;Object.DestroyImmediate(pixels);target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(holder);}
        }
    }
}
