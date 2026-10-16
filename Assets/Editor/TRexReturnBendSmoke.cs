using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class TRexReturnBendSmoke
    {
        private const string Key = "JurassicWorld.ReturnBendSmoke";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly float[] Angles = { -40f, -45f, -50f, -55f, 45f, 50f, -65f, 65f, -80f, 80f, 45f };
        private static DinosaurAgent rex;
        private static TRexFrontTestController test;
        private static int scenario, frame, movingFrames;
        private static float began, waited, walked;
        private static Vector3 previous;
        private static readonly System.Text.StringBuilder report = new System.Text.StringBuilder();
        private static object Get(string name) => typeof(DinosaurAgent).GetField(name, Flags).GetValue(rex);
        private static void Set(string name, object value) => typeof(DinosaurAgent).GetField(name, Flags).SetValue(rex, value);
        private static void Call(string name, params object[] args) => typeof(DinosaurAgent).GetMethod(name, Flags).Invoke(rex, args);
        static TRexReturnBendSmoke()
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
                Time.captureDeltaTime = 1f / 60f; scenario = frame = 0; report.Clear(); Prepare(); EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Prepare()
        {
            if ((bool)Get("frontActive")) Call("FinishFrontInteraction");
            rex.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, -rex.VisualYaw, 0f));
            test.playerCamera.transform.position = new Vector3(0f, 1.7f, 24f);
            Call("BeginFrontInteraction"); Set("frontTestWaiting", false); Call("BeginFrontReturn");
            Set("frontStage", Enum.Parse(typeof(DinosaurAgent).GetNestedType("FrontStage", BindingFlags.NonPublic), "ReturnWalk"));
            var trail = (System.Collections.Generic.List<Vector3>)Get("frontTrail"); trail.Clear();
            float length = scenario < 4 ? 3f : 20f;
            trail.Add(Quaternion.Euler(0f, Angles[scenario], 0f) * Vector3.forward * length);
            if (scenario == 10)
            {
                // The closest earlier bend must win even when a farther point is straight ahead.
                trail[0] = Vector3.forward * 20f;
                trail.Add(Quaternion.Euler(0f, 45f, 0f) * Vector3.forward * 6f);
            }
            Set("frontReturnIndex", trail.Count - 1); Set("frontReturnWalked", 0f); Set("hasCuriosityPath", false);
            Set("frontReturnAligning", true);
            var profile = (DinosaurInteractionProfile)Get("interactionProfile"); Call("UseFrontEnvelope", profile.Find(InteractionMotionRole.Walk));
            began = Time.time; waited = walked = 0f; movingFrames = 0; previous = rex.transform.position;
        }
        private static void Tick()
        {
            try
            {
                if (frame == Time.frameCount) return; frame = Time.frameCount;
                float step = Vector3.Distance(previous, rex.transform.position); previous = rex.transform.position;
                if (scenario == 10 && movingFrames == 0 && step > .001f && (int)Get("frontReturnIndex") != 1)
                    throw new InvalidOperationException("Return skipped the nearest recorded bend to favour its current heading.");
                if (step > .001f) movingFrames++;
                if (step > .001f && (bool)Get("frontActive") && Get("frontStage").ToString() == "ReturnWalk"
                    && !(bool)Get("frontWaiting") && rex.CurrentAnimationSourceName == "walk slow loop")
                {
                    var state = rex.GetComponentInChildren<Animation>()["walk slow loop"];
                    if (Mathf.Abs(state.speed - rex.WalkSpeed / rex.MoveAnimationGroundSpeed) > .01f)
                        throw new InvalidOperationException("Return bend slowed foot cadence: " + state.speed);
                }
                if (step > .4f) throw new InvalidOperationException("Root jumped during return.");
                walked = Mathf.Max(walked, (float)Get("frontReturnWalked"));
                if ((bool)Get("frontWaiting")) waited += Time.deltaTime;
                if (waited > .55f) throw new InvalidOperationException("Open return bend waited: " + Angles[scenario] + "/" + rex.FrontBlockingReason);
                if (!(bool)Get("frontActive"))
                {
                    if (movingFrames < 20 || walked <= .3f) throw new InvalidOperationException("Return did not actually walk.");
                    report.AppendLine($"PASS angle={Angles[scenario]}deg length={(scenario < 4 ? 3 : 20)}m; walked={walked:F3}s; wait={waited:F3}s; frames={movingFrames}");
                    if (++scenario == Angles.Length) { Finish(null); return; } Prepare();
                }
                if (Time.time - began > 35f) throw new InvalidOperationException("Return never finished: " + Angles[scenario] + "/" + rex.FrontInteractionStage);
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            string result = (error == null ? "PASS return bends\n" : error + "\n") + report;
            File.WriteAllText("Logs/trex-return-bend-result.txt", result);
            if (error == null) Debug.Log(result); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
