using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class TRexFrontTestSmoke
    {
        private const string Key = "JurassicWorld.FrontTestSceneSmoke";
        private static TRexFrontTestController test;
        private static DinosaurAgent rex;
        private static float started;
        private static int stage, frame;
        private static bool sawIntro, sawRun, sawEnding, sawTurn, sawReturn;
        private static Vector3 previous;
        static TRexFrontTestSmoke()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) EditorApplication.delayCall += Begin;
            };
            SceneManager.sceneLoaded += (_, __) =>
            {
                if (SessionState.GetBool(Key + ".Reset", false)) EditorApplication.delayCall += VerifyReset;
            };
        }
        public static void Run()
        {
            EditorSceneManager.OpenScene(TRexFrontTestController.ScenePath);
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Begin()
        {
            try
            {
                test = UnityEngine.Object.FindObjectOfType<TRexFrontTestController>(); rex = test.dinosaur;
                Require(UnityEngine.Object.FindObjectsOfType<DinosaurAgent>().Length == 1, "Test scene contains extra dinosaurs.");
                Require(rex.transform.position.magnitude < .001f && Vector3.Distance(test.playerCamera.transform.position, new Vector3(0f, 1.7f, 40f)) < .001f,
                    "Fixed test spawn was randomized.");
                var cameraControl = test.playerCamera.GetComponent<SmoothExplorerCamera>();
                Require(Mathf.Abs(Mathf.DeltaAngle(cameraControl.TravelHeading, 180f)) < .1f, "W does not approach the dinosaur.");
                Require(new SerializedObject(cameraControl).FindProperty("moveSpeed").floatValue == 3f, "Test camera speed is not 3m/s.");
                Require(!test.playerCamera.GetComponent<EmotivPlayerInput>(), "Test camera has Emotiv input.");
                cameraControl.enabled = false;
                stage = frame = 0; started = Time.time; previous = rex.transform.position;
                sawIntro = sawRun = sawEnding = sawTurn = sawReturn = false;
                Time.captureDeltaTime = 1f / 60f; EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Tick()
        {
            try
            {
                if (test == null || frame == Time.frameCount) return;
                int elapsedFrames = Mathf.Max(1, Time.frameCount - frame); frame = Time.frameCount;
                float elapsed = Time.time - started;
                if (Vector3.Distance(previous, rex.transform.position) >= .75f * elapsedFrames) Debug.LogWarning(
                    "Root jumped in test scene: " + Vector3.Distance(previous, rex.transform.position) + "m over " + elapsedFrames + " frames, dt=" + Time.deltaTime + ", t=" + elapsed + ", " + rex.FrontInteractionStage + "/" + rex.CurrentAnimationSourceName); previous = rex.transform.position;
                if (stage == 0)
                {
                    Require(!test.Triggered && rex.transform.position.magnitude < .001f, "Dinosaur moved or discovered player before zone entry.");
                    if (elapsed >= 1f) stage = 1;
                }
                else if (stage == 1)
                {
                    test.playerCamera.transform.position += Vector3.back * (3f * Time.deltaTime);
                    if (test.Triggered) { Require(test.IsInsideTrigger, "Interaction did not trigger inside marked zone."); stage = 2; }
                }
                else if (stage == 2)
                {
                    string action = rex.CurrentAnimationSourceName;
                    Require(action != "run head loop" && action != "Chase Roar" && action != "run to stop" && action != "idle attack" && !action.StartsWith("mama cave"), "Front sequence uses a removed action.");
                    sawIntro |= action == "idle investigate";
                    sawRun |= action == "walk head low";
                    sawEnding |= action == "look forward 2";
                    sawTurn |= action == "turn 180 right"; sawReturn |= rex.FrontInteractionStage == "ReturnWalk";
                    if (rex.FrontInteractionStage == "Follow") test.playerCamera.transform.position = rex.transform.position + Vector3.forward * 24f + Vector3.up * 1.7f;
                    if (rex.FrontInteractionStage == "Ending" || rex.FrontInteractionStage == "TurnBack") test.playerCamera.transform.position = new Vector3(200f, 2f, 200f);
                    if (sawReturn && rex.FrontInteractionStage == "Inactive")
                    {
                        Require(sawIntro && sawRun && sawEnding && sawTurn, "Test scene skipped part of real sequence.");
                        Require(test.Triggered, "Zone rearmed after sequence.");
                        EditorApplication.update -= Tick;
                        SessionState.SetBool(Key + ".Reset", true); test.Reload();
                    }
                }
                Require(elapsed < 115f, "Test scene sequence timed out: " + rex.FrontInteractionStage + "/" + rex.CurrentAnimationSourceName);
            }
            catch (Exception error) { Finish(error); }
        }
        private static void VerifyReset()
        {
            try
            {
                var fresh = UnityEngine.Object.FindObjectOfType<TRexFrontTestController>();
                Require(fresh != null && !fresh.Triggered && fresh.dinosaur.transform.position.magnitude < .001f
                    && Vector3.Distance(fresh.playerCamera.transform.position, new Vector3(0f, 1.7f, 40f)) < .001f,
                    "R reload did not restore actor/player/zone.");
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); SessionState.SetBool(Key + ".Reset", false); Time.captureDeltaTime = 0f;
            Directory.CreateDirectory("Logs");
            string result = error == null ? "PASS test scene: fixed spawns; waiting actor; W direction/speed; zone entry; actual complete front sequence and movement; single-shot gate; R scene reload resets actor/player/trigger. Short smoke; visual Play feedback required." : error.ToString();
            File.WriteAllText("Logs/trex-front-test-result.txt", result);
            if (error == null) Debug.Log(result); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}


