using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class TRexLookPlaybackSmoke
    {
        private const string Key = "JurassicWorld.LookPlayback";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static DinosaurAgent rex;
        private static Camera player;
        private static Animation animation;
        private static Transform[] bones;
        private static GameObject reference;
        private static Transform[] referenceBones;
        private static AnimationClip source;
        private static Quaternion[] initial;
        private static int frame, side, lookIndex;
        private static float began, previousTime, movement, minimumWeight;
        private static string previous;
        private static bool capturedPose;
        private static float maximumPoseError;
        private static readonly System.Text.StringBuilder report = new System.Text.StringBuilder();
        private static object Get(string name) => typeof(DinosaurAgent).GetField(name, Flags).GetValue(rex);
        private static void Set(string name, object value) => typeof(DinosaurAgent).GetField(name, Flags).SetValue(rex, value);
        private static void Call(string name, params object[] args) => typeof(DinosaurAgent).GetMethod(name, Flags).Invoke(rex, args);
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static TRexLookPlaybackSmoke()
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
                rex = test.dinosaur; player = test.playerCamera; test.enabled = false;
                player.GetComponent<SmoothExplorerCamera>().enabled = false;
                animation = rex.GetComponentInChildren<Animation>();
                bones = animation.GetComponentsInChildren<Transform>().Where(t => t.name == "jt_Head_C" || t.name.StartsWith("jt_Neck")).ToArray();
                source = animation["look forward 2"].clip;
                reference = UnityEngine.Object.Instantiate(animation.gameObject);
                reference.hideFlags = HideFlags.HideAndDontSave;
                reference.GetComponent<Animation>().enabled = false;
                foreach (var renderer in reference.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
                referenceBones = bones.Select(b => reference.GetComponentsInChildren<Transform>().First(t => t.name == b.name)).ToArray();
                Check(bones.Length >= 4, "Head/neck bones missing.");
                Time.captureDeltaTime = 1f / 60f; side = frame = 0; report.Clear(); Prepare(); EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Prepare()
        {
            if ((bool)Get("frontActive")) Call("FinishFrontInteraction");
            rex.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, -rex.VisualYaw, 0f));
            player.transform.position = new Vector3(0f, 1.7f, 9.8f);
            Call("BeginFrontInteraction"); Set("frontTestWaiting", false); Call("BeginFrontFollow", false); Call("BeginFrontNear");
            Set("frontNearBite", side == 0 ? InteractionMotionRole.BiteCloseLeft : InteractionMotionRole.BiteCloseRight);
            began = Time.time; previous = ""; lookIndex = 0;
        }
        private static void EndLook()
        {
            report.AppendLine($"side={side} look={lookIndex}: endTime={previousTime:F3}, minWeight={minimumWeight:F3}, head/neck change={movement:F4}deg, source pose error={maximumPoseError:F4}deg");
            Check(previousTime >= 1.95f, "Look animation time did not reach 2 seconds.");
            Check(movement > .01f, "Look head/neck pose is frozen.");
            Check(minimumWeight > .8f, "Look animation is not blended in.");
            Check(maximumPoseError < .5f, "Actual head/neck pose does not follow the original clip.");
        }
        private static void Tick()
        {
            try
            {
                if (frame == Time.frameCount) return; frame = Time.frameCount;
                string action = rex.CurrentAnimationSourceName;
                if (action != previous)
                {
                    if (previous == "look forward 2") EndLook();
                    if (action == "look forward 2")
                    {
                        lookIndex++; previousTime = 0f; movement = 0f; minimumWeight = 1f;
                        capturedPose = false;
                        maximumPoseError = 0f;
                        initial = bones.Select(t => t.localRotation).ToArray();
                    }
                    previous = action;
                }
                if (action == "look forward 2")
                {
                    var motion = ((DinosaurInteractionProfile)Get("interactionProfile")).Find(InteractionMotionRole.LookForwardTwo);
                    var state = animation[motion.StateName];
                    Check(state.time >= previousTime - .02f, "Look time moved backwards.");
                    previousTime = state.time;
                    if (state.time >= .4f)
                    {
                        Check(state.enabled, "Look state disabled while its clock advances.");
                        minimumWeight = Mathf.Min(minimumWeight, state.weight);
                        if (!capturedPose) { initial = bones.Select(t => t.localRotation).ToArray(); capturedPose = true; }
                        source.SampleAnimation(reference, state.time);
                        for (int i = 0; i < bones.Length; i++)
                        {
                            movement = Mathf.Max(movement, Quaternion.Angle(initial[i], bones[i].localRotation));
                            maximumPoseError = Mathf.Max(maximumPoseError, Quaternion.Angle(referenceBones[i].localRotation, bones[i].localRotation));
                        }
                    }
                }
                if (rex.FrontInteractionStage == "Near") player.transform.position = rex.transform.position + Vector3.forward * 9.8f + Vector3.up * 1.7f;
                if (rex.FrontInteractionStage == "TurnBack") player.transform.position = new Vector3(200f, 2f, 200f);
                if (lookIndex == 2 && action == "turn 180 right")
                {
                    if (++side == 2) { Finish(null); return; } Prepare();
                }
                Check(Time.time - began < 25f, "Playback stalled: " + rex.FrontInteractionStage + "/" + action + ", time=" + Get("frontActionTime") + ", blocked=" + Get("frontBlocked"));
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            if (reference != null) UnityEngine.Object.DestroyImmediate(reference);
            File.WriteAllText("Logs/trex-look-playback-result.txt", (error == null ? "PASS look playback\n" : error + "\n") + report);
            if (error == null) Debug.Log("PASS look playback\n" + report); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
