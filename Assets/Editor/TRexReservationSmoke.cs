using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class TRexReservationSmoke
    {
        private const string Key = "JurassicWorld.ReservationSmoke";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private const int Scenarios = 3;
        private static DinosaurAgent rex;
        private static TRexFrontTestController test;
        private static DinosaurInteractionSettings settings;
        private static Camera camera;
        private static int frame, scenario, phase;
        private static float began, phaseBegan, waitBegan, maxReturnWalked, maxActionTime;
        private static Vector3 previous, anchor;
        private static bool sawEnding, sawTurn, sawReturn, sawWait, sawHistoryEnd, sawPeer, sawFreeWalk;
        private static GameObject peerObject;
        private static List<Vector3> trail;
        private static int trailCount;
        private static Vector3 returnStart;
        private static readonly System.Text.StringBuilder report = new System.Text.StringBuilder();
        private static object Get(string field) => typeof(DinosaurAgent).GetField(field, Flags).GetValue(rex);
        private static void Set(string field, object value) => typeof(DinosaurAgent).GetField(field, Flags).SetValue(rex, value);
        private static object Call(string method, params object[] args) => typeof(DinosaurAgent).GetMethod(method, Flags).Invoke(rex, args);
        private static bool Flag(string field) => (bool)Get(field);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static TRexReservationSmoke()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) EditorApplication.delayCall += Begin;
            };
        }
        [MenuItem("Jurassic World/Tests/Verify Historical Return and Peer Rules")]
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
                camera = rex.ViewCamera;
                test.enabled = false; test.playerCamera.GetComponent<SmoothExplorerCamera>().enabled = false;
                Time.captureDeltaTime = 1f / 60f; scenario = phase = 0; report.Clear();
                Prepare(); EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Prepare()
        {
            if (rex.IsFrontInteractionActive) Call("FinishFrontInteraction");
            if (peerObject != null) UnityEngine.Object.DestroyImmediate(peerObject);
            Set("peers", new[] { rex }); Set("frontTestControlled", true); Set("frontTestWaiting", false);
            settings.solidObstacles = Array.Empty<Bounds>();
            // Short return budget keeps the smoke bounded; the same accumulation path is used.
            Set("frontReturnWalkTime", 2f); Set("frontFollowDuration", 30f);
            // Terrain height is no longer an interaction gate. The flat fixture would not
            // expose a regression, so assert the removed gate has not returned.
            Require(typeof(DinosaurAgent).GetMethod("FrontGroundValid", Flags) == null, "Terrain height gate resurfaced.");
            rex.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, -rex.VisualYaw, 0f));
            camera.transform.position = new Vector3(0f, 1.7f, 30f);
            var animation = rex.GetComponentInChildren<Animation>(); animation.Stop();
            var idle = (string)Get("idleClip"); animation.Play(idle); animation[idle].time = 0f; animation.Sample();
            rex.GetComponentInChildren<DinosaurFootLock>()?.ResetSupportAnchors();
            Call("BeginInteractionSequence", false, false); Call("BeginFrontFollow", false);
            Require(rex.IsFrontInteractionActive && rex.FrontInteractionStage.StartsWith("Follow"), "Fixture did not enter Follow.");
            phase = 0; phaseBegan = began = Time.time; waitBegan = -1f;
            maxReturnWalked = maxActionTime = 0f; previous = anchor = returnStart = rex.transform.position;
            sawEnding = sawTurn = sawReturn = sawWait = sawHistoryEnd = sawPeer = sawFreeWalk = false;
            trail = (List<Vector3>)Get("frontTrail"); trailCount = 0;
        }
        private static void Tick()
        {
            try
            {
                if (frame == Time.frameCount) return; frame = Time.frameCount;
                float elapsed = Time.time - began, jump = Vector3.Distance(previous, rex.transform.position);
                Require(jump < .6f, $"Position jumped {jump:F3}m at {rex.FrontInteractionStage}/{rex.CurrentAnimationSourceName}.");
                previous = rex.transform.position;
                Require(rex.FrontExitReviewCount == 0, "Follow still runs the removed periodic exit review.");
                Require(!rex.FrontEndReason.Contains("Space:"), "Legacy Space: end reason resurfaced.");
                Require(!rex.FrontBlockingReason.Contains("Ground") && !rex.LastFrontSpaceFailure.Contains("Ground"),
                    "Terrain height still blocks interaction: " + rex.FrontBlockingReason);
                Require(elapsed < 90f, $"Reservation smoke timeout in case {scenario}: {rex.FrontInteractionStage} / {rex.CurrentAnimationSourceName} / {rex.FrontBlockingReason}");
                if (scenario == 0) TickOpenFollow();
                else if (scenario == 1) TickAnimationClipping();
                else TickPeerPriority();
            }
            catch (Exception error) { Finish(error); }
        }
        private static void TickOpenFollow()
        {
            if (phase == 0)
            {
                Require(rex.FrontInteractionStage.StartsWith("Follow"), "Open follow left the follow stage: " + rex.FrontInteractionStage);
                camera.transform.position = rex.transform.position + Vector3.forward * 30f + Vector3.up * 1.7f;
                Require(!Flag("frontWaiting") && !Flag("frontPeerClearing") && !Flag("frontExitPending"), "Open follow waited without an obstacle.");
                Require(rex.FrontExitSearchCount == 0, "Open follow replanned an exit without an obstacle: " + rex.FrontExitFailure);
                if (Vector3.Distance(anchor, rex.transform.position) > .5f)
                    Require(trail.Count >= 2 && Vector3.Distance(trail[0], trail[trail.Count - 1]) > .2f, "Follow trail did not record actual travel.");
                if (Time.time - began < 5f) return;
                Require(Vector3.Distance(anchor, rex.transform.position) > 2f, "Follow did not move far enough to build a return path.");
                Require(trail.Count >= 4, "Follow trail is too coarse to walk back: " + trail.Count + " points.");
                trailCount = trail.Count;
                Set("frontReturnWalkTime", 2f);
                Call("BeginFrontEnding"); phase = 1; phaseBegan = Time.time;
                return;
            }
            sawEnding |= rex.FrontInteractionStage.StartsWith("Ending");
            sawTurn |= rex.CurrentAnimationSourceName == "turn 180 right";
            if (rex.FrontInteractionStage.StartsWith("ReturnWalk"))
            {
                sawReturn = true;
                maxReturnWalked = Mathf.Max(maxReturnWalked, (float)Get("frontReturnWalked"));
                if ((int)Get("frontReturnIndex") < 0) sawHistoryEnd = true;
            }
            if (rex.IsFrontInteractionActive) return;
            Require(sawEnding && sawTurn && sawReturn, "Follow did not end with Ending, a 180-degree turn and a historical return.");
            Require(maxReturnWalked >= 1.98f || sawHistoryEnd, "Return did not walk the configured time before finishing: " + maxReturnWalked.ToString("F3"));
            Require(rex.InteractionCooldownRemaining > 9.9f, "Return did not hand back to roaming with the full cooldown.");
            report.AppendLine($"PASS open follow: {trailCount} recorded points, no exit search, no terrain-height gate, returned {maxReturnWalked:F2}s along the reversed trail" + (sawHistoryEnd ? " (reached the trail start)." : "."));
            Next();
        }
        private static void TickAnimationClipping()
        {
            if (phase == 0)
            {
                Require(rex.FrontInteractionStage.StartsWith("Follow"), "Fixture did not follow before the clipping test.");
                // A longer trail leaves route left after the guaranteed opening walk, so the
                // static block is actually observed instead of the return finishing first.
                if (Time.time - began < 4f) return;
                Require(Vector3.Distance(anchor, rex.transform.position) > .5f, "Follow did not move before the clipping test.");
                Set("frontReturnWalkTime", 8f);
                // The performance envelope sits inside a solid obstacle; only walking may be blocked.
                settings.solidObstacles = new[] { new Bounds(rex.transform.position + Vector3.up * 3f, new Vector3(8f, 8f, 8f)) };
                Call("BeginFrontEnding"); phase = 1; phaseBegan = Time.time;
                return;
            }
            if (phase == 1)
            {
                sawEnding |= rex.FrontInteractionStage.StartsWith("Ending");
                sawTurn |= rex.CurrentAnimationSourceName == "turn 180 right";
                if (rex.FrontInteractionStage.StartsWith("Ending") || rex.FrontInteractionStage.StartsWith("TurnBack"))
                {
                    Require(!Flag("frontWaiting") && !Flag("frontPeerClearing"), "Special animation waited for a static obstacle: " + rex.FrontBlockingReason);
                    maxActionTime = Mathf.Max(maxActionTime, (float)Get("frontActionTime"));
                }
                if (!rex.FrontInteractionStage.StartsWith("ReturnWalk")) return;
                sawReturn = true;
                returnStart = rex.transform.position;
                // A body-wide obstacle moved onto the walking path from the actual pose.
                settings.solidObstacles = new[] { new Bounds(rex.transform.position + Vector3.up * 3f, new Vector3(12f, 6f, 12f)) };
                phase = 2; phaseBegan = Time.time; waitBegan = -1f;
                return;
            }
            if (phase == 2)
            {
                // The first two seconds after ending deliberately ignore static scenery.
                if (rex.ReturnFreeWalkActive)
                {
                    Require(!Flag("frontWaiting") && !Flag("frontPeerClearing"), "Guaranteed walk waited for scenery: " + rex.FrontBlockingReason);
                    if (Vector3.Distance(returnStart, rex.transform.position) > .3f) sawFreeWalk = true;
                    return;
                }
                if (Flag("frontWaiting") && waitBegan < 0f)
                {
                    waitBegan = Time.time; anchor = rex.transform.position;
                    Require(!string.IsNullOrEmpty(rex.FrontBlockingReason), "Static walking block has no reason.");
                    Require(!rex.FrontBlockingReason.Contains("Peer"), "Peer should not be the static walking block.");
                    sawWait = true;
                }
                if (waitBegan >= 0f)
                    Require(Time.time - waitBegan > .8f || Vector3.Distance(anchor, rex.transform.position) < .05f,
                        "Walked into the static obstacle instead of waiting.");
                if (!sawWait)
                {
                    Require(!rex.IsFrontInteractionActive, "Return walk ignored a static obstacle on its path.");
                    return;
                }
                Require(maxActionTime > .25f, "Special animation did not progress while its volume clipped a static obstacle.");
                if (Time.time - waitBegan < .8f) return;
                settings.solidObstacles = Array.Empty<Bounds>();
                phase = 3; phaseBegan = Time.time; anchor = rex.transform.position;
                return;
            }
            if (rex.IsFrontInteractionActive)
            {
                Require(Time.time - phaseBegan < 20f, "Return did not resume and finish after the static obstacle was cleared.");
                return;
            }
            Require(sawEnding && sawTurn && sawReturn && sawWait && sawFreeWalk, "Clipping fixture skipped animation, turn, guaranteed walk, return or static walking wait.");
            report.AppendLine($"PASS special animation: {maxActionTime:F2}s of performance progress with the body inside a solid obstacle; the guaranteed opening walk crossed it, then the same obstacle stopped ReturnWalk until cleared.");
            Next();
        }
        private static void TickPeerPriority()
        {
            if (phase == 0)
            {
                Require(rex.FrontInteractionStage.StartsWith("Follow"), "Peer fixture did not start in Follow.");
                if (Time.time - began < 1.2f) return;
                Require(Vector3.Distance(anchor, rex.transform.position) > .4f, "Follow did not move before placing the peer.");
                BuildPeer();
                phase = 1; phaseBegan = Time.time; waitBegan = -1f;
                return;
            }
            if (phase == 1)
            {
                if ((Flag("frontWaiting") || rex.GroundTrafficDuration > 0f && rex.GroundTrafficBlocker != "--") && waitBegan < 0f)
                {
                    waitBegan = Time.time; anchor = rex.transform.position;
                    sawPeer = true;
                    Require(rex.FrontInteractionStage.Contains("WaitingForPeerClearance") || rex.FrontInteractionStage.Contains("YieldingToPeer")
                        || rex.FrontInteractionStage.Contains("PassingPeer") || rex.GroundTrafficStatus != "Inactive",
                        "Peer block was not reported as a peer conflict: " + rex.FrontInteractionStage);
                }
                // The priority holder keeps its pose while the other dinosaur is expected to clear.
                if (waitBegan >= 0f && Time.time - waitBegan < 1f && !Flag("frontPeerClearing"))
                    Require(Vector3.Distance(anchor, rex.transform.position) < .05f, "Priority holder moved while a peer blocked the path.");
                if (Time.time - phaseBegan < 2.2f) return;
                Require(sawPeer, "Rex never noticed the peer body on its path.");
                Require(!Flag("frontExitPending"), "Peer contact was treated as static unreachability.");
                Require(rex.IsFrontInteractionActive, "Peer contact ended the interaction.");
                UnityEngine.Object.DestroyImmediate(peerObject); peerObject = null; Set("peers", new[] { rex });
                phase = 2; phaseBegan = Time.time; anchor = rex.transform.position;
                return;
            }
            Require(!Flag("frontExitPending"), "Peer release still reported unreachability.");
            Require(rex.IsFrontInteractionActive, "Interaction ended after the peer was removed.");
            if (Vector3.Distance(anchor, rex.transform.position) <= .2f)
            {
                Require(Time.time - phaseBegan < 3f, "Follow did not resume after the peer was removed.");
                return;
            }
            report.AppendLine($"PASS peer priority: held pose for up to 1s while blocked, treated it as non-static, and resumed within 3s of release.");
            Call("FinishFrontInteraction");
            Next();
        }
        private static void BuildPeer()
        {
            Vector3 forward = (Vector3)Call("GetVisualForward");
            peerObject = new GameObject("Temporary peer fixture");
            peerObject.transform.position = rex.transform.position + forward * 4.5f;
            var body = peerObject.AddComponent<DinosaurCoreBody>();
            body.bands = new[] { new DinosaurCoreBody.Band { layer = 2, bounds = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(2f, 2f, 2f)) } };
            var peer = peerObject.AddComponent<DinosaurAgent>();
            peer.enabled = false;
            typeof(DinosaurAgent).GetField("interactionBody", Flags).SetValue(peer, body);
            typeof(DinosaurAgent).GetField("collisionRadius", Flags).SetValue(peer, 1f);
            Set("peers", new[] { rex, peer });
        }
        private static void Next()
        {
            if (++scenario == Scenarios) { Finish(null); return; }
            Prepare();
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            if (settings != null) settings.solidObstacles = Array.Empty<Bounds>();
            if (peerObject != null) UnityEngine.Object.DestroyImmediate(peerObject);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/trex-reservation-result.txt", (error == null ? "PASS historical return\n" : error + "\n") + report);
            if (error == null) Debug.Log("PASS historical return\n" + report); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
