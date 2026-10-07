using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace JurassicWorld.Editor
{
    /// <summary>Actual Play Mode, same scene/cameras, before/after owned planting. No image capture.</summary>
    [InitializeOnLoad]
    public static class CentralForestPlayPerformance
    {
        private const string Running = "JurassicWorld.CentralForestPerformance.Running";
        private static Camera camera;
        private static GameObject forest, originalPreview;
        private static GameObject originalRegionTrees;
        private static Terrain terrain;
        private static TerrainData originalTerrainData, baselineTerrainData;
        private static RenderTexture target;
        private static RenderTexture previousTarget;
        private static Vector3 originalPosition;
        private static Quaternion originalRotation;
        private static readonly List<double>[] Times = Enumerable.Range(0, 6).Select(_ => new List<double>()).ToArray();
        private static readonly List<double>[] FrameTimes = Enumerable.Range(0, 6).Select(_ => new List<double>()).ToArray();
        private static readonly List<double>[] GpuTimes = Enumerable.Range(0, 6).Select(_ => new List<double>()).ToArray();
        private static readonly FrameTiming[] Timing = new FrameTiming[1];
        private static int view, frame, lastFrame;
        private static double deadline;
        static CentralForestPlayPerformance()
        {
            EditorApplication.playModeStateChanged += OnState;
        }
        [MenuItem("Jurassic World/Diagnostics/Central Forest/Compare Play Render Cost (No Images)")]
        private static void RunMenu() => JurassicMenuGuard.Run(Run, true);

        [MenuItem("Jurassic World/Diagnostics/Central Forest/Compare Play Render Cost (No Images)", true)]
        private static bool RunMenuEnabled() => JurassicMenuGuard.CanRun;

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(CentralForestDensification.ScenePath);
            SessionState.SetBool(Running, true);
            EditorApplication.isPlaying = true;
        }
        private static void OnState(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Running, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                try
                {
                    forest = GameObject.Find(CentralForestDensification.RootName);
                    camera = Camera.main;
                    if (forest == null || camera == null) throw new InvalidOperationException("Forest or main camera missing.");
                    terrain = Terrain.activeTerrain; originalTerrainData = terrain.terrainData;
                    ForestRegionBackup region = forest.GetComponentInChildren<ForestRegionBackup>();
                    if (region != null && region.applied)
                    {
                        baselineTerrainData = UnityEngine.Object.Instantiate(originalTerrainData);
                        baselineTerrainData.SetTreeInstances(region.RestoreTreeArray(originalTerrainData.treeInstances), false);
                        Transform originals = region.transform.Find("Original Scene Trees");
                        if (originals != null)
                        { originalRegionTrees = UnityEngine.Object.Instantiate(originals.gameObject); originalRegionTrees.name = "Performance baseline region originals"; originalRegionTrees.SetActive(false); }
                    }
                    foreach (var controller in UnityEngine.Object.FindObjectsOfType<SmoothExplorerCamera>()) controller.enabled = false;
                    originalPosition = camera.transform.position; originalRotation = camera.transform.rotation;
                    previousTarget = camera.targetTexture;
                    target = new RenderTexture(1280, 720, 24); target.Create(); camera.targetTexture = target;
                    Transform backup = forest.transform.Find("Preview Backup");
                    if (backup != null)
                    { originalPreview = UnityEngine.Object.Instantiate(backup.gameObject); originalPreview.name = "Performance baseline preview"; originalPreview.SetActive(false); }
                    foreach (var list in Times.Concat(FrameTimes).Concat(GpuTimes)) list.Clear();
                    view = -1; NextView(); lastFrame = -1;
                    deadline = EditorApplication.timeSinceStartup + 180;
                    EditorApplication.update += Tick;
                }
                catch (Exception error) { Finish(error); }
            }
            if (state == PlayModeStateChange.ExitingPlayMode) Cleanup();
        }
        private static void NextView()
        {
            view++; frame = 0;
            bool populated = view >= 3;
            forest.SetActive(populated);
            if (terrain != null && baselineTerrainData != null) terrain.terrainData = populated ? originalTerrainData : baselineTerrainData;
            if (originalRegionTrees != null) originalRegionTrees.SetActive(!populated);
            if (originalPreview != null) originalPreview.SetActive(!populated);
            TerrainTreeObstacleRegistry.Invalidate();
            int route = new[] { 0, 2, 5 }[view % 3];
            MovementCorridor.GetSampleAt(route, MovementCorridor.Length(route) * .24f, out Vector2 p, out Vector2 tangent);
            Vector3 direction = new Vector3(tangent.x, 0f, tangent.y);
            Vector3 eye = new Vector3(p.x, 0f, p.y) - direction * 3f;
            eye.y = Terrain.activeTerrain.SampleHeight(eye) + Terrain.activeTerrain.transform.position.y + 1.7f;
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(direction + Vector3.up * .08f));
            Debug.Log($"CENTRAL_FOREST_PERF_VIEW {view}: populated={populated}, route={route}, eye={eye}");
        }
        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Play performance check timed out.");
                if (lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                var watch = Stopwatch.StartNew();
                camera.Render(); watch.Stop();
                FrameTimingManager.CaptureFrameTimings();
                if (++frame <= 20) return; // warm materials and LODs after each camera/root change
                Times[view].Add(watch.Elapsed.TotalMilliseconds);
                FrameTimes[view].Add(Time.unscaledDeltaTime * 1000.0);
                if (FrameTimingManager.GetLatestTimings(1, Timing) > 0 && Timing[0].gpuFrameTime > 0)
                    GpuTimes[view].Add(Timing[0].gpuFrameTime);
                if (frame < 80) return;
                if (view < 5) { NextView(); return; }
                WriteReport(); Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }
        private static double Median(List<double> values)
        { var sorted = values.OrderBy(t => t).ToArray(); return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2]; }
        private static void WriteReport()
        {
            var text = new StringBuilder("# 中心加密 Play 渲染开销\n\n实际 Unity Play Mode；三个相同路线视点；1280×720 RenderTexture；每个视点预热20帧，测量60帧。\n\n");
            text.AppendLine("| 路线视点 | 修改前 Camera.Render CPU 中位 ms | 第一阶段 CPU 中位 ms | 差值 ms |\n|---|---:|---:|---:|");
            for (int i = 0; i < 3; i++) text.AppendLine($"| {new[] { 0, 2, 5 }[i]} | {Median(Times[i]):F2} | {Median(Times[i + 3]):F2} | {Median(Times[i + 3]) - Median(Times[i]):F2} |");
            double baseline = Median(Times.Take(3).SelectMany(t => t).ToList()), populated = Median(Times.Skip(3).SelectMany(t => t).ToList());
            text.AppendLine($"\n合并样本 CPU 中位：{baseline:F2} → {populated:F2} ms（{(populated / baseline - 1) * 100:+0.0;-0.0;0.0}%）。");
            text.AppendLine($"\nEditor Play 帧间隔中位：{Median(FrameTimes.Take(3).SelectMany(t => t).ToList()):F2} → {Median(FrameTimes.Skip(3).SelectMany(t => t).ToList()):F2} ms。含编辑器调度、额外测量渲染和同步等待，不能作为发布版 FPS。");
            if (GpuTimes.All(t => t.Count > 0)) text.AppendLine($"\nFrameTimingManager GPU 帧时间中位：{Median(GpuTimes.Take(3).SelectMany(t => t).ToList()):F2} → {Median(GpuTimes.Skip(3).SelectMany(t => t).ToList()):F2} ms。");
            else text.AppendLine("\n本次环境没有提供有效 GPU 帧计时；Camera.Render 数值是 CPU 渲染提交与等待耗时，不等同于 GPU 时间。");
            text.AppendLine("\n修改前基线关闭加密根对象，并临时恢复五棵预览树、被区域替换的场景树及完整地形树数组。基线地形使用内存副本；测试只改变 Play 内存状态，不修改场景或地形资产；无截图。恐龙路线运动另用 RunWithoutImages 验收。");
            Directory.CreateDirectory(CentralForestDensification.Output);
            File.WriteAllText(CentralForestDensification.Output + "/PlayPerformance.md", text.ToString());
            Debug.Log($"CENTRAL_FOREST_PLAY_PERFORMANCE_OK: CPU render median {baseline:F2} -> {populated:F2}ms, delta={populated - baseline:F2}ms; actual Play, three views, no screenshots.");
        }
        private static void Cleanup()
        {
            EditorApplication.update -= Tick;
            if (camera != null) { camera.targetTexture = previousTarget; camera.transform.SetPositionAndRotation(originalPosition, originalRotation); }
            if (forest != null) forest.SetActive(true);
            if (terrain != null && originalTerrainData != null) terrain.terrainData = originalTerrainData;
            if (baselineTerrainData != null) UnityEngine.Object.Destroy(baselineTerrainData);
            if (originalRegionTrees != null) UnityEngine.Object.Destroy(originalRegionTrees);
            if (originalPreview != null) UnityEngine.Object.Destroy(originalPreview);
            if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); }
            TerrainTreeObstacleRegistry.Invalidate();
        }
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Running, false); Cleanup();
            if (error != null) Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}
