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
        private static float began, waitBegan, minimumWait, lastWaitTime, headMovement;
        private static bool sawWait, sawPass, resumed, released;
        private static Vector3 position;
        private static Quaternion headPose;
        private static string waitSource;
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
            Call("BeginFrontInteraction"); Set("frontTestWaiting", false); Call("BeginFrontFollow", false);
            if (scenario == 0 || scenario == 4) Call("BeginFrontEnding");
            if (scenario == 1) Call("BeginFrontReturn");
            if (scenario == 3 || scenario == 6)
            {
                Call("BeginFrontReturn");
                Set("frontStage", Enum.Parse(typeof(DinosaurAgent).GetNestedType("FrontStage", BindingFlags.NonPublic), "ReturnWalk"));
                var trail = (System.Collections.Generic.List<Vector3>)Get("frontTrail"); trail.Clear(); trail.Add(Vector3.forward * 20f);
                Set("frontReturnIndex", 0); Set("frontReturnWalked", scenario == 3 ? 5f : 0f);
            }
            if (scenario == 4 || scenario == 6)
            {
                peerObject = new GameObject("Blocking peer fixture"); peerObject.transform.position = Vector3.right * 8f;
                var body = peerObject.AddComponent<DinosaurCoreBody>();
                // Cover the outgoing performance envelope too: the former small fixture missed ROAR entirely.
                body.bands = new[] { new DinosaurCoreBody.Band { layer = 2, bounds = new Bounds(Vector3.up * 2f, new Vector3(16f, 4f, 16f)) } };
                var peer = peerObject.AddComponent<DinosaurAgent>(); peer.enabled = false;
                typeof(DinosaurAgent).GetField("interactionBody", Flags).SetValue(peer, body); Set("peers", new[] { rex, peer });
            }
            else if (scenario == 5)
            {
                hole = new GameObject("Navigation hole fixture"); hole.transform.position = Vector3.up * 2f;
                var obstacle = hole.AddComponent<NavMeshObstacle>(); obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.size = new Vector3(4f, 4f, 4f); obstacle.carving = true; obstacle.carveOnlyStationary = false;
                Set("frontReservationAt", Time.time + 100f);
            }
            else settings.solidObstacles = new[] { new Bounds(Vector3.up * 2f, new Vector3(1f, 4f, 1f)) };
            began = Time.time; waitBegan = -1f; minimumWait = 100f; lastWaitTime = headMovement = 0f; waitFrames = 0;
            sawWait = sawPass = resumed = released = false; position = rex.transform.position; headPose = head.localRotation;
            waitSource = "";
        }
        private static void Tick()
        {
            try
            {
                if (frame == Time.frameCount) return; frame = Time.frameCount;
                float elapsed = Time.time - began; bool waiting = (bool)Get("frontWaiting");
                Require(Vector3.Distance(position, rex.transform.position) < .4f, "Root teleported."); position = rex.transform.position;
                sawPass |= (bool)Get("frontStaticPass");
                if (waiting)
                {
                    if (!sawWait) { sawWait = true; waitBegan = Time.time; headPose = head.localRotation; }
                    waitFrames++;
                    Require(rex.CurrentAnimationSourceName.Contains("wait"), "Wait did not use a dedicated animation.");
                    float clock = rex.FrontActionPlaybackTime;
                    Require(clock > lastWaitTime || waitFrames <= 1 || waitSource != rex.CurrentAnimationSourceName,
                        "Waiting animation froze or restarted in case " + scenario + ": " + lastWaitTime + " -> " + clock);
                    lastWaitTime = clock; waitSource = rex.CurrentAnimationSourceName;
                    headMovement = Mathf.Max(headMovement, Quaternion.Angle(headPose, head.localRotation));
                    if (scenario == 4 || scenario == 6)
                    {
                        Require(!(bool)Get("frontStaticPass"), "Peer was treated as a passable static obstacle.");
                        Require(Vector3.Distance(rex.transform.position, Vector3.zero) < .01f, "Moved through peer.");
                        // Removing the previous case's carved obstacle updates NavMesh on the next frame.
                        if (elapsed > .55f) Require(rex.FrontBlockingReason == "Peer", "Missing peer wait reason.");
                    }
                }
                else if (sawWait && !resumed) { resumed = true; minimumWait = Time.time - waitBegan; }
                if ((scenario == 4 || scenario == 6) && elapsed >= 8f && !released)
                {
                    released = true; UnityEngine.Object.DestroyImmediate(peerObject); Set("peers", new[] { rex });
                }
                bool passed = scenario == 0 ? resumed && (float)Get("frontActionTime") > .5f
                    : scenario == 1 ? resumed && ((float)Get("frontActionTime") > 2.5f || Get("frontStage").ToString() == "ReturnWalk")
                    : scenario == 3 ? !(bool)Get("frontActive")
                    : scenario == 4 ? released && resumed && (float)Get("frontActionTime") > .3f
                    : scenario == 6 ? released && resumed && rex.transform.position.magnitude > .5f
                    : resumed && (rex.transform.position.magnitude > .5f || (float)Get("frontActionTime") > .5f);
                if (passed)
                {
                    Require(sawWait && waitFrames > 20 && headMovement > .005f, "Waiting pose did not animate: " + headMovement);
                    if (scenario != 4 && scenario != 6) Require(sawPass, "Static obstacle never permitted progress.");
                    if (scenario == 0 || scenario == 1) Require(minimumWait >= .9f && minimumWait < 1.7f, "Action wait was not bounded to approximately one second.");
                    if (scenario == 2 || scenario == 5) Require(minimumWait >= 1.9f && minimumWait < 2.7f, "Walking wait was not bounded to approximately two seconds.");
                    if (scenario == 3) Require((bool)Call("FrontClearOfStatic") && !(bool)Get("frontStaticPass"), "Resumed roaming inside a static obstacle.");
                    report.AppendLine($"PASS case {scenario}: wait={minimumWait:F3}s, animated frames={waitFrames}, head change={headMovement:F4}deg, resumed={resumed}, staticPass={sawPass}");
                    if (++scenario == 7) { Finish(null); return; } Prepare();
                }
                Require(elapsed < 25f, "Timeout case " + scenario + ": " + rex.FrontInteractionStage + ", " + rex.FrontBlockingReason);
            }
            catch (Exception error) { Finish(error); }
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
