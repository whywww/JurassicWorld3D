using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    // Historical-return acceptance: the follow trail is the route. No exit is predicted.
    [InitializeOnLoad]
    public static class TRexExitPlannerAcceptance
    {
        private const string Key = "JurassicWorld.ExitPlannerAcceptance";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private const float Rate = 60f;
        private const int Scenarios = 4;
        private static DinosaurAgent rex;
        private static DinosaurInteractionSettings settings;
        private static Camera camera;
        private static DinosaurFootLock feet;
        private static int scenario, phase, lastFrame, originalVsync, originalRate;
        private static int trailAtReturn, searchAtFollow, returnQueries;
        private static float began, followAt, endingBegan, waitBegan, maxReturnWalked, maxStep, error, displacement, bendAngle;
        private static Vector3 previous, waitAnchor, followOrigin, rootSnapshot, connectionSnapshot;
        private static Vector3[] trailSnapshot;
        private static float angleSnapshot;
        private static bool connectionChecked;
        private static string footContext;
        private static bool sawEnding, sawTurn, sawReturn, sawWait, sawHistoryStart, bentPlayer, blockedInjected, clearedObstacle, checkedTargetBehind;
        private static double deadline;
        private static bool leftOnly;
        private static List<Vector3> trail;
        private static readonly StringBuilder Report = new StringBuilder();
        private static object Get(string field) => typeof(DinosaurAgent).GetField(field, Flags).GetValue(rex);
        private static void Set(string field, object value) => typeof(DinosaurAgent).GetField(field, Flags).SetValue(rex, value);
        private static object Call(string method, params object[] args) => typeof(DinosaurAgent).GetMethod(method, Flags).Invoke(rex, args);
        private static bool Flag(string field) => (bool)Get(field);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static TRexExitPlannerAcceptance()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) EditorApplication.delayCall += Begin;
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + ".Restore", false))
                { SessionState.SetBool(Key + ".Restore", false); EditorSceneManager.OpenScene("Assets/Scenes/Main.unity"); }
            };
        }
        [MenuItem("Jurassic World/Acceptance/Verify Follow Exits and Step4")]
        public static void Run() => Start(false);
        [MenuItem("Jurassic World/Acceptance/Verify Left Only Exit")]
        public static void RunLeftOnly() => Start(true);
        private static void Start(bool left)
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorSceneManager.GetActiveScene().isDirty, "Stop Play and save before exit acceptance.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            SessionState.SetBool(Key + ".Restore", true);
            SessionState.SetInt(Key + ".Start", left ? 3 : 0);
            SessionState.SetInt(Key + ".End", left ? 4 : Scenarios);
            SessionState.SetBool(Key + ".LeftOnly", left);
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }
        private static string ReportPath => RainforestSample.Output + (leftOnly ? "/SideOnlyExitAcceptance.md" : "/ExitPlannerAcceptance.md");
        private static void Begin()
        {
            try
            {
                leftOnly = SessionState.GetBool(Key + ".LeftOnly", false);
                originalVsync = QualitySettings.vSyncCount; originalRate = Application.targetFrameRate;
                rex = TRexOpenFollowAcceptance.PrepareWorld(out camera); settings = Object.FindObjectOfType<DinosaurInteractionSettings>();
                feet = rex.GetComponentInChildren<DinosaurFootLock>(); trail = (List<Vector3>)Get("frontTrail");
                scenario = SessionState.GetInt(Key + ".Start", 0); SessionState.EraseInt(Key + ".Start");
                Report.Clear();
                Report.AppendLine("# Historical return acceptance\n\nMain actor and profile in an isolated flat Play fixture. The follow trail is walked in reverse; special animations check peer bodies only.\n");
                lastFrame = -1; Prepare(); EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Prepare()
        {
            if (rex.IsFrontInteractionActive) Call("FinishFrontInteraction");
            Call("ClearSteppingTurn");
            settings.solidObstacles = Array.Empty<Bounds>(); TerrainTreeObstacleRegistry.Invalidate();
            Set("peers", new[] { rex }); Set("frontTestControlled", true); Set("frontTestWaiting", false);
            Set("frontFollowDuration", 30f); Set("frontReturnWalkTime", scenario == 2 ? 5f : 2f);
            rex.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animation = rex.GetComponentInChildren<Animation>(); animation.Stop();
            var idle = (string)Get("idleClip"); animation.Play(idle); animation[idle].time = 0f; animation.Sample(); feet?.ResetSupportAnchors();
            camera.transform.position = scenario == 3 ? new Vector3(-16f, 1.7f, 20f) : new Vector3(0f, 1.7f, 24f);
            if (scenario == 3)
            {
                // Walls close the front, rear and right; the player is visible and reachable
                // only through the open left side, so the follow route and its reverse both bend left.
                settings.solidObstacles = new[]
                {
                    new Bounds(new Vector3(6f, 4f, 0f), new Vector3(.4f, 8f, 30f)),
                    new Bounds(new Vector3(4.5f, 4f, 12f), new Vector3(21f, 8f, .4f)),
                    new Bounds(new Vector3(4.5f, 4f, -10f), new Vector3(21f, 8f, .4f))
                };
            }
            Time.captureDeltaTime = 1f / Rate;
            Call("BeginInteractionSequence", false, false); rex.enabled = true;
            previous = followOrigin = rex.transform.position;
            began = Time.time; followAt = endingBegan = waitBegan = -1f;
            phase = 0; maxReturnWalked = maxStep = error = displacement = bendAngle = 0f;
            sawEnding = sawTurn = sawReturn = sawWait = sawHistoryStart = bentPlayer = blockedInjected = clearedObstacle = checkedTargetBehind = connectionChecked = false;
            trailAtReturn = 0; searchAtFollow = 0; returnQueries = 0; footContext = "";
            trailSnapshot = null; rootSnapshot = connectionSnapshot = Vector3.zero; angleSnapshot = 0f;
            UnityEngine.Random.InitState(7300 + scenario * 71);
            deadline = EditorApplication.timeSinceStartup + 180;
        }
        private static void Tick()
        {
            try
            {
                if (Time.frameCount == lastFrame) return; lastFrame = Time.frameCount;
                Require(EditorApplication.timeSinceStartup < deadline, "Historical return fixture wall-clock timeout in case " + scenario + ".");
                float step = Vector3.Distance(previous, rex.transform.position); previous = rex.transform.position;
                maxStep = Mathf.Max(maxStep, step);
                Require(step <= Mathf.Max(.6f, 2.4f * Time.deltaTime + .05f), $"Root teleported {step:F3}m in {rex.FrontInteractionStage}.");
                TrackFeet();
                Require(rex.FrontExitReviewCount == 0, "Removed periodic exit review is still running.");
                Require(!rex.FrontEndReason.Contains("Space:"), "Legacy Space: end reason resurfaced.");
                Require(!rex.FrontBlockingReason.Contains("Ground") && !rex.LastFrontSpaceFailure.Contains("Ground"), "Terrain height still blocks the interaction.");
                TrackCommon();
                if (scenario == 0) DriveCurvedFollow();
                else if (scenario == 1) DriveDisplacedConnector();
                else if (scenario == 2) DriveBlockedReturn();
                else DriveWalledReturn();
                if (!rex.IsFrontInteractionActive) FinishScenario();
                else Require(Time.time - began < 120f, "Historical return timed out: " + rex.FrontInteractionStage + " / " + rex.FrontExitStatus);
            }
            catch (Exception error) { Finish(error); }
        }
        private static void TrackFeet()
        {
            if (feet == null) return;
            float current = Mathf.Max(feet.LeftPlanted ? feet.LeftPlantError : 0f, feet.RightPlanted ? feet.RightPlantError : 0f);
            if (current <= error) return;
            error = current;
            footContext = $"{rex.FrontInteractionStage}/{rex.CurrentAnimationSourceName}, left={(feet.LeftSolePosition - feet.LeftAnchor):F3}, right={(feet.RightSolePosition - feet.RightAnchor):F3}";
        }
        private static void TrackCommon()
        {
            string stage = rex.FrontInteractionStage;
            sawEnding |= stage.StartsWith("Ending");
            sawTurn |= rex.CurrentAnimationSourceName == "turn 180 right";
            if (!stage.StartsWith("ReturnWalk")) return;
            if (!sawReturn)
            {
                sawReturn = true;
                trailAtReturn = trail.Count;
                trailSnapshot = trail.ToArray();
                displacement = trail.Count > 0 ? Vector3.Distance(rex.transform.position, trail[trail.Count - 1]) : 0f;
                returnQueries = rex.FrontExitPathQueries;
            }
            maxReturnWalked = Mathf.Max(maxReturnWalked, (float)Get("frontReturnWalked"));
            Require(trail.Count == trailAtReturn, "Return path appended new history points.");
            if (rex.FrontExitPathQueries > returnQueries)
            {
                // Each connector plan must start from wherever the turn actually left the actor.
                returnQueries = rex.FrontExitPathQueries;
                connectionChecked = true;
                rootSnapshot = rex.transform.position;
                connectionSnapshot = rex.FrontExitConnection;
                angleSnapshot = rex.FrontExitAngle;
                float gap = Vector3.Distance(connectionSnapshot, rootSnapshot);
                Require(gap <= 1.5f, $"Return connector started {gap:F2}m from the actual pose: connection={connectionSnapshot:F3}, pose={rootSnapshot:F3}, stage={rex.FrontInteractionStage}.");
            }
            if ((int)Get("frontReturnIndex") < 0) sawHistoryStart = true;
            if (!checkedTargetBehind && (float)Get("frontReturnWalked") > .25f && (int)Get("frontReturnIndex") >= 0)
            {
                var target = trail[(int)Get("frontReturnIndex")];
                Vector3 direction = target - rex.transform.position; direction.y = 0f;
                if (direction.sqrMagnitude <= .01f) return;
                checkedTargetBehind = true;
                Require(Vector3.Dot((Vector3)Call("GetVisualForward"), direction.normalized) > .2f,
                    "ReturnWalk did not head back toward older trail points.");
            }
        }
        private static void BeginFollowClock()
        {
            if (followAt >= 0f || !rex.FrontInteractionStage.StartsWith("Follow")) return;
            followAt = Time.time; followOrigin = rex.transform.position; searchAtFollow = rex.FrontExitSearchCount;
        }
        private static void RequireUnobstructedFollow()
        {
            Require(!Flag("frontWaiting") && !Flag("frontPeerClearing"), "Unobstructed follow waited: " + rex.FrontBlockingReason);
            Require(!Flag("frontExitPending"), "Unobstructed follow ended early: " + rex.FrontEndReason);
        }
        private static void DriveCurvedFollow()
        {
            if (phase == 0)
            {
                BeginFollowClock();
                if (followAt < 0f) return;
                RequireUnobstructedFollow();
                if (!bentPlayer && Time.time - followAt >= 2f)
                {
                    bentPlayer = true;
                    camera.transform.position = new Vector3(rex.transform.position.x + 12f, 1.7f, rex.transform.position.z + 16f);
                }
                if (Time.time - followAt < 5f) return;
                Require(rex.FrontExitSearchCount == searchAtFollow, "Follow searched for an exit without an obstacle.");
                Require(trail.Count >= 4, "Curved follow recorded too few trail points.");
                bendAngle = Bend();
                Require(bendAngle >= 20f, "Curved follow did not record its route corner: " + bendAngle.ToString("F1"));
                Require(Vector3.Distance(followOrigin, rex.transform.position) > 3f, "Curved follow did not travel far enough.");
                Call("BeginFrontEnding"); phase = 1; endingBegan = Time.time;
            }
        }
        private static void DriveDisplacedConnector()
        {
            if (phase != 0) return;
            BeginFollowClock();
            if (followAt < 0f) return;
            RequireUnobstructedFollow();
            if (Time.time - followAt < 3f) return;
            Require(rex.FrontExitSearchCount == searchAtFollow, "Follow searched for an exit without an obstacle.");
            Call("BeginFrontEnding"); phase = 1; endingBegan = Time.time;
        }
        private static void DriveBlockedReturn()
        {
            if (phase == 0)
            {
                BeginFollowClock();
                if (followAt < 0f) return;
                RequireUnobstructedFollow();
                if (Time.time - followAt < 4f) return;
                Require(rex.FrontExitSearchCount == searchAtFollow, "Follow searched for an exit without an obstacle.");
                Call("BeginFrontEnding"); phase = 1; endingBegan = Time.time;
                return;
            }
            // The opening two seconds after a return turn deliberately cross static scenery.
            // Inject only after that guaranteed walk, while the actor is walking normally.
            if (!blockedInjected && sawReturn && (float)Get("frontReturnWalked") > .2f
                && !rex.ReturnFreeWalkActive && !rex.IsSteppingTurn && !Flag("frontWaiting"))
            {
                Vector3 target = trail[Mathf.Max(0, (int)Get("frontReturnIndex"))];
                Vector3 direction = target - rex.transform.position; direction.y = 0f;
                if (direction.sqrMagnitude < .01f) direction = (Vector3)Call("GetVisualForward");
                settings.solidObstacles = new[] { new Bounds(rex.transform.position + direction.normalized * 1.5f + Vector3.up * 3f, new Vector3(2.5f, 6f, 2.5f)) };
                blockedInjected = true; waitBegan = -1f;
            }
            if (!blockedInjected || clearedObstacle) return;
            if (Flag("frontWaiting") && !rex.IsSteppingTurn && !rex.ReturnFreeWalkActive && waitBegan < 0f)
            {
                waitBegan = Time.time; waitAnchor = rex.transform.position;
                sawWait = true;
                Require(!string.IsNullOrEmpty(rex.FrontBlockingReason), "Blocked return has no reason.");
                Require(!rex.FrontBlockingReason.Contains("Peer"), "Peer should not be the static return block.");
            }
            if (waitBegan >= 0f)
            {
                if (!rex.IsSteppingTurn && !rex.ReturnFreeWalkActive)
                    Require(Vector3.Distance(waitAnchor, rex.transform.position) < .05f, "Return walked through the injected static obstacle.");
                if (Time.time - waitBegan >= .8f) { settings.solidObstacles = Array.Empty<Bounds>(); clearedObstacle = true; }
            }
            Require(Time.time - endingBegan < 30f, "Injected static obstacle never stopped the return walk: " + rex.FrontInteractionStage);
        }
        private static void DriveWalledReturn()
        {
            TrackWalkingPose();
            if (phase != 0) return;
            BeginFollowClock();
            if (followAt < 0f) return;
            RequireUnobstructedFollow();
            if (Time.time - followAt < 4f) return;
            Require(trail.Count >= 4, "Walled follow never recorded the left-side route: " + trail.Count);
            Call("BeginFrontEnding"); phase = 1; endingBegan = Time.time;
        }
        private static void TrackWalkingPose()
        {
            // Return turns and their guaranteed opening walk may cross static scenery by design.
            if (rex.IsSteppingTurn || rex.ReturnFreeWalkActive) return;
            Vector3 point = rex.transform.position + Vector3.up * 2f;
            foreach (var wall in settings.solidObstacles)
                Require(!wall.Contains(point), "Walking body entered a wall: " + rex.transform.position);
        }
        private static void FinishScenario()
        {
            Require(sawEnding && sawTurn && sawReturn, "Interaction skipped Ending, the 180-degree turn or the historical return.");
            Require(maxReturnWalked >= 1.98f || sawHistoryStart, "Return did not accumulate the configured walking time: " + maxReturnWalked.ToString("F3"));
            Require(checkedTargetBehind, "Return never walked back toward an older trail point.");
            Require(connectionChecked, "Return never planned a connector back to the trail.");
            Require(scenario != 1 || displacement > .3f, "Ending produced too little displacement to exercise the connector: " + displacement.ToString("F3"));
            Require(scenario != 2 || (sawWait && clearedObstacle), "Blocked return did not stop and resume.");
            Require(rex.InteractionCooldownRemaining > 9.9f, "Interaction did not hand back to roaming with its cooldown.");
            Require(error <= .25f, $"Support foot error {error:F4}m: {footContext}.");
            Pass($"return walked {maxReturnWalked:F2}s, displacement {displacement:F2}m, {trailAtReturn} trail points, {rex.FrontExitSearchCount} search(es), max frame step {maxStep:F3}m");
        }
        private static float Bend()
        {
            float best = 0f;
            for (int i = 1; i + 1 < trail.Count; i++)
            {
                Vector3 a = trail[i] - trail[i - 1], b = trail[i + 1] - trail[i]; a.y = b.y = 0f;
                if (a.sqrMagnitude < .0001f || b.sqrMagnitude < .0001f) continue;
                best = Mathf.Max(best, Vector3.Angle(a, b));
            }
            return best;
        }
        [Serializable] private sealed class ReturnPicture
        {
            public int scenario; public Vector3 root, connection; public Vector3[] path; public Bounds[] solids;
            public float angle, returnWalked, displacement;
        }
        private static void Pass(string note)
        {
            if (scenario < Scenarios)
                File.WriteAllText(RainforestSample.Output + "/exit-case-" + scenario + ".json", JsonUtility.ToJson(new ReturnPicture
                {
                    scenario = scenario, root = connectionChecked ? rootSnapshot : rex.transform.position,
                    connection = connectionChecked ? connectionSnapshot : rex.FrontExitConnection,
                    path = trailSnapshot ?? rex.FrontExitPath, solids = settings.solidObstacles,
                    angle = connectionChecked ? angleSnapshot : rex.FrontExitAngle,
                    displacement = displacement, returnWalked = maxReturnWalked
                }, true));
            bool curved = scenario == 0;
            Report.AppendLine($"- PASS case {scenario}: {note}{(curved ? $", recorded bend {bendAngle:F1}deg" : "")}.");
            File.WriteAllText(ReportPath, "RUNNING\n\n" + Report);
            sceneResult();
        }
        private static void sceneResult()
        {
            scenario++;
            if (scenario < SessionState.GetInt(Key + ".End", Scenarios)) Prepare();
            else { SessionState.EraseInt(Key + ".End"); SessionState.EraseBool(Key + ".LeftOnly"); Finish(null); }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            QualitySettings.vSyncCount = originalVsync; Application.targetFrameRate = originalRate;
            if (settings != null) settings.solidObstacles = Array.Empty<Bounds>();
            string result = error == null ? "PASS HISTORICAL_RETURN" : "FAILED " + error;
            Directory.CreateDirectory(RainforestSample.Output);
            File.WriteAllText(ReportPath, result + "\n\n" + Report);
            if (error == null) Debug.Log(result); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
