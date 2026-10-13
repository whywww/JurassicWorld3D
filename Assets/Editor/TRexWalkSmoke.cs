using System;
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
    public static class TRexWalkSmoke
    {
        private const string Key = "JurassicWorld.WalkSmoke";
        static TRexWalkSmoke()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                    EditorApplication.delayCall += () => new GameObject("Walk smoke probe").AddComponent<TRexWalkSmokeProbe>();
            };
        }
        public static void Run()
        {
            TRexTurnCalibration.RefreshWalkContacts();
            foreach (var behaviour in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                if (behaviour is EmotivPlayerInput || behaviour.GetType().Namespace == "Crest") behaviour.enabled = false;
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }
        internal static void Finish(string result, string trace)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/trex-walk-smoke-result.txt", result);
            File.WriteAllText("Logs/trex-walk-smoke.csv", trace);
            SessionState.SetBool(Key, false);
            Time.captureDeltaTime = 0f;
            Debug.Log(result);
            if (Application.isBatchMode) EditorApplication.Exit(result.StartsWith("PASS") ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }

    [DefaultExecutionOrder(2000)]
    public sealed class TRexWalkSmokeProbe : MonoBehaviour
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private DinosaurAgent rex;
        private DinosaurFootLock feet;
        private Animation animationPlayer;
        private Transform left, right;
        private Vector3 previousLeft, previousRight;
        private int frames, contacts;
        private float positionError, yawError, maximumStep;
        private readonly StringBuilder trace = new StringBuilder("frame,time,leftContact,rightContact,leftError,rightError,leftYaw,rightYaw,footStep,legCorrection\n");
        private void Start()
        {
            rex = FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex");
            foreach (var other in FindObjectsOfType<DinosaurAgent>()) other.enabled = false;
            rex.transform.SetPositionAndRotation(new Vector3(23f, rex.MovementHeight, 18f), Quaternion.Euler(0f, -rex.VisualYaw, 0f));
            feet = rex.GetComponentInChildren<DinosaurFootLock>();
            feet.ResetSupportAnchors();
            animationPlayer = rex.GetComponentInChildren<Animation>();
            animationPlayer.cullingType = AnimationCullingType.AlwaysAnimate;
            foreach (var skin in rex.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.updateWhenOffscreen = true;
            left = TRexTurnCalibration.Bone(animationPlayer.transform, "jt_ClawMiddle_L");
            right = TRexTurnCalibration.Bone(animationPlayer.transform, "jt_ClawMiddle_R");
            typeof(DinosaurAgent).GetMethod("PlayMovementClip", Private).Invoke(rex, new object[] { rex.WalkSpeed });
            Time.captureDeltaTime = 1f / 60f;
        }
        private void Update()
        {
            if (rex == null) return;
            typeof(DinosaurAgent).GetMethod("ApplyMoveLoopWindow", Private).Invoke(rex, null);
            rex.transform.position += Vector3.forward * rex.WalkSpeed * Time.deltaTime;
        }
        private void LateUpdate()
        {
            if (rex == null) return;
            frames++;
            float step = frames == 1 ? 0f : Mathf.Max(Vector3.Distance(left.position, previousLeft), Vector3.Distance(right.position, previousRight));
            previousLeft = left.position; previousRight = right.position;
            if (frames > 60)
            {
                if (feet.LeftPlanted) { contacts++; positionError = Mathf.Max(positionError, feet.LeftPlantError); yawError = Mathf.Max(yawError, feet.LeftYawError); }
                if (feet.RightPlanted) { contacts++; positionError = Mathf.Max(positionError, feet.RightPlantError); yawError = Mathf.Max(yawError, feet.RightYawError); }
                maximumStep = Mathf.Max(maximumStep, step);
            }
            trace.AppendLine(FormattableString.Invariant($"{frames},{animationPlayer["walk slow loop"].time},{feet.LeftPlanted},{feet.RightPlanted},{feet.LeftPlantError},{feet.RightPlantError},{feet.LeftYawError},{feet.RightYawError},{step},{feet.MaximumLegCorrection}"));
            if (frames < 720) return;
            bool pass = contacts > 120 && positionError <= .02f && yawError <= 2f && maximumStep < .3f;
            TRexWalkSmoke.Finish($"{(pass ? "PASS" : "FAIL")} 12s straight walk, contacts={contacts}, plantError={positionError:F4}m, yawError={yawError:F3}deg, maxFootStep60fps={maximumStep:F4}m. Short smoke only; visual Play feedback required.", trace.ToString());
            enabled = false;
        }
    }
}
