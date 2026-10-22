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
    /// <summary>Independent, reversible thinning of the grove reported at (2.9,-24).</summary>
    public static class LocalTreeThinning
    {
        internal const string Output = "Tools/VegetationDiversity/LocalTreeThinning";
        private const string Config = "Assets/Editor/LocalTreeThinning.json";
        private static readonly Vector2 Center = new Vector2(2.9f,-24f);
        private const float Range = 6f, RootGap = 2f, WoodGap = .25f;
        private static bool busy;
        private static readonly Dictionary<string,TreeTrunkSection[]> WoodCache = new Dictionary<string,TreeTrunkSection[]>();
        [Serializable] private sealed class State { public bool enabled; public Entry[] withdrawn=Array.Empty<Entry>(); }
        [Serializable] private sealed class Entry
        {
            public string key,kind,name,prefab,parent,owner,reason;
            public Vector3 position,scale;
            public Quaternion rotation;
            public TreeInstance terrain;
            public Vector3 terrainPosition;
            public float terrainWidth,terrainHeight,terrainRotation;
            public Color32 terrainColor,terrainLightmapColor;
            public float radius;
            public bool hardBranches;
        }
        private sealed class Tree
        {
            internal Entry e; internal SceneTreeObstacle scene; internal int index=-1,priority;
            internal GameObject prefab; internal TreeTrunkSection[] wood;
        }
        private static string F(float n)=>n.ToString("R",CultureInfo.InvariantCulture);
        private static Vector2 XZ(Vector3 p)=>new Vector2(p.x,p.z);
        private static string Key(Entry e)=>e.kind+"|"+e.prefab+"|"+e.name+"|"+e.position.x.ToString("F4",CultureInfo.InvariantCulture)+"|"+e.position.z.ToString("F4",CultureInfo.InvariantCulture)+"|"+e.rotation.eulerAngles.y.ToString("F3",CultureInfo.InvariantCulture);
        private static State Load()=>File.Exists(Config)?JsonUtility.FromJson<State>(File.ReadAllText(Config)):new State();
        private static void Persist(State s){File.WriteAllText(Config,JsonUtility.ToJson(s,true)+"\n");AssetDatabase.ImportAsset(Config);}
        private static string Hierarchy(Transform t)=>t==null?"":(t.parent==null?t.name:Hierarchy(t.parent)+"/"+t.name);
        private static List<Tree> Collect()
        {
            var result=new List<Tree>();var terrain=Terrain.activeTerrain;var data=terrain.terrainData;
            foreach(var marker in Object.FindObjectsOfType<SceneTreeObstacle>())
            {
                var t=marker.transform;string path=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(marker.gameObject);
                var e=new Entry {kind="Scene",name=t.name,prefab=path,parent=Hierarchy(t.parent),owner=t.root.name,
                    position=t.position,rotation=t.rotation,scale=t.localScale,radius=marker.Radius,hardBranches=marker.includeHardBranches};e.key=Key(e);
                int priority=t.name.Contains("_SceneTree")?0:(e.owner==CentralForestDensification.RootName||e.owner==LocalForestRepair.RootName||e.owner==RainforestExpansion.RootName||e.owner==RainforestSample.RootName?2:1);
                result.Add(new Tree {e=e,scene=marker,priority=priority,prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path)});
            }
            var instances=data.treeInstances;
            for(int i=0;i<instances.Length;i++)
            {
                var instance=instances[i];if(instance.widthScale<=0)continue;var prefab=data.treePrototypes[instance.prototypeIndex].prefab;
                var e=new Entry {kind="Terrain",name=prefab.name,prefab=AssetDatabase.GetAssetPath(prefab),owner="Terrain",terrain=instance,
                    position=terrain.transform.position+Vector3.Scale(instance.position,data.size),rotation=Quaternion.Euler(0,instance.rotation*Mathf.Rad2Deg,0),scale=new Vector3(instance.widthScale,instance.heightScale,instance.widthScale),radius=.7f*instance.widthScale,
                    terrainPosition=instance.position,terrainWidth=instance.widthScale,terrainHeight=instance.heightScale,terrainRotation=instance.rotation,terrainColor=instance.color,terrainLightmapColor=instance.lightmapColor};e.key=Key(e);
                result.Add(new Tree {e=e,index=i,prefab=prefab,priority=1});
            }
            if(result.GroupBy(t=>t.e.key).Any(g=>g.Count()>1))throw new InvalidOperationException("Ambiguous tree identity; no thinning committed.");
            return result;
        }
        private static string Signature(IEnumerable<Tree> trees)=>string.Join("\n",trees.OrderBy(t=>t.e.key,StringComparer.Ordinal).Select(t=>t.e.key+"|"+t.e.position.ToString("F6")+t.e.rotation.ToString("F6")+t.e.scale.ToString("F6")+"|"+JsonUtility.ToJson(t.e.terrain)));
        private static string Conflict(Tree a,Tree b)
        {
            if(Vector2.Distance(XZ(a.e.position),XZ(b.e.position))<RootGap-.0001f)return "根距不足2m: "+b.e.key;
            if(a.wood==null||b.wood==null)return null;
            foreach(var x in a.wood)foreach(var y in b.wood)
            {
                if(x.top<=y.bottom||y.top<=x.bottom)continue;
                if(Vector2.Distance(x.center+XZ(a.e.position),y.center+XZ(b.e.position))<x.radius+y.radius+WoodGap-.0001f)return "0–3m木质间隔不足0.25m: "+b.e.key;
            }
            return null;
        }
        private static void Measure(List<Tree> trees)
        {
            var terrain=Terrain.activeTerrain;Func<Transform,TreeTrunkSection[]> probe=null;
            var holder=new GameObject("Local thinning wood probes") {hideFlags=HideFlags.HideAndDontSave};
            var prefabs=new Dictionary<GameObject,GameObject>();
            try
            {
                foreach(var row in trees)
                {
                    string cache=row.e.key+row.e.position.ToString("F6")+row.e.scale.ToString("F6")+row.e.rotation.ToString("F6");
                    if(WoodCache.TryGetValue(cache,out var measured)){row.wood=measured;continue;}
                    Bounds bounds=CentralForestDensification.BoundsOf(row.scene!=null?row.scene.gameObject:row.prefab);
                    // Bounds may include leaves: only a broad-phase rejection, never the wood test.
                    float reach=new Vector2(bounds.extents.x,bounds.extents.z).magnitude*(row.scene!=null?1:row.e.scale.x);
                    if(Vector2.Distance(XZ(row.e.position),Center)>Range+RootGap+reach+1)continue;
                    if(probe==null)probe=RouteObstacleClearance.CreateWoodSectionProbe(terrain);
                    Transform t=row.scene!=null?row.scene.transform:null;
                    if(t==null)
                    {
                        if(!prefabs.TryGetValue(row.prefab,out var tree))
                        {
                            tree=(GameObject)PrefabUtility.InstantiatePrefab(row.prefab,holder.transform);
                            if(tree.GetComponent<SceneTreeObstacle>()==null)tree.AddComponent<SceneTreeObstacle>();prefabs.Add(row.prefab,tree);
                        }
                        t=tree.transform;t.SetPositionAndRotation(row.e.position,row.e.rotation);t.localScale=Vector3.Scale(row.prefab.transform.localScale,row.e.scale);t.GetComponent<SceneTreeObstacle>().SetRadius(row.e.radius);
                    }
                    float ground=terrain.SampleHeight(row.e.position)+terrain.transform.position.y;
                    row.wood=probe(t).Where(s=>s.top>ground && s.bottom<ground+3).Select(s=>new TreeTrunkSection {center=s.center,radius=s.radius,bottom=Mathf.Max(s.bottom,ground),top=Mathf.Min(s.top,ground+3)}).ToArray();
                    WoodCache[cache]=row.wood;
                }
            }
            finally {Object.DestroyImmediate(holder);}
        }
        private static void Apply(State state)
        {
            var rows=Collect();Measure(rows);var entries=state.withdrawn.ToDictionary(e=>e.key);
            var kept=rows.Where(t=>Vector2.Distance(XZ(t.e.position),Center)>Range).ToList();
            foreach(var row in rows.Where(t=>Vector2.Distance(XZ(t.e.position),Center)<=Range).OrderBy(t=>t.priority).ThenBy(t=>t.e.key,StringComparer.Ordinal))
            {
                if(entries.ContainsKey(row.e.key))continue;
                string reason=kept.Select(t=>Conflict(row,t)).FirstOrDefault(s=>s!=null);
                if(reason==null)kept.Add(row);else {row.e.reason=reason;entries.Add(row.e.key,row.e);}
            }
            var remove=new HashSet<int>();
            foreach(var row in rows.Where(t=>entries.ContainsKey(t.e.key)))
            {if(row.scene!=null)row.scene.gameObject.SetActive(false);else remove.Add(row.index);}
            if(remove.Count>0)
            {
                var data=Terrain.activeTerrain.terrainData;data.SetTreeInstances(data.treeInstances.Where((t,i)=>!remove.Contains(i)).ToArray(),false);EditorUtility.SetDirty(data);
            }
            state.withdrawn=entries.Values.OrderBy(e=>e.key,StringComparer.Ordinal).ToArray();TerrainTreeObstacleRegistry.Invalidate();
        }
        private static void Restore(State state,bool recreateMissing=true)
        {
            var all=Object.FindObjectsOfType<SceneTreeObstacle>(true);var data=Terrain.activeTerrain.terrainData;var instances=data.treeInstances.ToList();
            foreach(var e in state.withdrawn)
            {
                if(e.kind=="Terrain")
                {
                    int prototype=Array.FindIndex(data.treePrototypes,p=>AssetDatabase.GetAssetPath(p.prefab)==e.prefab);
                    if(prototype<0)throw new InvalidOperationException("Missing terrain prototype "+e.prefab);
                    if(instances.Any(t=>t.prototypeIndex==prototype && t.position==e.terrainPosition))continue;
                    instances.Add(new TreeInstance {prototypeIndex=prototype,position=e.terrainPosition,widthScale=e.terrainWidth,heightScale=e.terrainHeight,rotation=e.terrainRotation,color=e.terrainColor,lightmapColor=e.terrainLightmapColor});
                }
                else
                {
                    var marker=all.FirstOrDefault(m=>m.name==e.name && Vector3.Distance(m.transform.position,e.position)<.001f && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(m.gameObject)==e.prefab);
                    if(marker!=null)marker.gameObject.SetActive(true);
                    else if(recreateMissing)
                    {
                        var parent=GameObject.Find(e.parent);if(parent==null)throw new InvalidOperationException("Missing original tree parent: "+e.parent);
                        var tree=CentralForestDensification.Instantiate(e.prefab,parent.transform);tree.name=e.name;tree.transform.SetPositionAndRotation(e.position,e.rotation);tree.transform.localScale=e.scale;
                        marker=tree.GetComponent<SceneTreeObstacle>()??tree.AddComponent<SceneTreeObstacle>();marker.SetRadius(e.radius);marker.includeHardBranches=e.hardBranches;
                        foreach(var c in tree.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
                    }
                }
            }
            data.SetTreeInstances(instances.ToArray(),false);EditorUtility.SetDirty(data);TerrainTreeObstacleRegistry.Invalidate();
        }
        internal static void RestoreForRegeneration()
        {
            if(busy)return;var state=Load();if(!state.enabled)return;
            FoliageInteractionPreparation.RestoreCurrent();Restore(state,false);
        }
        internal static void SeedConfigured()
        {
            if(busy)return;var state=Load();if(!state.enabled)return;
            WoodCache.Clear();
            FoliageInteractionPreparation.RestoreCurrent();Apply(state);Validate(Collect());Persist(state);
        }
        private static void Validate(List<Tree> rows)
        {
            Measure(rows);var local=rows.Where(t=>Vector2.Distance(XZ(t.e.position),Center)<=Range).ToArray();
            foreach(var a in local)foreach(var b in rows)
                if(a!=b && Conflict(a,b)!=null)throw new InvalidOperationException("Local spacing failed: "+a.e.name+" / "+b.e.name);
        }
        private static void Open()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorSceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Save and stop Play first.");
            if(EditorSceneManager.GetActiveScene().path!=CentralForestDensification.ScenePath)EditorSceneManager.OpenScene(CentralForestDensification.ScenePath);
        }
        private static void Finish()
        {
            TreeHeightAdjustment.RefreshGeometry();FoliageInteractionPreparation.SeedConfigured();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());AssetDatabase.SaveAssets();
        }
        [MenuItem("Jurassic World/Environment/Local Tree Thinning/Build and Verify")]
        public static void BuildAndVerify()
        {
            Open();Directory.CreateDirectory(Output);var state=Load();string oldConfig=File.Exists(Config)?File.ReadAllText(Config):null;
            WoodCache.Clear();
            string checkpoint=Output+"/Main_Before.unity";File.Copy(CentralForestDensification.ScenePath,checkpoint,true);
            var data=Terrain.activeTerrain.terrainData;var backup=Object.Instantiate(data);busy=true;
            try
            {
                FoliageInteractionPreparation.RestoreCurrent();if(state.enabled)Restore(state);
                var original=Collect();var snapshot=new ProtectedSnapshot();
                state.enabled=true;Apply(state);Validate(Collect());
                snapshot.Check();CheckPopulation(original,Collect(),state);Persist(state);
                WriteReport(original,Collect(),state);Finish();
                FoliageInteractionPreparation.RestoreCurrent();snapshot.Check();CheckPopulation(original,Collect(),state);Validate(Collect());
                FoliageInteractionPreparation.SeedConfigured();EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());AssetDatabase.SaveAssets();
                if(File.Exists(Output+"/Failure.md"))File.Delete(Output+"/Failure.md");
                File.WriteAllText(Output+"/Validation.md","PASS: 2m roots / 0.25m low-wood gap; outside trees, retained poses/species/scales, terrain and dinosaur poses unchanged. Counts by source: Layout.md. Acceptance reduced to layout/preservation only by user request; no additional motion, replay or visual acceptance. Navigation, wood profiles and foliage rebuilt.\n");
                Debug.Log("LOCAL_TREE_THINNING_PASS removed="+state.withdrawn.Length);
            }
            catch(Exception error)
            {
                if(oldConfig==null){if(File.Exists(Config))File.Delete(Config);}else File.WriteAllText(Config,oldConfig);
                EditorUtility.CopySerialized(backup,data);EditorUtility.SetDirty(data);
                File.Copy(checkpoint,CentralForestDensification.ScenePath,true);AssetDatabase.ImportAsset(CentralForestDensification.ScenePath);
                EditorSceneManager.OpenScene(CentralForestDensification.ScenePath);AssetDatabase.SaveAssets();
                File.WriteAllText(Output+"/Failure.md",error.ToString());throw;
            }
            finally {busy=false;Object.DestroyImmediate(backup);}
        }
        [MenuItem("Jurassic World/Environment/Local Tree Thinning/Undo This Thinning")]
        public static void Undo()
        {
            Open();var state=Load();busy=true;
            try {FoliageInteractionPreparation.RestoreCurrent();Restore(state);state.enabled=false;Persist(state);Finish();}
            finally {busy=false;}
        }
        private static void CheckPopulation(List<Tree> before,List<Tree> after,State state)
        {
            var removed=new HashSet<string>(state.withdrawn.Select(e=>e.key));
            if(Signature(before.Where(t=>!removed.Contains(t.e.key)))!=Signature(after))throw new InvalidOperationException("Protected/outside tree population changed.");
            if(state.withdrawn.Any(e=>Vector2.Distance(XZ(e.position),Center)>Range+.0001f))throw new InvalidOperationException("Withdrawal outside local radius.");
        }
        private sealed class ProtectedSnapshot
        {
            private readonly TerrainData data=Terrain.activeTerrain.terrainData;
            private readonly float[] heights,paint;private readonly int[][] details;
            private readonly Dictionary<Transform,string> actors=Object.FindObjectsOfType<DinosaurAgent>(true).Select(a=>a.transform).ToDictionary(t=>t,t=>t.position.ToString("F6")+t.rotation.ToString("F6")+t.localScale.ToString("F6")+t.gameObject.activeSelf);
            private readonly string previousWithdrawals=File.ReadAllText("Assets/Editor/TreeOverlapWithdrawals.json"),heightPolicy=File.ReadAllText("Assets/Editor/TreeHeightPolicy.json");
            internal ProtectedSnapshot(){heights=data.GetHeights(0,0,data.heightmapResolution,data.heightmapResolution).Cast<float>().ToArray();paint=data.GetAlphamaps(0,0,data.alphamapWidth,data.alphamapHeight).Cast<float>().ToArray();details=Enumerable.Range(0,data.detailPrototypes.Length).Select(i=>data.GetDetailLayer(0,0,data.detailResolution,data.detailResolution,i).Cast<int>().ToArray()).ToArray();}
            internal void Check()
            {
                if(!heights.SequenceEqual(data.GetHeights(0,0,data.heightmapResolution,data.heightmapResolution).Cast<float>())||!paint.SequenceEqual(data.GetAlphamaps(0,0,data.alphamapWidth,data.alphamapHeight).Cast<float>()))throw new InvalidOperationException("Terrain height/paint changed.");
                for(int i=0;i<details.Length;i++)if(!details[i].SequenceEqual(data.GetDetailLayer(0,0,data.detailResolution,data.detailResolution,i).Cast<int>()))throw new InvalidOperationException("Terrain understory changed.");
                foreach(var p in actors)if(p.Key==null||p.Value!=p.Key.position.ToString("F6")+p.Key.rotation.ToString("F6")+p.Key.localScale.ToString("F6")+p.Key.gameObject.activeSelf)throw new InvalidOperationException("Dinosaur pose/status changed.");
                if(previousWithdrawals!=File.ReadAllText("Assets/Editor/TreeOverlapWithdrawals.json")||heightPolicy!=File.ReadAllText("Assets/Editor/TreeHeightPolicy.json"))throw new InvalidOperationException("Previous withdrawal/height policy changed.");
            }
        }
        private static void WriteReport(List<Tree> before,List<Tree> after,State state)
        {
            File.WriteAllText(Output+"/Trees.csv","status,owner,name,prefab,x,z,reason\n"+string.Join("\n",before.Where(t=>Vector2.Distance(XZ(t.e.position),Center)<=Range).OrderBy(t=>t.e.key).Select(t=>{var e=state.withdrawn.FirstOrDefault(w=>w.key==t.e.key);return (e==null?"kept":"withdrawn")+","+t.e.owner+","+t.e.name+","+t.e.prefab+","+F(t.e.position.x)+","+F(t.e.position.z)+","+(e==null?"":e.reason);}))); 
            var report=new StringBuilder("# 局部疏树\n\n中心(2.9,-24)，半径6m；根距≥2m、0–3m木质间隔≥0.25m。树冠和林下植物保留，保留树不移动、不缩放。\n\n|来源|原数量|保留|撤下|\n|---|---:|---:|---:|\n");
            foreach(var g in before.Where(t=>Vector2.Distance(XZ(t.e.position),Center)<=Range).GroupBy(t=>t.e.owner)){int n=after.Count(t=>t.e.owner==g.Key&&Vector2.Distance(XZ(t.e.position),Center)<=Range);report.AppendLine($"|{g.Key}|{g.Count()}|{n}|{g.Count()-n}|");}
            report.AppendLine($"\n全场乔木{before.Count}→{after.Count}；累计局部撤下{state.withdrawn.Length}。以前7棵记录与禁用的树高配置未改变。\n");File.WriteAllText(Output+"/Layout.md",report.ToString());
        }
    }
}
