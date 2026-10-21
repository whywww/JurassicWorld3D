using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Profiling;

namespace JurassicWorld.Editor
{
    // Replaces the old exit-planner benchmark: there is no planner. This measures the
    // recorded trail and the reverse return so periodic exit searches cannot creep back in.
    [InitializeOnLoad]
    public static class TRexExitPlannerPerformance
    {
        private const string Key = "JurassicWorld.ExitPerformance";
        private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
        private static DinosaurAgent rex;
        private static Camera camera;
        private static float began, followAt, endingAt, maxReturnWalked;
        private static int lastFrame, originalVsync, originalRate;
        private static bool sawReturn, sawHistoryStart;
        private static int trailPoints, queriesAtFollow;
        private static DinosaurInteractionProfile.Motion lastTurnMotion;
        private static float lastTurnCheckAt, lastTurnTime;
        private static int turnCount;
        private static ProfilerRecorder gcRecorder;
        private static readonly List<float> FrameGc = new List<float>();
        static TRexExitPlannerPerformance()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) EditorApplication.delayCall += Begin;
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + ".Restore", false))
                { SessionState.SetBool(Key + ".Restore", false); EditorSceneManager.OpenScene("Assets/Scenes/Main.unity"); }
            };
        }
        [MenuItem("Jurassic World/Acceptance/Measure Exit Planner Cost")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save before profiling.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".Restore", true); EditorApplication.isPlaying = true;
        }
        private static object Call(string name, params object[] args) => typeof(DinosaurAgent).GetMethod(name, Flags).Invoke(rex, args);
        private static void Set(string name, object value) => typeof(DinosaurAgent).GetField(name, Flags).SetValue(rex, value);
        private static void Begin()
        {
            try
            {
                originalVsync = QualitySettings.vSyncCount; originalRate = Application.targetFrameRate;
                rex = TRexOpenFollowAcceptance.PrepareWorld(out camera);
                rex.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var animation = rex.GetComponentInChildren<Animation>(); animation.Stop();
                string idle = (string)Field("idleClip"); animation.Play(idle); animation[idle].time = 0f; animation.Sample();
                rex.GetComponentInChildren<DinosaurFootLock>()?.ResetSupportAnchors();
                camera.transform.position = Vector3.forward * 24f + Vector3.up * 1.7f;
                Set("frontFollowDuration", 30f); Set("frontReturnWalkTime", 8f);
                Time.captureDeltaTime = 1f / 60f;
                Call("BeginInteractionSequence", false, false); rex.enabled = true;
                began = Time.time; followAt = endingAt = -1f; lastFrame = -1;
                maxReturnWalked = 0f; sawReturn = false; sawHistoryStart = false; trailPoints = 0; queriesAtFollow = 0;
                lastTurnMotion = null; turnCount = 0; lastTurnCheckAt = lastTurnTime = 0f;
                FrameGc.Clear(); gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Tick()
        {
            try
            {
                if (Time.frameCount == lastFrame) return; lastFrame = Time.frameCount;
                if (rex.IsFrontInteractionActive)
                {
                    string stage = rex.FrontInteractionStage;
                    if (!stage.StartsWith("Intro") && !stage.StartsWith("Follow") && !stage.StartsWith("Near")
                        && !stage.StartsWith("SideStartRun") && !stage.StartsWith("Ending")
                        && !stage.StartsWith("TurnBack") && !stage.StartsWith("ReturnWalk"))
                        throw new InvalidOperationException("Cost fixture left the interaction: " + stage + " / " + rex.FrontEndReason);
                    if (stage.StartsWith("Follow"))
                    {
                        if (followAt < 0f) { followAt = Time.time; queriesAtFollow = rex.FrontExitPathQueries; }
                        Require(!((bool)Field("frontWaiting")) && !((bool)Field("frontPeerClearing")), "Open follow waited: " + rex.FrontBlockingReason);
                        // Keep the player ahead so a 10s follow never enters the Near band.
                        camera.transform.position = rex.transform.position + Vector3.forward * 24f + Vector3.up * 1.7f;
                        if (Time.time - followAt >= 10f) { RequireTrail(); Call("BeginFrontEnding"); endingAt = Time.time; }
                    }
                    if (stage.StartsWith("ReturnWalk"))
                    {
                        sawReturn = true;
                        if ((int)Field("frontReturnIndex") < 0) sawHistoryStart = true;
                        maxReturnWalked = Mathf.Max(maxReturnWalked, (float)Field("frontReturnWalked"));
                        if (gcRecorder.Valid) FrameGc.Add(gcRecorder.LastValue);
                        var turn = Field("stepTurnMotion") as DinosaurInteractionProfile.Motion;
                        float turnTime = (float)Field("stepTurnTime"), turnEnd = (float)Field("stepTurnEnd");
                        if (turn != null && turn != lastTurnMotion)
                        {
                            turnCount++;
                            lastTurnMotion = turn; lastTurnCheckAt = Time.time; lastTurnTime = turnTime;
                            var corners = (Vector3[])Field("curiosityCorners");
                            int cornerIndex = (int)Field("curiosityCorner");
                            Vector3 visual = rex.transform.rotation * Quaternion.Euler(0f, rex.VisualYaw, 0f) * Vector3.forward;
                            string target = corners != null && cornerIndex >= 0 && cornerIndex < corners.Length
                                ? corners[cornerIndex].ToString("F2") + $" angle={Vector3.SignedAngle(visual, corners[cornerIndex] - rex.transform.position, Vector3.up):F1}"
                                : "none";
                            Debug.Log($"[Return cost] turn {turnCount} began at {Time.time - began:F1}s: {turn.sourceName}"
                                + $" playback={turn.turnPlaybackSpeed:F2} end={turnEnd:F2}s pos={rex.transform.position:F2} corner={target}");
                        }
                        else if (turn == null)
                        {
                            if (lastTurnMotion != null)
                                Debug.Log($"[Return cost] turn {turnCount} ended at {Time.time - began:F1}s, walked={maxReturnWalked:F2}s");
                            lastTurnMotion = null;
                        }
                        else if (Time.time - lastTurnCheckAt >= 5f)
                        {
                            Require(turnTime > lastTurnTime + .001f,
                                $"Stepping turn stalled: {turn.sourceName} time={turnTime:F3}/{turnEnd:F3}, playback={turn.turnPlaybackSpeed:F2}");
                            lastTurnCheckAt = Time.time; lastTurnTime = turnTime;
                        }
                    }
                    Require(Time.time - began < 150f, "Return cost fixture timed out: " + rex.FrontInteractionStage
                        + ", returned=" + Field("frontReturnWalked") + ", index=" + Field("frontReturnIndex")
                        + ", waiting=" + Field("frontWaiting") + ", block=" + rex.FrontBlockingReason
                        + ", peer=" + Field("frontPeerClearing") + ", turn=" + (Field("stepTurnMotion") != null));
                    return;
                }
                Require(sawReturn && endingAt > 0f, "Interaction ended without a historical return.");
                Require(maxReturnWalked >= 7.9f || sawHistoryStart, "Return did not walk its configured time or reach the trail start: " + maxReturnWalked.ToString("F3"));
                var allocations = FrameGc.OrderBy(v => v).ToArray();
                string report = "# Historical trail and return cost\n\nPASS HISTORICAL_RETURN_COST\n\n" +
                    "Isolated Main actor/profile in empty flat navigation at a fixed 60 FPS. The recorded follow trail is the only return route; no exit is searched during follow or return. " +
                    "This measures the cost of trail recording and reversed walking, not a planner.\n\n" +
                    $"|Metric|Value|\n|---|---:|\n" +
                    $"|Follow time|10.00 s|\n" +
                    $"|Trail points|{trailPoints}|\n" +
                    $"|Return walked|{maxReturnWalked:F2} s|\n" +
                    $"|Stepping turns|{turnCount}|\n" +
                    $"|Path queries|{rex.FrontExitPathQueries} (at follow start {queriesAtFollow})|\n" +
                    $"|Searches / reviews|{rex.FrontExitSearchCount} / {rex.FrontExitReviewCount}|\n" +
                    $"|Per-frame GC during return median/P95/max|{Q(allocations, .5f):F0}/{Q(allocations, .95f):F0}/{Q(allocations, 1):F0} bytes|\n\n" +
                    "Searches stay zero: an unobstructed follow and return never replan an exit. Frame GC covers the whole fixture (animation, editor and return), not only the trail. " +
                    "Stepping turns keep the calibrated pose playback, so fixture time can exceed the accumulated walking time; every turn is checked to keep advancing.\n";
                Directory.CreateDirectory(RainforestSample.Output);
                File.WriteAllText(RainforestSample.Output + "/ExitPlannerPerformance.md", report);
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }
        private static void RequireTrail()
        {
            var trail = (List<Vector3>)Field("frontTrail");
            trailPoints = trail.Count;
            Require(trailPoints >= 8, "Follow trail is too sparse for a return route: " + trailPoints);
            Require(rex.FrontExitSearchCount == 0, "Unobstructed follow searched for an exit.");
            Require(rex.FrontExitReviewCount == 0, "Removed periodic exit review is still running.");
            Require(Vector3.Distance(trail[0], trail[trailPoints - 1]) > 5f, "Follow trail did not cover the walked distance.");
        }
        private static object Field(string name) => typeof(DinosaurAgent).GetField(name, Flags).GetValue(rex);
        private static float Q(float[] values, float p) => values.Length == 0 ? 0f : values[Mathf.Min(values.Length - 1, Mathf.FloorToInt((values.Length - 1) * p))];
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Finish(Exception error)
        {
            if (gcRecorder.Valid) gcRecorder.Dispose();
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            QualitySettings.vSyncCount = originalVsync; Application.targetFrameRate = originalRate;
            if (error != null)
            {
                Directory.CreateDirectory(RainforestSample.Output);
                File.WriteAllText(RainforestSample.Output + "/ExitPlannerPerformance.md", "FAILED " + error);
                Debug.LogException(error);
            }
            else Debug.Log("PASS HISTORICAL_RETURN_COST");
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
