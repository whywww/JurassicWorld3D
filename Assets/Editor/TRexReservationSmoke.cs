using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
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
        private static DinosaurAgent rex;
        private static TRexFrontTestController test;
        private static DinosaurInteractionSettings settings;
        private static int frame, scenario;
        private static float began, blockedAt;
        private static bool sawWait, sawTurn, sawSpaceExit;
        private static Vector3 previous;
        private static readonly List<string> actions = new List<string>();
        private static string lastAction;
        private static GameObject peerObject;
        private static bool forcedLeft;
        private static readonly System.Text.StringBuilder report = new System.Text.StringBuilder();
        private static object Get(string field) => typeof(DinosaurAgent).GetField(field, Flags).GetValue(rex);
        private static void Set(string field, object value) => typeof(DinosaurAgent).GetField(field, Flags).SetValue(rex, value);
        private static object Call(string method, params object[] args) => typeof(DinosaurAgent).GetMethod(method, Flags).Invoke(rex, args);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static TRexReservationSmoke()
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
                Time.captureDeltaTime = 1f / 60f; scenario = frame = 0; report.Clear(); Prepare(); EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Prepare()
        {
            if ((bool)Get("frontActive")) Call("FinishFrontInteraction");
            if (peerObject != null) UnityEngine.Object.DestroyImmediate(peerObject);
            Set("peers", new[] { rex }); forcedLeft = false;
            settings.solidObstacles = Array.Empty<Bounds>();
            rex.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, -rex.VisualYaw, 0f));
            test.playerCamera.transform.position = new Vector3(0f, 1.7f, scenario == 0 ? 10.2f : 30f);
            Call("BeginFrontInteraction"); Set("frontTestWaiting", false); Call("BeginFrontFollow", false);
            report.AppendLine("CASE " + scenario + " initialMask=" + Get("frontBiteMask") + " reason=" + rex.FrontBlockingReason);
            if (scenario == 0) report.AppendLine(TRexPredictionBenchmark.Measure(rex));
            if (scenario == 1)
            {
                // A wide dead-end wall: the locomotion pose fits well before the full exit no longer fits.
                bool found = false;
                for (float z = 12f; z <= 45f; z += 1f)
                {
                    settings.solidObstacles = new[] { new Bounds(new Vector3(0f, 3f, z), new Vector3(40f, 6f, 2f)) };
                    int current = (int)Call("FrontExitMask", Vector3.forward * 2f, rex.transform.rotation);
                    int ahead = (int)Call("FrontExitMask", Vector3.forward * 5f, rex.transform.rotation);
                    if (current != 0 && ahead == 0) { found = true; report.AppendLine("Dead-end wall z=" + z); break; }
                }
                Require(found, "Could not construct a safe-to-unsafe dead-end fixture.");
                Call("RefreshFrontReservation", true);
            }
            if (scenario == 2) Set("frontFollowElapsed", 29.9f);
            if (scenario == 3) Set("frontFollowElapsed", 29.9f);
            if (scenario == 4)
            {
                bool found = false;
                for (float start = 12f; start <= 30f && !found; start += 2f)
                for (float width = 7f; width <= 22f; width += 1f)
                {
                    var walls = new List<Bounds>();
                    for (int row = 0; row < 4; row++)
                    {
                        float z = start + row * 4f, x = width + (3 - row) * 1.5f;
                        walls.Add(new Bounds(new Vector3(-x, 3f, z), new Vector3(2f, 6f, 4f)));
                        walls.Add(new Bounds(new Vector3(x, 3f, z), new Vector3(2f, 6f, 4f)));
                    }
                    settings.solidObstacles = walls.ToArray();
                    if ((int)Call("FrontExitMask", Vector3.forward * 2f, rex.transform.rotation) != 0
                        && (int)Call("FrontExitMask", Vector3.forward * 5f, rex.transform.rotation) == 0)
                    { found = true; report.AppendLine("Narrow corridor width=" + width); break; }
                }
                Require(found, "Could not construct narrow-corridor fixture.");
            }
            if (scenario == 5)
            {
                Set("curiosityCorners", new[] { Vector3.zero, Vector3.forward * 3f, new Vector3(3f, 0f, 6f), new Vector3(10f, 0f, 12f) });
                Set("curiosityCorner", 1); Set("hasCuriosityPath", true); Set("nextReplan", Time.time + 100f);
                Set("frontFollowDuration", 5f);
            }
            var timer = System.Diagnostics.Stopwatch.StartNew(); Call("RefreshFrontReservation", true); timer.Stop();
            report.AppendLine("Full reservation refresh ms=" + timer.Elapsed.TotalMilliseconds.ToString("F2"));
            began = Time.time; blockedAt = -1f; sawWait = sawTurn = sawSpaceExit = false;
            previous = rex.transform.position; actions.Clear(); lastAction = "";
        }
        private static void Tick()
        {
            try
            {
                if (frame == Time.frameCount) return; frame = Time.frameCount;
                float elapsed = Time.time - began;
                string action = rex.CurrentAnimationSourceName, stage = rex.FrontInteractionStage;
                if (action != lastAction) { actions.Add(action); lastAction = action; }
                Require(Vector3.Distance(previous, rex.transform.position) < .4f, "Position jumped: " + Vector3.Distance(previous, rex.transform.position) + "m, " + action); previous = rex.transform.position;
                sawSpaceExit |= (bool)Get("frontSpaceExit"); sawTurn |= action == "turn 180 right";
                if (scenario == 2 && stage == "Ending" && (int)Get("frontActionIndex") == 0 && !forcedLeft)
                { Set("frontNearBite", InteractionMotionRole.BiteCloseLeft); forcedLeft = true; }
                if (scenario == 0 && stage == "Near" && (int)Get("frontActionIndex") == 0 && !forcedLeft)
                { Set("frontNearBite", InteractionMotionRole.BiteCloseRight); forcedLeft = true; }
                if ((scenario == 2 || scenario == 3) && stage == "TurnBack" && blockedAt < 0f)
                {
                    blockedAt = Time.time;
                    if (scenario == 2) settings.solidObstacles = new[] { new Bounds(rex.transform.position + Vector3.up * 2f, new Vector3(1f, 4f, 1f)) };
                    else
                    {
                        peerObject = new GameObject("Temporary peer fixture"); peerObject.transform.position = rex.transform.position + Vector3.right * 8f;
                        var body = peerObject.AddComponent<DinosaurCoreBody>(); body.bands = new[] { new DinosaurCoreBody.Band { layer = 2, bounds = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(4f, 4f, 4f)) } };
                        var peer = peerObject.AddComponent<DinosaurAgent>(); peer.enabled = false;
                        typeof(DinosaurAgent).GetField("interactionBody", Flags).SetValue(peer, body); Set("peers", new[] { rex, peer });
                    }
                }
                if ((scenario == 2 || scenario == 3) && blockedAt >= 0f)
                {
                    if (stage.StartsWith("WaitingForSpace"))
                    {
                        sawWait = true; Require(!string.IsNullOrEmpty(rex.FrontBlockingReason), "Waiting reason is missing.");
                    }
                    if (Time.time - blockedAt >= 2f)
                    {
                        settings.solidObstacles = Array.Empty<Bounds>();
                        if (peerObject != null) { UnityEngine.Object.DestroyImmediate(peerObject); Set("peers", new[] { rex }); }
                    }
                }
                if (stage == "Inactive")
                {
                    Require(sawTurn && actions.Contains("look forward 2") && actions.Exists(a => a.StartsWith("bite close")), "Exit skipped 180 turn: " + string.Join(" -> ", actions));
                    Require(actions.FindAll(a => a == "ROAR").Count == 1, "Ending sequence restarted.");
                    if (scenario == 1 || scenario == 4) Require(sawSpaceExit, "Static obstruction did not trigger early space exit.");
                    if (scenario == 2 || scenario == 3) Require(sawWait, "Temporary obstruction did not wait.");
                    report.AppendLine("PASS " + scenario + ": " + string.Join(" -> ", actions));
                    if (++scenario == 6) { Finish(null); return; } Prepare();
                }
                Require(elapsed < 50f, "Timed out: " + stage + "/" + action + ", reason=" + rex.FrontBlockingReason + ", position=" + rex.transform.position);
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            if (settings != null) settings.solidObstacles = Array.Empty<Bounds>();
            if (peerObject != null) UnityEngine.Object.DestroyImmediate(peerObject);
            File.WriteAllText("Logs/trex-reservation-result.txt", (error == null ? "PASS reservation\n" : error + "\n") + report);
            if (error == null) Debug.Log("PASS reservation\n" + report); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
