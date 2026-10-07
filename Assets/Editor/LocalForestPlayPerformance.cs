using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class LocalForestPlayPerformance
    {
        private const string Key = "JurassicWorld.LocalForestPlayPerformance";
        private const string Output = "Tools/VegetationDiversity/LocalForestRepair";
        private static Camera camera;
        private static Terrain terrain;
        private static TerrainData repaired;
        private static GameObject root;
        private static RenderTexture target;
        private static int view, frame, lastFrame;
        private static double deadline;
        private static readonly List<double> Times = new List<double>();
        private static readonly List<string> Report = new List<string>();
        private static readonly int[] Headings = { 0, 78, 180, 270, 0, 90, 180, 270 };
        static LocalForestPlayPerformance() { EditorApplication.playModeStateChanged += OnState; }

        [MenuItem("Jurassic World/Diagnostics/Southeast Forest/Compare Play Render Cost")]
        private static void RunMenu() => JurassicMenuGuard.Run(Run, true);

        [MenuItem("Jurassic World/Diagnostics/Southeast Forest/Compare Play Render Cost", true)]
        private static bool RunMenuEnabled() => JurassicMenuGuard.CanRun;

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save and exit Play Mode first.");
            EditorSceneManager.OpenScene(CentralForestDensification.ScenePath);
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }

        private static void OnState(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key, false) || state != PlayModeStateChange.EnteredPlayMode) return;
            try
            {
                camera = Camera.main; terrain = Terrain.activeTerrain; repaired = terrain.terrainData;
                root = GameObject.Find(LocalForestRepair.RootName);
                if (root == null) throw new InvalidOperationException("Repair root missing.");
                foreach (var controller in UnityEngine.Object.FindObjectsOfType<SmoothExplorerCamera>()) controller.enabled = false;
                target = new RenderTexture(1280, 720, 24); target.Create(); camera.targetTexture = target;
                deadline = EditorApplication.timeSinceStartup + 150; view = -1; lastFrame = -1;
                Report.Clear(); NextView(); EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void NextView()
        {
            view++; frame = 0; Times.Clear();
            if (view == 16) { Finish(null); return; }
            bool after = view >= 8;
            root.SetActive(after); terrain.terrainData = after ? repaired : LocalForestRepair.Baseline;
            int index = view % 8, heading = Headings[index];
            Vector2 p = LocalForestRepair.Center;
            if (index >= 4) p += new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad)) * 32f;
            var eye = new Vector3(p.x, 0f, p.y);
            eye.y = terrain.SampleHeight(eye) + terrain.transform.position.y + 1.7f;
            camera.transform.SetPositionAndRotation(eye, Quaternion.Euler(0f, heading, 0f));
        }

        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Local Play performance timeout.");
                if (Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                var watch = Stopwatch.StartNew(); camera.Render();
                if (frame++ >= 12) Times.Add(watch.Elapsed.TotalMilliseconds);
                if (frame < 36) return;
                Times.Sort();
                string label = view < 8 ? "before" : "after";
                int index = view % 8;
                Report.Add($"{label} {(index < 4 ? "center" : "edge")} {Headings[index]}°: CPU Camera.Render median {Times[Times.Count/2]:F2}ms");
                var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                RenderTexture active = RenderTexture.active;
                try
                {
                    RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                    File.WriteAllBytes($"{Output}/play-{label}-{index}.png", image.EncodeToPNG());
                }
                finally { RenderTexture.active = active; UnityEngine.Object.Destroy(image); }
                NextView();
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Finish(Exception error)
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
            if (terrain != null && repaired != null) terrain.terrainData = repaired;
            if (root != null) root.SetActive(true);
            if (camera != null) camera.targetTexture = null;
            if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); }
            if (error != null) Debug.LogException(error);
            else
            {
                File.WriteAllLines(Output + "/PlayRenderCost.txt", Report.Concat(new[] { "1280x720 actual Play Mode. CPU render submission only; GPU timing unavailable. Not a release FPS measurement." }));
                Debug.Log("LOCAL_FOREST_PLAY_RENDER_OK: center and boundary views, baseline and repaired, assets not saved.");
            }
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}
