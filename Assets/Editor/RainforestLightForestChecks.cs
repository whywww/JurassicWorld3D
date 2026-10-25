using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class RainforestLightForestChecks
    {
        private const string Key = "JurassicWorld.LightForest.Check";
        private const string Baseline = "Assets/Editor/LightForestCostBaseline.prefab";
        private static Camera camera;
        private static RenderTexture target;
        private static ProfilerRecorder draws, triangles, mainThread;
        private static int frame, view;
        private static int cameraMask;
        private static readonly FrameTiming[] Timing = new FrameTiming[1];
        private static readonly List<double> Cpu = new List<double>(), Gpu = new List<double>(), Main = new List<double>(), Draws = new List<double>(), Triangles = new List<double>();
        private static readonly List<Row> Rows = new List<Row>();
        [Serializable] private sealed class Row
        {
            public int view;
            public double cameraRenderCpuMs, mainThreadMs, gpuFrameMs, drawCalls, triangles;
            public long visibleMeshTriangles;
            public int gpuSamples;
        }
        [Serializable] private sealed class Results { public string mode, note; public Row[] views; }
        static RainforestLightForestChecks() => EditorApplication.playModeStateChanged += Changed;

        public static void BeforeBatch() => Begin("Before");
        public static void AfterBatch() => Begin("After");
        public static void GreenWallCheck() => Begin("GreenWall");
        private static void Begin(string mode)
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Directory.CreateDirectory(RainforestLightForest.Report);
            if (mode == "Before" && File.Exists(RainforestLightForest.Report+"/Backdrop.Before.prefab"))
            {
                File.Copy(RainforestLightForest.Report+"/Backdrop.Before.prefab",Baseline,true);
                AssetDatabase.ImportAsset(Baseline,ImportAssetOptions.ForceSynchronousImport);
            }
            if (mode != "Before")
            {
                RainforestBackdropStageThree.ApplyConfigured(); RainforestBackdropStageThree.ApplyConfigured();
                RainforestBackdropStageThree.Validate();
            }
            SessionState.SetString(Key+".Mode",mode);
            SessionState.SetString(Key+".Hashes",Hashes());
            SessionState.SetInt(Key+".Tier",QualitySettings.GetQualityLevel());
            SessionState.SetBool(Key,true);
            SessionState.SetBool(Key+".Passed",false);
            EditorApplication.isPlaying = true;
        }

        private static string Hashes()
        {
            using (SHA256 hash = SHA256.Create()) return string.Join(";", new[] {
                "Assets/Scenes/Main.unity",RainforestAtmosphereStageOne.ProfilePath,
                "Assets/Environment/Terrain/Prepared/MainInteractionTerrain.asset","Assets/Navigation/TRexInteractionNavMesh.asset",
                "Assets/Environment/Backdrop/Prepared/RainforestBackdrop.prefab"
            }.Select(path => { using (FileStream file = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(file)); }));
        }

        private static void Changed(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key,false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                try
                {
                    camera = Camera.main;
                    cameraMask = camera.cullingMask;
                    if (SessionState.GetString(Key+".Mode","") == "Before" && File.Exists(Baseline))
                    {
                        Object.DestroyImmediate(GameObject.Find(RainforestBackdropStageThree.RootName));
                        GameObject root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Baseline));
                        root.name = RainforestBackdropStageThree.RootName;
                        RainforestBackdrop backdrop = root.GetComponent<RainforestBackdrop>();
                        backdrop.viewCamera = camera; backdrop.ApplyCamera();
                    }
                    foreach (Camera other in Object.FindObjectsOfType<Camera>()) if (other != camera) other.enabled = false;
                    foreach (Crest.OceanPlanarReflection reflection in Object.FindObjectsOfType<Crest.OceanPlanarReflection>()) reflection.enabled = false;
                    foreach (MonoBehaviour behaviour in camera.GetComponents<MonoBehaviour>()) behaviour.enabled = false;
                    camera.enabled = true;
                    // Fixed render size and medium tier; only the backdrop is measured to isolate this change.
                    QualitySettings.SetQualityLevel(1,true);
                    Object.FindObjectOfType<RainforestAtmosphereController>().ApplyQuality(1);
                    camera.cullingMask = 1 << LayerMask.NameToLayer("Backdrop");
                    target = new RenderTexture(1280,720,24);
                    camera.targetTexture = target; camera.aspect = 1280f/720f;
                    draws = ProfilerRecorder.StartNew(ProfilerCategory.Render,"Draw Calls Count",1);
                    triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render,"Triangles Count",1);
                    mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread",1);
                    Rows.Clear(); Reset(); frame = -16; view = 0;
                    EditorApplication.update += Tick;
                }
                catch (Exception error) { Fail(error); }
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                bool passed = SessionState.GetBool(Key+".Passed",false) && Hashes() == SessionState.GetString(Key+".Hashes","");
                SessionState.SetBool(Key,false);
                QualitySettings.SetQualityLevel(SessionState.GetInt(Key+".Tier",1),true);
                if (File.Exists(Baseline)) AssetDatabase.DeleteAsset(Baseline);
                Debug.Log("LIGHT_FOREST_"+SessionState.GetString(Key+".Mode","").ToUpperInvariant()+"_CHECK_"+(passed ? "PASS" : "FAIL")+": Play file hashes checked.");
                if (SessionState.GetString(Key+".Mode","") == "GreenWall")
                {
                    File.AppendAllText(RainforestGreenWallFix.Report+"/Checks.txt","Runtime low/medium/high, minimap/reflection exclusion and Play file hashes: "+(passed ? "PASS" : "FAIL")+". Manual Play approval pending.\n");
                    File.WriteAllText("Logs/rainforest-status.txt",passed ? "DONE green-wall-fix; runtime check PASS" : "FAILED green-wall runtime check");
                }
                if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
            }
        }

        private static void Reset() { Cpu.Clear(); Gpu.Clear(); Main.Clear(); Draws.Clear(); Triangles.Clear(); }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            try
            {
                if (SessionState.GetString(Key+".Mode","") == "GreenWall")
                {
                    if (++frame < 1) return;
                    EditorApplication.update -= Tick;
                    camera.cullingMask = cameraMask;
                    foreach (MonoBehaviour behaviour in camera.GetComponents<MonoBehaviour>())
                        if (behaviour.GetType().Name == "PostProcessLayer") behaviour.enabled = true;
                    RainforestBackdropStageThree.Validate();
                    RainforestGreenWallFix.ValidateGround(Terrain.activeTerrain);
                    for (int tier = 0; tier < 3; tier++)
                    {
                        QualitySettings.SetQualityLevel(tier,true);
                        Object.FindObjectOfType<RainforestAtmosphereController>().ApplyQuality(tier);
                        string suffix = "Runtime-"+new[] { "Low","Medium","High" }[tier];
                        RainforestBoundaryTransition.Capture(camera,suffix,RainforestGreenWallFix.Report);
                        RainforestGreenWallFix.CaptureUserView(suffix);
                    }
                    CheckMasks();
                    SessionState.SetBool(Key+".Passed",true);
                    Cleanup(); EditorApplication.isPlaying = false;
                    return;
                }
                RainforestBoundaryTransition.BoundaryPose(view,out Vector3 position,out Quaternion rotation);
                camera.transform.SetPositionAndRotation(position,rotation);
                var watch = Stopwatch.StartNew(); camera.Render(); watch.Stop();
                FrameTimingManager.CaptureFrameTimings();
                if (++frame <= 8) return;
                Cpu.Add(watch.Elapsed.TotalMilliseconds);
                if (draws.Valid) Draws.Add(draws.LastValue);
                if (triangles.Valid) Triangles.Add(triangles.LastValue);
                if (mainThread.Valid && mainThread.LastValue > 0) Main.Add(mainThread.LastValue*1e-6);
                if (FrameTimingManager.GetLatestTimings(1,Timing) > 0 && Timing[0].gpuFrameTime > 0) Gpu.Add(Timing[0].gpuFrameTime);
                if (frame < 24) return;
                Rows.Add(new Row { view=view, cameraRenderCpuMs=Median(Cpu), mainThreadMs=Median(Main), gpuFrameMs=Median(Gpu),
                    drawCalls=Median(Draws),triangles=Median(Triangles),gpuSamples=Gpu.Count,visibleMeshTriangles=VisibleTriangles() });
                if (++view < 16) { frame=0; Reset(); return; }
                EditorApplication.update -= Tick;
                string mode = SessionState.GetString(Key+".Mode","");
                File.WriteAllText(RainforestLightForest.Report+"/Performance."+mode+".json",JsonUtility.ToJson(new Results {
                    mode=mode,views=Rows.ToArray(),note="Medium, 1280x720, identical 16 static views. Backdrop-only camera; other cameras/reflections disabled in this temporary Play session. Automatic rendering plus one Camera.Render CPU probe per editor update, identical in both modes. Recorder counters include editor-frame work; visibleMeshTriangles is the separate conservative frustum mesh budget, not a measured GPU counter. Camera.Render CPU is submission time; main-thread/GPU values cover the editor frame, not isolated GPU time. Zero or missing samples mean unavailable, not zero cost. Short diagnostic, not an FPS guarantee or timed-route acceptance."
                },true));
                if (mode == "After")
                {
                    RainforestBackdropStageThree.Validate();
                    camera.cullingMask = cameraMask;
                    foreach (MonoBehaviour behaviour in camera.GetComponents<MonoBehaviour>())
                        if (behaviour.GetType().Name == "PostProcessLayer") behaviour.enabled = true;
                    for (int tier = 0; tier < 3; tier++)
                    {
                        QualitySettings.SetQualityLevel(tier,true);
                        Object.FindObjectOfType<RainforestAtmosphereController>().ApplyQuality(tier);
                        RainforestBoundaryTransition.Capture(camera,"Runtime-"+new[] { "Low","Medium","High" }[tier]);
                    }
                    CheckMasks();
                }
                SessionState.SetBool(Key+".Passed",true);
                Cleanup(); EditorApplication.isPlaying = false;
            }
            catch (Exception error) { Fail(error); }
        }

        private static void CheckMasks()
        {
            int bit = 1 << LayerMask.NameToLayer("Backdrop");
            foreach (Camera other in Object.FindObjectsOfType<Camera>())
                if (other.name.Contains("Landscape") && (other.cullingMask&bit) != 0) throw new InvalidOperationException("Minimap includes forest backdrop.");
            foreach (Crest.OceanPlanarReflection reflection in Object.FindObjectsOfType<Crest.OceanPlanarReflection>())
                if ((new SerializedObject(reflection).FindProperty("_reflectionLayers").intValue&bit) != 0) throw new InvalidOperationException("Reflection includes forest backdrop.");
        }

        private static long VisibleTriangles()
        {
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
            return GameObject.Find(RainforestBackdropStageThree.RootName).GetComponentsInChildren<MeshFilter>()
                .Where(f => f.GetComponent<Renderer>().enabled && GeometryUtility.TestPlanesAABB(planes,f.GetComponent<Renderer>().bounds))
                .Sum(f => (long)f.sharedMesh.triangles.Length/3);
        }

        private static double Median(List<double> values)
        {
            if (values.Count == 0) return 0;
            double[] ordered = values.OrderBy(v => v).ToArray();
            return ordered[ordered.Length/2];
        }
        private static void Cleanup()
        {
            EditorApplication.update -= Tick;
            draws.Dispose(); triangles.Dispose(); mainThread.Dispose();
            if (camera != null) camera.targetTexture = null;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
        }
        private static void Fail(Exception error)
        {
            Debug.LogException(error); Cleanup();
            SessionState.SetBool(Key+".Passed",false); EditorApplication.isPlaying = false;
        }
    }
}
