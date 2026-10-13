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
    public static class TRexNearInteractionSmoke
    {
        private const string Key = "JurassicWorld.NearSmoke";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static DinosaurAgent rex;
        private static Camera player;
        private static DinosaurInteractionSettings settings;
        private static bool wallPlaced;
        private static int frame, scenario;
        private static float began, nearAt, actionAt, leaveAt, expectedWait, idleAt;
        private static bool movedAway, sawIdle, resumed, sawNear;
        private static string previous;
        private static readonly List<string> actions = new List<string>();
        private static readonly List<string> results = new List<string>();
        private static object Get(string name) => typeof(DinosaurAgent).GetField(name, Flags).GetValue(rex);
        private static void Set(string name, object value) => typeof(DinosaurAgent).GetField(name, Flags).SetValue(rex, value);
        private static object Call(string name, params object[] args) => typeof(DinosaurAgent).GetMethod(name, Flags).Invoke(rex, args);
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static TRexNearInteractionSmoke()
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
                var test = UnityEngine.Object.FindObjectOfType<TRexFrontTestController>();
                rex = test.dinosaur; player = test.playerCamera;
                settings = UnityEngine.Object.FindObjectOfType<DinosaurInteractionSettings>();
                test.enabled = false; player.GetComponent<SmoothExplorerCamera>().enabled = false;
                Time.captureDeltaTime = 1f / 60f; frame = scenario = 0; results.Clear();
                player.transform.position = new Vector3(0f, 1.7f, 31f);
                Check((bool)Call("CanSeePlayer"), "Forward detection does not reach 31m.");
                Prepare(); EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Prepare()
        {
            if ((bool)Get("frontActive")) Call("FinishFrontInteraction");
            rex.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, -rex.VisualYaw, 0f));
            player.transform.position = new Vector3(0f, 1.7f, 24f);
            Call("BeginFrontInteraction"); Set("frontTestWaiting", false); Call("BeginFrontFollow", false);
            began = Time.time; nearAt = actionAt = idleAt = -1f; movedAway = sawIdle = resumed = sawNear = false;
            previous = ""; actions.Clear();
            wallPlaced = false; settings.solidObstacles = Array.Empty<Bounds>();
            Check(rex.CurrentAnimationSourceName == "walk head low", "Follow did not start walking.");
        }
        private static void Tick()
        {
            try
            {
                if (frame == Time.frameCount) return; frame = Time.frameCount;
                float elapsed = Time.time - began;
                string stage = rex.FrontInteractionStage, action = rex.CurrentAnimationSourceName;
                if (action != previous)
                {
                    if (scenario == 0 && previous == "look forward 2")
                        Check(Time.time - actionAt >= 1.95f && Time.time - actionAt <= 2.1f, "Look forward 2 did not play 2 seconds at 1x.");
                    actions.Add(action); previous = action; actionAt = Time.time;
                }
                Check(action != "Chase Roar" && action != "run to stop" && action != "idle attack" && !action.StartsWith("mama cave"), "Removed action still played.");
                if (!sawNear && stage == "Follow")
                    player.transform.position = rex.transform.position + Vector3.forward * (elapsed < 1f || scenario == 6 ? 24f : 9.8f) + Vector3.up * 1.7f;
                if (scenario == 6)
                {
                    if (!wallPlaced && elapsed >= 1f && stage == "Follow")
                    {
                        float front = 0f;
                        foreach (var band in ((DinosaurCoreBody)Get("interactionBody")).bands) front = Mathf.Max(front, band.bounds.max.z);
                        settings.solidObstacles = new[] { new Bounds(rex.transform.position + Vector3.forward * (front + 1.3f) + Vector3.up * 3f, new Vector3(40f, 6f, 2f)) };
                        wallPlaced = true;
                    }
                    if (stage == "Ending" || stage == "TurnBack" || stage == "ReturnWalk")
                    { settings.solidObstacles = Array.Empty<Bounds>(); player.transform.position = new Vector3(200f, 2f, 200f); }
                    if (wallPlaced && stage == "Inactive")
                    {
                        Check(actions.Contains("ROAR") && actions.Contains("look forward 2"), "Blocked follow did not stop and end.");
                        results.Add("CASE_PASS blocked: " + string.Join(" -> ", actions)); Finish(null); return;
                    }
                }
                if (stage == "Near")
                {
                    if (!sawNear) { sawNear = true; nearAt = Time.time; }
                    if (!movedAway) player.transform.position = rex.transform.position + Vector3.forward * 9.8f + Vector3.up * 1.7f;
                    string target = scenario == 1 || scenario == 5 ? "ROAR" : scenario == 3 ? "bite close" : "look forward 2";
                    if (scenario > 0 && !movedAway && action.StartsWith(target, StringComparison.Ordinal) && (scenario != 4 || actions.FindAll(a => a == "look forward 2").Count >= 2) && Time.time - actionAt >= .3f)
                    {
                        movedAway = true; leaveAt = Time.time;
                        var motion = Array.Find(((DinosaurInteractionProfile)Get("interactionProfile")).motions, m => m.sourceName == action);
                        expectedWait = scenario == 2 || scenario == 4 ? 0f : motion.clip.length - (float)Get("frontActionTime");
                        player.transform.position = scenario == 5 ? new Vector3(200f, 2f, 200f)
                            : rex.transform.position + Vector3.forward * 20f + Vector3.up * 1.7f;
                    }
                    if (action == "look forward 2" && Time.time - actionAt < .02f) idleAt = Time.time;
                    sawIdle |= action == "look forward 2";
                }
                if (sawNear && stage == "Follow")
                {
                    Check(scenario > 0 && movedAway, "Near player unexpectedly resumed follow.");
                    Check(rex.CurrentAnimationSourceName == "walk head low", "Resume did not return to walk loop.");
                    Check((float)Get("frontFollowElapsed") > .5f, "Resume reset accumulated follow time.");
                    if (!resumed)
                    {
                        Check(Time.time - leaveAt >= expectedWait - .05f, "Stopped action was interrupted early.");
                        if (scenario == 2 || scenario == 4) Check(Time.time - leaveAt < .1f, "Look forward 2 did not interrupt promptly.");
                    }
                    if (!resumed)
                    {
                        results.Add("RESUME_PASS stage=" + scenario + ": " + string.Join(" -> ", actions));
                        resumed = true; Set("frontFollowElapsed", 29.8f);
                    }
                    player.transform.position = rex.transform.position + Vector3.forward * 24f + Vector3.up * 1.7f;
                }
                if (stage == "TurnBack" || stage == "ReturnWalk")
                {
                    player.transform.position = new Vector3(200f, 2f, 200f);
                    if (scenario == 0)
                    {
                        Check(actions.Contains("ROAR") && sawIdle && actions.Exists(a => a.StartsWith("bite close")), "Near sequence skipped an action.");
                        Check(!actions.Contains("mama cave bite"), "Near completion repeated old ending.");
                        if (stage == "TurnBack" && action != "turn 180 right")
                            Check(Time.time - idleAt >= 1.95f && Time.time - idleAt <= 2.1f, "Look forward 2 did not last 2 seconds.");
                    }
                }
                if (sawNear && stage == "Inactive")
                {
                    Check(scenario == 0 || scenario == 5 || resumed, "Ended instead of resuming after player walked away.");
                    if (scenario == 5)
                    {
                        Check(actions.FindAll(a => a == "ROAR").Count == 1,
                            "Lost target during Near repeated stopping/roar.");
                        Check(actions.Exists(a => a.StartsWith("bite close")) && !resumed, "Lost target did not use ending sequence.");
                    }
                    results.Add("CASE_PASS " + scenario + ": " + string.Join(" -> ", actions));
                    ++scenario;
                    Prepare();
                }
                Check(elapsed < 70f, "Near test timed out: " + scenario + " " + stage + "/" + action);
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            string result = error == null ? "PASS near: direct ROAR; both looks 2 seconds; leave during roar/bite/both looks resumes; lost/blocked endings once; cumulative timeout preserved; 32m detection.\n" + string.Join("\n", results) : error + "\n" + string.Join("\n", results);
            File.WriteAllText("Logs/trex-near-result.txt", result);
            if (error == null) Debug.Log(result); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}


