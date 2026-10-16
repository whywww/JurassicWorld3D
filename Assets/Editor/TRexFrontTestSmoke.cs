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
        private static float started, cooldownEnds;
        private static int stage, frame, completed;
        private static bool sawIntro, sawRun, sawEnding, sawTurn, sawReturn;
        private static Vector3 previous;
        private static Vector3 initialPlayer;
        private static Vector3 sequenceDirection;
        private static Quaternion initialLayoutRotation;
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
            var control = UnityEngine.Object.FindObjectOfType<TRexFrontTestController>();
            SessionState.SetInt(Key + ".SavedSide", (int)control.initialSide);
            SessionState.SetFloat(Key + ".SavedAngle", control.sideAngle);
            // Test Front independently of whichever side the user has saved for manual debugging.
            control.initialSide = TRexFrontTestController.ApproachSide.Front;
            EditorUtility.SetDirty(control);
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Begin()
        {
            try
            {
                test = UnityEngine.Object.FindObjectOfType<TRexFrontTestController>(); rex = test.dinosaur;
                initialPlayer = test.playerCamera.transform.position;
                initialLayoutRotation = test.approachLayout.rotation;
                Require(UnityEngine.Object.FindObjectsOfType<DinosaurAgent>().Length == 1, "Test scene contains extra dinosaurs.");
                Require(rex.transform.position.magnitude < .001f && Vector3.Distance(test.approachLayout.InverseTransformPoint(initialPlayer), new Vector3(0f, 1.7f, 40f)) < .001f,
                    "Fixed test spawn was randomized.");
                var cameraControl = test.playerCamera.GetComponent<SmoothExplorerCamera>();
                Require(Vector3.Dot(Quaternion.Euler(0f, cameraControl.TravelHeading, 0f) * Vector3.forward, -test.approachLayout.forward) > .999f,
                    "W does not approach the dinosaur.");
                Require(new SerializedObject(cameraControl).FindProperty("moveSpeed").floatValue == 3f, "Test camera speed is not 3m/s.");
                Require(!test.playerCamera.GetComponent<EmotivPlayerInput>(), "Test camera has Emotiv input.");
                VerifyApproachLayouts();
                cameraControl.enabled = false;
                stage = frame = completed = 0; started = Time.time; previous = rex.transform.position;
                Require(rex.IsWaitingForInteraction && rex.InteractionCooldownRemaining == 0f, "Initial state is not ready.");
                VerifyAngles();
                VerifyDetection();
                sequenceDirection = test.approachLayout.forward;
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
                    Require(!test.Triggered && rex.transform.position.magnitude < .001f, "Dinosaur moved or discovered an out-of-range player.");
                    if (elapsed >= 1f) stage = 1;
                }
                else if (stage == 1)
                {
                    test.playerCamera.transform.position = test.approachLayout.TransformPoint(new Vector3(3f, 1.7f, 28f));
                    if (test.Triggered) { Require(!test.IsInsideTrigger, "Natural detection test must be outside the yellow marker."); stage = 2; }
                }
                else if (stage == 2)
                {
                    string action = rex.CurrentAnimationSourceName;
                    Require(action != "run head loop" && action != "Chase Roar" && action != "run to stop" && action != "idle attack" && !action.StartsWith("mama cave"), "Sequence uses a removed action.");
                    sawIntro |= action == (completed == 0 ? "idle investigate" : completed == 1 ? "look left" : "look right");
                    sawRun |= action == "walk head low";
                    sawEnding |= action == "look forward 2";
                    sawTurn |= action == "turn 180 right"; sawReturn |= rex.FrontInteractionStage == "ReturnWalk";
                    if (rex.FrontInteractionStage == "Follow" || rex.FrontInteractionStage == "SideStartRun")
                        test.playerCamera.transform.position = rex.transform.position + sequenceDirection * 24f + Vector3.up * 1.7f;
                    if (rex.FrontInteractionStage == "Ending" || rex.FrontInteractionStage == "TurnBack") test.playerCamera.transform.position = new Vector3(200f, 2f, 200f);
                    if (sawReturn && rex.FrontInteractionStage == "Inactive")
                    {
                        Require(sawIntro && sawRun && sawEnding && sawTurn, "Test scene skipped part of real sequence.");
                        completed++;
                        Require(rex.InteractionCooldownRemaining > 9.9f && rex.InteractionCooldownRemaining <= 10f,
                            "Sequence did not start a ten-second cooldown.");
                        if (completed == 3)
                        {
                            EditorApplication.update -= Tick;
                            SessionState.SetBool(Key + ".Reset", true); test.Reload();
                        }
                        else { cooldownEnds = Time.time + rex.InteractionCooldownRemaining; stage = 3; }
                    }
                }
                else if (stage == 3)
                {
                    Vector3 forward = rex.transform.rotation * Quaternion.Euler(0f, rex.VisualYaw, 0f) * Vector3.forward;
                    sequenceDirection = Quaternion.Euler(0f, completed == 1 ? -45f : 90f, 0f) * forward;
                    test.playerCamera.transform.position = rex.transform.position + sequenceDirection * 31f + Vector3.up * 1.7f;
                    if (Time.time < cooldownEnds + .25f)
                        Require(rex.FrontTestInteractionCount == completed, "Interaction triggered during cooldown or before a fresh detection hold.");
                    if (rex.FrontTestInteractionCount == completed + 1)
                    {
                        Require(Time.time >= cooldownEnds + .3f - Time.deltaTime - .001f, "Cooldown accumulated visible time.");
                        Require(rex.IsSideTestSequence, "Repeated side approach did not choose a side sequence.");
                        sawIntro = sawRun = sawEnding = sawTurn = sawReturn = false; stage = 2;
                    }
                }
                Require(elapsed < 360f, "Test scene sequence timed out: " + rex.FrontInteractionStage + "/" + rex.CurrentAnimationSourceName);
            }
            catch (Exception error) { Finish(error); }
        }
        private static void VerifyReset()
        {
            try
            {
                var fresh = UnityEngine.Object.FindObjectOfType<TRexFrontTestController>();
                var savedSide = (TRexFrontTestController.ApproachSide)SessionState.GetInt(Key + ".SavedSide", 0);
                float savedAngle = savedSide == TRexFrontTestController.ApproachSide.Front ? 0f
                    : Mathf.Clamp(SessionState.GetFloat(Key + ".SavedAngle", 45f), 20f, 90f);
                Quaternion savedLayout = initialLayoutRotation * Quaternion.Euler(0f,
                    savedSide == TRexFrontTestController.ApproachSide.Left ? -savedAngle : savedAngle, 0f);
                Vector3 savedPlayer = savedLayout * new Vector3(0f, 1.7f, 40f);
                Require(fresh != null && !fresh.Triggered && fresh.dinosaur.IsWaitingForInteraction
                    && fresh.dinosaur.InteractionCooldownRemaining == 0f && fresh.dinosaur.transform.position.magnitude < .001f
                    && Vector3.Distance(fresh.playerCamera.transform.position, savedPlayer) < .001f
                    && fresh.ActiveSide == savedSide && Mathf.Abs(fresh.ActiveAngle - savedAngle) < .001f
                    && Quaternion.Angle(fresh.approachLayout.rotation, savedLayout) < .01f,
                    "R reload did not restore actor/player/zone.");
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }
        private static void VerifyAngles()
        {
            Vector3 forward = rex.transform.rotation * Quaternion.Euler(0f, rex.VisualYaw, 0f) * Vector3.forward;
            foreach (float angle in new[] { 0f, 90f, 180f })
            {
                test.playerCamera.transform.position = rex.transform.position
                    + Quaternion.Euler(0f, angle, 0f) * forward * 28f + Vector3.up * 1.7f;
                Require(Mathf.Abs(rex.PlayerInteractionAngle - angle) < .01f, "Incorrect live player angle: " + angle);
            }
            test.playerCamera.transform.position = initialPlayer;
        }
        private static void VerifyDetection()
        {
            var canSee = typeof(DinosaurAgent).GetMethod("CanSeePlayer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Vector3 forward = rex.transform.rotation * Quaternion.Euler(0f, rex.VisualYaw, 0f) * Vector3.forward;
            foreach (float angle in new[] { 0f, 20f, 21f, -45f, 89.9f, 90f, 90.1f, 180f })
            foreach (float distance in new[] { 31.9f, 32.1f })
            {
                test.playerCamera.transform.position = rex.transform.position
                    + Quaternion.Euler(0f, angle, 0f) * forward * distance + Vector3.up * 1.7f;
                bool expected = Mathf.Abs(angle) <= 90f && distance <= 32f;
                Require((bool)canSee.Invoke(rex, null) == expected, $"Detection boundary mismatch at {angle}deg / {distance}m.");
            }
            test.playerCamera.transform.position = rex.transform.position + forward * 28f + Vector3.up * 1.7f;
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                obstacle.transform.position = Vector3.Lerp(rex.transform.position + Vector3.up * rex.ViewFocusHeight,
                    test.playerCamera.transform.position, .5f);
                obstacle.transform.localScale = Vector3.one * 2f;
                Physics.SyncTransforms();
                Require(!(bool)canSee.Invoke(rex, null), "An occluded player passed detection.");
            }
            finally { UnityEngine.Object.DestroyImmediate(obstacle); Physics.SyncTransforms(); }
            test.playerCamera.transform.position = initialPlayer;
        }
        private static void VerifyApproachLayouts()
        {
            foreach (TRexFrontTestController.ApproachSide side in Enum.GetValues(typeof(TRexFrontTestController.ApproachSide)))
            foreach (float angle in new[] { 20f, 45f, 90f })
            {
                var holder = new GameObject("Approach layout smoke");
                holder.SetActive(false);
                try
                {
                    var probe = holder.AddComponent<TRexFrontTestController>();
                    probe.dinosaur = rex; probe.initialSide = side; probe.sideAngle = angle;
                    probe.approachLayout = UnityEngine.Object.Instantiate(test.approachLayout, holder.transform);
                    probe.playerCamera = new GameObject("Probe camera").AddComponent<Camera>();
                    probe.playerCamera.transform.SetParent(holder.transform, false);
                    // Exercise actual initialization without enabling Update or triggering the shared actor.
                    probe.enabled = false;
                    holder.SetActive(true);
                    float signedAngle = side == TRexFrontTestController.ApproachSide.Front ? 0f
                        : side == TRexFrontTestController.ApproachSide.Left ? -angle : angle;
                    Vector3 visualForward = rex.transform.rotation * Quaternion.Euler(0f, rex.VisualYaw, 0f) * Vector3.forward;
                    Require(Mathf.Abs(Mathf.DeltaAngle(Vector3.SignedAngle(visualForward, probe.approachLayout.forward, Vector3.up), signedAngle)) < .01f,
                        "Approach side/angle mismatch: " + side + "/" + angle);
                    Require(Vector3.Distance(probe.approachLayout.InverseTransformPoint(probe.playerCamera.transform.position), new Vector3(0f, 1.7f, 40f)) < .001f,
                        "Approach spawn mismatch.");
                    Require(Vector3.Dot(Vector3.ProjectOnPlane(probe.playerCamera.transform.forward, Vector3.up).normalized, -probe.approachLayout.forward) > .999f,
                        "Approach camera is not facing the actor.");
                    Require(!probe.IsInsideTrigger, "Spawn is inside trigger.");
                    foreach (Vector3 outside in new[] { new Vector3(0f, 1.7f, 29.9f), new Vector3(0f, 1.7f, 32.1f),
                        new Vector3(-2.1f, 1.7f, 31f), new Vector3(2.1f, 1.7f, 31f) })
                    {
                        probe.playerCamera.transform.position = probe.approachLayout.TransformPoint(outside);
                        Require(!probe.IsInsideTrigger, "Outside point triggers interaction.");
                    }
                    probe.playerCamera.transform.position = probe.approachLayout.TransformPoint(new Vector3(0f, 1.7f, 31f));
                    Require(probe.IsInsideTrigger, "Rotated yellow zone does not trigger.");
                    Require(probe.approachLayout.childCount == 4, "Approach markers are missing.");
                    Require(Vector3.Distance(probe.approachLayout.Find("Trigger Zone 30-32m").position,
                        probe.approachLayout.TransformPoint(new Vector3(0f, .015f, 31f))) < .001f, "Visible zone is misaligned.");
                    probe.initialSide = TRexFrontTestController.ApproachSide.Front; probe.sideAngle = 25f;
                    Require(Mathf.Abs(probe.ActiveAngle - Mathf.Abs(signedAngle)) < .001f && probe.ActiveSide == side,
                        "Runtime Inspector changes altered the active configuration.");
                }
                finally { UnityEngine.Object.DestroyImmediate(holder); }
            }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); SessionState.SetBool(Key + ".Reset", false); Time.captureDeltaTime = 0f;
            Directory.CreateDirectory("Logs");
            string result = error == null ? "PASS test scene: front/left/right marker layouts; live 0/90/180 degree angles; 32m/90deg detection boundaries and occlusion; natural detection outside yellow marker; three complete front/left/right sequences selected by live bearing; ten-second cooldown and fresh detection hold; R resets actor/player/configuration/cooldown. Visual Play feedback required." : error.ToString();
            File.WriteAllText("Logs/trex-front-test-result.txt", result);
            if (error == null) Debug.Log(result); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}


