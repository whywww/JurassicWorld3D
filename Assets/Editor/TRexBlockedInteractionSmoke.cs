using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class TRexBlockedInteractionSmoke
    {
        private const string Key = "JurassicWorld.BlockedInteractionSmoke";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static DinosaurAgent rex;
        private static TRexFrontTestController test;
        private static DinosaurInteractionSettings settings;
        private static Animation animation;
        private static Transform head;
        private static GameObject peerObject, hole;
        private static int scenario, frame, waitFrames;
        private static int Case => scenario % 7;
        private static bool Side => scenario >= 7;
        private static float began, waitBegan, minimumWait, lastWaitTime, headMovement;
        private static bool sawWait, sawPass, resumed, released;
        private static Vector3 position;
        private static Quaternion headPose;
        private static string waitSource, waitStage;
        private static int recoveryPhase;
        private static float recoveryTime;
        private static Vector3 recoveryPosition;
        private static readonly System.Collections.Generic.List<string> recoveryActions = new System.Collections.Generic.List<string>();
        private static readonly System.Text.StringBuilder report = new System.Text.StringBuilder();
        private static object Get(string name) => typeof(DinosaurAgent).GetField(name, Flags).GetValue(rex);
        private static void Set(string name, object value) => typeof(DinosaurAgent).GetField(name, Flags).SetValue(rex, value);
        private static object Call(string name, params object[] args) => typeof(DinosaurAgent).GetMethod(name, Flags).Invoke(rex, args);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static TRexBlockedInteractionSmoke()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) EditorApplication.delayCall += Begin;
            };
        }
        public static void Run()
        {
            EditorSceneManager.OpenScene(TRexFrontTestController.ScenePath);
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }
        private static void Begin()
        {
            try
            {
                test = UnityEngine.Object.FindObjectOfType<TRexFrontTestController>(); rex = test.dinosaur;
                settings = UnityEngine.Object.FindObjectOfType<DinosaurInteractionSettings>();
                test.enabled = false; test.playerCamera.GetComponent<SmoothExplorerCamera>().enabled = false;
                animation = rex.GetComponentInChildren<Animation>(); head = TRexTurnCalibration.Bone(animation.transform, "jt_Head_C");
                Time.captureDeltaTime = 1f / 60f; scenario = frame = 0; report.Clear(); Prepare(); EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Prepare()
        {
            settings.solidObstacles = Array.Empty<Bounds>();
            if (peerObject != null) UnityEngine.Object.DestroyImmediate(peerObject);
            if (hole != null) UnityEngine.Object.DestroyImmediate(hole);
            if ((bool)Get("frontActive")) Call("FinishFrontInteraction");
            Set("peers", new[] { rex });
            rex.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, -rex.VisualYaw, 0f));
            test.playerCamera.transform.position = new Vector3(0f, 1.7f, 24f);
            Call("BeginInteractionSequence", Side, scenario % 2 == 0);
            Set("frontTestWaiting", false); Call("BeginFrontFollow", false);
            if (scenario >= 14) { PrepareRecovery(); return; }
            if (Side && Case == 2)
            {
                Set("frontStage", Enum.Parse(typeof(DinosaurAgent).GetNestedType("FrontStage", BindingFlags.NonPublic), "Follow"));
                Call("PlayFrontFollow");
            }
            if (Case == 0 || Case == 4) Call("BeginFrontEnding");
            if (Case == 1) Call("BeginFrontReturn");
            if (Case == 3 || Case == 6)
            {
                Call("BeginFrontReturn");
                Set("frontStage", Enum.Parse(typeof(DinosaurAgent).GetNestedType("FrontStage", BindingFlags.NonPublic), "ReturnWalk"));
                var trail = (System.Collections.Generic.List<Vector3>)Get("frontTrail"); trail.Clear(); trail.Add(Vector3.forward * 20f);
                Set("frontReturnIndex", 0); Set("frontReturnWalked", Case == 3 ? 5f : 0f);
            }
            if (Case == 4 || Case == 6)
            {
                peerObject = new GameObject("Blocking peer fixture"); peerObject.transform.position = Vector3.right * 8f;
                var body = peerObject.AddComponent<DinosaurCoreBody>();
                // Cover the outgoing performance envelope too: the former small fixture missed ROAR entirely.
                body.bands = new[] { new DinosaurCoreBody.Band { layer = 2, bounds = new Bounds(Vector3.up * 2f, new Vector3(16f, 4f, 16f)) } };
                var peer = peerObject.AddComponent<DinosaurAgent>(); peer.enabled = false;
                typeof(DinosaurAgent).GetField("interactionBody", Flags).SetValue(peer, body); Set("peers", new[] { rex, peer });
            }
            else if (Case == 5)
            {
                hole = new GameObject("Navigation hole fixture"); hole.transform.position = Vector3.up * 2f;
                var obstacle = hole.AddComponent<NavMeshObstacle>(); obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.size = new Vector3(4f, 4f, 4f); obstacle.carving = true; obstacle.carveOnlyStationary = false;
                Set("frontReservationAt", Time.time + 100f);
            }
            else settings.solidObstacles = new[] { new Bounds(Vector3.up * 2f, new Vector3(1f, 4f, 1f)) };
            began = Time.time; waitBegan = -1f; minimumWait = 100f; lastWaitTime = headMovement = 0f; waitFrames = 0;
            sawWait = sawPass = resumed = released = false; position = rex.transform.position; headPose = head.localRotation;
            waitSource = waitStage = "";
        }
        private static void Tick()
        {
            try
            {
                if (frame == Time.frameCount) return; frame = Time.frameCount;
                if (scenario >= 14) { TickRecovery(); return; }
                float elapsed = Time.time - began; bool waiting = (bool)Get("frontWaiting");
                Require(Vector3.Distance(position, rex.transform.position) < .4f, "Root teleported."); position = rex.transform.position;
                sawPass |= (bool)Get("frontStaticPass");
                string currentStage = Get("frontStage").ToString();
                if (currentStage == "Follow" || currentStage == "SideStartRun")
                {
                    Require(!(bool)Get("frontStaticPass"), "Step 2 enabled static obstacle passing.");
                    if (sawWait && !resumed) Require(Time.time - waitBegan < 2.1f, "Blocked follow did not end after two seconds.");
                }
                if (waiting)
                {
                    if (waitStage != currentStage) { lastWaitTime = -1f; waitStage = currentStage; }
                    if (!sawWait) { sawWait = true; waitBegan = Time.time; headPose = head.localRotation; }
                    waitFrames++;
                    Require(rex.FrontInteractionStage.StartsWith(currentStage + " (", StringComparison.Ordinal)
                        && !rex.FrontInteractionStage.Contains(":"), "Stage status is not a parenthesized suffix.");
                    Require(rex.CurrentAnimationSourceName.Contains("wait"), "Wait did not use a dedicated animation.");
                    float clock = rex.FrontActionPlaybackTime;
                    Require(clock > lastWaitTime || waitFrames <= 1 || waitSource != rex.CurrentAnimationSourceName,
                        "Waiting animation froze or restarted in case " + scenario + ": " + lastWaitTime + " -> " + clock);
                    lastWaitTime = clock; waitSource = rex.CurrentAnimationSourceName;
                    headMovement = Mathf.Max(headMovement, Quaternion.Angle(headPose, head.localRotation));
                    if (Case == 4 || Case == 6)
                    {
                        Require(!(bool)Get("frontStaticPass"), "Peer was treated as a passable static obstacle.");
                        Require(Vector3.Distance(rex.transform.position, Vector3.zero) < .01f, "Moved through peer.");
                        // Removing the previous case's carved obstacle updates NavMesh on the next frame.
                        if (elapsed > .55f) Require(rex.FrontBlockingReason == "Peer", "Missing peer wait reason.");
                    }
                }
                else if (sawWait && !resumed) { resumed = true; minimumWait = Time.time - waitBegan; }
                if ((Case == 4 || Case == 6) && elapsed >= 8f && !released)
                {
                    released = true; UnityEngine.Object.DestroyImmediate(peerObject); Set("peers", new[] { rex });
                }
                bool passed = Case == 0 ? resumed && (float)Get("frontActionTime") > .5f
                    : Case == 1 ? resumed && ((float)Get("frontActionTime") > 2.5f || currentStage == "ReturnWalk")
                    : Case == 3 ? !(bool)Get("frontActive")
                    : Case == 4 ? released && resumed && (float)Get("frontActionTime") > .3f
                    : Case == 6 ? released && resumed && rex.transform.position.magnitude > .5f
                    : resumed && currentStage == "Ending" && (float)Get("frontActionTime") > .5f;
                if (passed)
                {
                    Require(sawWait && waitFrames > 20 && headMovement > .005f, "Waiting pose did not animate: " + headMovement);
                    if (Case != 2 && Case != 4 && Case != 5 && Case != 6) Require(sawPass, "Static obstacle never permitted progress.");
                    if (Case == 0 || Case == 1) Require(minimumWait >= .9f && minimumWait < 1.7f, "Action wait was not bounded to approximately one second.");
                    if (Case == 2 || Case == 5) Require(minimumWait >= 1.9f && minimumWait < 2.1f, "Blocked following did not end at two seconds.");
                    if (Case == 3) Require((bool)Call("FrontClearOfStatic") && !(bool)Get("frontStaticPass"), "Resumed roaming inside a static obstacle.");
                    report.AppendLine($"PASS case {scenario}: wait={minimumWait:F3}s, animated frames={waitFrames}, head change={headMovement:F4}deg, resumed={resumed}, staticPass={sawPass}");
                    scenario++; Prepare();
                }
                Require(elapsed < 25f, "Timeout case " + scenario + ": " + rex.FrontInteractionStage + ", " + rex.FrontBlockingReason);
            }
            catch (Exception error) { Finish(error); }
        }
        private static void PrepareRecovery()
        {
            Set("frontStage", Enum.Parse(typeof(DinosaurAgent).GetNestedType("FrontStage", BindingFlags.NonPublic), "Follow"));
            Call("PlayFrontFollow");
            if (scenario == 16)
            {
                var use = Enum.Parse(typeof(DinosaurAgent).GetNestedType("TurnUse", BindingFlags.NonPublic), "FrontFollow");
                Require((bool)Call("TryBeginSteppingTurn", 90f, use), "Turn fixture could not start.");
            }
            if (scenario == 15)
            {
                peerObject = new GameObject("Follow peer fixture");
                var body = peerObject.AddComponent<DinosaurCoreBody>();
                body.bands = new[] { new DinosaurCoreBody.Band { layer = 2, bounds = new Bounds(Vector3.up * 2f, new Vector3(30f, 4f, 30f)) } };
                var peer = peerObject.AddComponent<DinosaurAgent>(); peer.enabled = false;
                typeof(DinosaurAgent).GetField("interactionBody", Flags).SetValue(peer, body); Set("peers", new[] { rex, peer });
            }
            else settings.solidObstacles = new[] { new Bounds(rex.transform.position + Vector3.up * 2f, new Vector3(30f, 4f, 30f)) };
            began = Time.time; waitBegan = -1f; recoveryTime = Time.time; recoveryPhase = 0;
            recoveryPosition = rex.transform.position; recoveryActions.Clear();
        }
        private static void TickRecovery()
        {
            string stage = Get("frontStage").ToString();
            bool waiting = (bool)Get("frontWaiting");
            float elapsed = Time.time - began;
            if (stage == "Follow") Require(!(bool)Get("frontStaticPass"), "Follow passed through an obstacle.");
            if (recoveryPhase == 0)
            {
                if (waiting && waitBegan < 0f) waitBegan = Time.time;
                if (scenario == 14 && waitBegan >= 0f && Time.time - waitBegan >= 1f)
                { settings.solidObstacles = Array.Empty<Bounds>(); recoveryPhase = 1; }
                else if (scenario == 15 && waitBegan >= 0f && Time.time - waitBegan >= 3f)
                {
                    Require(stage == "Follow", "Peer wait ended the follow.");
                    UnityEngine.Object.DestroyImmediate(peerObject); Set("peers", new[] { rex }); recoveryPhase = 1;
                }
                else if (scenario == 16 && stage == "Ending")
                {
                    Require(waitBegan >= 0f && Time.time - waitBegan >= 1.9f && Time.time - waitBegan < 2.1f,
                        "Blocked follow turn did not exit after two seconds.");
                    Require(!rex.IsSteppingTurn, "Follow turn still owns the ending.");
                    settings.solidObstacles = Array.Empty<Bounds>(); recoveryPhase = 3;
                }
                else Require(stage == "Follow", "Follow ended before the expected timeout.");
            }
            else if (recoveryPhase == 1)
            {
                Require(stage == "Follow", "Temporary obstacle did not resume following.");
                if (!waiting && Vector3.Distance(recoveryPosition, rex.transform.position) > .1f)
                {
                    if (scenario == 15) { CompleteRecovery(); return; }
                    recoveryTime = Time.time; recoveryPhase = 2;
                    settings.solidObstacles = new[] { new Bounds(rex.transform.position + Vector3.up * 2f, new Vector3(30f, 4f, 30f)) };
                }
            }
            else if (recoveryPhase == 2)
            {
                if (stage == "Ending")
                {
                    Require(Time.time - recoveryTime >= 1.99f && Time.time - recoveryTime < 2.15f,
                        "Successful movement did not reset the follow blocker timer.");
                    settings.solidObstacles = Array.Empty<Bounds>(); recoveryPhase = 3;
                }
                else Require(stage == "Follow", "Unexpected stage during repeated blocking.");
            }
            else
            {
                Require(stage != "Follow" && stage != "Near", "A finished follow resumed.");
                string action = rex.CurrentAnimationSourceName;
                if (recoveryActions.Count == 0 || recoveryActions[recoveryActions.Count - 1] != action) recoveryActions.Add(action);
                if (!(bool)Get("frontActive"))
                {
                    Require(recoveryActions.Contains("ROAR") && recoveryActions.Contains("look forward 2")
                        && recoveryActions.Exists(a => a.StartsWith("bite close", StringComparison.Ordinal))
                        && recoveryActions.Contains("turn 180 right"), "Blocked exit skipped steps 3-4.");
                    CompleteRecovery(); return;
                }
            }
            Require(elapsed < 60f, "Recovery scenario timed out: " + scenario + "/" + rex.FrontInteractionStage);
        }
        private static void CompleteRecovery()
        {
            report.AppendLine("PASS recovery case " + scenario + ": temporary block/reset, peer yielding or blocked turn; ending stays final.");
            if (++scenario == 17) Finish(null); else Prepare();
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            if (settings != null) settings.solidObstacles = Array.Empty<Bounds>();
            if (peerObject != null) UnityEngine.Object.DestroyImmediate(peerObject);
            if (hole != null) UnityEngine.Object.DestroyImmediate(hole);
            string result = (error == null ? "PASS blocked interaction\n" : error + "\n") + report;
            File.WriteAllText("Logs/trex-blocked-interaction-result.txt", result);
            if (error == null) Debug.Log(result); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
