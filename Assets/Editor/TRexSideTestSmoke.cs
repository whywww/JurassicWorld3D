using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class TRexSideTestSmoke
    {
        private const string Key = "JurassicWorld.SideTestSmoke";
        private const int CaseCount = 14;
        private static TRexFrontTestController test;
        private static DinosaurAgent rex;
        private static int frame, index;
        private static float started, loopStarted, resumedAt, maximumStep, blockStarted;
        private static Vector3 previous, blockPosition;
        private static bool requestedResume, resumed, sawReturn, blocked, cleared;
        private static readonly List<string> actions = new List<string>();
        private static readonly HashSet<string> captures = new HashSet<string>();
        private static string lastAction;
        private static DinosaurInteractionSettings settings;
        private static Bounds[] originalObstacles;
        private static Transform cog, head;
        private static Vector3 cogFacing, headFacing;
        private static readonly StringBuilder headingSamples = new StringBuilder();
        private static bool baseline;
        private static float startRunBegan, startRunDistance, startRunRate;
        private static Vector3 startRunPrevious;
        private static bool startRunMeasured;
        private static bool blendBaseline, sawBlend, finishedBlend;
        private static float previousBlendWalkTime, waitingBlendTime, waitingWalkTime;
        private static readonly StringBuilder blendSamples = new StringBuilder();
        private static Camera blendObserver;
        private static float blendShotBegan;
        private static int blendShot;
        private static string handoffError;
        private static bool handoffObserved, lastLeftPlant, lastRightPlant;
        private static float handoffHeadYaw, handoffSideSign, handoffFootSign, maximumHeadRecoil, maximumFootSlip;
        private static Vector3 lastLeftSole, lastRightSole;
        private static object AgentField(string name) => typeof(DinosaurAgent).GetField(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(rex);

        public static void RunBeforeWalkBlend()
        {
            SessionState.SetBool(Key + ".BlendBaseline", true);
            Run();
        }
        public static void RunWalkBlendPreview()
        {
            SessionState.SetBool(Key + ".BlendPreview", true);
            Run();
        }

        public static void RunBeforeYawFix()
        {
            SessionState.SetBool(Key + ".Baseline", true);
            Run();
        }

        static TRexSideTestSmoke()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                if (!SessionState.GetBool(Key, false)) return;
                if (change == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += Begin;
                if (change == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += StartCase;
            };
        }

        [MenuItem("Jurassic World/Tests/Verify Side Interaction Sequence")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save the current scene before the side smoke test.");
            SessionState.SetBool(Key, true); SessionState.SetInt(Key + ".Case", 0);
            EditorSceneManager.OpenScene(TRexFrontTestController.ScenePath);
            var savedControl = UnityEngine.Object.FindObjectOfType<TRexFrontTestController>();
            SessionState.SetFloat(Key + ".SavedAngle", savedControl != null ? savedControl.sideAngle : 42.4f);
            SessionState.SetInt(Key + ".SavedSide", savedControl != null ? (int)savedControl.initialSide : 1);
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/trex-side-test-result.txt", "RUNNING\n");
            StartCase();
        }

        private static void StartCase()
        {
            if (SessionState.GetInt(Key + ".Case", 0) >= (SessionState.GetBool(Key + ".BlendBaseline", false)
                || SessionState.GetBool(Key + ".BlendPreview", false) ? 6 : CaseCount)) { Finish(null); return; }
            EditorSceneManager.OpenScene(TRexFrontTestController.ScenePath);
            var control = UnityEngine.Object.FindObjectOfType<TRexFrontTestController>();
            int current = SessionState.GetInt(Key + ".Case", 0);
            control.initialSide = current % 2 == 0 ? TRexFrontTestController.ApproachSide.Left : TRexFrontTestController.ApproachSide.Right;
            control.sideAngle = current < 6 ? new[] { 21f, 45f, 90f }[current / 2] : 45f;
            if (current == 11)
            {
                control.initialSide = (TRexFrontTestController.ApproachSide)SessionState.GetInt(Key + ".SavedSide", 1);
                control.sideAngle = SessionState.GetFloat(Key + ".SavedAngle", 42.4f);
                if (control.initialSide == TRexFrontTestController.ApproachSide.Front) control.initialSide = TRexFrontTestController.ApproachSide.Left;
            }
            // These case settings exist only in the editor's Play snapshot; the saved scene is untouched.
            EditorUtility.SetDirty(control);
            EditorApplication.isPlaying = true;
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private static void Begin()
        {
            try
            {
                index = SessionState.GetInt(Key + ".Case", 0);
                test = UnityEngine.Object.FindObjectOfType<TRexFrontTestController>(); rex = test.dinosaur;
                baseline = SessionState.GetBool(Key + ".Baseline", false);
                blendBaseline = SessionState.GetBool(Key + ".BlendBaseline", false);
                if (!baseline) TRexSideYawAudit.Verify(rex);
                cog = TRexTurnCalibration.Bone(rex.transform, "jt_Cog_C");
                head = TRexTurnCalibration.Bone(rex.transform, "jt_Head_C");
                Vector3 visualForward = rex.transform.rotation * Quaternion.Euler(0f, rex.VisualYaw, 0f) * Vector3.forward;
                cogFacing = cog.InverseTransformDirection(visualForward);
                headFacing = head.InverseTransformDirection(visualForward);
                headingSamples.Clear();
                headingSamples.AppendLine("elapsed,stage,action,clip_time,target_yaw,actor_yaw,body_local_twist_yaw,body_world_yaw,head_world_yaw");
                test.playerCamera.GetComponent<SmoothExplorerCamera>().enabled = false;
                settings = UnityEngine.Object.FindObjectOfType<DinosaurInteractionSettings>();
                originalObstacles = settings.solidObstacles;
                startRunRate = index == 14 || index == 15 ? 1f : .8f;
                var serializedRex = new SerializedObject(rex);
                Require(Mathf.Abs(serializedRex.FindProperty("sideStartRunPlaybackSpeed").floatValue - .8f) < .001f,
                    "Side start default is not 0.8x.");
                serializedRex.FindProperty("sideStartRunPlaybackSpeed").floatValue = startRunRate;
                Require(Mathf.Abs(serializedRex.FindProperty("sideWalkBlendDuration").floatValue - .3f) < .001f,
                    "Side walk blend default is not 0.3s.");
                if (blendBaseline) serializedRex.FindProperty("sideWalkBlendDuration").floatValue = 0f;
                Require(Mathf.Abs(serializedRex.FindProperty("sideRoarHandoffDuration").floatValue - .4f) < .001f,
                    "Side roar handoff default is not 0.4s.");
                if (blendBaseline) serializedRex.FindProperty("sideRoarHandoffDuration").floatValue = 0f;
                serializedRex.ApplyModifiedPropertiesWithoutUndo();
                startRunBegan = -1f; startRunDistance = 0f; startRunMeasured = false;
                sawBlend = finishedBlend = false; previousBlendWalkTime = 0f;
                waitingBlendTime = waitingWalkTime = -1f;
                blendShotBegan = -1f; blendShot = 0; blendObserver = null;
                blendSamples.Clear(); blendSamples.AppendLine("time,stage,source_time,walk_time,walk_weight,speed,left_contact,right_contact,left_error,right_error,head_yaw,root_yaw,handoff_age,left_x,left_z,right_x,right_z");
                handoffError = null; handoffObserved = lastLeftPlant = lastRightPlant = false;
                maximumHeadRecoil = maximumFootSlip = 0f;
                rex.gameObject.AddComponent<TRexSideHandoffProbe>().observe = ObserveSideHandoff;
                Require(test.ActiveSide != TRexFrontTestController.ApproachSide.Front, "Side configuration did not reach Play.");
                started = Time.time; loopStarted = resumedAt = -1f; frame = 0;
                requestedResume = resumed = sawReturn = blocked = cleared = false;
                maximumStep = 0f; previous = rex.transform.position;
                blockStarted = 0f;
                actions.Clear(); captures.Clear(); lastAction = null;
                Time.captureDeltaTime = 1f / 60f;
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Tick()
        {
            try
            {
                if (test == null || frame == Time.frameCount) return;
                Require(handoffError == null, handoffError);
                int frames = Mathf.Max(1, Time.frameCount - frame); frame = Time.frameCount;
                float elapsed = Time.time - started;
                maximumStep = Mathf.Max(maximumStep, Vector3.Distance(previous, rex.transform.position) / frames);
                Require(maximumStep < .5f, "Root jump in side test: " + maximumStep);
                previous = rex.transform.position;
                string stage = rex.FrontInteractionStage, action = rex.CurrentAnimationSourceName;
                bool blending = AgentField("sideWalkMotion") != null;
                if (blending)
                {
                    sawBlend = true;
                    float walkTime = (float)AgentField("sideWalkTime"), blendTime = (float)AgentField("sideWalkBlendTime");
                    if ((bool)AgentField("frontWaiting"))
                    {
                        if (waitingBlendTime >= 0f) Require(Mathf.Abs(blendTime - waitingBlendTime) < .0001f
                            && Mathf.Abs(walkTime - waitingWalkTime) < .0001f, "Waiting advanced the side walk blend.");
                        waitingBlendTime = blendTime; waitingWalkTime = walkTime;
                    }
                    else waitingBlendTime = waitingWalkTime = -1f;
                    previousBlendWalkTime = walkTime;
                }
                else if (sawBlend && !finishedBlend && stage == "Follow")
                {
                    var walk = rex.GetComponentInChildren<Animation>()["Curiosity_FollowWalk"];
                    Require(Mathf.Repeat(walk.time - previousBlendWalkTime, walk.length) < .1f,
                        "Blend completion reset the walking phase.");
                    Require(walk.weight > .99f, "Blend did not complete at full walking weight.");
                    finishedBlend = true;
                }
                if (index >= 14 && index < 18 && !startRunMeasured)
                {
                    if (stage == "SideStartRun" || stage == "Follow" && startRunBegan >= 0f)
                    {
                        if (startRunBegan < 0f) { startRunBegan = Time.time; startRunPrevious = rex.transform.position; }
                        startRunDistance += Vector3.Distance(startRunPrevious, rex.transform.position);
                        startRunPrevious = rex.transform.position;
                    }
                    if (stage == "Follow" && startRunBegan >= 0f)
                    {
                        startRunMeasured = true;
                        var profile = (DinosaurInteractionProfile)new SerializedObject(rex).FindProperty("interactionProfile").objectReferenceValue;
                        var role = test.ActiveSide == TRexFrontTestController.ApproachSide.Left
                            ? InteractionMotionRole.SideStartRunLeft : InteractionMotionRole.SideStartRunRight;
                        float duration = Time.time - startRunBegan;
                        Require(Mathf.Abs(duration - profile.Find(role).clip.length / startRunRate) < .04f,
                            "Side start duration does not match the selected rate: " + duration);
                        string key = Key + (test.ActiveSide == TRexFrontTestController.ApproachSide.Left ? ".LeftDistance" : ".RightDistance");
                        if (index < 16) SessionState.SetFloat(key, startRunDistance);
                        else Require(Mathf.Abs(startRunDistance - SessionState.GetFloat(key, -100f))
                            < Mathf.Max(.1f, SessionState.GetFloat(key, 0f) * .1f),
                            "Startup blend changed travel by more than ten percent: " + startRunDistance);
                        File.AppendAllText("Logs/trex-side-test-result.txt", $"PASS start rate {startRunRate:F1}x: {test.ActiveSide}, duration={duration:F3}s, travel={startRunDistance:F3}m\n");
                    }
                }
                Vector3 direction = test.approachLayout.forward;
                if (stage == "Intro" || stage == "SideStartRun" || stage == "Follow" && loopStarted < 0f)
                {
                    Vector3 target = test.playerCamera.transform.position - rex.transform.position;
                    float Yaw(Vector3 forward) => Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                    Quaternion twist = TRexInteractionBake.SideYawTwist(cog.localRotation);
                    headingSamples.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:F4},{1},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4}",
                        elapsed, stage, action, rex.FrontActionPlaybackTime, Yaw(target),
                        Yaw(rex.transform.rotation * Quaternion.Euler(0f, rex.VisualYaw, 0f) * Vector3.forward),
                        Mathf.DeltaAngle(0f, twist.eulerAngles.y), Yaw(cog.TransformDirection(cogFacing)), Yaw(head.TransformDirection(headFacing))));
                    if (index == 2 || index == 11)
                    {
                        string shot = action + "-" + Mathf.FloorToInt(rex.FrontActionPlaybackTime * 4f);
                        if ((stage == "SideStartRun" || stage == "Follow" || action.StartsWith("roar", StringComparison.Ordinal) && rex.FrontActionPlaybackTime > 4f)
                            && captures.Add(shot)) Capture((baseline ? "before-" : "after-") + "case-" + index + "-" + shot);
                    }
                }
                if (!test.Triggered)
                {
                    Require(rex.transform.position.magnitude < .001f, "Actor moved before side trigger.");
                    if (elapsed > .5f) test.playerCamera.transform.position = test.approachLayout.TransformPoint(new Vector3(0f, 1.7f, 31f));
                }
                else
                {
                    Require(rex.FrontBlockingReason != "Player", "Player proximity blocked an interaction stage.");
                    Require(stage != "SideStartRun" && !action.StartsWith("idle attack to run", StringComparison.Ordinal),
                        "Side interaction still used its skipped startup.");
                    Require(action != "run head loop" && action != "run to stop" && action != "run head low" && action != "idle investigate" && action != "idle investigae out" && action != "Big roar step",
                        "Side test played a front action: " + action);
                    if (stage == "Intro" || stage == "SideStartRun" || stage == "Follow"
                        || stage == "Near" || stage == "Ending" || stage == "TurnBack" || stage == "ReturnWalk")
                    {
                        if (action != lastAction && action != "look forward") { actions.Add(action); lastAction = action; }
                    }
                    if (stage == "Follow" && loopStarted < 0f) loopStarted = Time.time;
                    if (stage == "SideStartRun" || stage == "Follow")
                    {
                        float distance = 24f;
                        if ((index < 6 || index >= 11) && loopStarted >= 0f && Time.time - loopStarted > 3f) distance = index >= 12 ? 0f : 9.5f;
                        if (index == 6 && loopStarted >= 0f && (!resumed || Time.time - resumedAt > 2f)) distance = 9.5f;
                        if (index == 10 && stage == "Follow") distance = 9.5f;
                        if (index == 19 && blending && (float)AgentField("sideWalkBlendTime") >= .08f) distance = 9.5f;
                        test.playerCamera.transform.position = rex.transform.position + direction * distance + Vector3.up * 1.7f;
                        if (index == 7 && loopStarted >= 0f && Time.time - loopStarted > 1f)
                            test.playerCamera.transform.position = rex.transform.position + Vector3.up * 1.7f - rex.transform.forward * 100f;
                        if (index == 9 && loopStarted >= 0f && !blocked)
                        {
                            Vector3 p = rex.transform.position;
                            settings.solidObstacles = new[] { new Bounds(p, new Vector3(30f, 12f, 30f)) };
                            blocked = true;
                        }
                    }
                    if (index == 9 && (bool)typeof(DinosaurAgent).GetField("frontWaiting", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(rex) && !cleared)
                    {
                        if (blockStarted <= 0f) { blockStarted = Time.time; blockPosition = rex.transform.position; }
                        Require(Vector3.Distance(blockPosition, rex.transform.position) < .001f, "Actor moved through a persistent obstacle.");
                        if (Time.time - blockStarted > 1f) { settings.solidObstacles = originalObstacles; cleared = true; }
                    }
                    if (index == 9 && cleared && stage == "Follow")
                        test.playerCamera.transform.position = rex.transform.position + direction * 9.5f + Vector3.up * 1.7f;
                    if (index == 18 && blocked && stage.StartsWith("Ending", StringComparison.Ordinal) && !cleared)
                    { settings.solidObstacles = originalObstacles; cleared = true; }
                    if (index == 6 && stage == "Near" && action == "look forward 2" && !requestedResume)
                    {
                        requestedResume = true;
                        test.playerCamera.transform.position = rex.transform.position + direction * 24f + Vector3.up * 1.7f;
                    }
                    if (index == 6 && requestedResume && stage == "Follow" && !resumed)
                    { resumed = true; resumedAt = Time.time; Require(action == "walk head low", "Resume replayed start sequence."); }
                    if ((stage == "Near" && !(index == 6 && requestedResume && !resumed)))
                        test.playerCamera.transform.position = rex.transform.position + direction * 9.5f + Vector3.up * 1.7f;
                    if (stage == "Ending" || stage == "TurnBack" || stage == "ReturnWalk")
                        test.playerCamera.transform.position = new Vector3(200f, 1.7f, 200f);
                    // Reproduce the reported close-player return, including direct overlap with the actor.
                    if (index >= 12 && (stage == "Near" || stage == "Ending" || stage == "TurnBack" || stage == "ReturnWalk"))
                        test.playerCamera.transform.position = rex.transform.position + Vector3.up * 1.7f;
                    sawReturn |= stage == "ReturnWalk";
                    if (index == 2 && rex.FrontActionPlaybackTime > .6f && !captures.Contains(action))
                    { captures.Add(action); Capture(action); }
                    if (sawReturn && stage == "Inactive") { CompleteCase(); return; }
                }
                Require(elapsed < 135f, "Side test timeout: case " + index + ", " + stage + "/" + action + "; " + string.Join(" > ", actions));
            }
            catch (Exception error) { Finish(error); }
        }

        private static void CompleteCase()
        {
            if (index >= 14 && index < 18) Require(startRunMeasured, "Start rate scenario skipped its full start animation.");
            if (index == 18 || index == 19)
                Require(sawBlend && !finishedBlend && AgentField("sideWalkMotion") == null && !(bool)AgentField("frontWaiting"),
                    "Blend interruption did not finish the interaction.");
            string side = test.ActiveSide == TRexFrontTestController.ApproachSide.Left ? "left" : "right";
            string[] expected = { "look " + side, "roar " + side, "walk head low",
                "ROAR", "look forward 2", "bite close", "look forward 2", "turn 180 right", "walk slow loop" };
            var sequence = actions.Where(a => a != "turn 90 left" && a != "turn 90 right").ToList();
            if (index != 6 && index != 10 && index != 18 && index != 19)
            {
                Require(sequence.Count >= expected.Length, "Side sequence incomplete: " + string.Join(" > ", actions));
                // Navigation turns may interrupt locomotion, but must not change the interaction order.
                sequence = sequence.Where((a, i) => i == 0 || a != sequence[i - 1]).ToList();
                for (int i = 0; i < expected.Length; i++) Require(sequence[i].StartsWith(expected[i], StringComparison.Ordinal),
                    "Unexpected sequence at " + i + ": " + string.Join(" > ", actions));
            }
            if (index == 6)
            {
                Require(resumed, "Near interaction did not resume.");
                Require(actions.Count(a => a.StartsWith("idle attack to run", StringComparison.Ordinal)) == 0 && !actions.Contains("run head low"),
                    "Resume repeated the start sequence.");
            }
            if (index == 9) Require(blocked && cleared, "Obstacle wait was not exercised.");
            if (index == 18) Require(blocked && cleared, "Blocked blend did not exit to Ending.");
            if (index == 10) Require(actions.Contains("walk head low"), "Close-player scenario did not enter direct walking.");
            Require(!actions.Contains("run to stop") && actions.Contains("ROAR"), "Missing direct interaction after stopping.");
            File.AppendAllText("Logs/trex-side-test-result.txt", $"PASS case {index}: {side} {test.ActiveAngle:F0}deg; max root step {maximumStep:F4}m; {string.Join(" > ", actions)}\n");
            File.WriteAllText($"Logs/side-yaw-{(baseline ? "before" : "after")}-case-{index}.csv", headingSamples.ToString());
            if (index < 6)
            {
                File.WriteAllText($"Logs/side-roar-handoff-{(blendBaseline ? "before" : "after")}-case-{index}.csv", blendSamples.ToString());
                File.AppendAllText("Logs/trex-side-test-result.txt", $"HANDOFF {index}: recoil={maximumHeadRecoil:F3}deg, support slip={maximumFootSlip:F4}m\n");
                if (!blendBaseline) Require(handoffObserved, "Side handoff was not observed.");
            }
            if (blendObserver != null) UnityEngine.Object.DestroyImmediate(blendObserver.gameObject);
            EditorApplication.update -= Tick; Time.captureDeltaTime = 0f;
            SessionState.SetInt(Key + ".Case", index + 1); EditorApplication.isPlaying = false;
        }

        private static void ObserveSideHandoff()
        {
            if (handoffError != null || rex == null) return;
            try
            {
                string stage = (string)AgentField("frontStage").ToString();
                bool active = (bool)AgentField("sideRoarHandoffActive");
                float age = (float)AgentField("sideRoarHandoffTime");
                var feet = rex.GetComponentInChildren<DinosaurFootLock>();
                Vector3 visual = rex.transform.rotation * Quaternion.Euler(0f, rex.VisualYaw, 0f) * Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, visual);
                Vector3 gaze = head.TransformDirection(headFacing);
                float yaw = Mathf.Atan2(gaze.x, gaze.z) * Mathf.Rad2Deg;
                if (stage == "Follow" && !handoffObserved)
                {
                    handoffObserved = true; handoffHeadYaw = yaw;
                    float rootYaw = Mathf.Atan2(visual.x, visual.z) * Mathf.Rad2Deg;
                    handoffSideSign = Mathf.Sign(Mathf.DeltaAngle(rootYaw, yaw));
                    handoffFootSign = Mathf.Sign(Vector3.Dot(feet.RightSolePosition - feet.LeftSolePosition, right));
                }
                if (handoffObserved && stage == "Follow" && (active ? age <= .4f : blendBaseline && loopStarted >= 0f && Time.time - loopStarted <= .4f))
                {
                    float recoil = -handoffSideSign * Mathf.DeltaAngle(handoffHeadYaw, yaw);
                    maximumHeadRecoil = Mathf.Max(maximumHeadRecoil, recoil);
                    float slip = Mathf.Max(lastLeftPlant && feet.LeftPlanted ? Vector3.Distance(lastLeftSole, feet.LeftSolePosition) : 0f,
                        lastRightPlant && feet.RightPlanted ? Vector3.Distance(lastRightSole, feet.RightSolePosition) : 0f);
                    maximumFootSlip = Mathf.Max(maximumFootSlip, slip);
                    if (!blendBaseline && !(bool)AgentField("frontWaiting"))
                    {
                        Require(recoil < 8f, "Head recoiled during roar-to-walk handoff: " + recoil);
                        Require(slip < .08f, "A planted foot jumped during handoff: " + slip);
                        Require(Vector3.Dot(feet.RightSolePosition - feet.LeftSolePosition, right) * handoffFootSign >= -.03f,
                            "Feet crossed during the initial handoff.");
                    }
                }
                lastLeftPlant = feet.LeftPlanted; lastRightPlant = feet.RightPlanted;
                lastLeftSole = feet.LeftSolePosition; lastRightSole = feet.RightSolePosition;
                CaptureWalkBlend(stage);
            }
            catch (Exception error) { handoffError = error.ToString(); }
        }
        private static void CaptureWalkBlend(string stage)
        {
            if (index >= 6) return;
            var profile = (DinosaurInteractionProfile)new SerializedObject(rex).FindProperty("interactionProfile").objectReferenceValue;
            var motion = profile.Find(test.ActiveSide == TRexFrontTestController.ApproachSide.Left
                ? InteractionMotionRole.SideRoarLeft : InteractionMotionRole.SideRoarRight);
            if (blendShotBegan < 0f && stage == "Intro" && rex.CurrentAnimationSourceName == motion.sourceName
                && motion.clip.length - rex.FrontActionPlaybackTime <= .301f)
                blendShotBegan = Time.time;
            if (blendShotBegan < 0f || Time.time - blendShotBegan > 3f) return;
            var walk = rex.GetComponentInChildren<Animation>()["Curiosity_FollowWalk"];
            var feet = rex.GetComponentInChildren<DinosaurFootLock>();
            rex.GetFootPhase(out float left, out float right);
            Vector3 gaze = head.TransformDirection(headFacing), facing = rex.transform.rotation * Quaternion.Euler(0f, rex.VisualYaw, 0f) * Vector3.forward;
            blendSamples.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0:F4},{1},{2:F4},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11:F4},{12:F4},{13:F4},{14:F4},{15:F4},{16:F4}",
                Time.time - blendShotBegan, stage, rex.FrontActionPlaybackTime, walk.time, walk.weight,
                (float)AgentField("actualInteractionSpeed"), left, right, feet.LeftPlantError, feet.RightPlantError,
                Mathf.Atan2(gaze.x, gaze.z) * Mathf.Rad2Deg, Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg,
                (float)AgentField("sideRoarHandoffTime"), feet.LeftSolePosition.x, feet.LeftSolePosition.z,
                feet.RightSolePosition.x, feet.RightSolePosition.z));
            float[] shots = { 0f, .15f, .3f, .45f, .7f, 1.1f, 1.7f, 2.3f, 2.7f };
            if (blendShot >= shots.Length || Time.time - blendShotBegan + .001f < shots[blendShot]
                || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            if (blendObserver == null)
            {
                blendObserver = new GameObject("Side blend fixed observer").AddComponent<Camera>();
                blendObserver.CopyFrom(test.playerCamera); blendObserver.enabled = false;
                blendObserver.orthographic = true; blendObserver.orthographicSize = 5.5f;
                Quaternion heading = rex.transform.rotation * Quaternion.Euler(0f, rex.VisualYaw, 0f);
                blendObserver.transform.position = rex.transform.position + heading * new Vector3(-12f, 6f, 2f);
                blendObserver.transform.LookAt(rex.transform.position + Vector3.up * 3.2f);
            }
            var texture = new RenderTexture(960, 540, 24);
            var pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
            RenderTexture oldTarget = RenderTexture.active;
            try
            {
                blendObserver.targetTexture = texture; blendObserver.Render(); RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); pixels.Apply();
                File.WriteAllBytes($"Logs/side-roar-handoff-{(blendBaseline ? "before" : "after")}-case-{index}-{blendShot}.png", pixels.EncodeToPNG());
                blendShot++;
            }
            finally
            {
                blendObserver.targetTexture = null; RenderTexture.active = oldTarget;
                UnityEngine.Object.DestroyImmediate(pixels); texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        private static void Capture(string action)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var observer = new GameObject("Side smoke observer").AddComponent<Camera>();
            var texture = new RenderTexture(960, 540, 24);
            var pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
            RenderTexture previousTarget = RenderTexture.active;
            try
            {
                observer.CopyFrom(test.playerCamera); observer.enabled = false;
                observer.transform.position = rex.transform.position + new Vector3(-15f, 8f, 16f);
                observer.transform.LookAt(rex.transform.position + Vector3.up * 3f);
                observer.targetTexture = texture; observer.Render(); RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); pixels.Apply();
                File.WriteAllBytes("Logs/side-" + action.Replace(' ', '-') + ".png", pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousTarget;
                UnityEngine.Object.DestroyImmediate(observer.gameObject); UnityEngine.Object.DestroyImmediate(pixels);
                texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            SessionState.SetBool(Key + ".Baseline", false);
            if (error != null) { File.AppendAllText("Logs/trex-side-test-result.txt", error + "\n"); Debug.LogException(error); }
            else { File.AppendAllText("Logs/trex-side-test-result.txt", blendBaseline || SessionState.GetBool(Key + ".BlendPreview", false)
                ? "PASS six roar-to-walk preview scenarios.\n" : "PASS all 14 side scenarios: roar-to-walk handoff, navigation turning, waiting, resume and close-player interactions; no startup.\n"); Debug.Log("PASS side scenarios."); }
            SessionState.SetBool(Key + ".BlendBaseline", false);
            SessionState.SetBool(Key + ".BlendPreview", false);
            if (blendObserver != null) UnityEngine.Object.DestroyImmediate(blendObserver.gameObject);
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else EditorSceneManager.OpenScene(TRexFrontTestController.ScenePath);
        }
    }
}
