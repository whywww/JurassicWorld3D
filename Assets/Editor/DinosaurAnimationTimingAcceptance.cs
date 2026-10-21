using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Media;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    // Isolated motion timing in Play, with the production pose checks and foot solver.
    // No automatic behaviour selection: advance one selected action per simulated frame.
    [InitializeOnLoad]
    public static class DinosaurAnimationTimingAcceptance
    {
        private const string Key = "JurassicWorld.AnimationTiming";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly int[] Rates = { 30, 60, 144 };
        private static readonly InteractionMotionRole[] Roles = { InteractionMotionRole.TurnLeft,
            InteractionMotionRole.TurnRight, InteractionMotionRole.BiteCloseLeft, InteractionMotionRole.BiteCloseRight };
        private static DinosaurAgent rex;
        private static DinosaurFootLock feet;
        private static DinosaurInteractionProfile.Motion motion;
        private static RainforestSampleLayout layout;
        private static DinosaurInteractionSettings settings;
        private static Camera camera;
        private static RenderTexture target;
        private static Texture2D frame;
        private static MediaEncoder encoder;
        private static int scenario, lastFrame, ticks;
        private static float expected, error, elapsed;
        private static float previousTravel;
        private static Vector3 previousRoot;
        private static bool turn;
        private static double deadline;
        private static readonly StringBuilder Report = new StringBuilder();
        private static object Get(string name) => typeof(DinosaurAgent).GetField(name, Flags).GetValue(rex);
        private static void Set(string name, object value) => typeof(DinosaurAgent).GetField(name, Flags).SetValue(rex, value);
        private static object Call(string name, params object[] args) => typeof(DinosaurAgent).GetMethod(name, Flags).Invoke(rex, args);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static DinosaurAnimationTimingAcceptance()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                    EditorApplication.delayCall += Begin;
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + ".Restore", false))
                { SessionState.SetBool(Key + ".Restore", false); EditorSceneManager.OpenScene(CentralForestDensification.ScenePath); }
            };
        }
        [MenuItem("Jurassic World/Acceptance/Verify Animation Timing")]
        public static void Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorSceneManager.GetActiveScene().isDirty,
                "Stop Play and save the scene before timing acceptance.");
            EditorSceneManager.OpenScene(CentralForestDensification.ScenePath);
            SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".Restore", true);
            EditorApplication.isPlaying = true;
        }
        public static void RepairAndRun() { TRexTurnCalibration.RepairLegacyRootContinuity(); Run(); }
        private static void Begin()
        {
            try
            {
                rex = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex");
                feet = rex.GetComponentInChildren<DinosaurFootLock>();
                foreach (var agent in Object.FindObjectsOfType<DinosaurAgent>()) agent.enabled = false;
                // Invoke the solver explicitly after the accepted animation sample, exactly once.
                feet.enabled = false;
                foreach (var behaviour in Object.FindObjectsOfType<MonoBehaviour>())
                    if (behaviour is SmoothExplorerCamera || behaviour is EmotivPlayerInput || behaviour.GetType().Namespace == "Crest") behaviour.enabled = false;
                foreach (var debug in Object.FindObjectsOfType<MovementCorridorDebug>()) debug.gameObject.SetActive(false);
                settings = Object.FindObjectOfType<DinosaurInteractionSettings>();
                layout = Object.FindObjectOfType<RainforestSampleLayout>(); camera = Camera.main; camera.enabled = false;
                target = new RenderTexture(640, 360, 24); target.Create(); camera.targetTexture = target;
                frame = new Texture2D(640, 360, TextureFormat.RGBA32, false);
                Report.Clear(); Report.AppendLine("# Animation timing after");
                Report.AppendLine("\nControlled Main clearing; original collision checks, sampled root motion and foot IK. Simulated FPS, not hardware performance.\n");
                Report.AppendLine("|FPS|Action|Expected s|Actual s|Max planted-foot error m|Result|\n|---|---|---:|---:|---:|---|");
                scenario = 0; lastFrame = -1; Prepare(); EditorApplication.update += Tick;
            }
            catch (Exception e) { Finish(e); }
        }
        private static void Prepare()
        {
            if ((bool)Get("frontActive")) Call("FinishFrontInteraction");
            Call("ClearSteppingTurn"); Set("peers", new[] { rex });
            Set("frontTestControlled", true); Set("frontTestWaiting", true);
            var profile = (DinosaurInteractionProfile)Get("interactionProfile");
            motion = profile.Find(Roles[scenario % Roles.Length]); turn = scenario % Roles.Length < 2;
            Time.timeScale = 1f; Time.captureDeltaTime = 1f / Rates[scenario / Roles.Length];
            bool started = false;
            for (int place = 0; place < layout.clearingPositions.Length && !started; place++)
            {
                Vector3 p = layout.clearingPositions[place]; p.y = rex.MovementHeight;
                rex.transform.SetPositionAndRotation(p, layout.clearingRotations[place]);
                var animation = rex.GetComponentInChildren<Animation>(); animation.Stop();
                animation.Play((string)Get("idleClip")); animation[(string)Get("idleClip")].time = 0f; animation.Sample();
                feet.ResetSupportAnchors();
                Call("BeginInteractionSequence", false, false);
                Set("frontAction", null); Set("frontWaiting", false);
                if (turn)
                {
                    var use = Enum.Parse(typeof(DinosaurAgent).GetNestedType("TurnUse", BindingFlags.NonPublic), "FrontReturn");
                    started = (bool)Call("TryBeginSteppingTurn", scenario % Roles.Length == 0 ? -90f : 90f, use);
                }
                else started = (bool)Call("StartFrontAction", motion.role);
            }
            Require(started, "No safe clearing for " + motion.sourceName + ": " + rex.FrontBlockingReason);
            expected = turn ? (float)Get("stepTurnEnd") / motion.turnPlaybackSpeed : motion.clip.length;
            ticks = 0; elapsed = error = 0f; deadline = EditorApplication.timeSinceStartup + 240;
            previousTravel = rex.AnimationRootTravel; previousRoot = rex.transform.position;
            Vector3 forward = rex.transform.rotation * Quaternion.Euler(0f, rex.VisualYaw, 0f) * Vector3.forward;
            camera.transform.position = rex.transform.position + Quaternion.Euler(0f, 35f, 0f) * forward * 21f + Vector3.up * 3f;
            camera.transform.LookAt(rex.transform.position + Vector3.up * 2.5f);
            if (Rates[scenario / Roles.Length] == 60 && (turn || motion.role == InteractionMotionRole.BiteCloseRight))
                encoder = new MediaEncoder(RainforestSample.Output + "/timing-after-" + motion.role + ".mp4",
                    new VideoTrackAttributes { width = 640, height = 360, frameRate = new MediaRational(60), includeAlpha = false, bitRateMode = VideoBitrateMode.High });
        }
        private static void Tick()
        {
            if (!EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Timing test wall timeout.");
                bool done = (bool)Call(turn ? "TickSteppingTurn" : "TickFrontAction");
                ticks++; elapsed += Time.deltaTime;
                typeof(DinosaurFootLock).GetMethod("LateUpdate", Flags).Invoke(feet, null);
                float allowed = rex.AnimationRootTravel - previousTravel
                    + Mathf.Min(2.4f, Mathf.Max(rex.WalkSpeed, rex.ApproachSpeed) * .8f) * Mathf.Min(Time.deltaTime, .05f) + .001f;
                Require(Vector3.Distance(previousRoot, rex.transform.position) <= allowed, "Root moved beyond its sampled track and foot correction budget.");
                previousTravel = rex.AnimationRootTravel; previousRoot = rex.transform.position;
                error = Mathf.Max(error, feet.LeftPlanted ? feet.LeftPlantError : 0f, feet.RightPlanted ? feet.RightPlantError : 0f);
                Require(!rex.FrontActionWaitingForSpace, "Unobstructed timing action entered space wait: " + rex.FrontBlockingReason);
                var body = (DinosaurCoreBody)Get("interactionBody");
                Vector3 forward = rex.transform.rotation * Quaternion.Euler(0f, rex.VisualYaw, 0f) * Vector3.forward;
                Require(!settings.PoseBlocked(new Vector2(rex.transform.position.x, rex.transform.position.z), new Vector2(forward.x, forward.z), body), "Timing action penetrated hard obstacle.");
                if (encoder != null)
                {
                    camera.Render(); var old = RenderTexture.active; RenderTexture.active = target;
                    frame.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); frame.Apply(); RenderTexture.active = old; encoder.AddFrame(frame);
                    if (ticks == Rates[scenario / Roles.Length]) File.WriteAllBytes(RainforestSample.Output + "/timing-after-" + motion.role + ".png", frame.EncodeToPNG());
                }
                Require(elapsed < expected * 20f + 10f, "Animation never completed: " + motion.sourceName);
                if (!done) return;
                bool correct = Mathf.Abs(elapsed - expected) <= 2f / Rates[scenario / Roles.Length] + .0001f;
                Report.AppendLine($"|{Rates[scenario / Roles.Length]}|{motion.sourceName}|{expected:F4}|{elapsed:F4}|{error:F4}|{(correct ? "PASS" : "SLOW")}|");
                Debug.Log($"ANIMATION_TIMING {motion.sourceName} fps={Rates[scenario / Roles.Length]} expected={expected:F4} actual={elapsed:F4} support={error:F4}");
                encoder?.Dispose(); encoder = null;
                Require(correct, "Animation duration differs from authored timing.");
                Require(error <= .25f, "Planted foot error exceeded existing 0.25m limit.");
                File.WriteAllText(RainforestSample.Output + "/AnimationTimingAfter.md", "RUNNING\n\n" + Report);
                if (++scenario < Rates.Length * Roles.Length) Prepare(); else Finish(null);
            }
            catch (Exception e) { Finish(e); }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false);
            encoder?.Dispose(); encoder = null; Time.captureDeltaTime = 0f; Time.timeScale = 1f;
            if (target != null) { if (camera != null) camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); }
            if (frame != null) Object.DestroyImmediate(frame);
            string result = error == null ? "PASS ANIMATION_TIMING" : "FAILED " + error;
            File.WriteAllText(RainforestSample.Output + "/AnimationTimingAfter.md", result + "\n\n" + Report);
            File.WriteAllText("Logs/rainforest-status.txt", result);
            if (error == null) Debug.Log(result); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
