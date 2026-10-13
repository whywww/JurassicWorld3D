using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class TRexFollowContinuitySmoke
    {
        private const string Key = "JurassicWorld.FollowContinuity";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static DinosaurAgent rex;
        private static TRexFrontTestController test;
        private static DinosaurInteractionSettings settings;
        private static Animation animation;
        private static AnimationState walk;
        private static Transform left, right;
        private static Vector3 position, leftPosition, rightPosition;
        private static float began, previousTime, savedTime, maximumFootStep, maximumRootStep;
        private static int frame, movingFrames, stillFrames, wraps;
        private static bool blocked, sawWait, resumed;
        private static string footPeak;
        private static GameObject reference;
        private static Transform referenceLeft, referenceRight;
        private static float maximumExcessStep;
        private static float maximumSeamStep;
        private static object Get(string name) => typeof(DinosaurAgent).GetField(name, Flags).GetValue(rex);
        private static void Call(string name, params object[] args) => typeof(DinosaurAgent).GetMethod(name, Flags).Invoke(rex, args);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static TRexFollowContinuitySmoke()
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
                test.enabled = false; test.playerCamera.GetComponent<SmoothExplorerCamera>().enabled = false;
                settings = UnityEngine.Object.FindObjectOfType<DinosaurInteractionSettings>();
                test.playerCamera.transform.position = new Vector3(0f, 1.7f, 24f);
                Call("BeginFrontInteraction"); typeof(DinosaurAgent).GetField("frontTestWaiting", Flags).SetValue(rex, false);
                Call("BeginFrontFollow", false);
                animation = rex.GetComponentInChildren<Animation>();
                walk = animation["Curiosity_FollowWalk"];
                left = TRexTurnCalibration.Bone(animation.transform, "jt_ClawMiddle_L");
                right = TRexTurnCalibration.Bone(animation.transform, "jt_ClawMiddle_R");
                reference = UnityEngine.Object.Instantiate(animation.gameObject);
                reference.hideFlags = HideFlags.HideAndDontSave;
                foreach (var component in reference.GetComponentsInChildren<MonoBehaviour>()) component.enabled = false;
                reference.GetComponent<Animation>().enabled = false;
                foreach (var renderer in reference.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
                referenceLeft = TRexTurnCalibration.Bone(reference.transform, "jt_ClawMiddle_L");
                referenceRight = TRexTurnCalibration.Bone(reference.transform, "jt_ClawMiddle_R");
                maximumExcessStep = 0f;
                maximumSeamStep = 0f;
                position = rex.transform.position; leftPosition = left.position; rightPosition = right.position;
                previousTime = walk.time; began = Time.time; frame = movingFrames = stillFrames = wraps = 0;
                blocked = sawWait = resumed = false; maximumRootStep = maximumFootStep = 0f;
                Time.captureDeltaTime = 1f / 60f; EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Tick()
        {
            try
            {
                if (frame == Time.frameCount) return; frame = Time.frameCount;
                float elapsed = Time.time - began;
                float step = Vector3.Distance(position, rex.transform.position);
                bool waiting = (bool)Get("frontWaiting");
                test.playerCamera.transform.position = rex.transform.position + Vector3.forward * 24f + Vector3.up * 1.7f;
                if (!blocked && elapsed >= 6f)
                {
                    blocked = true;
                    settings.solidObstacles = new[] { new Bounds(rex.transform.position + Vector3.up * 2f, new Vector3(1f, 4f, 1f)) };
                }
                if (waiting && !sawWait) { sawWait = true; savedTime = (float)Get("frontSavedWalkTime"); }
                if (blocked && elapsed >= 7.5f) settings.solidObstacles = Array.Empty<Bounds>();
                if (sawWait && !waiting && !resumed)
                {
                    Require(Mathf.Abs(walk.time - savedTime) < .04f && walk.time > .2f, "Wait restarted walking instead of preserving its phase.");
                    resumed = true;
                }
                if (elapsed > .5f && (elapsed < 5.5f || elapsed > 8.5f))
                {
                    Require(rex.FrontInteractionStage == "Follow" && rex.CurrentAnimationSourceName == "walk head low", "Open-space prediction interrupted following.");
                    if (step < .001f) stillFrames++; else movingFrames++;
                    if (walk.time < previousTime - .1f) { Require(previousTime >= walk.length - .05f, "Walk unexpectedly reset mid-cycle."); wraps++; }
                    maximumRootStep = Mathf.Max(maximumRootStep, step);
                    float footStep = Mathf.Max(Vector3.Distance(left.position, leftPosition), Vector3.Distance(right.position, rightPosition));
                    walk.clip.SampleAnimation(reference, previousTime);
                    Vector3 rawLeft = referenceLeft.position, rawRight = referenceRight.position;
                    walk.clip.SampleAnimation(reference, walk.time);
                    float rawStep = Mathf.Max(Vector3.Distance(rawLeft, referenceLeft.position), Vector3.Distance(rawRight, referenceRight.position));
                    if (walk.time >= previousTime) maximumExcessStep = Mathf.Max(maximumExcessStep, footStep - rawStep - step);
                    else maximumSeamStep = Mathf.Max(maximumSeamStep, footStep);
                    if (footStep > maximumFootStep) { maximumFootStep = footStep; footPeak = $"elapsed={elapsed:F3}, phase={previousTime:F3}->{walk.time:F3}, L={Vector3.Distance(left.position,leftPosition):F4}, R={Vector3.Distance(right.position,rightPosition):F4}, authoredStep={rawStep:F4}"; }
                }
                position = rex.transform.position; previousTime = walk.time; leftPosition = left.position; rightPosition = right.position;
                if (elapsed >= 14f)
                {
                    Require(sawWait && resumed && movingFrames > 500 && stillFrames == 0 && wraps >= 1, "Following was not continuous.");
                    Require(maximumRootStep < .08f && maximumExcessStep < .1f && maximumSeamStep < .1f,
                        "Root or foot jumped beyond authored swing motion, or at the loop seam.");
                    Finish(null);
                }
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            if (settings != null) settings.solidObstacles = Array.Empty<Bounds>();
            string result = (error == null ? "PASS follow continuity" : error.ToString()) + $"\nmoving={movingFrames}, stopped={stillFrames}, wraps={wraps}, maxRootStep={maximumRootStep:F4}m, maxFootStep={maximumFootStep:F4}m, waitPhase={savedTime:F3}s, resumed={resumed}";
            result += "\nPeak: " + footPeak;
            result += $"\nMaximum excess over authored foot motion and root movement={maximumExcessStep:F4}m";
            result += $"; loop seam step={maximumSeamStep:F4}m";
            if (reference != null) UnityEngine.Object.DestroyImmediate(reference);
            File.WriteAllText("Logs/trex-follow-continuity-result.txt", result);
            if (error == null) Debug.Log(result); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
