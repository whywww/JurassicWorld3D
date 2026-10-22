using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static partial class BaurusuchusContinuousAcceptance
    {
        private const string Key = "JurassicWorld.BaurusuchusContinuous144";
        private static DinosaurAgent live;
        private static DinosaurFootLock liveFeet;
        private static DinosaurInteractionSettings settings;
        private static Bounds[] originalSolids;
        private static Vector3 previousPosition, previousGoal;
        private static Quaternion turnStart;
        private static float began, travel, error, blockedTravel, blockedError, resumeTime = -1f;
        private static int frame, stage, handoffs, idleFrames, stoppedFrames;
        private static double deadline;
        private static bool addedWalls, sawTurning;
        private static int oldVsync, oldFrameRate;
        private static readonly StringBuilder MainReport = new StringBuilder();
        static BaurusuchusContinuousAcceptance()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                    EditorApplication.delayCall += BeginMain;
            };
        }
        [MenuItem("Jurassic World/Acceptance/Verify Baurusuchus Continuous Walk in Main (144 FPS)")]
        public static void RunMain()
        {
            Directory.CreateDirectory(Output);
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorSceneManager.GetActiveScene().isDirty,
                "Stop Play and save scene edits first.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Object.FindObjectOfType<DinosaurInteractionSettings>().spawnSeed = 20261022;
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }
        private static void BeginMain()
        {
            try
            {
                live = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Baurusuchus");
                settings = Object.FindObjectOfType<DinosaurInteractionSettings>(); originalSolids = settings.solidObstacles;
                foreach (var agent in Object.FindObjectsOfType<DinosaurAgent>())
                    if (agent != live) { agent.enabled = false; var solver = agent.GetComponent<DinosaurFootLock>(); if (solver != null) solver.enabled = false; }
                foreach (var behaviour in Object.FindObjectsOfType<MonoBehaviour>())
                    if (behaviour is EmotivPlayerInput || behaviour is SmoothExplorerCamera) behaviour.enabled = false;
                foreach (var camera in Object.FindObjectsOfType<Camera>()) camera.enabled = false;
                liveFeet = live.GetComponent<DinosaurFootLock>();
                Require(liveFeet != null && liveFeet.GaitFootCount == 4, "Four support feet did not bind.");
                Require(Mathf.Abs(live.GaitProfile.clip.length - BaurusuchusWalkingClip.Duration) < .001f, "Full gesture clip still bound.");
                oldVsync = QualitySettings.vSyncCount; oldFrameRate = Application.targetFrameRate;
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
                Time.captureDeltaTime = Dt; began = Time.time; frame = Time.frameCount; stage = 0;
                travel = error = blockedTravel = blockedError = 0f; handoffs = idleFrames = stoppedFrames = 0;
                // Start with a short valid forward goal so handoff is exercised within ten seconds,
                // independently of the random 12..30m goal selected on the first game update.
                Vector3 initialGoal = live.transform.position + live.transform.rotation * Quaternion.Euler(0f, live.VisualYaw, 0f) * Vector3.forward * 4.5f;
                Set(live, "curiosityCorners", new[] { initialGoal }); Set(live, "curiosityCorner", 0);
                Set(live, "hasCuriosityPath", true); Set(live, "freeGoal", initialGoal);
                Set(live, "continuousProbeRemaining", 0f); Set(live, "continuousGoalRetry", 0f);
                previousPosition = live.transform.position; previousGoal = initialGoal;
                deadline = EditorApplication.timeSinceStartup + 600;
                MainReport.Clear(); MainReport.AppendLine("# Baurusuchus Main runtime / 144 FPS only\n\nProduction controller, navigation, body sweeps, static peers and safety distances remain enabled. Other animal AI is held still for reproducibility.\n");
                File.WriteAllText(Output + "/Main144.md", "RUNNING\n\n" + MainReport);
                EditorApplication.update += TickMain;
            }
            catch (Exception e) { FinishMain(e); }
        }
        private static void TickMain()
        {
            if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
            frame = Time.frameCount;
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Main runtime test timed out.");
                float elapsed = Time.time - began;
                float distance = Vector3.Distance(previousPosition, live.transform.position);
                previousPosition = live.transform.position; travel += distance;
                error = Mathf.Max(error, liveFeet.GaitMaximumReachError);
                Require(liveFeet.GaitMaximumReachError <= .02f, $"Main planted error {liveFeet.GaitMaximumReachError:F5}m");
                if (elapsed > .25f && live.CurrentState == DinosaurAgent.DinosaurState.Idle) idleFrames++;
                Vector3 goal = (Vector3)Get(live, "freeGoal");
                if ((goal - previousGoal).sqrMagnitude > .01f) { handoffs++; previousGoal = goal; }
                if (live.GaitMotionActive) Require(distance > .00001f, "In-place stepping entered continuous walking.");
                if (stage == 0 && elapsed >= 10f)
                {
                    MainReport.AppendLine($"Normal roaming: travel={travel:F3}m, goal handoffs={handoffs}, idle frames={idleFrames}.");
                    Require(handoffs > 0 && travel > 2f && idleFrames == 0, "Normal roaming did not hand off without an idle rest.");
                    bool turn = false;
                    foreach (float sign in new[] { 1f, -1f })
                    {
                        object[] args = { live.GaitProfile.turnSpeed * sign, 2f, Vector3.zero, Quaternion.identity };
                        if (!(bool)Call(live, "WalkingArcSafe", args)) continue;
                        Vector3 end = (Vector3)args[2]; Quaternion rotation = (Quaternion)args[3];
                        Vector3 target = end + rotation * Quaternion.Euler(0f, live.VisualYaw, 0f) * Vector3.forward * 3f;
                        Set(live, "curiosityCorners", new[] { target }); Set(live, "curiosityCorner", 0);
                        Set(live, "hasCuriosityPath", true); Set(live, "freeGoal", target);
                        Set(live, "continuousProbeRemaining", 0f); Set(live, "continuousGoalRetry", 10f);
                        turnStart = live.transform.rotation; turn = true; break;
                    }
                    Require(turn, "No clear forward-turning arc at the main fixture position.");
                    stage = 1; began = Time.time;
                }
                else if (stage == 1)
                {
                    if (distance > .00001f && Quaternion.Angle(turnStart, live.transform.rotation) >= 5f) sawTurning = true;
                    if (elapsed < 3f) return;
                    Require(sawTurning, "Main turn did not keep moving forward.");
                    MainReport.AppendLine("Curved turn: forward motion retained while heading changed by at least 5 degrees.");
                    AddBlockingWalls(); stage = 2; began = Time.time;
                }
                else if (stage == 2)
                {
                    blockedTravel += distance;
                    if (distance <= .00001f) stoppedFrames++;
                    blockedError = Mathf.Max(blockedError, liveFeet.GaitMaximumReachError);
                    if (elapsed < 3f) return;
                    Require(stoppedFrames > 100 && blockedTravel < .5f, "Enclosed animal did not wait safely.");
                    MainReport.AppendLine($"Blocked enclosure: travel={blockedTravel:F3}m, stationary frames={stoppedFrames}, no idle rest state.");
                    settings.solidObstacles = originalSolids; addedWalls = false;
                    Set(live, "continuousProbeRemaining", 0f); Set(live, "continuousGoalRetry", 0f);
                    stage = 3; began = Time.time;
                }
                else if (stage == 3)
                {
                    if (distance > .00001f && resumeTime < 0f) resumeTime = elapsed;
                    if (elapsed < 5f) return;
                    Require(resumeTime >= 0f && resumeTime <= 1f, "Walking did not resume promptly after unblocking.");
                    Require(idleFrames == 0, "An active idle rest was introduced.");
                    MainReport.AppendLine($"Unblocked: forward movement resumed after {resumeTime:F4}s. Total travel={travel:F3}m; maximum planted error={error:F5}m.");
                    FinishMain(null);
                }
            }
            catch (Exception e) { FinishMain(e); }
        }
        private static void AddBlockingWalls()
        {
            var body = (DinosaurCoreBody)Get(live, "interactionBody");
            Bounds bounds = default; bool first = true;
            Quaternion rotation = live.transform.rotation * Quaternion.Euler(0f, live.VisualYaw, 0f);
            foreach (var band in body.bands)
                foreach (float x in new[] { band.bounds.min.x, band.bounds.max.x })
                    foreach (float z in new[] { band.bounds.min.z, band.bounds.max.z })
                    {
                        Vector3 p = live.transform.position + rotation * new Vector3(x, 0f, z);
                        if (first) { bounds = new Bounds(p, Vector3.zero); first = false; } else bounds.Encapsulate(p);
                    }
            var walls = new[] {
                new Bounds(new Vector3(bounds.min.x - .4f, 2f, bounds.center.z), new Vector3(.2f, 6f, bounds.size.z + 3f)),
                new Bounds(new Vector3(bounds.max.x + .4f, 2f, bounds.center.z), new Vector3(.2f, 6f, bounds.size.z + 3f)),
                new Bounds(new Vector3(bounds.center.x, 2f, bounds.min.z - .4f), new Vector3(bounds.size.x + 3f, 6f, .2f)),
                new Bounds(new Vector3(bounds.center.x, 2f, bounds.max.z + .4f), new Vector3(bounds.size.x + 3f, 6f, .2f)) };
            settings.solidObstacles = originalSolids.Concat(walls).ToArray(); addedWalls = true;
            Set(live, "continuousProbeRemaining", 0f);
        }
        private static void FinishMain(Exception error)
        {
            EditorApplication.update -= TickMain; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            if (addedWalls && settings != null) settings.solidObstacles = originalSolids;
            QualitySettings.vSyncCount = oldVsync; Application.targetFrameRate = oldFrameRate;
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/Main144.md", MainReport + (error == null ? "\nPASS MAIN_CONTINUOUS_144\n" : "\nFAILED " + error));
            if (error == null) Debug.Log("PASS MAIN_CONTINUOUS_144"); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else { EditorApplication.isPlaying = false; EditorApplication.delayCall += () => EditorSceneManager.OpenScene("Assets/Scenes/Main.unity"); }
        }
    }
}
