using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace JurassicWorld
{
    // Opt-in only. Samples normal automatic camera rendering, never Camera.Render().
    public sealed class RuntimePerformanceBenchmark : MonoBehaviour
    {
        public static bool Requested => Array.IndexOf(Environment.GetCommandLineArgs(), "-jwPerf") >= 0;
        public static bool Finished { get; private set; }
        public static bool Failed { get; private set; }
        private readonly List<ProfilerRecorder> recorders = new List<ProfilerRecorder>();
        private readonly string[] counters = { "Main Thread", "CPU Render Thread Frame Time", "GC Allocated In Frame", "Draw Calls Count", "Triangles Count", "Batches Count", "SetPass Calls Count",
            "JurassicWorld.FoliageInteraction", "JurassicWorld.DinosaurBehaviour", "JurassicWorld.DinosaurFootLock", "JurassicWorld.NavigationHud", "JurassicWorld.Visibility.Capture", "JurassicWorld.Visibility.SceneRender", "JurassicWorld.Visibility.ReadResults",
            "Camera.Render", "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForGfxCommandsFromMainThread", "Gfx.WaitForRenderThread" };
        private readonly FrameTiming[] timing = new FrameTiming[1];
        private new Camera camera;
        private RenderTexture output, previousTarget;
        private Vector3 originalPosition;
        private Quaternion originalRotation;
        private Vector3 dinosaurEye, dinosaurLook, creekEye = new Vector3(12, 0, 12), creekLook = new Vector3(35, 2, 30);
        private Vector3 forestEye = new Vector3(-45, 0, -20), forestLook;
        private Vector3[] route;
        private readonly List<Behaviour> disabled = new List<Behaviour>();
        private int savedVsync, savedTarget;
        private bool configured;
        private string directory;
        private int automaticRenders;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Seed()
        {
            Finished = Failed = false;
            if (Requested) UnityEngine.Random.InitState(73021);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
            if (Requested) new GameObject("Runtime Performance Benchmark").AddComponent<RuntimePerformanceBenchmark>();
        }

        private static string Argument(string key, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }
        private static float Seconds(string key, string fallback) => Mathf.Max(.1f, float.Parse(Argument(key, fallback), CultureInfo.InvariantCulture));

        private IEnumerator Start()
        {
            yield return null; // Let scene Start methods and quality application finish.
            try { Configure(); }
            catch (Exception error) { Complete(error); yield break; }
            var routine = Measure();
            while (true)
            {
                object current;
                try
                {
                    if (!routine.MoveNext()) break;
                    current = routine.Current;
                }
                catch (Exception error) { Complete(error); yield break; }
                yield return current;
            }
            Complete(null);
        }

        private void Configure()
        {
            camera = Camera.main;
            if (camera == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Benchmark needs Main camera and a real graphics device.");
            string label = Argument("-jwPerfLabel", "measurement");
            if (label.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("Invalid benchmark label.");
            directory = Path.GetFullPath(Path.Combine("Logs", "RuntimePerformance", label));
            Directory.CreateDirectory(directory);
            savedVsync = QualitySettings.vSyncCount; savedTarget = Application.targetFrameRate;
            originalPosition = camera.transform.position; originalRotation = camera.transform.rotation;
            // Retained baseline's initial dinosaur viewpoint. Actor startup order can change
            // its initial position despite Random.InitState; do not move the test camera with it.
            dinosaurEye = new Vector3(59.4972f, 1.6999f, 43.1379f);
            dinosaurLook = dinosaurEye + Quaternion.Euler(351.4688f, 0f, 0f) * Vector3.forward * 20f;
            foreach (var point in FindObjectsOfType<Transform>())
                if (point.name == "Creek Ground View 1") { creekEye = point.position; creekLook = creekEye + point.forward * 20; break; }
            var layout = FindObjectOfType<RainforestSampleLayout>();
            if (layout != null && layout.clearingPositions.Length > 0)
            {
                forestEye = layout.clearingPositions[0];
                forestLook = forestEye + layout.clearingRotations[0] * Vector3.forward * 20;
                route = layout.corridor;
            }
            else forestLook = forestEye + Vector3.forward * 20;
            configured = true;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            foreach (var input in FindObjectsOfType<SmoothExplorerCamera>()) Disable(input);
            foreach (var input in FindObjectsOfType<EmotivPlayerInput>()) Disable(input);
            int width = int.Parse(Argument("-jwPerfWidth", "1920"));
            int height = int.Parse(Argument("-jwPerfHeight", "1080"));
            previousTarget = camera.targetTexture;
            output = new RenderTexture(width, height, 24) { name = "Automatic benchmark output" };
            output.Create(); camera.targetTexture = output;
            Camera.onPostRender += CameraRendered;
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var descriptions = new List<ProfilerRecorderDescription>();
            var available = new StringBuilder();
            foreach (var handle in handles)
            {
                var description = ProfilerRecorderHandle.GetDescription(handle);
                descriptions.Add(description);
                available.AppendLine(description.Name + " | " + description.Category.Name + " | " + description.UnitType);
            }
            File.WriteAllText(Path.Combine(directory, "available-counters.txt"), available.ToString());
            foreach (string counter in counters)
            {
                ProfilerCategory category = counter.Contains("Thread") ? ProfilerCategory.Internal :
                    counter.StartsWith("GC") ? ProfilerCategory.Memory : counter.StartsWith("JurassicWorld.") ? ProfilerCategory.Scripts : ProfilerCategory.Render;
                foreach (var description in descriptions) if (description.Name == counter) { category = description.Category; break; }
                recorders.Add(ProfilerRecorder.StartNew(category, counter, 1));
            }
            string metadata = $"# Runtime performance: {label}\n\n" +
                $"UTC: {DateTime.UtcNow:O}\nUnity: {Application.unityVersion}\nEditor: {Application.isEditor}\nBatchmode: {Application.isBatchMode}\nDevelopment: {Debug.isDebugBuild}\n" +
                $"CPU: {SystemInfo.processorType} ({SystemInfo.processorCount})\nGPU: {SystemInfo.graphicsDeviceName}\nAPI: {SystemInfo.graphicsDeviceType}\n" +
                $"RAM / VRAM MB: {SystemInfo.systemMemorySize} / {SystemInfo.graphicsMemorySize}\n" +
                $"Automatic camera target: {width}x{height}\nQuality: {QualitySettings.names[QualitySettings.GetQualityLevel()]}\n" +
                $"Warmup seconds: {Argument("-jwPerfWarmup", "15")}\nSample seconds: {Argument("-jwPerfSeconds", "60")}\nRepeats: {Argument("-jwPerfRepeats", "3")}\n" +
                "Render validity: automatic main-camera callbacks required.\n" +
                "Render thread timing: ProfilerRecorder, with valid FrameTimingManager CPU render-thread time as fallback.\n" +
                "Seed: 73021. VSync off, uncapped during capture; restored at completion. Animals remain active.\n" +
                "No extra measurement renders. CPU counters include waits; GPU unavailable unless FrameTiming supplies valid values.\n\n" +
                "|Case|View|Repeat|Frames|Frame median ms|Frame P95 ms|Main median ms|Render median ms|GPU median ms|GC median bytes|Draws median|Triangles median|Batches median|SetPass median|Foliage ms|AI ms|Feet ms|HUD ms|Visibility ms|Probe ms|Decode ms|Camera render ms|Present wait ms|Command wait ms|Render wait ms|\n" +
                "|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|\n";
            File.WriteAllText(Path.Combine(directory, "results.md"), metadata);
        }

        private void Disable(Behaviour item) { if (item.enabled) { disabled.Add(item); item.enabled = false; } }

        private IEnumerator Measure()
        {
            float warmup = Seconds("-jwPerfWarmup", "15"), duration = Seconds("-jwPerfSeconds", "60");
            int repeats = Mathf.Max(1, int.Parse(Argument("-jwPerfRepeats", "3")));
            string[] cases = Argument("-jwPerfCases", "normal").Split(',');
            string[] views = Argument("-jwPerfViews", "forest,creek,dinosaur,route").Split(',');
            foreach (string testCase in cases)
            {
                Action restore = Isolate(testCase);
                try
                {
                    foreach (string view in views)
                    for (int repeat = 1; repeat <= repeats; repeat++)
                    {
                        var samples = new List<double>[counters.Length + 2];
                        for (int c = 0; c < samples.Length; c++) samples[c] = new List<double>(8192);
                        var csv = new StringBuilder("frame_ms," + string.Join(",", counters) + ",gpu_ms\n");
                        double start = Time.realtimeSinceStartupAsDouble;
                        int warmupRenders = automaticRenders;
                        while (Time.realtimeSinceStartupAsDouble - start < warmup)
                        { Position(view, 0f); yield return null; }
                        if (automaticRenders == warmupRenders)
                            throw new InvalidOperationException("Main camera did not render automatically. Keep the Windows Player window visible; do not use -batchmode or -nographics.");
                        start = Time.realtimeSinceStartupAsDouble;
                        double last = start;
                        int previousRender = automaticRenders;
                        while (Time.realtimeSinceStartupAsDouble - start < duration)
                        {
                            Position(view, (float)(Time.realtimeSinceStartupAsDouble - start) / duration);
                            yield return null;
                            double now = Time.realtimeSinceStartupAsDouble;
                            // A simulation tick without a main-camera draw is not a rendered frame.
                            if (automaticRenders == previousRender) continue;
                            previousRender = automaticRenders;
                            samples[0].Add((now - last) * 1000); last = now;
                            FrameTimingManager.CaptureFrameTimings();
                            bool hasTiming = FrameTimingManager.GetLatestTimings(1, timing) > 0;
                            for (int c = 0; c < recorders.Count; c++)
                            {
                                double value = recorders[c].Valid && recorders[c].Count > 0 && !(c < 2 && recorders[c].LastValue <= 0)
                                    ? recorders[c].LastValue / (c < 2 || c >= 7 ? 1000000.0 : 1.0) : double.NaN;
                                if (c == 1 && double.IsNaN(value) && hasTiming && timing[0].cpuRenderThreadFrameTime > 0)
                                    value = timing[0].cpuRenderThreadFrameTime;
                                samples[c + 1].Add(value);
                            }
                            samples[samples.Length - 1].Add(hasTiming && timing[0].gpuFrameTime > 0
                                ? timing[0].gpuFrameTime : double.NaN);
                        }
                        // Formatting, file IO and screenshots occur outside the timed sample.
                        int count = samples[0].Count;
                        if (count == 0) throw new InvalidOperationException("No main-camera frames were rendered during the sample.");
                        for (int f = 0; f < count; f++)
                        {
                            for (int c = 0; c < samples.Length; c++)
                            { if (c > 0) csv.Append(','); if (!double.IsNaN(samples[c][f])) csv.Append(samples[c][f].ToString("F4", CultureInfo.InvariantCulture)); }
                            csv.AppendLine();
                        }
                        string name = testCase + "-" + view + "-" + repeat;
                        File.WriteAllText(Path.Combine(directory, name + ".csv"), csv.ToString());
                        var row = new StringBuilder($"|{testCase}|{view}|{repeat}|{count}|{Percentile(samples[0], .5)}|{Percentile(samples[0], .95)}|");
                        foreach (int c in new[] { 1, 2, samples.Length - 1, 3, 4, 5, 6, 7 }) row.Append(Percentile(samples[c], .5)).Append('|');
                        for (int c = 8; c < samples.Length - 1; c++) row.Append(Percentile(samples[c], .5)).Append('|');
                        File.AppendAllText(Path.Combine(directory, "results.md"), row + "\n");
                        File.AppendAllText(Path.Combine(directory, "views.txt"), $"{name}: position={camera.transform.position:F4}, rotation={camera.transform.eulerAngles:F4}\n");
                        var visibility = FindObjectOfType<DinosaurVisibility>();
                        File.AppendAllText(Path.Combine(directory, "diagnostics.txt"), $"{name}: active foliage={FoliageInteractionSystem.Instance?.ActiveTreeCount}, " +
                            $"main-depth captures={visibility?.MainDepthCaptureCount}, probe captures={visibility?.ProbeCaptureCount}, pending={visibility?.IsPending}\n");
                        if (repeat == 1)
                        {
                            // WaitForEndOfFrame is not resumed by the batchmode Editor.
                            // The automatic target already contains the preceding completed frame.
                            yield return null;
                            SaveImage(Path.Combine(directory, name + ".png"));
                        }
                        Debug.Log("JW_PERF_SAMPLE " + name + " " + row);
                    }
                }
                finally { restore(); }
            }
        }

        private Action Isolate(string testCase)
        {
            if (testCase == "normal") return () => { };
            if (testCase == "no-shadows")
            { var previous = QualitySettings.shadows; QualitySettings.shadows = ShadowQuality.Disable; return () => QualitySettings.shadows = previous; }
            if (testCase == "no-finish")
            {
                var finish = camera.GetComponent<RainforestImageFinish>(); bool previous = finish != null && finish.enabled;
                if (finish != null) finish.enabled = false;
                return () => { if (finish != null) finish.enabled = previous; };
            }
            if (testCase == "no-visibility")
            {
                var card = FindObjectOfType<DinosaurInfoCard>(); bool previous = card != null && card.enabled;
                if (card != null) card.enabled = false;
                return () => { if (card != null) card.enabled = previous; };
            }
            if (testCase == "no-interaction")
            {
                var system = FindObjectOfType<FoliageInteractionSystem>(); bool previous = system != null && system.interactionsEnabled;
                if (system != null) system.interactionsEnabled = false;
                return () => { if (system != null) system.interactionsEnabled = previous; };
            }
            throw new ArgumentException("Unknown benchmark case: " + testCase);
        }

        private void Position(string view, float progress)
        {
            Vector3 eye, look;
            switch (view)
            {
                case "forest": eye = forestEye; look = forestLook; break;
                case "creek": eye = creekEye; look = creekLook; break;
                case "dinosaur": eye = dinosaurEye; look = dinosaurLook; break;
                case "route":
                    if (route != null && route.Length > 1)
                    {
                        float index = Mathf.Clamp01(progress) * (route.Length - 1);
                        int segment = Mathf.Min((int)index, route.Length - 2);
                        eye = Vector3.Lerp(route[segment], route[segment + 1], index - segment);
                        look = eye + (route[segment + 1] - route[segment]).normalized * 20;
                    }
                    else { eye = forestEye; look = forestLook; }
                    break;
                default: throw new ArgumentException("Unknown benchmark view: " + view);
            }
            var terrain = Terrain.activeTerrain;
            eye.y = (terrain != null ? terrain.SampleHeight(eye) + terrain.transform.position.y : 0f) + 1.7f;
            if (view == "route") look.y = eye.y;
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
        }

        private void SaveImage(string path)
        {
            var previous = RenderTexture.active;
            var pixels = new Texture2D(output.width, output.height, TextureFormat.RGB24, false);
            try
            { RenderTexture.active = output; pixels.ReadPixels(new Rect(0, 0, output.width, output.height), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG()); }
            finally { RenderTexture.active = previous; Destroy(pixels); }
        }

        private static string Percentile(List<double> values, double fraction)
        {
            values.RemoveAll(double.IsNaN);
            if (values.Count == 0) return "unavailable";
            values.Sort();
            return values[Mathf.Clamp((int)Math.Ceiling(values.Count * fraction) - 1, 0, values.Count - 1)].ToString("F2", CultureInfo.InvariantCulture);
        }

        private void Cleanup()
        {
            Camera.onPostRender -= CameraRendered;
            foreach (var recorder in recorders) recorder.Dispose(); recorders.Clear();
            if (!configured) return;
            configured = false;
            QualitySettings.vSyncCount = savedVsync; Application.targetFrameRate = savedTarget;
            if (camera != null) { camera.targetTexture = previousTarget; camera.transform.SetPositionAndRotation(originalPosition, originalRotation); }
            if (output != null) Destroy(output);
            foreach (var item in disabled) if (item != null) item.enabled = true;
        }
        private void Complete(Exception error)
        {
            Cleanup(); Failed = error != null; Finished = true;
            if (error != null) Debug.LogException(error);
            else Debug.Log("JW_PERF_COMPLETE " + directory);
            if (!Application.isEditor) Application.Quit(Failed ? 1 : 0);
        }
        private void OnDestroy() => Cleanup();
        private void CameraRendered(Camera rendered) { if (rendered == camera) automaticRenders++; }
    }
}
