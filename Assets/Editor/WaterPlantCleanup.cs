using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Crest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object=UnityEngine.Object;

namespace JurassicWorld.Editor
{
    /// <summary>Rooted-water exclusion; dry banks and overhanging crowns are preserved.</summary>
    [InitializeOnLoad]
    public static class WaterPlantCleanup
    {
        internal const string Output="Tools/VegetationDiversity/WaterCleanup";
        private const string Config="Assets/Editor/WaterPlantCleanup.json";
        private static bool busy;
        private static State cached;
        private static Query query;
        static WaterPlantCleanup(){EditorApplication.update+=Poll;}
        private static void Poll()
        {
            const string request="Logs/water-cleanup-request.txt",status="Logs/water-cleanup-status.txt";
            if(Application.isBatchMode)return;
            if(!File.Exists(request))return;
            if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            if(EditorApplication.isPlayingOrWillChangePlaymode){File.WriteAllText(status,"WAITING_FOR_PLAY_TO_STOP");return;}
            if(EditorSceneManager.GetActiveScene().isDirty){File.WriteAllText(status,"WAITING_FOR_SCENE_SAVE");return;}
            File.Delete(request);File.WriteAllText(status,"RUNNING");
            try {CleanAndVerify();File.WriteAllText(status,"PASS WATER_PLANTS_CLEAN");}
            catch(Exception e){File.WriteAllText(status,"FAIL "+e);Debug.LogException(e);}
        }
        [Serializable] private sealed class State
        {
            public bool enabled;
            public SceneEntry[] scenes=Array.Empty<SceneEntry>();
            public TreeEntry[] trees=Array.Empty<TreeEntry>();
            public DetailEntry[] details=Array.Empty<DetailEntry>();
        }
        [Serializable] private sealed class SceneEntry
        {
            public string id,key,guid,name,parent,owner;
            public Vector3 position,scale;
            public Quaternion rotation;
            public bool tree,ground,hardBranches,noColliders;
            public float radius;
        }
        [Serializable] private sealed class TreeEntry
        {
            public string guid;public int index;
            public Vector3 position;
            public float width,height,rotation;
            public Color32 color,lightmap;
        }
        [Serializable] private sealed class DetailEntry {public int layer,x,z,value,resolution;}
        private sealed class Water {internal Matrix4x4 inverse;internal Bounds bounds;internal float level;}
        private sealed class Query
        {
            internal readonly int scene=EditorSceneManager.GetActiveScene().handle;
            private readonly Water[] waters;
            private readonly Terrain[] terrains=Terrain.activeTerrains;
            internal Query()
            {
                waters=Object.FindObjectsOfType<WaterBody>().Where(w=>w.isActiveAndEnabled).Select(w=>
                {
                    var ocean=w.GetComponentInParent<OceanRenderer>()??Object.FindObjectOfType<OceanRenderer>();
                    var bounds=new Bounds(w.transform.TransformPoint(new Vector3(-.5f,0,-.5f)),Vector3.zero);
                    for(int x=0;x<2;x++)for(int z=0;z<2;z++)bounds.Encapsulate(w.transform.TransformPoint(new Vector3(x-.5f,0,z-.5f)));
                    // Crest's runtime Root is not initialized in a headless edit-mode
                    // command. Its authored renderer transform stores the mean level.
                    return new Water {inverse=w.transform.worldToLocalMatrix,bounds=bounds,level=ocean==null?w.transform.position.y:ocean.transform.position.y};
                }).ToArray();
            }
            internal bool Wet(Vector3 p)
            {
                foreach(var w in waters)
                {
                    if(p.x<w.bounds.min.x || p.x>w.bounds.max.x || p.z<w.bounds.min.z || p.z>w.bounds.max.z)continue;
                    var local=w.inverse.MultiplyPoint3x4(new Vector3(p.x,w.level,p.z));
                    if(Mathf.Abs(local.x)>.5f || Mathf.Abs(local.z)>.5f)continue;
                    foreach(var terrain in terrains)
                    {
                        var a=terrain.transform.position;var size=terrain.terrainData.size;
                        if(p.x<a.x||p.x>a.x+size.x||p.z<a.z||p.z>a.z+size.z)continue;
                        if(terrain.SampleHeight(p)+a.y<w.level-.01f)return true;
                    }
                }
                return false;
            }
        }
        internal static bool Enabled {get {if(cached==null)cached=Load();return cached.enabled;}}
        internal static bool IsWet(Vector3 p) {if(query==null||query.scene!=EditorSceneManager.GetActiveScene().handle)query=new Query();return query.Wet(p);}
        private static State Load()=>File.Exists(Config)?JsonUtility.FromJson<State>(File.ReadAllText(Config)):new State();
        private static void Persist(State s){cached=s;File.WriteAllText(Config,JsonUtility.ToJson(s,true)+"\n");AssetDatabase.ImportAsset(Config);}
        private static State Merge(State prior,State delta)
        {
            prior.scenes=prior.scenes.Concat(delta.scenes).GroupBy(e=>e.key).Select(g=>g.First()).ToArray();
            prior.trees=prior.trees.Concat(delta.trees).GroupBy(e=>e.guid+e.position.ToString("F6")+e.rotation.ToString("R",CultureInfo.InvariantCulture)).Select(g=>g.First()).ToArray();
            prior.details=prior.details.Concat(delta.details).GroupBy(e=>e.layer+"|"+e.x+"|"+e.z).Select(g=>g.First()).ToArray();return prior;
        }
        private static string F(float n)=>n.ToString("F4",CultureInfo.InvariantCulture);
        private static string PathOf(Transform t)=>t.parent==null?t.name:PathOf(t.parent)+"/"+t.name;
        private static string Guid(GameObject g)=>AssetDatabase.AssetPathToGUID(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g));
        private static string Key(GameObject g)=>Guid(g)+"|"+g.name+"|"+F(g.transform.position.x)+"|"+F(g.transform.position.z);
        private static bool Plant(GameObject root)
        {
            if(root.GetComponent<SceneTreeObstacle>()!=null || root.GetComponent<CenterGapGroundPlant>()!=null)return true;
            string path=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root).Replace('\\','/');
            if(path.Contains("/Environment/Vegetation/") && !path.Contains("/Interaction/"))return true;
            string name=System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            return path.Contains("/NatureStarterKit2/") && new[]{"tree","bush","grass","fern","flower","plant"}.Any(name.StartsWith);
        }
        private static GameObject[] Plants()
        {
            var result=new HashSet<GameObject>();
            foreach(var marker in Object.FindObjectsOfType<SceneTreeObstacle>())result.Add(marker.gameObject);
            foreach(var r in Object.FindObjectsOfType<Renderer>())
            {
                if(r.GetComponentInParent<DinosaurAgent>()!=null)continue;
                var root=PrefabUtility.GetNearestPrefabInstanceRoot(r.gameObject);
                if(root!=null && Plant(root))result.Add(root);
            }
            return result.Where(g=>g.activeInHierarchy).ToArray();
        }
        private static int Apply(State state)
        {
            int changed=0;var scenes=state.scenes.ToDictionary(e=>e.key);var trees=state.trees.ToList();var details=state.details.ToDictionary(e=>e.layer+"|"+e.x+"|"+e.z);
            foreach(var g in Plants().Where(g=>IsWet(g.transform.position)))
            {
                string key=Key(g);
                if(!scenes.ContainsKey(key))scenes.Add(key,new SceneEntry {id=GlobalObjectId.GetGlobalObjectIdSlow(g).ToString(),key=key,guid=Guid(g),name=g.name,
                    parent=g.transform.parent==null?"":PathOf(g.transform.parent),owner=PathOf(g.transform).StartsWith(CenterForestGapFill.RootName+"/")?"CenterGapFill":PathOf(g.transform).StartsWith(RainforestExpansion.RootName+"/")?"Expansion":PathOf(g.transform).StartsWith(RainforestSample.RootName+"/")?"Sample":"ExistingScene",
                    position=g.transform.position,rotation=g.transform.rotation,scale=g.transform.localScale,tree=g.GetComponent<SceneTreeObstacle>()!=null,
                    ground=g.GetComponent<CenterGapGroundPlant>()!=null,hardBranches=g.GetComponent<SceneTreeObstacle>()?.includeHardBranches??false,
                    radius=g.GetComponent<SceneTreeObstacle>()?.Radius??0,noColliders=g.GetComponentsInChildren<Collider>(true).Length==0});
                g.SetActive(false);changed++;
            }
            var terrain=Terrain.activeTerrain;var data=terrain.terrainData;var instances=data.treeInstances;var retained=new List<TreeInstance>();
            for(int i=0;i<instances.Length;i++)
            {
                var t=instances[i];var p=terrain.transform.position+Vector3.Scale(t.position,data.size);
                if(t.widthScale<=0 || !IsWet(p)){retained.Add(t);continue;}
                string guid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(data.treePrototypes[t.prototypeIndex].prefab));
                if(!trees.Any(e=>e.guid==guid&&e.position==t.position&&e.rotation==t.rotation))trees.Add(new TreeEntry {guid=guid,index=i,position=t.position,width=t.widthScale,height=t.heightScale,rotation=t.rotation,color=t.color,lightmap=t.lightmapColor});
                changed++;
            }
            if(retained.Count!=instances.Length){data.SetTreeInstances(retained.ToArray(),false);EditorUtility.SetDirty(data);}
            int n=data.detailResolution;
            // Compute wet cells once; only nonzero details are recorded or changed.
            var wet=new List<Vector2Int>();
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)
            {var p=terrain.transform.position+new Vector3((x+.5f)*data.size.x/n,0,(z+.5f)*data.size.z/n);if(IsWet(p))wet.Add(new Vector2Int(x,z));}
            for(int layer=0;layer<data.detailPrototypes.Length;layer++)
            {
                var map=data.GetDetailLayer(0,0,n,n,layer);bool dirty=false;
                foreach(var cell in wet)
                {
                    if(map[cell.y,cell.x]==0)continue;
                    string key=layer+"|"+cell.x+"|"+cell.y;
                    if(!details.ContainsKey(key))details.Add(key,new DetailEntry {layer=layer,x=cell.x,z=cell.y,value=map[cell.y,cell.x],resolution=n});
                    map[cell.y,cell.x]=0;dirty=true;changed++;
                }
                if(dirty){data.SetDetailLayer(0,0,layer,map);EditorUtility.SetDirty(data);}
            }
            state.scenes=scenes.Values.OrderBy(e=>e.key).ToArray();state.trees=trees.OrderBy(t=>t.index).ToArray();state.details=details.Values.OrderBy(e=>e.layer).ThenBy(e=>e.z).ThenBy(e=>e.x).ToArray();
            TerrainTreeObstacleRegistry.Invalidate();return changed;
        }
        private static int Restore(State s,bool preserveEdits)
        {
            int skipped=0;var all=Object.FindObjectsOfType<Transform>(true).Where(t=>t.gameObject.scene==EditorSceneManager.GetActiveScene()).ToArray();
            foreach(var e in s.scenes)
            {
                GameObject obj=null;
                if(!Application.isPlaying && GlobalObjectId.TryParse(e.id,out var id))obj=GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
                if(obj==null)obj=all.Select(t=>t.gameObject).FirstOrDefault(g=>Key(g)==e.key);
                if(obj==null && Application.isPlaying)obj=all.Select(t=>t.gameObject).FirstOrDefault(g=>g.name==e.name&&Vector3.Distance(g.transform.position,e.position)<.001f);
                if(obj==null)
                {
                    string path=AssetDatabase.GUIDToAssetPath(e.guid);if(string.IsNullOrEmpty(path)){skipped++;continue;}
                    var parent=all.FirstOrDefault(t=>PathOf(t)==e.parent);
                    obj=CentralForestDensification.Instantiate(path,parent);obj.name=e.name;obj.transform.SetPositionAndRotation(e.position,e.rotation);obj.transform.localScale=e.scale;
                    if(e.tree){var marker=obj.GetComponent<SceneTreeObstacle>()??obj.AddComponent<SceneTreeObstacle>();marker.SetRadius(e.radius);marker.includeHardBranches=e.hardBranches;}
                    if(e.ground && obj.GetComponent<CenterGapGroundPlant>()==null)obj.AddComponent<CenterGapGroundPlant>();
                    if(e.noColliders)foreach(var collider in obj.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
                }
                if(preserveEdits && (Vector3.Distance(obj.transform.position,e.position)>.0001f||Vector3.Distance(obj.transform.localScale,e.scale)>.0001f||Quaternion.Angle(obj.transform.rotation,e.rotation)>.01f)){skipped++;continue;}
                obj.SetActive(true);
            }
            var terrain=Terrain.activeTerrain;var data=terrain.terrainData;var instances=data.treeInstances.ToList();
            foreach(var e in s.trees.OrderBy(e=>e.index))
            {
                int index=Array.FindIndex(data.treePrototypes,p=>AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p.prefab))==e.guid);
                if(index<0){skipped++;continue;}
                if(instances.Any(t=>t.prototypeIndex==index&&t.position==e.position&&t.rotation==e.rotation))continue;
                instances.Insert(Mathf.Min(e.index,instances.Count),new TreeInstance {prototypeIndex=index,position=e.position,widthScale=e.width,heightScale=e.height,rotation=e.rotation,color=e.color,lightmapColor=e.lightmap});
            }
            data.SetTreeInstances(instances.ToArray(),false);
            foreach(var group in s.details.GroupBy(e=>e.layer))
            {
                if(group.Key>=data.detailPrototypes.Length||group.Any(e=>e.resolution!=data.detailResolution)){skipped+=group.Count();continue;}
                var map=data.GetDetailLayer(0,0,data.detailResolution,data.detailResolution,group.Key);
                foreach(var e in group)if(!preserveEdits||map[e.z,e.x]==0)map[e.z,e.x]=e.value;else skipped++;
                data.SetDetailLayer(0,0,group.Key,map);
            }
            EditorUtility.SetDirty(data);TerrainTreeObstacleRegistry.Invalidate();return skipped;
        }
        internal static void SeedConfigured()
        {
            if(busy)return;cached=Load();if(!cached.enabled)return;
            busy=true;
            try
            {
                query=new Query();FoliageInteractionPreparation.RestoreCurrent();
                CenterForestGapFill.MarkWaterExclusions();
                int changed=Apply(cached);Persist(cached);if(changed>0)RainforestSample.RebuildNavigation();
            }
            finally {busy=false;}
        }
        private static void Open()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorSceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Save and stop Play first.");
            EditorSceneManager.OpenScene(CentralForestDensification.ScenePath);Directory.CreateDirectory(Output);query=new Query();cached=Load();
        }
        private static void Save()
        {
            RainforestSample.RebuildNavigation();FoliageInteractionPreparation.SeedConfigured();EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());AssetDatabase.SaveAssets();
        }
        private sealed class Link {internal Vector3 a,b;}
        private static List<Link> Links()
        {
            var result=new List<Link>();var settings=Object.FindObjectOfType<DinosaurInteractionSettings>();var handle=NavMesh.AddNavMeshData(settings.navigationData);
            var filter=new NavMeshQueryFilter {agentTypeID=settings.navigationAgentTypeId,areaMask=1};
            Action<Vector3,Vector3> add=(a,b)=>
            {
                var path=new NavMeshPath();
                if(NavMesh.SamplePosition(a,out var aa,3f,filter)&&NavMesh.SamplePosition(b,out var bb,3f,filter)&&NavMesh.CalculatePath(aa.position,bb.position,filter,path)&&path.status==NavMeshPathStatus.PathComplete)
                    result.Add(new Link {a=aa.position,b=bb.position});
            };
            try
            {
                foreach(int side in new[]{-1,1})
                {
                    Vector3? prior=null;
                    foreach(var body in Object.FindObjectsOfType<WaterBody>().OrderBy(w=>w.transform.position.z))
                    {
                        Vector3 p=body.transform.TransformPoint(new Vector3(side*.65f,0,0));p.y=Terrain.activeTerrain.SampleHeight(p)+Terrain.activeTerrain.transform.position.y;
                        if(prior.HasValue)add(prior.Value,p);prior=p;
                    }
                }
                for(int r=0;r<MovementCorridor.RouteCount;r++)
                {
                    MovementCorridor.GetSampleAt(r,0,out Vector2 a,out _);MovementCorridor.GetSampleAt(r,MovementCorridor.Length(r)*.5f,out Vector2 b,out _);
                    add(new Vector3(a.x,0,a.y),new Vector3(b.x,0,b.y));
                }
            }
            finally {handle.Remove();}
            return result;
        }
        private static void VerifyLinks(List<Link> links)
        {
            var settings=Object.FindObjectOfType<DinosaurInteractionSettings>();var handle=NavMesh.AddNavMeshData(settings.navigationData);
            try
            {
                var filter=new NavMeshQueryFilter {agentTypeID=settings.navigationAgentTypeId,areaMask=1};
                foreach(var link in links){var path=new NavMeshPath();if(!NavMesh.CalculatePath(link.a,link.b,filter,path)||path.status!=NavMeshPathStatus.PathComplete)throw new InvalidOperationException("Existing bank/route connection lost.");}
            }
            finally {handle.Remove();}
        }
        private static string Signature()
        {
            var data=Terrain.activeTerrain.terrainData;
            var text=new StringBuilder(string.Join("\n",Plants().OrderBy(Key).Select(g=>Key(g)+g.transform.position.ToString("F6")+g.transform.rotation.ToString("F6")+g.transform.localScale.ToString("F6"))));
            foreach(var t in data.treeInstances)text.AppendLine(JsonUtility.ToJson(new TreeEntry {index=t.prototypeIndex,position=t.position,width=t.widthScale,height=t.heightScale,rotation=t.rotation,color=t.color,lightmap=t.lightmapColor}));
            for(int i=0;i<data.detailPrototypes.Length;i++)foreach(int v in data.GetDetailLayer(0,0,data.detailResolution,data.detailResolution,i))text.Append(v).Append(',');
            using(var sha=System.Security.Cryptography.SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
        }
        [MenuItem("Jurassic World/Environment/Water Plants/Clean and Verify")]
        public static void CleanAndVerify()
        {
            Open();var terrain=Terrain.activeTerrain;string folder=Output+"/Checkpoint";Directory.CreateDirectory(folder);
            foreach(string path in new[]{CentralForestDensification.ScenePath,AssetDatabase.GetAssetPath(terrain.terrainData),"Assets/Editor/CenterForestGapFill.json",Config})
                if(File.Exists(path)&&!File.Exists(folder+"/"+Path.GetFileName(path)))File.Copy(path,folder+"/"+Path.GetFileName(path));
            string oldConfig=File.Exists(Config)?File.ReadAllText(Config):null;
            const string gapConfig="Assets/Editor/CenterForestGapFill.json";
            string oldGap=File.Exists(gapConfig)?File.ReadAllText(gapConfig):null;
            TreeInstance[] originalTrees=null;int[][,] originalDetails=null;GameObject[] originalPlants=null;
            busy=true;
            try
            {
                FoliageInteractionPreparation.RestoreCurrent();Capture("before");
                var links=Links();
                var actors=Object.FindObjectsOfType<DinosaurAgent>(true).ToDictionary(a=>a,a=>a.transform.position.ToString("F6")+a.transform.rotation.ToString("F6")+a.transform.localScale.ToString("F6")+a.gameObject.activeSelf);
                var water=Object.FindObjectsOfType<WaterBody>().ToDictionary(w=>w,w=>w.transform.localToWorldMatrix.ToString("F6"));
                originalPlants=Plants();originalTrees=terrain.terrainData.treeInstances;
                originalDetails=Enumerable.Range(0,terrain.terrainData.detailPrototypes.Length).Select(i=>terrain.terrainData.GetDetailLayer(0,0,terrain.terrainData.detailResolution,terrain.terrainData.detailResolution,i)).ToArray();
                var dry=Plants().Where(g=>!IsWet(g.transform.position)).ToDictionary(g=>g,g=>g.transform.position.ToString("F6")+g.transform.rotation.ToString("F6")+g.transform.localScale.ToString("F6"));
                var transaction=new State();
                string initial=Signature();int changed=Apply(transaction);string first=Signature();
                if(Apply(transaction)!=0||Signature()!=first)throw new InvalidOperationException("Repeated cleanup changed population.");
                if(Restore(transaction,false)!=0||Signature()!=initial)throw new InvalidOperationException("Cleanup restore differs.");
                Apply(transaction);if(Signature()!=first)throw new InvalidOperationException("Cleanup replay differs.");
                if(Plants().Any(g=>IsWet(g.transform.position)))throw new InvalidOperationException("Rooted water plants remain.");
                foreach(var p in dry)if(!p.Key.activeInHierarchy||p.Value!=p.Key.transform.position.ToString("F6")+p.Key.transform.rotation.ToString("F6")+p.Key.transform.localScale.ToString("F6"))throw new InvalidOperationException("Dry bank plant changed.");
                cached=Merge(cached,transaction);cached.enabled=true;Persist(cached);CenterForestGapFill.MarkWaterExclusions();
                Save();VerifyLinks(links);Capture("after");
                foreach(var p in actors)if(p.Value!=p.Key.transform.position.ToString("F6")+p.Key.transform.rotation.ToString("F6")+p.Key.transform.localScale.ToString("F6")+p.Key.gameObject.activeSelf)throw new InvalidOperationException("Dinosaur pose/activation changed.");
                foreach(var p in water)if(p.Value!=p.Key.transform.localToWorldMatrix.ToString("F6"))throw new InvalidOperationException("Water body moved.");
                File.WriteAllText(Output+"/Report.md",$"# 水域植被清理\n\n水体局部边界与平均水位联合地形高度判定；地面低于水位超过1cm才算水中，无岸边扩张缓冲。\n\n本次改变 {changed} 项；累计场景植物 {cached.scenes.Length}、地形树 {cached.trees.Length}、非零地形细节格 {cached.details.Length}。细节格总面积（按层计） {cached.details.Sum(e=>Mathf.Pow(terrain.terrainData.size.x/e.resolution,2)):F3}m²。干岸植物 {dry.Count} 个位置/尺寸/朝向/启用状态保持。\n\n重复清理、撤销恢复及重放签名一致。清理后水中扎根植物为零（统一判定复查）。已重新准备硬障碍、导航与枝叶交互。旧动态性能仍未验证。\n");
                File.AppendAllText(Output+"/Report.md",$"\n复核 {links.Count} 对原可达岸边/路线连接通过；{actors.Count} 只恐龙根位置、尺寸、朝向及启用状态不变，{water.Count} 个水体边界变换不变。\n\n|场景植物来源|累计撤下|\n|---|---:|\n"+string.Join("\n",cached.scenes.GroupBy(e=>e.owner).Select(g=>$"|{g.Key}|{g.Count()}|")));
                Debug.Log("WATER_PLANTS_CLEAN_PASS");
            }
            catch(Exception error)
            {
                if(originalPlants!=null)
                {
                    foreach(var g in originalPlants)if(g!=null)g.SetActive(true);
                    terrain.terrainData.SetTreeInstances(originalTrees,false);
                    for(int i=0;i<originalDetails.Length;i++)terrain.terrainData.SetDetailLayer(0,0,i,originalDetails[i]);
                    if(oldConfig==null){if(File.Exists(Config))File.Delete(Config);}else File.WriteAllText(Config,oldConfig);
                    if(oldGap!=null)File.WriteAllText(gapConfig,oldGap);
                    cached=oldConfig==null?new State():JsonUtility.FromJson<State>(oldConfig);
                    Save();
                }
                File.WriteAllText(Output+"/Failure.md","Cleanup stopped and original active plants/detail data restored.\n\n"+error);throw;
            }
            finally {busy=false;}
        }
        [MenuItem("Jurassic World/Environment/Water Plants/Undo Water Cleanup")]
        public static void Undo()
        {
            Open();busy=true;
            try {FoliageInteractionPreparation.RestoreCurrent();int skipped=Restore(cached,true);cached.enabled=false;Persist(cached);Save();Debug.Log("WATER_PLANTS_RESTORED skipped edited entries="+skipped);}
            finally {busy=false;}
        }
        internal static TerrainData PreviewBefore()
        {
            var terrain=Terrain.activeTerrain;var original=terrain.terrainData;
            terrain.terrainData=Object.Instantiate(original);Restore(Load(),false);return original;
        }
        internal static void PreviewAfter(TerrainData original)
        {
            var state=Load();var all=Object.FindObjectsOfType<Transform>(true);
            foreach(var e in state.scenes)
            {
                var root=all.FirstOrDefault(t=>t.name==e.name&&Vector3.Distance(t.position,e.position)<.001f);
                if(root!=null)root.gameObject.SetActive(false);
            }
            var terrain=Terrain.activeTerrain;var clone=terrain.terrainData;terrain.terrainData=original;
            if(clone!=original)Object.DestroyImmediate(clone);
        }
        private static void Capture(string prefix)
        {
            var source=Camera.main;if(source==null)return;var holder=new GameObject("Water cleanup camera");var camera=holder.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;
            var target=new RenderTexture(1280,720,24);var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            var eyes=new[]{new Vector3(53,2,-15),new Vector3(58,2,37),new Vector3(56,3,76)};var aims=new[]{new Vector3(60,-.1f,-8),new Vector3(66,-.1f,45),new Vector3(61,-.1f,83)};
            try {target.Create();camera.targetTexture=target;for(int i=0;i<3;i++){camera.transform.SetPositionAndRotation(eyes[i],Quaternion.LookRotation(aims[i]-eyes[i]));camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();File.WriteAllBytes(Output+"/"+prefix+"-"+i+".png",pixels.EncodeToPNG());}}
            finally {RenderTexture.active=previous;Object.DestroyImmediate(pixels);target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(holder);}
        }
    }
}
