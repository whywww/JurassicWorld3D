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
    /// <summary>Absolute, reversible whole-scene sizing. Source dimensions survive all generator replays.</summary>
    public static class TreeHeightAdjustment
    {
        internal const string Output = "Tools/VegetationDiversity/TreeHeights";
        private const string ConfigPath = "Assets/Editor/TreeHeightPolicy.json";
        private const string LedgerPath = "Assets/Editor/TreeHeightLedger.json";
        private static readonly Rect Center = Rect.MinMaxRect(-60,-50,60,82);
        [Serializable] private sealed class Policy
        {
            public bool enabled;
            public float centerMin=5, centerMainMax=8, centerMax=10;
            public float outerMin=8, outerMainMax=12, outerMax=14, transition=10;
        }
        [Serializable] private sealed class Ledger { public Record[] trees=Array.Empty<Record>(); }
        [Serializable] private sealed class Record
        {
            public string key, kind, species, owner;
            public Vector3 position, originalScale, appliedScale;
            public float originalHeight, appliedHeight, originalRadius, appliedRadius, originalY, appliedY;
        }
        private sealed class Tree
        {
            internal Record record;
            internal Transform transform;
            internal int terrainIndex=-1;
            internal GameObject prefab;
            internal Vector3 position;
            internal Quaternion rotation;
            internal Vector3 scale;
            internal float height, radius;
        }
        private static Policy policy;
        private static Dictionary<string,Record> records;
        private static bool applying;
        private static void Load()
        {
            policy=File.Exists(ConfigPath)?JsonUtility.FromJson<Policy>(File.ReadAllText(ConfigPath)):new Policy();
            var ledger=File.Exists(LedgerPath)?JsonUtility.FromJson<Ledger>(File.ReadAllText(LedgerPath)):new Ledger();
            records=(ledger.trees??Array.Empty<Record>()).ToDictionary(r=>r.key);
        }
        internal static bool Enabled
        {
            get { if(policy==null)Load();return policy.enabled; }
        }
        private static void Persist()
        {
            File.WriteAllText(ConfigPath,JsonUtility.ToJson(policy,true)+"\n");
            File.WriteAllText(LedgerPath,JsonUtility.ToJson(new Ledger { trees=records.Values.OrderBy(r=>r.key).ToArray() },true)+"\n");
            AssetDatabase.ImportAsset(ConfigPath);AssetDatabase.ImportAsset(LedgerPath);
        }
        private static Vector2 XZ(Vector3 p)=>new Vector2(p.x,p.z);
        private static string F(float v)=>v.ToString("F4",CultureInfo.InvariantCulture);
        private static string Key(string kind, GameObject prefab, Vector3 p, Quaternion q)
            =>kind+"|"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab))+"|"+F(p.x)+"|"+F(p.z)+"|"+q.eulerAngles.y.ToString("F2",CultureInfo.InvariantCulture);
        private static float Distance(Vector3 p)
        {
            float dx=Mathf.Max(Center.xMin-p.x,0,p.x-Center.xMax), dz=Mathf.Max(Center.yMin-p.z,0,p.z-Center.yMax);
            return Mathf.Sqrt(dx*dx+dz*dz);
        }
        private static string Region(Vector3 p)=>Center.Contains(XZ(p))?"Center":Distance(p)<policy.transition?"Transition":"Perimeter";
        private static void Range(Vector3 p,out float low,out float main,out float cap)
        {
            float t=Mathf.SmoothStep(0,1,Mathf.Clamp01(Distance(p)/policy.transition));
            low=Mathf.Lerp(policy.centerMin,policy.outerMin,t);main=Mathf.Lerp(policy.centerMainMax,policy.outerMainMax,t);cap=Mathf.Lerp(policy.centerMax,policy.outerMax,t);
        }
        private static float Target(Record record)
        {
            Range(record.position,out float low,out float main,out _);
            if(record.originalHeight<=low)return record.originalHeight;
            // Monotonic compression preserves the source species/seed ordering without
            // pulling short species up or flattening all large trees to the same cap.
            return Mathf.Lerp(low,main,1f-Mathf.Exp(-(record.originalHeight-low)/8f));
        }
        private static GameObject Source(GameObject item)
        {
            string path=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(item);
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(source==null)throw new InvalidOperationException("Tree is not a prefab instance: "+item.name);
            return source;
        }
        private static string Owner(Transform tree)
        {
            for(var t=tree;t!=null;t=t.parent)
            {
                if(t.name==RainforestSample.RootName)return "Sample";
                if(t.name==RainforestExpansion.RootName)return "Expansion";
            }
            return "Existing scene";
        }
        private static void Ground(Transform tree,Terrain terrain)
        {
            var p=tree.position;
            p.y+=terrain.SampleHeight(p)+terrain.transform.position.y-CentralForestDensification.BoundsOf(tree.gameObject).min.y-.02f;
            tree.position=p;
        }
        internal static void SeedConfigured()
        {
            if(applying)return; Load();if(!policy.enabled)return;
            FoliageInteractionPreparation.RestoreCurrent();
            var terrain=Terrain.activeTerrain;var instances=terrain.terrainData.treeInstances;
            var originals=Object.FindObjectsOfType<SceneTreeObstacle>().ToDictionary(t=>t,t=>(scale:t.transform.localScale,position:t.transform.position,radius:t.Radius));
            string ledger=JsonUtility.ToJson(new Ledger {trees=records.Values.ToArray()});
            try {ApplyCurrent(true);Validate();RebuildGeometry();Persist();}
            catch
            {
                terrain.terrainData.SetTreeInstances(instances,false);EditorUtility.SetDirty(terrain.terrainData);
                foreach(var pair in originals)
                {pair.Key.transform.localScale=pair.Value.scale;pair.Key.transform.position=pair.Value.position;pair.Key.SetRadius(pair.Value.radius);}
                records=JsonUtility.FromJson<Ledger>(ledger).trees.ToDictionary(r=>r.key);
                RebuildGeometry();throw;
            }
        }

        [MenuItem("Jurassic World/Environment/Tree Heights/Apply and Verify")]
        public static void ApplyAndVerify()
        {
            OpenClean();Directory.CreateDirectory(Output);Checkpoint();Load();
            AuditCurrent();
            string oldPolicy=JsonUtility.ToJson(policy),oldLedger=JsonUtility.ToJson(new Ledger {trees=records.Values.ToArray()});
            FoliageInteractionPreparation.RestoreCurrent();Capture("before");
            TreeOverlapCleanup.ApplyCurrentAndPersist();
            var terrain=Terrain.activeTerrain;var originalInstances=terrain.terrainData.treeInstances;
            var originals=Object.FindObjectsOfType<SceneTreeObstacle>().ToDictionary(t=>t,t=>new Vector4(t.transform.localScale.x,t.transform.localScale.y,t.transform.localScale.z,t.transform.position.y));
            var radii=originals.Keys.ToDictionary(t=>t,t=>t.Radius);
            var snapshot=new PopulationSnapshot();string initial=Dimensions();
            policy.enabled=true;
            try
            {
                ApplyCurrent(true);snapshot.Check();Validate();string first=Dimensions();
                ApplyCurrent(false);snapshot.Check();if(first!=Dimensions())throw new InvalidOperationException("Repeated application changed tree sizes.");
                RestoreDimensions(false);snapshot.Check();if(initial!=Dimensions())throw new InvalidOperationException("Undo did not restore original dimensions.");
                ApplyCurrent(true);snapshot.Check();Validate();if(first!=Dimensions())throw new InvalidOperationException("Bake replay changed dimensions.");
                Persist();Rebuild();Capture("after");Save();
                if(File.Exists(Output+"/Failure.md"))File.Delete(Output+"/Failure.md");
                File.WriteAllText("Logs/rainforest-status.txt","PASS TREE_HEIGHTS_APPLIED");
                Debug.Log("TREE_HEIGHTS_APPLIED: counts/species/XZ/rotations unchanged; repeat, undo, replay, height limits and no new clearance conflicts passed.");
            }
            catch(Exception error)
            {
                terrain.terrainData.SetTreeInstances(originalInstances,false);
                foreach(var pair in originals)
                {
                    if(pair.Key==null)continue;var v=pair.Value;pair.Key.transform.localScale=new Vector3(v.x,v.y,v.z);
                    var p=pair.Key.transform.position;p.y=v.w;pair.Key.transform.position=p;pair.Key.SetRadius(radii[pair.Key]);
                }
                policy=JsonUtility.FromJson<Policy>(oldPolicy);records=JsonUtility.FromJson<Ledger>(oldLedger).trees.ToDictionary(r=>r.key);
                Persist();Rebuild();Save();
                File.WriteAllText(Output+"/Failure.md","# 树高调整停止并恢复\n\n"+error+"\n\n整批尺寸已恢复；没有移树或放宽碰撞规则。用户授权的近乎重合树撤下独立保留，详情与恢复记录见OverlapCleanup.md。\n");
                throw;
            }
        }
        [MenuItem("Jurassic World/Environment/Tree Heights/Undo Height Adjustment")]
        public static void Undo()
        {
            OpenClean();Load();FoliageInteractionPreparation.RestoreCurrent();
            RestoreDimensions(true);policy.enabled=false;Persist();Rebuild();Save();
            Debug.Log("TREE_HEIGHTS_UNDONE: matching trees restored; unrelated scene changes retained.");
        }
        [MenuItem("Jurassic World/Environment/Tree Heights/Apply and Verify",true)]
        [MenuItem("Jurassic World/Environment/Tree Heights/Undo Height Adjustment",true)]
        private static bool MenuEnabled()=>JurassicMenuGuard.CanRun;
        private static void OpenClean()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the scene and stop Play before resizing trees.");
            if(EditorSceneManager.GetActiveScene().path!=CentralForestDensification.ScenePath)EditorSceneManager.OpenScene(CentralForestDensification.ScenePath);
        }
        private static void Save()
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());AssetDatabase.SaveAssets();
        }
        private static void Checkpoint()
        {
            string folder=Output+"/Checkpoint";Directory.CreateDirectory(folder);
            if(!File.Exists(folder+"/Main_AtApplication.unity"))File.Copy(CentralForestDensification.ScenePath,folder+"/Main_AtApplication.unity");
            foreach(var path in new[] { CentralForestDensification.ScenePath,ConfigPath,LedgerPath,"Assets/Editor/RainforestSamplePlanting.json","Assets/Editor/RainforestExpansion.json" })
                if(File.Exists(path) && !File.Exists(folder+"/"+Path.GetFileName(path)))File.Copy(path,folder+"/"+Path.GetFileName(path));
        }
        private static List<Tree> Collect()
        {
            var terrain=Terrain.activeTerrain;var result=new List<Tree>();
            foreach(var marker in Object.FindObjectsOfType<SceneTreeObstacle>())
            {
                var prefab=Source(marker.gameObject);var t=marker.transform;float height=CentralForestDensification.BoundsOf(marker.gameObject).size.y;
                string key=Key("Scene",prefab,t.position,t.rotation);
                if(!records.TryGetValue(key,out var record))
                {
                    record=new Record { key=key,kind="Scene",species=AssetDatabase.GetAssetPath(prefab),owner=Owner(t),position=t.position,
                        originalScale=t.localScale,originalY=t.position.y,originalRadius=marker.Radius,originalHeight=height };
                    records.Add(key,record);
                }
                result.Add(new Tree {record=record,transform=t,prefab=prefab,position=t.position,rotation=t.rotation,scale=t.localScale,height=height,radius=marker.Radius});
            }
            var instances=terrain.terrainData.treeInstances;var heights=new Dictionary<GameObject,float>();
            for(int i=0;i<instances.Length;i++)
            {
                var instance=instances[i];if(instance.widthScale<=0)continue;
                var prefab=terrain.terrainData.treePrototypes[instance.prototypeIndex].prefab;
                if(!heights.TryGetValue(prefab,out float basis)) {basis=CentralForestDensification.BoundsOf(prefab).size.y;heights.Add(prefab,basis);}
                Vector3 p=terrain.transform.position+Vector3.Scale(instance.position,terrain.terrainData.size);
                Quaternion q=Quaternion.Euler(0,instance.rotation*Mathf.Rad2Deg,0);string key=Key("Terrain",prefab,p,q);
                if(!records.TryGetValue(key,out var record))
                {
                    record=new Record {key=key,kind="Terrain",species=AssetDatabase.GetAssetPath(prefab),owner="Terrain",position=p,
                        originalScale=new Vector3(instance.widthScale,instance.heightScale,instance.widthScale),originalHeight=basis*instance.heightScale,originalRadius=.7f*instance.widthScale};
                    records.Add(key,record);
                }
                result.Add(new Tree {record=record,terrainIndex=i,prefab=prefab,position=p,rotation=q,
                    scale=new Vector3(instance.widthScale,instance.heightScale,instance.widthScale),height=basis*instance.heightScale,radius=.7f*instance.widthScale});
            }
            return result;
        }
        internal static List<RainforestSample.ReservedPose> Reservations(Transform helpers)
        {
            var result=new List<RainforestSample.ReservedPose>();var layout=Object.FindObjectOfType<RainforestSampleLayout>();
            var agents=Object.FindObjectsOfType<DinosaurAgent>();var rex=agents.Single(a=>a.name=="Tyrannosaurus Rex");
            var profile=new SerializedObject(rex).FindProperty("interactionProfile").objectReferenceValue as DinosaurInteractionProfile;
            if(layout!=null)
            {
                for(int i=0;i<layout.clearingPositions.Length;i++)result.AddRange(RainforestSample.PerformancePoses(profile,layout.clearingPositions[i],layout.clearingRotations[i],rex.VisualYaw,helpers));
                var body=new GameObject("Height walk reservation").AddComponent<DinosaurCoreBody>();body.transform.SetParent(helpers,false);
                body.bands=profile.Find(InteractionMotionRole.Walk).bodyBands;result.AddRange(RainforestExpansion.Along(layout.corridor,body));
            }
            foreach(var agent in agents)
            {
                var body=new SerializedObject(agent).FindProperty("interactionBody").objectReferenceValue as DinosaurCoreBody;
                if(body==null)continue;var forward=agent.transform.rotation*Quaternion.Euler(0,agent.VisualYaw,0)*Vector3.forward;
                result.AddRange(RainforestExpansion.Along(new[] {agent.transform.position,agent.transform.position+forward*6},body));
            }
            result.AddRange(RainforestExpansion.HeightReservations());return result;
        }
        private static HashSet<int> Conflicts(Transform tree,Func<Transform,HashSet<int>> routes,List<RainforestSample.ReservedPose> poses)
        {
            var marker=tree.GetComponent<SceneTreeObstacle>();bool flag=marker.includeHardBranches;
            marker.includeHardBranches=true;HashSet<int> result;
            try {result=routes(tree);}finally {marker.includeHardBranches=flag;}
            float reach=marker.trunkSections.Length==0?marker.Radius:marker.trunkSections.Max(s=>s.center.magnitude+s.radius);
            Vector2 p=XZ(tree.position);
            for(int i=0;i<poses.Count;i++)
            {
                var pose=poses[i];if(Vector2.Distance(pose.position,p)>reach+pose.body.BroadRadius)continue;
                if(marker.trunkSections.Any(s=>pose.body.Intersects(pose.position,pose.forward,new TreeTrunkSection {center=s.center+p,radius=s.radius,bottom=s.bottom,top=s.top})))result.Add(1000000+i);
            }
            return result;
        }
        private static void Resize(Transform t,float oldHeight,float target,float radius,Terrain terrain)
        {
            t.localScale*=target/oldHeight;t.GetComponent<SceneTreeObstacle>().SetRadius(radius*target/oldHeight);Ground(t,terrain);
        }
        private static void ApplyCurrent(bool clearance)
        {
            if(applying)return;
            var terrain=Terrain.activeTerrain;var rows=Collect();var instances=terrain.terrainData.treeInstances;
            var helpers=new GameObject("Tree height clearance helpers") {hideFlags=HideFlags.HideAndDontSave};
            var probes=new Dictionary<GameObject,GameObject>();
            applying=true;
            try
            {
                var routes=clearance?RouteObstacleClearance.CreateHeightConflictProbe(terrain):null;
                var poses=clearance?Reservations(helpers.transform):null;
                foreach(var row in rows)
                {
                    Transform tree=row.transform;
                    if(tree==null)
                    {
                        if(!probes.TryGetValue(row.prefab,out var probe))
                        {
                            probe=(GameObject)PrefabUtility.InstantiatePrefab(row.prefab,helpers.transform);
                            probe.hideFlags=HideFlags.HideAndDontSave;
                            foreach(var c in probe.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
                            if(probe.GetComponent<SceneTreeObstacle>()==null)probe.AddComponent<SceneTreeObstacle>();probes.Add(row.prefab,probe);
                        }
                        tree=probe.transform;tree.SetPositionAndRotation(row.position,row.rotation);
                        tree.localScale=Vector3.Scale(row.prefab.transform.localScale,row.scale);tree.GetComponent<SceneTreeObstacle>().SetRadius(row.radius);
                        tree.GetComponent<SceneTreeObstacle>().includeHardBranches=true;
                    }
                    Vector3 startScale=tree.localScale;Vector3 startPosition=tree.position;
                    float startHeight=CentralForestDensification.BoundsOf(tree.gameObject).size.y;
                    float target=row.record.appliedHeight>0?row.record.appliedHeight:Target(row.record);
                    if(row.record.appliedHeight>0 && (row.scale-row.record.appliedScale).sqrMagnitude<1e-10f
                        && Mathf.Abs(row.height-target)<.001f && (row.transform==null || Mathf.Abs(row.position.y-row.record.appliedY)<.00001f))continue;
                    var baseline=clearance?Conflicts(tree,routes,poses):null;
                    Range(row.position,out float low,out float main,out float cap);
                    var choices=new[] {target}.Concat(Enumerable.Range(0,61).Select(i=>Mathf.Lerp(low,cap,i/60f))
                        .OrderBy(h=>h>main?1000+Mathf.Abs(h-target):Mathf.Abs(h-target))).ToArray();
                    bool found=false;
                    foreach(float choice in choices)
                    {
                        if(row.record.originalHeight<low && choice!=target)break;
                        tree.localScale=startScale;tree.position=startPosition;tree.GetComponent<SceneTreeObstacle>().SetRadius(row.radius);
                        Resize(tree,startHeight,choice,row.radius,terrain);
                        if(clearance && !Conflicts(tree,routes,poses).IsSubsetOf(baseline))continue;
                        target=choice;found=true;break;
                    }
                    if(!found)throw new InvalidOperationException($"No safe height: {row.record.owner}/{row.record.species} at {row.position}, original={row.record.originalHeight:F3}m, allowed={low:F2}..{cap:F2}m.");
                    var marker=tree.GetComponent<SceneTreeObstacle>();row.record.appliedHeight=target;row.record.appliedRadius=marker.Radius;
                    if(row.terrainIndex>=0)
                    {
                        var instance=instances[row.terrainIndex];float factor=target/row.height;
                        instance.widthScale*=factor;instance.heightScale*=factor;instances[row.terrainIndex]=instance;
                        row.record.appliedScale=new Vector3(instance.widthScale,instance.heightScale,instance.widthScale);
                    }
                    else {row.record.appliedScale=tree.localScale;row.record.appliedY=tree.position.y;}
                }
                terrain.terrainData.SetTreeInstances(instances,false);EditorUtility.SetDirty(terrain.terrainData);
            }
            finally {Object.DestroyImmediate(helpers);applying=false;TerrainTreeObstacleRegistry.Invalidate();}
        }
        private static void RestoreDimensions(bool preserveEdits)
        {
            var terrain=Terrain.activeTerrain;var rows=Collect();var instances=terrain.terrainData.treeInstances;int skipped=0;
            foreach(var row in rows)
            {
                var r=row.record;if(r.appliedHeight<=0)continue;
                if(preserveEdits && (row.scale-r.appliedScale).sqrMagnitude>1e-8f) {skipped++;continue;}
                if(row.transform!=null)
                {
                    if(preserveEdits && Mathf.Abs(row.transform.position.y-r.appliedY)>.001f) {skipped++;continue;}
                    row.transform.localScale=r.originalScale;var p=row.transform.position;p.y=r.originalY;row.transform.position=p;
                    row.transform.GetComponent<SceneTreeObstacle>().SetRadius(r.originalRadius);
                }
                else {var instance=instances[row.terrainIndex];instance.widthScale=r.originalScale.x;instance.heightScale=r.originalScale.y;instances[row.terrainIndex]=instance;}
            }
            terrain.terrainData.SetTreeInstances(instances,false);EditorUtility.SetDirty(terrain.terrainData);TerrainTreeObstacleRegistry.Invalidate();
            if(preserveEdits)File.WriteAllText(Output+"/Undo.md",$"Restored matching source sizes; skipped {skipped} subsequently edited trees. Other fields retained.\n");
        }
        private static string Dimensions()
        {
            var rows=Collect();return string.Join("\n",rows.OrderBy(t=>t.record.key).Select(t=>t.record.key+"|"+t.scale.ToString("F5")+"|"+F(t.position.y)));
        }
        private static void Rebuild()
        {
            bool prior=applying;applying=true;
            try
            {
                RebuildGeometry();FoliageInteractionPreparation.SeedConfigured();
            }
            finally {applying=prior;}
        }
        private static void RebuildGeometry()
        {
            var root=GameObject.Find("Tree Height Data")??new GameObject("Tree Height Data");
            RouteObstacleClearance.BakeRuntimeProfiles(root);TerrainTreeObstacleRegistry.Invalidate();RainforestSample.RebuildNavigation();
        }
        internal static void RefreshGeometry()=>RebuildGeometry();
        private static void Validate()
        {
            var rows=Collect();var report=new StringBuilder("# 全场景树高\n\n中心X[-60,60] Z[-50,82]，边界外10米平滑过渡。树高为LOD0渲染包围盒，树种、XZ位置、朝向、棵数不变；根部重新贴地。\n\n|区域|数量|调整前中位m|调整后中位m|调整后最高m|原高于下限乔木|进入主体范围|比例|\n|---|---:|---:|---:|---:|---:|---:|---:|\n");
            foreach(var group in rows.GroupBy(r=>Region(r.position)))
            {
                var list=group.ToArray();var eligible=list.Where(t=>{Range(t.position,out float low,out _,out _);return t.record.originalHeight>=low;}).ToArray();
                int inMain=eligible.Count(t=>{Range(t.position,out float low,out float main,out _);return t.height>=low-.02f && t.height<=main+.02f;});
                foreach(var t in list) {Range(t.position,out _,out _,out float cap);if(t.height>cap+.02f)throw new InvalidOperationException("Height cap exceeded: "+t.record.key);}
                float fraction=eligible.Length==0?1:inMain/(float)eligible.Length;
                report.AppendLine($"|{group.Key}|{list.Length}|{Median(list.Select(t=>(double)t.record.originalHeight)):F3}|{Median(list.Select(t=>(double)t.height)):F3}|{list.Max(t=>t.height):F3}|{eligible.Length}|{inMain}|{fraction:P1}|");
                if(fraction<.8f)throw new InvalidOperationException(group.Key+" main-height share below 80%: "+fraction);
            }
            report.AppendLine("\n|归属|数量|原最高m|现最高m|\n|---|---:|---:|---:|");
            foreach(var g in rows.GroupBy(r=>r.record.owner))report.AppendLine($"|{g.Key}|{g.Count()}|{g.Max(t=>t.record.originalHeight):F3}|{g.Max(t=>t.height):F3}|");
            float before=Coverage(rows,true),after=Coverage(rows,false);
            report.AppendLine($"\n树冠覆盖上界（2m网格、LOD0水平包围圆，含重叠）：{before:P1} → {after:P1}。此值不是实际叶面积覆盖率；没有增种填补缩小后的空隙。\n\n尺寸来自绝对原始记录，重复应用不累积缩小。安全检查对比每棵树调整前后完整硬木的路线/身体及林隙/出生/连接通道冲突集合，禁止新增冲突，原有冲突不隐藏。运行时碰撞策略未改变。\n");
            Directory.CreateDirectory(Output);File.WriteAllText(Output+"/HeightReport.md",report.ToString());
            File.WriteAllText(Output+"/Trees.csv","kind,owner,species,x,z,original_height,final_height\n"+string.Join("\n",rows.OrderBy(t=>t.record.key)
                .Select(t=>$"{t.record.kind},{t.record.owner},{t.record.species},{F(t.position.x)},{F(t.position.z)},{F(t.record.originalHeight)},{F(t.height)}")));
        }
        private static double Median(IEnumerable<double> values) {var a=values.OrderBy(v=>v).ToArray();return a.Length==0?0:a[a.Length/2];}
        private static float Coverage(List<Tree> rows,bool original)
        {
            var crowns=rows.Select(t=>{var b=CentralForestDensification.BoundsOf(t.transform!=null?t.transform.gameObject:t.prefab);
                float r=Mathf.Max(b.extents.x,b.extents.z)*(t.transform!=null?1:t.scale.x);
                if(original)r*=t.record.originalHeight/t.height;
                return new Vector3(t.position.x,t.position.z,r);}).ToArray();
            int hits=0;for(float x=-99;x<100;x+=2)for(float z=-99;z<100;z+=2)
                if(crowns.Any(c=>(new Vector2(c.x,c.y)-new Vector2(x,z)).sqrMagnitude<c.z*c.z))hits++;
            return hits/10000f;
        }
        private sealed class PopulationSnapshot
        {
            private readonly TerrainData data=Terrain.activeTerrain.terrainData;
            private readonly TreeInstance[] trees=Terrain.activeTerrain.terrainData.treeInstances;
            private readonly Dictionary<SceneTreeObstacle,string> scene=Object.FindObjectsOfType<SceneTreeObstacle>().ToDictionary(t=>t,t=>F(t.transform.position.x)+F(t.transform.position.z)+t.transform.rotation.ToString("F5"));
            private readonly Dictionary<DinosaurAgent,string> dinosaurs=Object.FindObjectsOfType<DinosaurAgent>(true).ToDictionary(t=>t,t=>t.transform.position.ToString("F5")+t.transform.rotation.ToString("F5")+t.transform.localScale.ToString("F5"));
            private readonly float[] heights,paint;
            internal PopulationSnapshot() {heights=data.GetHeights(0,0,data.heightmapResolution,data.heightmapResolution).Cast<float>().ToArray();paint=data.GetAlphamaps(0,0,data.alphamapWidth,data.alphamapHeight).Cast<float>().ToArray();}
            internal void Check()
            {
                var current=data.treeInstances;if(current.Length!=trees.Length || Object.FindObjectsOfType<SceneTreeObstacle>().Length!=scene.Count)throw new InvalidOperationException("Tree population changed.");
                for(int i=0;i<trees.Length;i++)if(trees[i].position!=current[i].position || trees[i].prototypeIndex!=current[i].prototypeIndex || trees[i].rotation!=current[i].rotation || !trees[i].color.Equals(current[i].color) || !trees[i].lightmapColor.Equals(current[i].lightmapColor))throw new InvalidOperationException("Terrain tree identity changed.");
                foreach(var p in scene)if(p.Key==null || p.Value!=F(p.Key.transform.position.x)+F(p.Key.transform.position.z)+p.Key.transform.rotation.ToString("F5"))throw new InvalidOperationException("Scene tree position/rotation changed.");
                foreach(var p in dinosaurs)if(p.Key==null || p.Value!=p.Key.transform.position.ToString("F5")+p.Key.transform.rotation.ToString("F5")+p.Key.transform.localScale.ToString("F5"))throw new InvalidOperationException("Dinosaur size/pose changed.");
                if(!heights.SequenceEqual(data.GetHeights(0,0,data.heightmapResolution,data.heightmapResolution).Cast<float>()) || !paint.SequenceEqual(data.GetAlphamaps(0,0,data.alphamapWidth,data.alphamapHeight).Cast<float>()))throw new InvalidOperationException("Terrain geometry/paint changed.");
            }
        }
        private static void Capture(string prefix)
        {
            var source=Camera.main;if(source==null)return;
            var holder=new GameObject("Tree height comparison camera") {hideFlags=HideFlags.HideAndDontSave};var camera=holder.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;
            var target=new RenderTexture(1280,720,24);var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            var eyes=new[] {new Vector3(0,1.7f,-13),new Vector3(-6,1.7f,14),new Vector3(29,1.7f,22),new Vector3(0,105,-42)};
            var aims=new[] {new Vector3(-5,4,18),new Vector3(9,4,30),new Vector3(0,4,20),new Vector3(0,0,18)};
            var debugStates=Object.FindObjectsOfType<MovementCorridorDebug>(true).ToDictionary(d=>d.gameObject,d=>d.gameObject.activeSelf);
            var timings=new StringBuilder("view,camera_render_cpu_median_ms,draw_calls_median,gpu_available_samples,gpu_median_ms\n");
            try
            {
                target.Create();camera.targetTexture=target;
                foreach(var debug in debugStates.Keys)debug.SetActive(false);
                for(int i=0;i<eyes.Length;i++)
                {
                    camera.transform.SetPositionAndRotation(eyes[i],Quaternion.LookRotation(aims[i]-eyes[i]));
                    var cpu=new List<double>();var draws=new List<double>();var gpu=new List<double>();var frameTimings=new FrameTiming[1];
                    for(int sample=0;sample<38;sample++)
                    {
                        var watch=System.Diagnostics.Stopwatch.StartNew();camera.Render();watch.Stop();
                        FrameTimingManager.CaptureFrameTimings();
                        if(sample<8)continue;
                        cpu.Add(watch.Elapsed.TotalMilliseconds);draws.Add(UnityStats.drawCalls);
                        if(FrameTimingManager.GetLatestTimings(1,frameTimings)>0 && frameTimings[0].gpuFrameTime>0)gpu.Add(frameTimings[0].gpuFrameTime);
                    }
                    timings.AppendLine(string.Join(",",i,Median(cpu).ToString("F4",CultureInfo.InvariantCulture),draws.Any(v=>v>0)?Median(draws).ToString("F0",CultureInfo.InvariantCulture):"unavailable",gpu.Count,
                        gpu.Count==0?"unavailable":Median(gpu).ToString("F4",CultureInfo.InvariantCulture)));
                    RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();File.WriteAllBytes(Output+"/"+prefix+"-"+i+".png",pixels.EncodeToPNG());
                }
                File.WriteAllText(Output+"/"+prefix+"-timings.csv",timings.ToString());
                File.WriteAllText(Output+"/Performance.md","# 固定机位渲染计时\n\nbefore/after-timings.csv 使用相同1280×720机位，各预热8次、采样30次 Camera.Render CPU耗时和绘制调用中位值。GPU仅在FrameTimingManager提供有效样本时报告，否则明确标为unavailable。\n\n这是编辑态静态画面对照，包含CPU渲染提交及可能的同步等待；不等同于启用所有恐龙、枝叶交互和导航后的整帧CPU耗时，不能据此判定自由漫游的15%性能目标通过。运动结果另见运行验收。\n");
            }
            finally {foreach(var pair in debugStates)if(pair.Key!=null)pair.Key.SetActive(pair.Value);RenderTexture.active=previous;Object.DestroyImmediate(pixels);target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(holder);}
        }
        // Batch follow-up after a rejected height transaction. Keeps the original
        // audit and verifies the saved withdrawal separately from the failed resize.
        public static void VerifySavedOverlapResult()
        {
            OpenClean();Load();
            foreach(string name in new[] {"Audit.md","OverlapPairs.csv"})
                if(File.Exists(Output+"/"+name) && !File.Exists(Output+"/Before-"+name))File.Copy(Output+"/"+name,Output+"/Before-"+name);
            AuditCurrent();
            foreach(string name in new[] {"Audit.md","OverlapPairs.csv"})File.Copy(Output+"/"+name,Output+"/After-"+name,true);
            FoliageInteractionPreparation.RestoreCurrent();
            var rows=Collect();var row=rows.FirstOrDefault(t=>t.transform!=null && Vector2.Distance(XZ(t.position),new Vector2(-17.64f,31.08f))<.02f);
            if(row!=null && File.Exists(Output+"/Failure.md"))
            {
                var terrain=Terrain.activeTerrain;var tree=row.transform;var scale=tree.localScale;var position=tree.position;float radius=row.radius;
                var helpers=new GameObject("Blocked height diagnostics") {hideFlags=HideFlags.HideAndDontSave};
                try
                {
                    var routes=RouteObstacleClearance.CreateHeightConflictProbe(terrain);var poses=Reservations(helpers.transform);var baseline=Conflicts(tree,routes,poses);
                    var report=new StringBuilder("height_m,new_conflict_id,reserved_pose_x,reserved_pose_z\n");
                    Range(position,out float low,out _,out float cap);
                    for(int i=0;i<=60;i++)
                    {
                        float h=Mathf.Lerp(low,cap,i/60f);tree.localScale=scale;tree.position=position;Resize(tree,row.height,h,radius,terrain);
                        foreach(int id in Conflicts(tree,routes,poses).Except(baseline).OrderBy(v=>v))
                        {
                            string xy=id>=1000000?F(poses[id-1000000].position.x)+","+F(poses[id-1000000].position.y):",";
                            report.AppendLine(h.ToString("F4",CultureInfo.InvariantCulture)+","+id+","+xy);
                        }
                    }
                    File.WriteAllText(Output+"/BlockedHeightConflicts.csv",report.ToString());
                }
                finally {tree.localScale=scale;tree.position=position;tree.GetComponent<SceneTreeObstacle>().SetRadius(radius);Object.DestroyImmediate(helpers);}
            }
            Rebuild();Save();Capture("after-overlap");
            Debug.Log("TREE_OVERLAP_SAVED_RESULT_VERIFIED: saved Main reloaded, withdrawal audit and blocked-height diagnostics written; original heights retained.");
        }
        [Serializable] private sealed class AuditTree {public string source,name;public Vector3 position;public float height,radius;}
        public static void AuditCurrent()
        {
            OpenClean();
            var terrain=Terrain.activeTerrain;if(terrain==null)throw new InvalidOperationException("No active terrain.");
            Directory.CreateDirectory(Output);var rows=new List<AuditTree>();var conversion=Object.FindObjectOfType<FoliageTerrainConversion>();
            var terrainTrees=terrain.terrainData.treeInstances.ToList();
            if(conversion!=null)terrainTrees.AddRange(conversion.removed.Select(t=>t.instance));
            foreach(var tree in terrainTrees)
            {
                if(tree.widthScale<=0)continue;var prefab=terrain.terrainData.treePrototypes[tree.prototypeIndex].prefab;
                rows.Add(new AuditTree {source="Terrain",name=prefab.name,position=terrain.transform.position+Vector3.Scale(tree.position,terrain.terrainData.size),
                    height=CentralForestDensification.BoundsOf(prefab).size.y*tree.heightScale,radius=.7f*tree.widthScale});
            }
            foreach(var marker in Object.FindObjectsOfType<SceneTreeObstacle>())
            {
                var foliage=marker.GetComponent<InteractiveFoliage>();if(foliage!=null && foliage.convertedTerrainTree)continue;
                rows.Add(new AuditTree {source=Owner(marker.transform),name=marker.name,position=marker.transform.position,height=CentralForestDensification.BoundsOf(marker.gameObject).size.y,radius=marker.Radius});
            }
            var report=new StringBuilder("# 全场景高度与重叠审计\n\n只读审计，地形转换代理不重复计数。树干相交按既有避障半径估计，不能当作木质网格精确相交；树冠交叠不视为重复树。\n\n|归属|树数|中位高度m|最高m|\n|---|---:|---:|---:|\n");
            foreach(var group in rows.GroupBy(r=>r.source))report.AppendLine($"|{group.Key}|{group.Count()}|{Median(group.Select(r=>(double)r.height)):F3}|{group.Max(r=>r.height):F3}|");
            int duplicates=0,near=0;var pairs=new StringBuilder("source_a,name_a,x_a,z_a,source_b,name_b,x_b,z_b,distance,kind\n");
            for(int i=0;i<rows.Count;i++)for(int j=i+1;j<rows.Count;j++)
            {
                var a=rows[i];var b=rows[j];float distance=Vector2.Distance(XZ(a.position),XZ(b.position));
                if(distance>=a.radius+b.radius)continue;bool duplicate=distance<.05f;
                if(duplicate)duplicates++;else near++;
                pairs.AppendLine($"{a.source},{a.name},{F(a.position.x)},{F(a.position.z)},{b.source},{b.name},{F(b.position.x)},{F(b.position.z)},{F(distance)},{(duplicate?"Same root position":"Trunk proxy overlap")}");
            }
            report.AppendLine($"\n合计{rows.Count}棵；根位置小于5cm的树对：{duplicates}；其他树干避障圆相交树对：{near}。逐对见OverlapPairs.csv。\n");
            File.WriteAllText(Output+"/Audit.md",report.ToString());File.WriteAllText(Output+"/OverlapPairs.csv",pairs.ToString());
            File.WriteAllText("Logs/tree-height-audit-status.txt","PASS trees="+rows.Count+" sameRootPairs="+duplicates+" proxyOverlapPairs="+near);
            Debug.Log("TREE_HEIGHT_AUDIT trees="+rows.Count+" sameRootPairs="+duplicates+" proxyOverlapPairs="+near);
        }
    }
}
