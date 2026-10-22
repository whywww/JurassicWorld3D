using System;
using System.IO;
using System.Linq;
using Crest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace JurassicWorld.Editor
{
    // Crest intentionally disables water rendering in batchmode. This short,
    // isolated non-batch preview changes only Play-mode clones and exits unsaved.
    [InitializeOnLoad]
    public static class WaterCleanupVisuals
    {
        private const string Key="JurassicWorld.WaterCleanupVisuals";
        private static TerrainData original;
        private static Camera camera;
        private static RenderTexture target;
        private static Texture2D pixels;
        private static int view,frames,lastFrame;
        private static double deadline;
        private static Vector3[] eyes,aims;
        static WaterCleanupVisuals()
        {
            EditorApplication.playModeStateChanged+=state=>
            {
                if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Key,false))EditorApplication.delayCall+=Begin;
                if(state==PlayModeStateChange.EnteredEditMode&&SessionState.GetBool(Key+".exit",false))
                {SessionState.SetBool(Key+".exit",false);EditorApplication.delayCall+=()=>EditorApplication.Exit(SessionState.GetInt(Key+".code",0));}
            };
        }
        public static void RunAndExit()
        {
            if(Application.isBatchMode)throw new InvalidOperationException("Crest requires a non-batch visual run.");
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorSceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Save and stop Play first.");
            EditorSceneManager.OpenScene(CentralForestDensification.ScenePath);SessionState.SetBool(Key,true);SessionState.SetBool(Key+".exit",true);EditorApplication.isPlaying=true;
        }
        private static void Begin()
        {
            try
            {
                Application.runInBackground=true;
                foreach(var b in Object.FindObjectsOfType<MonoBehaviour>())if(b is DinosaurAgent||b is SmoothExplorerCamera||b is EmotivPlayerInput)b.enabled=false;
                foreach(var a in Object.FindObjectsOfType<Animation>())foreach(AnimationState s in a)s.speed=0;
                original=WaterPlantCleanup.PreviewBefore();camera=Camera.main;
                target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
                eyes=new Vector3[3];aims=new Vector3[3];var bodies=Object.FindObjectsOfType<WaterBody>().OrderBy(b=>b.transform.position.z).ToArray();
                float[] wanted={-15,37,76};
                for(int i=0;i<3;i++)
                {
                    var body=bodies.OrderBy(b=>Mathf.Abs(b.transform.position.z-wanted[i])).First();
                    var next=bodies.Where(b=>b.transform.position.z>body.transform.position.z).FirstOrDefault()??body;
                    eyes[i]=body.transform.position+Vector3.up*(i==1?4f:1.8f);
                    aims[i]=next.transform.position+Vector3.up*.1f;
                }
                view=-1;lastFrame=-1;deadline=EditorApplication.timeSinceStartup+180;Next();EditorApplication.update+=Tick;
            }
            catch(Exception e){Finish(e);}
        }
        private static void Next()
        {
            view++;frames=0;
            if(view==3){WaterPlantCleanup.PreviewAfter(original);original=null;}
            camera.transform.SetPositionAndRotation(eyes[view%3],Quaternion.LookRotation(aims[view%3]-eyes[view%3]));
        }
        private static void Tick()
        {
            if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Water visual capture timed out.");
                if(++frames<45 || Object.FindObjectOfType<OceanRenderer>()?.Root==null)return;
                var previous=RenderTexture.active;
                try {camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();File.WriteAllBytes(WaterPlantCleanup.Output+"/water-"+(view<3?"before":"after")+"-"+(view%3)+".png",pixels.EncodeToPNG());}
                finally {RenderTexture.active=previous;}
                if(view<5)Next();else
                {
                    File.WriteAllText(WaterPlantCleanup.Output+"/Visuals.md","# 河道画面对照\n\n非batch Unity Play模式真实Crest水面，3个固定河道机位，各预热45帧；water-before/after-0..2.png。基线仅在Play内恢复本轮19株水中植物，地形数据使用克隆，不写回Main或资产。岸上植物与树冠悬垂保留。原before/after-0..2.png为编辑态检查图，Crest在batch模式不绘制真实水面，不作为水面视觉验收。\n");
                    Finish(null);
                }
            }
            catch(Exception e){Finish(e);}
        }
        private static void Finish(Exception e)
        {
            EditorApplication.update-=Tick;SessionState.SetBool(Key,false);SessionState.SetInt(Key+".code",e==null?0:1);
            if(original!=null)WaterPlantCleanup.PreviewAfter(original);
            if(camera!=null)camera.targetTexture=null;if(target!=null){target.Release();Object.DestroyImmediate(target);}if(pixels!=null)Object.DestroyImmediate(pixels);
            if(e!=null){File.WriteAllText(WaterPlantCleanup.Output+"/VisualError.txt",e.ToString());Debug.LogException(e);}
            EditorApplication.isPlaying=false;
        }
    }
}
