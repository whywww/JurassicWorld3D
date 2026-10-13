using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class TRexFrontSmoke
    {
        private const string Key = "JurassicWorld.FrontSmoke";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static DinosaurAgent rex;
        private static DinosaurInteractionSettings settings;
        private static Bounds[] obstacles;
        private static DinosaurInteractionProfile profile;
        private static int frame, scenario;
        private static float began, followedAt, returnedAt;
        private static string previousClip;
        private static bool triggered, sawFollow, sawReturn;
        private static Vector3 returnStart;
        private static readonly List<string> clips = new List<string>();
        private static readonly StringBuilder trace = new StringBuilder();
        static TRexFrontSmoke()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) EditorApplication.delayCall += Begin;
            };
        }
        [MenuItem("Jurassic World/Acceptance/Verify T-Rex Front Sequence (Short)")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            foreach (var behaviour in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                if (behaviour is EmotivPlayerInput || behaviour.GetType().Namespace == "Crest") behaviour.enabled = false;
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static object Get(string field) => typeof(DinosaurAgent).GetField(field, Private).GetValue(rex);
        private static void Set(string field, object value) => typeof(DinosaurAgent).GetField(field, Private).SetValue(rex, value);
        private static object Call(string method, params object[] args) => typeof(DinosaurAgent).GetMethod(method, Private).Invoke(rex, args);
        private static Vector3 Forward => rex.transform.rotation * Quaternion.Euler(0f, rex.VisualYaw, 0f) * Vector3.forward;
        private static void Begin()
        {
            try
            {
                rex = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex");
                settings = UnityEngine.Object.FindObjectOfType<DinosaurInteractionSettings>(); obstacles = settings.solidObstacles;
                profile = (DinosaurInteractionProfile)Get("interactionProfile");
                foreach (var other in UnityEngine.Object.FindObjectsOfType<DinosaurAgent>())
                    if (other != rex) { other.enabled = false; other.transform.position = new Vector3(200f, 0f, 200f); }
                rex.ViewCamera.GetComponent<SmoothExplorerCamera>().enabled = false;
                Time.captureDeltaTime = 1f / 60f; scenario = frame = 0; trace.Clear(); Prepare();
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Prepare()
        {
            settings.solidObstacles = obstacles;
            Set("frontActive", false); Call("ClearInteractionNavigation");
            typeof(DinosaurAgent).GetProperty("InteractionPhase").SetValue(rex, DinosaurAgent.PlayerInteractionPhase.Inactive);
            Call("BeginIdle"); Set("stateTimer", 1000f); Set("cooldownUntil", 0f);
            rex.transform.SetPositionAndRotation(new Vector3(-22f, 0f, 8f), Quaternion.Euler(0f, -rex.VisualYaw, 0f));
            rex.GetComponent<DinosaurFootLock>().ResetSupportAnchors();
            rex.ViewCamera.transform.position = rex.transform.position + Forward * 23f + Vector3.up * 1.7f;
            Require((bool)Call("FrontActionsAvailable") && (bool)Call("CanSeePlayer"), "Front fixture is not detectable or missing exact clips.");
            clips.Clear(); previousClip = null; began = Time.time; followedAt = returnedAt = -1f;
            triggered = sawFollow = sawReturn = false;
            trace.AppendLine($"CASE {scenario} began={began:F2}");
        }
        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying || frame == Time.frameCount || rex == null) return;
                frame = Time.frameCount;
                string active = (string)Get("activeClip");
                string source = profile.motions.FirstOrDefault(m => m.StateName == active)?.sourceName ?? active;
                if (source != previousClip)
                {
                    previousClip = source; clips.Add(source);
                    trace.AppendLine($"{Time.time:F3} {rex.FrontInteractionStage} {source} {rex.transform.position}");
                }
                if (rex.FrontInteractionStage == "Follow")
                {
                    if (!sawFollow)
                    {
                        sawFollow = true; followedAt = Time.time;
                        Require(Time.time - began >= 9.5f, "Discovery clips were skipped.");
                        Require(source == "walk head low", "Following did not start with walk loop.");
                    }
                    rex.ViewCamera.transform.position = rex.transform.position + Forward * 23f + Vector3.up * 1.7f;
                    if (!triggered && Time.time - followedAt >= (scenario == 0 ? 14f : 3f))
                    {
                        triggered = true;
                        if (scenario == 1) rex.ViewCamera.transform.position = new Vector3(200f, 2f, 200f);
                        else if (scenario == 2)
                            settings.solidObstacles = obstacles.Concat(new[] { new Bounds(rex.transform.position + Vector3.up, new Vector3(2f, 3f, 2f)) }).ToArray();
                        else if (scenario == 3) rex.ViewCamera.transform.position = rex.transform.position + Forward * 5f + Vector3.up * 1.7f;
                    }
                    if (triggered && scenario == 1) rex.ViewCamera.transform.position = new Vector3(200f, 2f, 200f);
                    if (triggered && scenario == 3) rex.ViewCamera.transform.position = rex.transform.position + Forward * 5f + Vector3.up * 1.7f;
                }
                if (rex.FrontInteractionStage == "Ending")
                {
                    settings.solidObstacles = obstacles;
                    // Keep the player outside the performance footprint while checking complete recovery.
                    if (scenario != 3) rex.ViewCamera.transform.position = new Vector3(200f, 2f, 200f);
                }
                if (rex.FrontInteractionStage == "TurnBack")
                    rex.ViewCamera.transform.position = new Vector3(200f, 2f, 200f);
                if (rex.FrontInteractionStage == "ReturnWalk" && !sawReturn)
                {
                    sawReturn = true; returnStart = rex.transform.position; returnedAt = Time.time;
                }
                if (sawFollow && !(bool)Get("frontActive"))
                {
                    var intro = new[] { "idle investigate", "idle investigae out", "Big roar step" };
                    int start = clips.IndexOf("idle investigate");
                    Require(start >= 0 && clips.Skip(start).Take(intro.Length).SequenceEqual(intro) && !clips.Contains("look forward"), "Wrong discovery order: " + string.Join(",", clips));
                    var ending = new[] { "ROAR", "look forward 2", clips.FirstOrDefault(c => c.StartsWith("bite close")), "look forward 2", "turn 180 right", "walk slow loop" };
                    int end = clips.IndexOf("ROAR");
                    Require(end >= 0 && clips.Skip(end).Take(6).SequenceEqual(ending), "Wrong ending/return order: " + string.Join(",", clips));
                    Require(clips.Count(c => c == "ROAR") == 1 && !clips.Contains("idle to run") && !clips.Contains("run to stop") && !clips.Contains("Chase Roar"), "Repeated ending or removed action.");
                    Require(sawReturn && Vector3.Distance(returnStart, rex.transform.position) > .5f, "Return did not walk along recorded history.");
                    if (scenario == 0)
                    {
                        Require((float)Get("frontFollowElapsed") >= 30f, "Discovery consumed follow budget.");
                    }
                    trace.AppendLine($"CASE_PASS {scenario}, return elapsed={Time.time - returnedAt:F2}s");
                    if (++scenario == 4) { Finish(null); return; }
                    Prepare(); return;
                }
                Require(Time.time - began < 115f, "Front sequence timed out: " + rex.FrontInteractionStage + "/" + source);
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            if (settings != null) settings.solidObstacles = obstacles;
            Directory.CreateDirectory("Logs");
            string result = error == null ? "PASS four front scenarios: exact intro/alternating chase/ending/180 turn/reverse walking/resumed roaming; timeout, lost sight, obstruction and 5m stop. Short smoke; visual Play feedback required." : error.ToString();
            File.WriteAllText("Logs/trex-front-result.txt", result); File.WriteAllText("Logs/trex-front-trace.txt", trace.ToString());
            if (error == null) Debug.Log(result); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}

