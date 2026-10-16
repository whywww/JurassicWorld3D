using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class TRexTurn170Smoke
    {
        private const string Key = "JurassicWorld.Turn170Smoke";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static DinosaurAgent rex;
        private static TRexFrontTestController test;
        private static Animation animation;
        private static Transform cog, left, right;
        private static DinosaurFootLock feet;
        private static int frame, scenario, movingFrames, blendFrames;
        private static bool sawTurn, sawBlend;
        private static float began, initialCogYaw, previousCogYaw, turnYaw, peakTurn, maximumAlignRate, maximumBlendFootStep, blendBodyYaw, peakBlendBodyYaw;
        private static Quaternion previousRotation, blendRotation;
        private static float maximumPlantError, maximumYawError;
        private static Vector3 previousPosition, previousLeft, previousRight, blendPosition;
        private static readonly System.Text.StringBuilder report = new System.Text.StringBuilder();
        private static object Get(string name) => typeof(DinosaurAgent).GetField(name, Flags).GetValue(rex);
        private static void Set(string name, object value) => typeof(DinosaurAgent).GetField(name, Flags).SetValue(rex, value);
        private static void Call(string name, params object[] args) => typeof(DinosaurAgent).GetMethod(name, Flags).Invoke(rex, args);
        private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
        static TRexTurn170Smoke()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) EditorApplication.delayCall += Begin;
            };
        }
        public static void Run()
            => Start(1f / 60f);
        public static void RunAt30Fps()
            => Start(1f / 30f);
        private static void Start(float delta)
        {
            SessionState.SetFloat(Key + ".Delta", delta);
            EditorSceneManager.OpenScene(TRexFrontTestController.ScenePath);
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }
        private static void Begin()
        {
            try
            {
                test = UnityEngine.Object.FindObjectOfType<TRexFrontTestController>(); rex = test.dinosaur;
                test.enabled = false; test.playerCamera.GetComponent<SmoothExplorerCamera>().enabled = false;
                animation = rex.GetComponentInChildren<Animation>(); cog = TRexTurnCalibration.Bone(animation.transform, "jt_Cog_C");
                feet = rex.GetComponentInChildren<DinosaurFootLock>();
                left = TRexTurnCalibration.Bone(animation.transform, "jt_ClawMiddle_L"); right = TRexTurnCalibration.Bone(animation.transform, "jt_ClawMiddle_R");
                Time.captureDeltaTime = SessionState.GetFloat(Key + ".Delta", 1f / 60f); scenario = frame = 0; report.Clear(); Prepare(); EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static float CogYaw() => Vector3.SignedAngle(Vector3.forward, Vector3.ProjectOnPlane(cog.forward, Vector3.up), Vector3.up);
        private static void Prepare()
        {
            if ((bool)Get("frontActive")) Call("FinishFrontInteraction");
            rex.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, -rex.VisualYaw, 0f));
            feet.ResetSupportAnchors();
            test.playerCamera.transform.position = new Vector3(0f, 1.7f, 10.2f);
            Call("BeginFrontInteraction"); Set("frontTestWaiting", false); Call("BeginFrontFollow", false); Call("BeginFrontNear");
            Set("frontNearBite", scenario % 2 == 0 ? InteractionMotionRole.BiteCloseLeft : InteractionMotionRole.BiteCloseRight);
            if (scenario >= 2)
            {
                var trail = (System.Collections.Generic.List<Vector3>)Get("frontTrail"); trail.Insert(0, Vector3.back * 24f);
            }
            began = Time.time; sawTurn = sawBlend = false; movingFrames = blendFrames = 0;
            peakTurn = turnYaw = maximumAlignRate = maximumBlendFootStep = 0f;
            maximumPlantError = maximumYawError = 0f;
            previousRotation = rex.transform.rotation; previousPosition = rex.transform.position; previousLeft = left.position; previousRight = right.position;
        }
        private static void Tick()
        {
            try
            {
                if (frame == Time.frameCount) return; frame = Time.frameCount;
                string stage = Get("frontStage").ToString();
                if (feet.ReturnHandoffActive)
                {
                    maximumPlantError = Mathf.Max(maximumPlantError, feet.LeftPlantError, feet.RightPlantError);
                    maximumYawError = Mathf.Max(maximumYawError, feet.LeftYawError, feet.RightYawError);
                    Require(maximumPlantError <= .02f && maximumYawError <= 2f, "Foot handoff slipped after a short trail ended.");
                }
                if (stage == "TurnBack")
                {
                    float heading = CogYaw();
                    if (!sawTurn) { sawTurn = true; initialCogYaw = previousCogYaw = heading; turnYaw = 0f; }
                    turnYaw += Mathf.DeltaAngle(previousCogYaw, heading); previousCogYaw = heading;
                    peakTurn = Mathf.Max(peakTurn, turnYaw);
                }
                if (stage == "ReturnWalk" && (bool)Get("frontActive"))
                {
                    if (feet.ReturnHandoffActive)
                    {
                        maximumPlantError = Mathf.Max(maximumPlantError, feet.LeftPlantError, feet.RightPlantError);
                        maximumYawError = Mathf.Max(maximumYawError, feet.LeftYawError, feet.RightYawError);
                        Require(maximumPlantError <= .02f && maximumYawError <= 2f, "First-two-step stance slipped or twisted: " + maximumPlantError + "/" + maximumYawError);
                    }
                    if (!sawBlend)
                    {
                        turnYaw += Mathf.DeltaAngle(previousCogYaw, CogYaw());
                        Require(Mathf.Abs(turnYaw - 170f) < 2f, "Actual body turn was not 170 degrees: " + turnYaw);
                        sawBlend = true; blendRotation = rex.transform.rotation; blendPosition = rex.transform.position;
                        blendBodyYaw = peakBlendBodyYaw = turnYaw; previousCogYaw = CogYaw();
                        float phase = (float)Get("frontReturnEntryPhase");
                        Require(phase >= (float)Get("moveClipLoopStart") && phase < (float)Get("moveClipLoopEnd"), "Walk entry is outside the established loop window.");
                        report.AppendLine($"side={scenario}: actual body turn={turnYaw:F3}deg, peak={peakTurn:F3}deg, selected walk phase={phase:F3}s");
                    }
                    if (Time.time < (float)Get("frontReturnBlendUntil"))
                    {
                        blendFrames++;
                        blendBodyYaw += Mathf.DeltaAngle(previousCogYaw, CogYaw()); previousCogYaw = CogYaw();
                        peakBlendBodyYaw = Mathf.Max(peakBlendBodyYaw, blendBodyYaw);
                        Require(peakBlendBodyYaw < 175f, "Body added excessive yaw during the walk blend: " + peakBlendBodyYaw);
                        Require(Quaternion.Angle(blendRotation, rex.transform.rotation) < .01f && Vector3.Distance(blendPosition, rex.transform.position) < .001f,
                            "Return moved or rotated during the entry blend.");
                        maximumBlendFootStep = Mathf.Max(maximumBlendFootStep, Vector3.Distance(left.position, previousLeft), Vector3.Distance(right.position, previousRight));
                    }
                    else if ((bool)Get("frontReturnAligning"))
                    {
                        float rate = Mathf.Abs(Mathf.DeltaAngle(previousRotation.eulerAngles.y, rex.transform.rotation.eulerAngles.y)) / Time.deltaTime;
                        maximumAlignRate = Mathf.Max(maximumAlignRate, rate);
                        Require(rate <= 65.1f, "Return steering exceeded 65 deg/s.");
                    }
                    if (Time.time >= (float)Get("frontReturnBlendUntil") && !(bool)Get("frontWaiting")
                        && !rex.IsSteppingTurn
                        && Vector3.Distance(previousPosition, rex.transform.position) > .001f)
                        Require(Mathf.Abs(animation["walk slow loop"].speed - rex.WalkSpeed / rex.MoveAnimationGroundSpeed) < .01f,
                            "Return foot cadence slowed while steering.");
                    if (Vector3.Distance(previousPosition, rex.transform.position) > .001f) movingFrames++;
                }
                Require(Vector3.Distance(previousPosition, rex.transform.position) < .4f * (Time.deltaTime * 60f),
                    "Root jumped: " + Vector3.Distance(previousPosition, rex.transform.position) + ", " + rex.FrontInteractionStage + "/" + rex.CurrentAnimationSourceName + ", time=" + (Time.time - began));
                previousRotation = rex.transform.rotation; previousPosition = rex.transform.position; previousLeft = left.position; previousRight = right.position;
                if (!(bool)Get("frontActive") && !feet.ReturnHandoffActive)
                {
                    Require(sawTurn && sawBlend && blendFrames >= Mathf.CeilToInt(.2f / Time.deltaTime) && movingFrames > Mathf.FloorToInt(.3f / Time.deltaTime), "Missing turn/blend/actual return walking.");
                    Require(maximumBlendFootStep < .1f, "Foot popped during the walk blend: " + maximumBlendFootStep);
                    report.AppendLine($"case={scenario}: blend frames={blendFrames}, max body yaw in blend={peakBlendBodyYaw:F3}deg, max foot step={maximumBlendFootStep:F4}m, initial steering={maximumAlignRate:F3}deg/s, walking frames={movingFrames}, stance error={maximumPlantError:F4}m/{maximumYawError:F3}deg");
                    if (++scenario == 4) { Finish(null); return; } Prepare();
                }
                Require(Time.time - began < 40f, "Sequence did not finish: " + rex.FrontInteractionStage + "/" + rex.FrontBlockingReason);
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            string result = (error == null ? "PASS turn170 transition\n" : error + "\n") + report;
            File.WriteAllText("Logs/trex-turn170-result.txt", result);
            if (error == null) Debug.Log(result); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
