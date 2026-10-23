using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Profiling;
using Object=UnityEngine.Object;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class CenterGapFillPerformance
    {
        private const string Key="JurassicWorld.CenterGapPerf";
        internal const string Baseline="Assets/Scenes/CenterGapFillBaseline.unity";
        private const string BaselineNav="Assets/Navigation/CenterGapFillBaseline.asset";
        private static bool Understory => SessionState.GetBool(Key+".Understory",false);
        private static bool ForestFloor => SessionState.GetBool(Key+".ForestFloor",false);
        private static string ResultOutput => ForestFloor ? "Tools/VegetationDiversity/ForestFloor" : Understory ? UnderstoryDetailSample.Output : CenterForestGapFill.Output;
        private static string SceneBaseline => ForestFloor ? "Assets/Environment/Terrain/Prepared/ForestFloor/Comparison.unity" : Understory ? "Assets/Scenes/UnderstoryDetailBaseline.unity" : Baseline;
        private static string NavigationBaseline => Understory ? "Assets/Navigation/UnderstoryDetailBaseline.asset" : BaselineNav;
        private static readonly List<double>[] Cpu=Enumerable.Range(0,8).Select(_=>new List<double>()).ToArray();
        private static readonly List<double>[] Gpu=Enumerable.Range(0,8).Select(_=>new List<double>()).ToArray();
        private static readonly List<double>[] Draws=Enumerable.Range(0,8).Select(_=>new List<double>()).ToArray();
        private static readonly List<double>[] Frames=Enumerable.Range(0,8).Select(_=>new List<double>()).ToArray();
        private static readonly FrameTiming[] Timing=new FrameTiming[1];
        private static readonly double[] FloorTravel=new double[8], FloorStep=new double[8];
        private static DinosaurAgent[] floorAgents;
        private static Vector3[] floorPrevious;
        private static ProfilerRecorder cpu,draws;
        private static RenderTexture target;
        private static Camera camera;
        private static int view,frame,lastFrame;
        private static bool loading;
        private static double deadline;
        static CenterGapFillPerformance()
        {
            EditorApplication.playModeStateChanged+=s=> {if(s==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key,false))EditorApplication.delayCall+=Begin;};
        }
        [MenuItem("Jurassic World/Acceptance/Compare Center Gap Fill Dynamic Performance")]
        public static void Run()
        { Launch(false); }
        [MenuItem("Jurassic World/Acceptance/Compare Understory Detail Dynamic Performance")]
        public static void RunUnderstory() { Launch(true); }
        public static void RunForestFloor() { Launch(false,true); }
        private static void Launch(bool understory, bool forestFloor=false)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Stop Play and save first.");
            SessionState.SetBool(Key+".Understory",understory);
            SessionState.SetBool(Key+".ForestFloor",forestFloor);
            PrepareBaseline();
            SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
        }
        internal static void PrepareBaseline()
        {
            if(ForestFloor)
            {
                File.Copy(ResultOutput+"/Checkpoint/Main.unity",SceneBaseline,true);
                const string ground="Assets/Environment/Terrain/Prepared/ForestFloor/ComparisonTerrain.asset";
                File.Copy(ResultOutput+"/Checkpoint/MainInteractionTerrain.asset",ground,true);
                AssetDatabase.ImportAsset(ground);AssetDatabase.ImportAsset(SceneBaseline);
                EditorSceneManager.OpenScene(SceneBaseline);
                var terrain=Terrain.activeTerrain;terrain.terrainData=AssetDatabase.LoadAssetAtPath<TerrainData>(ground);
                terrain.GetComponent<TerrainCollider>().terrainData=terrain.terrainData;
                const string grassMaterial="Assets/Environment/Terrain/Prepared/ForestFloor/ComparisonRouteGrass.mat";
                File.Copy(ResultOutput+"/Checkpoint/RouteGrassTuft.mat",grassMaterial,true);AssetDatabase.ImportAsset(grassMaterial);
                foreach(var renderer in Object.FindObjectsOfType<MeshRenderer>(true))
                {
                    var materials=renderer.sharedMaterials;bool changed=false;
                    for(int i=0;i<materials.Length;i++)if(AssetDatabase.GetAssetPath(materials[i]).EndsWith("/RouteGrassTuft.mat",StringComparison.Ordinal))
                    {materials[i]=AssetDatabase.LoadAssetAtPath<Material>(grassMaterial);changed=true;}
                    if(changed)renderer.sharedMaterials=materials;
                }
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                EditorSceneManager.OpenScene(CentralForestDensification.ScenePath);
                return;
            }
            File.Copy(ResultOutput+"/Checkpoint/Main.unity",SceneBaseline,true);
            File.Copy(ResultOutput+(Understory?"/Checkpoint/TRexInteractionNavMesh.asset":"/Checkpoint/Navigation_Before.asset"),NavigationBaseline,true);
            AssetDatabase.ImportAsset(SceneBaseline);AssetDatabase.ImportAsset(NavigationBaseline);
            EditorSceneManager.OpenScene(SceneBaseline);
            var settings=Object.FindObjectOfType<DinosaurInteractionSettings>();settings.navigationData=AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(NavigationBaseline);
            settings.spawnSeed=731716;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            EditorSceneManager.OpenScene(CentralForestDensification.ScenePath);
        }
        private static void Begin()
        {
            view=-1;foreach(var l in Cpu.Concat(Gpu).Concat(Draws).Concat(Frames))l.Clear();
            deadline=EditorApplication.timeSinceStartup+1200;
            EditorApplication.update+=Tick;Next();
        }
        private static void Next()
        {
            loading=true;Release();view++;frame=0;lastFrame=-1;
            SceneManager.sceneLoaded+=Loaded;
            EditorSceneManager.LoadSceneInPlayMode(view%2==0?SceneBaseline:CentralForestDensification.ScenePath,new LoadSceneParameters(LoadSceneMode.Single));
        }
        private static void Loaded(Scene s,LoadSceneMode m)
        {
            SceneManager.sceneLoaded-=Loaded;EditorApplication.delayCall+=Initialize;
        }
        private static void Initialize()
        {
            try
            {
                foreach(var control in Object.FindObjectsOfType<MonoBehaviour>())if(control is SmoothExplorerCamera || control is EmotivPlayerInput)control.enabled=false;
                foreach(var debug in Object.FindObjectsOfType<MovementCorridorDebug>())debug.gameObject.SetActive(false);
                var settings=Object.FindObjectOfType<DinosaurInteractionSettings>();settings.spawnSeed=731716;
                typeof(DinosaurSpawnRandomizer).GetMethod("RandomizeAll",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                camera=Camera.main;target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
                var eyes=new[]{new Vector3(0,1.7f,-13),new Vector3(-6,1.7f,14),new Vector3(29,1.7f,22),new Vector3(0,105,-42)};
                var aims=new[]{new Vector3(-5,4,18),new Vector3(9,4,30),new Vector3(0,4,20),new Vector3(0,0,18)};
                if(Understory)
                {
                    var state=JsonUtility.FromJson<UnderstoryCameraState>(File.ReadAllText("Assets/Editor/UnderstoryDetailSample.json"));
                    eyes=UnderstoryDetailSample.Eyes(state.area);Vector2 c=state.area.center;
                    float ground=Terrain.activeTerrain.SampleHeight(new Vector3(c.x,0,c.y))+Terrain.activeTerrain.transform.position.y;
                    aims=new[]{new Vector3(c.x,ground+.6f,c.y),new Vector3(c.x+5,ground+.35f,c.y+7),new Vector3(c.x,ground+.5f,c.y),new Vector3(c.x,ground,c.y)};
                }
                if(ForestFloor)
                {
                    eyes=new[]{new Vector3(43.2f,1.7f,-15),new Vector3(8,1.7f,11),new Vector3(45,1.7f,65),new Vector3(0,105,-42)};
                    aims=new[]{eyes[0]+Quaternion.Euler(10,284,0)*Vector3.forward*20,eyes[1]+Quaternion.Euler(10,35,0)*Vector3.forward*20,eyes[2]+Quaternion.Euler(10,210,0)*Vector3.forward*20,new Vector3(0,0,18)};
                    for(int i=0;i<3;i++){float y=Terrain.activeTerrain.SampleHeight(eyes[i])+Terrain.activeTerrain.transform.position.y;eyes[i].y+=y;aims[i].y+=y;}
                }
                camera.transform.SetPositionAndRotation(eyes[view/2],Quaternion.LookRotation(aims[view/2]-eyes[view/2]));
                if(ForestFloor)
                {
                    floorAgents=Object.FindObjectsOfType<DinosaurAgent>();floorPrevious=floorAgents.Select(a=>a.transform.position).ToArray();
                    FloorTravel[view]=0;FloorStep[view]=0;
                }
                QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;Time.captureDeltaTime=1f/60;
                cpu=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread",1);draws=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Draw Calls Count",1);
                loading=false;Debug.Log("GAP_DYNAMIC_VIEW "+view+" active dinosaurs="+Object.FindObjectsOfType<DinosaurAgent>().Length);
            }
            catch(Exception e){Finish(e);}
        }
        [Serializable] private sealed class UnderstoryCameraState { public Rect area; }
        private static void Tick()
        {
            if(loading||lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Dynamic performance timed out.");
                FrameTimingManager.CaptureFrameTimings();frame++;
                int warmup=ForestFloor?60:120,total=ForestFloor?180:420;
                if(ForestFloor)
                    for(int i=0;i<floorAgents.Length;i++)
                    {
                        float step=Vector3.Distance(floorPrevious[i],floorAgents[i].transform.position);
                        if(frame>2){FloorTravel[view]+=step;FloorStep[view]=Math.Max(FloorStep[view],step);}
                        floorPrevious[i]=floorAgents[i].transform.position;
                    }
                if(frame>warmup && frame<=total)
                {
                    if(cpu.Valid && cpu.LastValue>0)Cpu[view].Add(cpu.LastValue*1e-6);
                    if(draws.Valid && draws.LastValue>0)Draws[view].Add(draws.LastValue);
                    Frames[view].Add(Time.unscaledDeltaTime*1000);
                    if(FrameTimingManager.GetLatestTimings(1,Timing)>0 && Timing[0].gpuFrameTime>0)Gpu[view].Add(Timing[0].gpuFrameTime);
                }
                if(frame<total)return;
                if(view<7){Next();return;}WriteReport();Finish(null);
            }
            catch(Exception e){Finish(e);}
        }
        private static double Median(IEnumerable<double> v){var a=v.OrderBy(x=>x).ToArray();return a.Length==0?0:a[a.Length/2];}
        private static void WriteReport()
        {
            var report=new StringBuilder("# 中心补空动态性能\n\n真实Play Mode，全部原启用恐龙自由漫游、导航、脚锁和枝叶交互运行；每个基线/补植视点重新加载场景并使用同一出生种子731716。1280×720，60Hz模拟；预热120帧，测量300帧。CPU为Profiler Main Thread整帧，不额外调用Camera.Render，不录制视频。基线为实施前Main和导航副本。\n\n|机位|基线CPU ms|补植CPU ms|变化|基线绘制|补植绘制|\n|---|---:|---:|---:|---:|---:|\n");
            for(int i=0;i<4;i++)report.AppendLine($"|{i}|{Median(Cpu[i*2]):F3}|{Median(Cpu[i*2+1]):F3}|{Median(Cpu[i*2+1])/Math.Max(.0001,Median(Cpu[i*2]))-1:P1}|{(Draws[i*2].Count==0?"unavailable":Median(Draws[i*2]).ToString("F0"))}|{(Draws[i*2+1].Count==0?"unavailable":Median(Draws[i*2+1]).ToString("F0"))}|");
            bool available=Cpu.All(v=>v.Count>0);double ratio=Median(Cpu.Where((_,i)=>i%2==1).SelectMany(v=>v))/Math.Max(.0001,Median(Cpu.Where((_,i)=>i%2==0).SelectMany(v=>v)))-1;
            report.AppendLine(available?$"\nCPU合并中位增幅 {ratio:P1}；15%目标：{(ratio<=.15?"PASS":"FAIL，需要优化并复测")}。":"\nCPU整帧计时不可用，15%目标未验证。");
            report.AppendLine(Gpu.Any(g=>g.Count>0)?$"\nGPU基线/补植合并中位 {Median(Gpu.Where((_,i)=>i%2==0).SelectMany(v=>v)):F3}/{Median(Gpu.Where((_,i)=>i%2==1).SelectMany(v=>v)):F3}ms。":"\nGPU计时不可用，未推算GPU性能。");
            report.AppendLine("\n数值包含Unity编辑器开销，不代表发布版FPS。完整运动安全由独立NormalPlay/Acceptance报告判定。");
            if(Understory)report.Replace("中心补空动态性能","林下细节样板动态性能");
            if(ForestFloor)report.Replace("中心补空动态性能","林地地表动态性能");
            if(ForestFloor)
            {
                report.Replace("预热120帧，测量300帧","预热60帧，测量120帧");
                report.Replace("基线为实施前Main和导航副本","基线为实施前Main、地形及路线草材质快照，复用未改动的导航数据");
                for(int i=0;i<8;i++)report.AppendLine($"\n视点/版本 {i}：恐龙累计移动 {FloorTravel[i]:F2}m；最大单帧位移 {FloorStep[i]:F3}m。这是3秒短程冒烟检查，不代替长期漫游验收。");
            }
            report.AppendLine(Application.isBatchMode?"\n本次为batch Play条件；Crest禁用水面，不代表完整水面渲染成本。":"\n本次为非batch Play条件。");
            File.WriteAllText(ResultOutput+"/Performance.md",report.ToString());
            File.WriteAllText(ResultOutput+"/PerformanceResult.txt",available && ratio<=.15?"PASS":"FAIL");
        }
        private static void Release()
        {
            cpu.Dispose();draws.Dispose();if(camera!=null)camera.targetTexture=null;
            if(target!=null){target.Release();Object.DestroyImmediate(target);target=null;}
        }
        private static void Finish(Exception e)
        {
            EditorApplication.update-=Tick;SceneManager.sceneLoaded-=Loaded;SessionState.SetBool(Key,false);Time.captureDeltaTime=0;Release();
            if(e!=null){File.WriteAllText(ResultOutput+"/PerformanceError.txt",e.ToString());Debug.LogException(e);}
            if(Application.isBatchMode)EditorApplication.Exit(e==null?0:1);else EditorApplication.isPlaying=false;
        }
    }
}
