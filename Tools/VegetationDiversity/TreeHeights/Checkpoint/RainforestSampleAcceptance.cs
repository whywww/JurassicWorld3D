using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Media;
using UnityEngine;
using Unity.Profiling;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class RainforestSampleAcceptance
    {
        private const string Key = "JurassicWorld.RainforestAcceptance";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly int[] Rates = { 30, 60, 144 };
        private static readonly List<double>[] Cpu = Enumerable.Range(0, 6).Select(_ => new List<double>()).ToArray();
        private static readonly List<double>[] Frames = Enumerable.Range(0, 6).Select(_ => new List<double>()).ToArray();
        private static readonly List<double>[] Gpu = Enumerable.Range(0, 6).Select(_ => new List<double>()).ToArray();
        private static readonly List<double>[] Draws = Enumerable.Range(0, 6).Select(_ => new List<double>()).ToArray();
        private static readonly List<double>[] MainCpu = Enumerable.Range(0, 6).Select(_ => new List<double>()).ToArray();
        private static readonly FrameTiming[] Timing = new FrameTiming[1];
        private static readonly StringBuilder Report = new StringBuilder();
        private static DinosaurAgent rex;
        private static DinosaurInteractionSettings settings;
        private static RainforestSampleLayout layout;
        private static DinosaurFootLock feet;
        private static Camera camera;
        private static RenderTexture target;
        private static Bounds[] originalSolids;
        private static GameObject peerObject;
        private static int view, frames, lastFrame, scenario;
        private static float began, lastMoved;
        private static Vector3 previous;
        private static float previousAnimationTravel;
        private static bool sawWait, released;
        private static float walkingBegan;
        private static double deadline;
        private static float maxFootError;
        private static string maxFootContext;
        private static bool footFailure;
        private static int originalVSync, originalFrameRate;
        private static bool performanceOnly;
        private static GameObject performanceRoot;
        private static bool expansionPerformance;
        private static string ArtifactOutput => expansionPerformance ? RainforestExpansion.Output : RainforestSample.Output;
        private static bool recordingOnly;
        private static MediaEncoder encoder;
        private static Texture2D videoFrame;
        private static ProfilerRecorder drawRecorder;
        private static ProfilerRecorder mainThreadRecorder;
        private static object Get(string name) => typeof(DinosaurAgent).GetField(name, Flags).GetValue(rex);
        private static void Set(string name, object value) => typeof(DinosaurAgent).GetField(name, Flags).SetValue(rex, value);
        private static object Call(string name, params object[] args) => typeof(DinosaurAgent).GetMethod(name, Flags).Invoke(rex, args);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static bool WalkingStage()
        {
            string stage = rex.FrontInteractionStage;
            return stage.StartsWith("Follow") || stage.StartsWith("ReturnWalk");
        }
        static RainforestSampleAcceptance()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) EditorApplication.delayCall += Begin;
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + ".Restore", false))
                { SessionState.SetBool(Key + ".Restore", false); EditorSceneManager.OpenScene(CentralForestDensification.ScenePath); }
            };
        }
        [MenuItem("Jurassic World/Acceptance/Verify Rainforest Sample and Natural Interactions")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the scene and stop Play before acceptance.");
            EditorSceneManager.OpenScene(CentralForestDensification.ScenePath);
            Require(Object.FindObjectOfType<RainforestSampleLayout>() != null, "Build the rainforest sample first.");
            SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".Restore", true); EditorApplication.isPlaying = true;
        }
        public static void RunMovementOnly() { SessionState.SetBool(Key + ".MovementOnly", true); Run(); }
        public static void RunPerformanceOnly() { SessionState.SetBool(Key + ".PerformanceOnly", true); Run(); }
        [MenuItem("Jurassic World/Diagnostics/Rainforest Expansion/Compare Performance")]
        public static void RunExpansionPerformance()
        {
            SessionState.SetBool(Key + ".ExpansionPerformance", true); RunPerformanceOnly();
        }
        public static void RunExpansionMovement()
        {
            SessionState.SetBool(Key + ".ExpansionPerformance", true); RunMovementOnly();
        }
        public static void RunRecordingOnly()
        {
            SessionState.SetBool(Key + ".RecordingOnly", true);
            SessionState.SetBool(Key + ".MovementOnly", true); Run();
        }
        private static void Begin()
        {
            try
            {
                layout = Object.FindObjectOfType<RainforestSampleLayout>(); camera = Camera.main;
                expansionPerformance = SessionState.GetBool(Key + ".ExpansionPerformance", false);
                SessionState.SetBool(Key + ".ExpansionPerformance", false);
                performanceRoot = expansionPerformance ? GameObject.Find(RainforestExpansion.RootName) : layout.gameObject;
                Require(performanceRoot != null, "Build the comparison vegetation first.");
                Directory.CreateDirectory(ArtifactOutput);
                originalVSync = QualitySettings.vSyncCount; originalFrameRate = Application.targetFrameRate;
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
                performanceOnly = SessionState.GetBool(Key + ".PerformanceOnly", false);
                SessionState.SetBool(Key + ".PerformanceOnly", false);
                recordingOnly = SessionState.GetBool(Key + ".RecordingOnly", false);
                SessionState.SetBool(Key + ".RecordingOnly", false);
                rex = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex");
                feet = rex.GetComponentInChildren<DinosaurFootLock>();
                settings = Object.FindObjectOfType<DinosaurInteractionSettings>(); originalSolids = settings.solidObstacles;
                foreach (var debug in Object.FindObjectsOfType<MovementCorridorDebug>()) debug.gameObject.SetActive(false);
                foreach (var agent in Object.FindObjectsOfType<DinosaurAgent>()) agent.enabled = false;
                foreach (var behaviour in Object.FindObjectsOfType<MonoBehaviour>())
                    if (behaviour is SmoothExplorerCamera || behaviour is EmotivPlayerInput || behaviour.GetType().Namespace == "Crest") behaviour.enabled = false;
                target = new RenderTexture(recordingOnly ? 640 : 1280, recordingOnly ? 360 : 720, 24); target.Create(); camera.targetTexture = target;
                if (recordingOnly) videoFrame = new Texture2D(640, 360, TextureFormat.RGB24, false);
                foreach (var list in Cpu.Concat(Frames).Concat(Gpu).Concat(Draws).Concat(MainCpu)) list.Clear();
                Report.Clear(); Report.AppendLine("# 雨林样板验收\n");
                Report.AppendLine("受控互动测试：恐龙放在预留林隙，同伴或静态障碍按场景注入；重叠注入期间只检查安全停步与恢复，释放后再检查穿透。此报告不替代 NormalPlay.md 中全部恐龙启用、实际随机出生及自由漫游的逐帧验证。\n");
                footFailure = false;
                VerifyBranchBroadPhase();
                foreach (var animation in Object.FindObjectsOfType<Animation>())
                { foreach (AnimationState state in animation) state.speed = 0f; animation.Sample(); }
                drawRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 1);
                mainThreadRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
                Time.captureDeltaTime = 0f; view = -1; NextView(); lastFrame = -1;
                deadline = EditorApplication.timeSinceStartup + 1200;
                if (SessionState.GetBool(Key + ".MovementOnly", false))
                { SessionState.SetBool(Key + ".MovementOnly", false); BeginMovement(); }
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void NextView()
        {
            view++; frames = 0; performanceRoot.SetActive(view % 2 == 1);
            TerrainTreeObstacleRegistry.Invalidate();
            Vector3[] eyes = { new Vector3(0f, 1.7f, -13f), new Vector3(-6f, 1.7f, 14f), new Vector3(29f, 1.7f, 22f) };
            Vector3[] aims = { new Vector3(-5f, 4f, 18f), new Vector3(9f, 4f, 30f), new Vector3(0f, 4f, 20f) };
            camera.transform.SetPositionAndRotation(eyes[view / 2], Quaternion.LookRotation(aims[view / 2] - eyes[view / 2]));
        }
        private static void VerifyBranchBroadPhase()
        {
            var tree = new GameObject("Hard branch query regression");
            var bodyObject = new GameObject("Hard branch body regression");
            try
            {
                tree.transform.position = new Vector3(1000f, 0f, 1000f);
                var marker = tree.AddComponent<SceneTreeObstacle>(); marker.SetRadius(.1f);
                marker.trunkSections = new[] { new TreeTrunkSection { center = new Vector2(9f, 0f), radius = .5f, bottom = 1f, top = 2f } };
                var body = bodyObject.AddComponent<DinosaurCoreBody>();
                body.bands = new[] { new DinosaurCoreBody.Band { layer = 2, bounds = new Bounds(Vector3.up * 1.25f, Vector3.one * .5f) } };
                TerrainTreeObstacleRegistry.Invalidate();
                Require(TerrainTreeObstacleRegistry.IsCoreBlocked(new Vector2(1009f, 1000f), Vector2.up, body), "Hard branch beyond the trunk's grid bucket was missed.");
                body.bands[0] = new DinosaurCoreBody.Band { layer = 16, bounds = new Bounds(Vector3.up * 8.25f, Vector3.one * .5f) };
                Require(!TerrainTreeObstacleRegistry.IsCoreBlocked(new Vector2(1009f, 1000f), Vector2.up, body), "Height-separated branch blocked the dinosaur.");
                Report.AppendLine("- PASS hard-branch spatial indexing and height separation.");
            }
            finally { Object.DestroyImmediate(tree); Object.DestroyImmediate(bodyObject); TerrainTreeObstacleRegistry.Invalidate(); }
        }
        private static void Tick()
        {
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Rainforest acceptance timed out.");
                if (lastFrame == Time.frameCount) return; lastFrame = Time.frameCount;
                if (view < 6) { TickPerformance(); return; }
                TickScenario();
            }
            catch (Exception error) { Finish(error); }
        }
        private static void TickPerformance()
        {
            var watch = Stopwatch.StartNew(); camera.Render(); watch.Stop();
            FrameTimingManager.CaptureFrameTimings(); frames++;
            int sample = view / 2 + (view % 2) * 3;
            if (frames > 30 && frames <= 110)
            {
                Cpu[sample].Add(watch.Elapsed.TotalMilliseconds); Frames[sample].Add(Time.unscaledDeltaTime * 1000f);
                if (drawRecorder.Valid) Draws[sample].Add(drawRecorder.LastValue);
                if (mainThreadRecorder.Valid && mainThreadRecorder.LastValue > 0) MainCpu[sample].Add(mainThreadRecorder.LastValue * 1e-6);
                if (FrameTimingManager.GetLatestTimings(1, Timing) > 0 && Timing[0].gpuFrameTime > 0) Gpu[sample].Add(Timing[0].gpuFrameTime);
            }
            if (frames == 115) Capture($"{(view % 2 == 0 ? "before" : "after")}-{view / 2}");
            if (frames < 120) return;
            if (view < 5) { NextView(); return; }
            WritePerformance();
            if (performanceOnly) { Finish(null); return; }
            BeginMovement();
        }
        private static void BeginMovement()
        {
            view = 6; layout.gameObject.SetActive(true); performanceRoot.SetActive(true);
            camera.enabled = false;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            rex.GetComponentInChildren<Animation>().cullingType = AnimationCullingType.AlwaysAnimate;
            TerrainTreeObstacleRegistry.Invalidate(); scenario = 0; PrepareScenario();
        }
        private static double Median(IEnumerable<double> values)
        { var sorted = values.OrderBy(v => v).ToArray(); return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2]; }
        private static void WritePerformance()
        {
            var report = new StringBuilder("# 雨林样板 Play 性能\n\n同一Main场景，1280×720，三个固定机位；基线仅关闭本次新增植被。恐龙固定为相同姿态；每机位预热30帧、测量80帧，截图在计时后单独采集。CPU为Camera.Render提交耗时；帧间隔包含编辑器与额外Render调用，不能当发布版FPS。\n\n|机位|基线CPU ms|样板CPU ms|变化|基线帧间隔ms|样板帧间隔ms|基线绘制|样板绘制|\n|---|---:|---:|---:|---:|---:|---:|---:|\n");
            for (int i = 0; i < 3; i++) report.AppendLine($"|{i}|{Median(Cpu[i]):F3}|{Median(Cpu[i+3]):F3}|{Median(Cpu[i+3])/Median(Cpu[i])-1:P1}|{Median(Frames[i]):F3}|{Median(Frames[i+3]):F3}|{Median(Draws[i]):F0}|{Median(Draws[i+3]):F0}|");
            double change = Median(Cpu.Skip(3).SelectMany(v => v)) / Median(Cpu.Take(3).SelectMany(v => v)) - 1;
            report.AppendLine($"\n合并CPU渲染提交中位增幅：{change:P1}（仅供提交开销比较，不作为整个CPU帧耗的替代）。\n");
            if (MainCpu.All(v => v.Count > 0))
            {
                report.AppendLine("|机位|基线CPU主线程 ms|样板CPU主线程 ms|变化|\n|---|---:|---:|---:|");
                for (int i = 0; i < 3; i++) report.AppendLine($"|{i}|{Median(MainCpu[i]):F3}|{Median(MainCpu[i+3]):F3}|{Median(MainCpu[i+3])/Median(MainCpu[i])-1:P1}|");
                double mainChange = Median(MainCpu.Skip(3).SelectMany(v => v)) / Median(MainCpu.Take(3).SelectMany(v => v)) - 1;
                report.AppendLine($"\n同条件编辑器CPU主线程帧耗合并中位增幅：{mainChange:P1}；15%目标：{(mainChange <= .15 ? "PASS" : "FAIL，需要优化后复测")}。主线程包含编辑器及额外相机渲染，恐龙姿态固定；不代表发布版动态漫游性能。\n");
            }
            else report.AppendLine("CPU主线程帧计时不可用，15%整帧目标未验证；不以渲染提交计时代替。\n");
            report.AppendLine(Gpu.Any(v => v.Count > 0) ? $"GPU基线/样板中位：{Median(Gpu.Take(3).SelectMany(v => v)):F3}/{Median(Gpu.Skip(3).SelectMany(v => v)):F3}ms。" : "GPU计时不可用，未推算GPU性能。");
            if (expansionPerformance) report.Insert(0, "# 相邻圈扩展性能对照\n\n基线保留已认可样板及所有原植被；只切换本轮扩展根对象。下方“样板”指本轮扩展。\n\n");
            File.WriteAllText(ArtifactOutput + "/Performance.md", report.ToString());
        }
        private static void PrepareScenario()
        {
            if ((bool)Get("frontActive")) Call("FinishFrontInteraction");
            settings.solidObstacles = originalSolids;
            if (peerObject != null) Object.DestroyImmediate(peerObject);
            Set("peers", new[] { rex }); Set("frontTestControlled", true); Set("frontTestWaiting", true);
            Call("ClearSteppingTurn");
            Vector3 rootPosition = layout.clearingPositions[0]; rootPosition.y = rex.MovementHeight;
            rex.transform.SetPositionAndRotation(rootPosition, layout.clearingRotations[0]);
            var animation = rex.GetComponentInChildren<Animation>();
            animation.Stop(); animation.Play((string)Get("idleClip")); animation[(string)Get("idleClip")].time = 0f; animation.Sample();
            feet?.ResetSupportAnchors(); rex.GetComponent<DinosaurFoliageInteractor>()?.ResetHistory();
            Vector3 forward = rex.transform.rotation * Quaternion.Euler(0f, rex.VisualYaw, 0f) * Vector3.forward;
            camera.transform.position = rex.transform.position + forward * 24f + Vector3.up * 1.7f;
            camera.transform.LookAt(rex.transform.position + Vector3.up * 2f);
            if (recordingOnly)
            {
                Vector3 direction = Quaternion.Euler(0f, scenario == 1 ? -35f : scenario == 2 ? 35f : 0f, 0f) * forward;
                camera.transform.position = rex.transform.position + direction * 24f + Vector3.up * 1.7f;
                camera.transform.LookAt(rex.transform.position + Vector3.up * 2.5f);
                encoder = new MediaEncoder(ArtifactOutput + "/interaction-" + new[] { "front", "left", "right" }[scenario] + ".mp4",
                    new VideoTrackAttributes { width = 640, height = 360, frameRate = new MediaRational(30), includeAlpha = false, bitRateMode = VideoBitrateMode.High });
            }
            Time.captureDeltaTime = 1f / Rates[scenario / 8];
            Call("BeginInteractionSequence", scenario % 8 == 1 || scenario % 8 == 2, scenario % 8 == 1);
            // Direct fixtures start the sequence themselves; leaving the trigger armed skips
            // the normal update path (including the walk clip's loop window).
            Set("frontTestWaiting", false);
            Set("frontFollowDuration", 2f); Set("frontReturnWalkTime", 2f);
            int test = scenario % 8;
            if (test >= 3)
            {
                if (test == 3) Set("frontActionIndex", 2);
                if (test == 4 || test == 7) Call("BeginFrontEnding");
                if (test == 5 || test == 6) Call("BeginFrontReturn");
                if (test == 6)
                {
                    Set("frontStage", Enum.Parse(typeof(DinosaurAgent).GetNestedType("FrontStage", BindingFlags.NonPublic), "ReturnWalk"));
                    Set("frontReturnIndex", -1);
                }
                if (test == 7)
                {
                    peerObject = new GameObject("Temporary raptor space reservation");
                    peerObject.transform.position = rex.transform.position;
                    var body = peerObject.AddComponent<DinosaurCoreBody>();
                    body.bands = new[] { new DinosaurCoreBody.Band { layer = 4, bounds = new Bounds(Vector3.up * 2f, new Vector3(20f, 4f, 20f)) } };
                    var peer = peerObject.AddComponent<DinosaurAgent>(); peer.enabled = false;
                    typeof(DinosaurAgent).GetField("interactionBody", Flags).SetValue(peer, body);
                    Set("peers", new[] { rex, peer });
                }
                else settings.solidObstacles = originalSolids.Concat(new[] { new Bounds(rex.transform.position + Vector3.up * 2f, new Vector3(2f, 4f, 2f)) }).ToArray();
            }
            rex.enabled = true; began = lastMoved = Time.time; previous = rex.transform.position;
            previousAnimationTravel = rex.AnimationRootTravel;
            maxFootError = 0f; maxFootContext = ""; sawWait = released = false; walkingBegan = -1f;
            UnityEngine.Random.InitState(731716 + scenario);
        }
        private static void TickScenario()
        {
            if (recordingOnly)
            {
                camera.Render(); var old = RenderTexture.active; RenderTexture.active = target;
                videoFrame.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); videoFrame.Apply(); RenderTexture.active = old;
                encoder.AddFrame(videoFrame);
            }
            float elapsed = Time.time - began; int test = scenario % 8;
            float distance = Vector3.Distance(previous, rex.transform.position);
            float authored = rex.AnimationRootTravel - previousAnimationTravel;
            float limit = authored > 0f ? authored + Mathf.Min(2.4f, Mathf.Max(rex.WalkSpeed, rex.ApproachSpeed) * .8f)
                * Mathf.Min(Time.deltaTime, .05f) + .01f : Mathf.Max(.4f, 12f * Time.deltaTime);
            Require(distance <= limit, $"Dinosaur root moved outside sampled motion {distance:F3}m, allowed={limit:F3}m at dt={Time.deltaTime:F4}, case={scenario}, {rex.FrontInteractionStage}, {rex.CurrentAnimationSourceName}.");
            previousAnimationTravel = rex.AnimationRootTravel;
            if (distance > .001f) lastMoved = Time.time;
            previous = rex.transform.position;
            sawWait |= rex.FrontActionWaitingForSpace;
            if (feet != null && rex.IsFrontInteractionActive)
            {
                float error = Mathf.Max(feet.LeftPlanted ? feet.LeftPlantError : 0f, feet.RightPlanted ? feet.RightPlantError : 0f);
                if (error > maxFootError)
                {
                    maxFootError = error;
                    maxFootContext = $"{rex.FrontInteractionStage}/{rex.CurrentAnimationSourceName} at {elapsed:F3}s, state={rex.CurrentState}, handoff={feet.ReturnHandoffActive}, clip={rex.FrontActionPlaybackTime:F3}s, left={(feet.LeftSolePosition-feet.LeftAnchor):F3}, right={(feet.RightSolePosition-feet.RightAnchor):F3}";
                }
            }
            if (test >= 3 && !released)
            {
                if (test == 7)
                {
                    if (elapsed >= 4f)
                    {
                        Require(sawWait, "Blocked peer scenario never requested clearance.");
                        Require(rex.IsFrontInteractionActive, "Peer wait was treated as a static failure.");
                        settings.solidObstacles = originalSolids;
                        Object.DestroyImmediate(peerObject); peerObject = null;
                        Set("peers", new[] { rex }); released = true;
                    }
                }
                else
                {
                    if (walkingBegan < 0f && WalkingStage())
                    {
                        walkingBegan = elapsed;
                        // A performance can carry the actor away from the start-point wall.
                        // Put the same test wall at the actual walking pose instead.
                        if (test == 3)
                        {
                            Vector3 forward = rex.transform.rotation * Quaternion.Euler(0f, rex.VisualYaw, 0f) * Vector3.forward;
                            settings.solidObstacles = originalSolids.Concat(new[]
                            {
                                new Bounds(rex.transform.position + forward * .5f + Vector3.up * 2f, new Vector3(3f, 4f, 3f))
                            }).ToArray();
                        }
                    }
                    // Performances may clip the injected wall; once real walking starts it must
                    // wait instead of stepping through the solid obstacle.
                    if (walkingBegan >= 0f && elapsed - walkingBegan >= 1f)
                    {
                        Require(sawWait, "Blocked walking scenario never requested clearance: " + rex.FrontInteractionStage
                            + "/" + rex.CurrentAnimationSourceName + ", walking=" + (elapsed - walkingBegan).ToString("F2")
                            + "s, block=" + rex.FrontBlockingReason);
                        settings.solidObstacles = originalSolids; released = true;
                    }
                    Require(elapsed < 20f, "Blocked scenario never reached checked walking: " + rex.FrontInteractionStage);
                }
            }
            if (test < 3 || released)
            {
                var body = Get("interactionBody") as DinosaurCoreBody;
                if (body != null && WalkingStage())
                {
                    Vector3 forward = rex.transform.rotation * Quaternion.Euler(0f, rex.VisualYaw, 0f) * Vector3.forward;
                    Require(!settings.PoseBlocked(new Vector2(rex.transform.position.x, rex.transform.position.z), new Vector2(forward.x, forward.z), body),
                        "Walking body intersected a hard obstacle: " + rex.FrontInteractionStage);
                }
                else if (body != null && test != 7)
                {
                    // Special animations may clip static props but still cannot enter another dinosaur.
                    var peerGate = typeof(DinosaurAgent).GetMethod("FrontPeerPoseBlocked", Flags, null,
                        new[] { typeof(Vector3), typeof(Quaternion), typeof(DinosaurCoreBody) }, null);
                    Require(!(bool)peerGate.Invoke(rex, new object[] { rex.transform.position, rex.transform.rotation, body }),
                        "Special animation intersected another dinosaur: " + rex.FrontInteractionStage);
                }
            }
            if (!rex.IsFrontInteractionActive && elapsed > 1f)
            {
                footFailure |= maxFootError > .25f;
                Report.AppendLine($"- {(maxFootError <= .25f ? "PASS" : "FAIL foot support")} {Rates[scenario/8]} FPS case {test}: {elapsed:F2}s, maximum interaction support error {maxFootError:F4}m ({maxFootContext}); no teleport, walking collision checked, performances may clip static props.");
                if (encoder != null) { encoder.Dispose(); encoder = null; }
                File.WriteAllText(ArtifactOutput + (recordingOnly ? "/Recording.md" : "/Acceptance.md"), "RUNNING\n\n" + Report);
                if (++scenario < (recordingOnly ? 3 : 24)) { PrepareScenario(); return; }
                Finish(footFailure ? new InvalidOperationException("Support-foot errors exceeded 0.25m; see individual cases.") : null); return;
            }
            Require(elapsed < 65f, "Interaction did not finish after the blocker cleared: " + rex.FrontInteractionStage + "/" + rex.FrontBlockingReason);
            if (Time.time - lastMoved > 2f && rex.FrontInteractionStage.StartsWith("Follow"))
                Require(rex.FrontActionWaitingForSpace || (float)Get("frontFollowElapsed") <= 2.1f, "Continuous walking in place.");
            if (!recordingOnly && frames++ % (Rates[scenario / 8] * 2) == 0 && scenario < 3) Capture($"motion-{scenario}-{Mathf.RoundToInt(elapsed)}");
        }
        private static void Capture(string name)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                File.WriteAllBytes(ArtifactOutput + "/" + name + ".png", image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            QualitySettings.vSyncCount = originalVSync; Application.targetFrameRate = originalFrameRate;
            if (drawRecorder.Valid) drawRecorder.Dispose();
            if (mainThreadRecorder.Valid) mainThreadRecorder.Dispose();
            if (encoder != null) { encoder.Dispose(); encoder = null; }
            if (videoFrame != null) Object.DestroyImmediate(videoFrame);
            if (settings != null) settings.solidObstacles = originalSolids;
            if (peerObject != null) Object.DestroyImmediate(peerObject);
            if (target != null) { if (camera != null) camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); }
            string result = error == null ? "PASS RAINFOREST_NATURAL_INTERACTION_OK" : "FAILED " + error;
            if (!performanceOnly) File.WriteAllText(ArtifactOutput + (recordingOnly ? "/Recording.md" : "/Acceptance.md"), result + "\n\n" + Report);
            File.WriteAllText("Logs/rainforest-status.txt", result);
            if (error == null) Debug.Log(result); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}
