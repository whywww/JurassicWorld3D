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
    /// <summary>Reversible withdrawal of near-coincident roots; crowns are deliberately allowed to overlap.</summary>
    public static class TreeOverlapCleanup
    {
        private const string Path="Assets/Editor/TreeOverlapWithdrawals.json";
        [Serializable] private sealed class State {public bool enabled,restoreReplayVerified;public Entry[] withdrawn=Array.Empty<Entry>();}
        [Serializable] private sealed class Entry
        {
            public string key,kind,prefab,name;
            public Vector3 position,scale,localPosition;
            public Quaternion rotation;
            public float width,height,treeRotation,radius;
            public Color32 color,lightmap;
            public bool hardBranches;
        }
        private sealed class Candidate {internal Entry entry;internal Transform scene;internal int index=-1;internal float actualHeight;}
        private static string F(float v)=>v.ToString("F4",CultureInfo.InvariantCulture);
        private static string Key(string kind,string prefab,Vector3 p,float yaw)=>kind+"|"+prefab+"|"+F(p.x)+"|"+F(p.z)+"|"+F(yaw);
        private static State Load()=>File.Exists(Path)?JsonUtility.FromJson<State>(File.ReadAllText(Path)):new State();
        private static void Persist(State state) {File.WriteAllText(Path,JsonUtility.ToJson(state,true)+"\n");AssetDatabase.ImportAsset(Path);}
        private static List<Candidate> Collect()
        {
            var terrain=Terrain.activeTerrain;var result=new List<Candidate>();var trees=terrain.terrainData.treeInstances;
            for(int i=0;i<trees.Length;i++)
            {
                var t=trees[i];if(t.widthScale<=0)continue;var prefab=terrain.terrainData.treePrototypes[t.prototypeIndex].prefab;
                string path=AssetDatabase.GetAssetPath(prefab);Vector3 p=terrain.transform.position+Vector3.Scale(t.position,terrain.terrainData.size);
                result.Add(new Candidate {index=i,actualHeight=CentralForestDensification.BoundsOf(prefab).size.y*t.heightScale,
                    entry=new Entry {key=Key("Terrain",path,p,t.rotation),kind="Terrain",prefab=path,name=prefab.name,position=p,localPosition=t.position,
                        width=t.widthScale,height=t.heightScale,treeRotation=t.rotation,color=t.color,lightmap=t.lightmapColor}});
            }
            foreach(var marker in Object.FindObjectsOfType<SceneTreeObstacle>())
            {
                var t=marker.transform;string path=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(marker.gameObject);
                result.Add(new Candidate {scene=t,actualHeight=CentralForestDensification.BoundsOf(marker.gameObject).size.y,
                    entry=new Entry {key=Key("Scene",path,t.position,t.eulerAngles.y),kind="Scene",prefab=path,name=t.name,position=t.position,rotation=t.rotation,
                        scale=t.localScale,radius=marker.Radius,hardBranches=marker.includeHardBranches}});
            }
            return result;
        }
        internal static void ApplyCurrentAndPersist()
        {
            var state=Load();state.enabled=true;
            var terrain=Terrain.activeTerrain;var instances=terrain.terrainData.treeInstances;
            var active=Object.FindObjectsOfType<SceneTreeObstacle>();
            string original=Signature();
            Apply(state,true);string first=Signature();
            // Exercise restoration in memory before committing the withdrawal. No
            // parent, transform, or other scene edit is restored from an old scene.
            terrain.terrainData.SetTreeInstances(instances,false);
            foreach(var marker in active)marker.gameObject.SetActive(true);
            if(original!=Signature())throw new InvalidOperationException("Overlap withdrawal restore changed tree identity/dimensions.");
            Apply(state,false);
            if(first!=Signature())throw new InvalidOperationException("Overlap withdrawal replay changed the result.");
            var remaining=Collect();
            for(int i=0;i<remaining.Count;i++)for(int j=i+1;j<remaining.Count;j++)
                if(Vector2.Distance(new Vector2(remaining[i].entry.position.x,remaining[i].entry.position.z),new Vector2(remaining[j].entry.position.x,remaining[j].entry.position.z))<.05f)
                    throw new InvalidOperationException("Near-coincident roots remain after withdrawal.");
            state.restoreReplayVerified=true;Persist(state);
            File.AppendAllText(TreeHeightAdjustment.Output+"/OverlapCleanup.md","\n验证通过：撤销恢复身份/尺寸一致，重放结果一致，保留树中无根距不足5cm的树对。\n");
        }
        private static string Signature()=>string.Join("\n",Collect().OrderBy(r=>r.entry.key,StringComparer.Ordinal)
            .Select(r=>JsonUtility.ToJson(r.entry)));
        internal static void SeedConfigured()
        {
            if(!File.Exists(Path))return;var state=Load();if(!state.enabled)return;
            FoliageInteractionPreparation.RestoreCurrent();int changed=Apply(state,true);Persist(state);
            if(changed>0 && !TreeHeightAdjustment.Enabled)TreeHeightAdjustment.RefreshGeometry();
        }
        private static int Apply(State state,bool discover)
        {
            var rows=Collect();var withdrawals=state.withdrawn.ToDictionary(e=>e.key);var remove=new HashSet<string>(withdrawals.Keys);
            if(discover)
            {
                // Prefer authored trees, then the shorter tree. The other tree remains at
                // its original position in a recoverable inactive/terrain ledger record.
                var ordered=rows.OrderByDescending(r=>r.entry.name.Contains("_SceneTree")).ThenBy(r=>r.actualHeight).ThenBy(r=>r.entry.key,StringComparer.Ordinal).ToArray();
                var kept=new List<Candidate>();
                foreach(var row in ordered)
                {
                    if(remove.Contains(row.entry.key))continue;
                    if(kept.Any(k=>Vector2.Distance(new Vector2(k.entry.position.x,k.entry.position.z),new Vector2(row.entry.position.x,row.entry.position.z))<.05f))
                    {withdrawals[row.entry.key]=row.entry;remove.Add(row.entry.key);}
                    else kept.Add(row);
                }
            }
            int count=0;var terrain=Terrain.activeTerrain;var instances=terrain.terrainData.treeInstances;var indices=new HashSet<int>();
            foreach(var row in rows.Where(r=>remove.Contains(r.entry.key)))
            {
                if(row.scene!=null)row.scene.gameObject.SetActive(false);else indices.Add(row.index);count++;
            }
            if(indices.Count>0){terrain.terrainData.SetTreeInstances(instances.Where((t,i)=>!indices.Contains(i)).ToArray(),false);EditorUtility.SetDirty(terrain.terrainData);}
            state.withdrawn=withdrawals.Values.OrderBy(e=>e.key).ToArray();TerrainTreeObstacleRegistry.Invalidate();
            Directory.CreateDirectory(TreeHeightAdjustment.Output);
            var report=new StringBuilder("# 树根重叠处理\n\n按用户选择保留位置，撤下近乎重合的一棵；不清除树冠覆盖。判定为根部平面距离不足5cm。原场景树保持原父对象与变换，只停用；地形树完整实例字段另存，可恢复。\n\n|类型|撤下的树|原X|原Z|恢复来源|\n|---|---|---:|---:|---|\n");
            foreach(var e in state.withdrawn)report.AppendLine($"|{e.kind}|{e.name}|{e.position.x:F4}|{e.position.z:F4}|{e.prefab}|");
            report.AppendLine($"\n累计撤下{state.withdrawn.Length}棵（本次操作实际撤下{count}棵），其余树的位置、树种和数量不变。该数量变化是用户对原计划保持棵数要求的明确修订。记录：Assets/Editor/TreeOverlapWithdrawals.json。\n");
            if(state.restoreReplayVerified)report.AppendLine("\n已通过撤下、恢复身份/尺寸一致及重放一致性验证。\n");
            File.WriteAllText(TreeHeightAdjustment.Output+"/OverlapCleanup.md",report.ToString());return count;
        }
        [MenuItem("Jurassic World/Environment/Tree Heights/Restore Withdrawn Overlap Trees")]
        public static void Restore()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Save and stop Play first.");
            EditorSceneManager.OpenScene(CentralForestDensification.ScenePath);FoliageInteractionPreparation.RestoreCurrent();var state=Load();var terrain=Terrain.activeTerrain;
            var trees=terrain.terrainData.treeInstances.ToList();var prototypes=terrain.terrainData.treePrototypes;
            var markers=Object.FindObjectsOfType<SceneTreeObstacle>(true);
            foreach(var e in state.withdrawn)
            {
                if(e.kind=="Terrain")
                {
                    int prototype=Array.FindIndex(prototypes,p=>AssetDatabase.GetAssetPath(p.prefab)==e.prefab);if(prototype<0)throw new InvalidOperationException("Missing withdrawal prototype: "+e.prefab);
                    if(trees.Any(t=>t.prototypeIndex==prototype && Vector3.Distance(t.position,e.localPosition)<.000001f))continue;
                    trees.Add(new TreeInstance {prototypeIndex=prototype,position=e.localPosition,widthScale=e.width,heightScale=e.height,rotation=e.treeRotation,color=e.color,lightmapColor=e.lightmap});
                }
                else
                {
                    var marker=markers.FirstOrDefault(m=>m.name==e.name && Vector2.Distance(new Vector2(m.transform.position.x,m.transform.position.z),new Vector2(e.position.x,e.position.z))<.001f);
                    if(marker!=null)marker.gameObject.SetActive(true);
                    else
                    {
                        var root=GameObject.Find("Restored Overlap Trees")??new GameObject("Restored Overlap Trees");
                        var tree=CentralForestDensification.Instantiate(e.prefab,root.transform);tree.name=e.name;tree.transform.SetPositionAndRotation(e.position,e.rotation);tree.transform.localScale=e.scale;
                        marker=tree.GetComponent<SceneTreeObstacle>()??tree.AddComponent<SceneTreeObstacle>();marker.SetRadius(e.radius);marker.includeHardBranches=e.hardBranches;
                        foreach(var collider in tree.GetComponentsInChildren<Collider>())Object.DestroyImmediate(collider);
                    }
                }
            }
            terrain.terrainData.SetTreeInstances(trees.ToArray(),false);EditorUtility.SetDirty(terrain.terrainData);state.enabled=false;Persist(state);
            TreeHeightAdjustment.RefreshGeometry();FoliageInteractionPreparation.SeedConfigured();EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());AssetDatabase.SaveAssets();
        }
    }
}
