using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    [DefaultExecutionOrder(999)]
    public sealed class TRexBeforeFootAudit : MonoBehaviour
    {
        public Transform left, right, leftFoot, rightFoot, leftKnee, rightKnee, cog;
        public Vector3 l, r, lk, rk, lh, rh;
        public float leftHeelY, rightHeelY;
        public Vector3 leftLocalForward, rightLocalForward, leftPawnForward, rightPawnForward;
        private void LateUpdate()
        {
            l = left.position; r = right.position; lk = leftKnee.position; rk = rightKnee.position;
            lh = left.position - leftFoot.position; rh = right.position - rightFoot.position;
            leftHeelY = leftFoot.position.y; rightHeelY = rightFoot.position.y;
            leftPawnForward = leftFoot.TransformDirection(leftLocalForward); rightPawnForward = rightFoot.TransformDirection(rightLocalForward);
        }
    }
    [InitializeOnLoad]
    public static class TRexReturnFootAudit
    {
        private const string Key = "JurassicWorld.ReturnFootAudit";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static DinosaurAgent rex;
        private static TRexBeforeFootAudit before;
        private static DinosaurFootLock footLock;
        private static Animation animation;
        private static int frame;
        private static float began, returnedAt;
        private static bool visual;
        private static Camera sideCamera, feetCamera;
        private static RenderTexture render;
        private static Texture2D pixels;
        private static int imageFrame;
        private static readonly StringBuilder csv = new StringBuilder();
        private static object Get(string name) => typeof(DinosaurAgent).GetField(name, Flags).GetValue(rex);
        private static void Set(string name, object value) => typeof(DinosaurAgent).GetField(name, Flags).SetValue(rex, value);
        private static void Call(string name, params object[] args) => typeof(DinosaurAgent).GetMethod(name, Flags).Invoke(rex, args);
        static TRexReturnFootAudit()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) EditorApplication.delayCall += Begin;
            };
        }
        public static void Run()
        {
            EditorSceneManager.OpenScene(TRexFrontTestController.ScenePath); SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }
        public static void RunVisual()
        {
            SessionState.SetBool(Key + ".Visual", true); Run();
        }
        private static Transform Knee(Transform foot) { var node = foot.parent; while (node != null && !node.name.Contains("Knee")) node = node.parent; return node; }
        private static void Begin()
        {
            try
            {
                var test = UnityEngine.Object.FindObjectOfType<TRexFrontTestController>(); rex = test.dinosaur;
                test.enabled = false; test.playerCamera.GetComponent<SmoothExplorerCamera>().enabled = false;
                test.playerCamera.transform.position = new Vector3(0f, 1.7f, 10.2f);
                animation = rex.GetComponentInChildren<Animation>(); footLock = rex.GetComponentInChildren<DinosaurFootLock>();
                before = rex.gameObject.AddComponent<TRexBeforeFootAudit>();
                before.left = TRexTurnCalibration.Bone(animation.transform, "jt_ClawMiddle_L"); before.right = TRexTurnCalibration.Bone(animation.transform, "jt_ClawMiddle_R");
                before.leftFoot = TRexTurnCalibration.Bone(animation.transform, "jt_Foot_L"); before.rightFoot = TRexTurnCalibration.Bone(animation.transform, "jt_Foot_R");
                before.leftKnee = Knee(before.leftFoot); before.rightKnee = Knee(before.rightFoot);
                before.cog = TRexTurnCalibration.Bone(animation.transform, "jt_Cog_C");
                var ls = typeof(DinosaurFootLock).GetField("leftSupport", Flags).GetValue(footLock);
                var rs = typeof(DinosaurFootLock).GetField("rightSupport", Flags).GetValue(footLock);
                before.leftLocalForward = (Vector3)ls.GetType().GetField("soleForward").GetValue(ls);
                before.rightLocalForward = (Vector3)rs.GetType().GetField("soleForward").GetValue(rs);
                visual = SessionState.GetBool(Key + ".Visual", false); imageFrame = 0;
                if (visual)
                {
                    Directory.CreateDirectory("Logs/ReturnFootVideo/side"); Directory.CreateDirectory("Logs/ReturnFootVideo/feet");
                    render = new RenderTexture(640, 480, 24); pixels = new Texture2D(640, 480, TextureFormat.RGB24, false);
                    sideCamera = new GameObject("Side audit camera").AddComponent<Camera>(); feetCamera = new GameObject("Foot audit camera").AddComponent<Camera>();
                    foreach (var camera in new[] { sideCamera, feetCamera })
                    { camera.enabled = false; camera.targetTexture = render; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.45f,.58f,.68f); camera.fieldOfView = 50f; camera.nearClipPlane = .05f; }
                }
                Call("BeginFrontInteraction"); Set("frontTestWaiting", false); Call("BeginFrontFollow", false); Call("BeginFrontNear"); Set("frontNearBite", InteractionMotionRole.BiteCloseRight);
                Time.captureDeltaTime = 1f / 60f; began = Time.time; returnedAt = -1f; frame = 0;
                csv.Clear(); csv.AppendLine("time,stage,phase,rawLX,rawLZ,rawRX,rawRZ,ikLX,ikLZ,ikRX,ikRZ,rawLKX,rawRKX,ikLKX,ikRKX,rawLYaw,rawRYaw,ikLYaw,ikRYaw,leftPlant,rightPlant,leftError,rightError,correction,leftHeelY,rightHeelY,leftToeY,rightToeY,rawPawnLYaw,rawPawnRYaw,ikPawnLYaw,ikPawnRYaw,landings");
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static float Yaw(Vector3 vector, Quaternion inverse) { vector = inverse * vector; vector.y = 0f; return Vector3.SignedAngle(Vector3.forward, vector, Vector3.up); }
        private static void Tick()
        {
            try
            {
                if (frame == Time.frameCount) return; frame = Time.frameCount;
                string stage = (bool)Get("frontActive") ? Get("frontStage").ToString() : "FreeHandoff";
                if (stage == "ReturnWalk" && returnedAt < 0f) returnedAt = Time.time;
                if (stage == "TurnBack" || stage == "ReturnWalk" || stage == "FreeHandoff" && returnedAt >= 0f)
                {
                    Quaternion inverse = Quaternion.Inverse(rex.transform.rotation * Quaternion.Euler(0f, rex.VisualYaw, 0f));
                    Vector3 l = inverse * (before.l - rex.transform.position), r = inverse * (before.r - rex.transform.position);
                    Vector3 il = inverse * (before.left.position - rex.transform.position), ir = inverse * (before.right.position - rex.transform.position);
                    Vector3 lk = inverse * (before.lk - rex.transform.position), rk = inverse * (before.rk - rex.transform.position);
                    Vector3 ilk = inverse * (before.leftKnee.position - rex.transform.position), irk = inverse * (before.rightKnee.position - rex.transform.position);
                    float phase = stage != "TurnBack" ? animation["walk slow loop"].time : (float)Get("frontActionTime");
                    int landings = (int)typeof(DinosaurFootLock).GetField("returnLandings", Flags).GetValue(footLock);
                    csv.AppendLine(FormattableString.Invariant($"{Time.time-began},{stage},{phase},{l.x},{l.z},{r.x},{r.z},{il.x},{il.z},{ir.x},{ir.z},{lk.x},{rk.x},{ilk.x},{irk.x},{Yaw(before.lh,inverse)},{Yaw(before.rh,inverse)},{Yaw(before.left.position-before.leftFoot.position,inverse)},{Yaw(before.right.position-before.rightFoot.position,inverse)},{footLock.LeftPlanted},{footLock.RightPlanted},{footLock.LeftPlantError},{footLock.RightPlantError},{footLock.MaximumLegCorrection},{before.leftHeelY},{before.rightHeelY},{before.l.y},{before.r.y},{Yaw(before.leftPawnForward,inverse)},{Yaw(before.rightPawnForward,inverse)},{Yaw(before.leftFoot.TransformDirection(before.leftLocalForward),inverse)},{Yaw(before.rightFoot.TransformDirection(before.rightLocalForward),inverse)},{landings}"));
                }
                bool done = returnedAt >= 0f && (int)typeof(DinosaurFootLock).GetField("returnLandings", Flags).GetValue(footLock) >= 2;
                if (visual && (stage == "TurnBack" || returnedAt >= 0f) && (frame % 2 == 0 || done)) Capture();
                if (done) Finish(null);
                if (returnedAt >= 0f && Time.time - returnedAt > 9.5f) throw new InvalidOperationException("The first two footfalls did not complete.");
                if (Time.time - began > 35f) throw new InvalidOperationException("Foot audit timed out.");
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Capture()
        {
            Vector3 forward = Vector3.ProjectOnPlane(before.cog.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            sideCamera.transform.position = rex.transform.position + right * 24f + Vector3.up * 4f;
            sideCamera.transform.LookAt(rex.transform.position + Vector3.up * 2.5f - forward);
            Vector3 center = (before.left.position + before.right.position) * .5f + Vector3.up * 1.1f;
            feetCamera.transform.position = center + right * 7f + forward * 3f + Vector3.up * 1.3f;
            feetCamera.transform.LookAt(center);
            foreach (var camera in new[] { sideCamera, feetCamera })
            {
                camera.Render(); var previous = RenderTexture.active; RenderTexture.active = render;
                pixels.ReadPixels(new Rect(0,0,640,480),0,0); pixels.Apply(); RenderTexture.active = previous;
                string folder = camera == sideCamera ? "side" : "feet";
                File.WriteAllBytes($"Logs/ReturnFootVideo/{folder}/{imageFrame:D4}.png", pixels.EncodeToPNG());
            }
            imageFrame++;
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            SessionState.SetBool(Key + ".Visual", false);
            if (sideCamera != null) UnityEngine.Object.DestroyImmediate(sideCamera.gameObject);
            if (feetCamera != null) UnityEngine.Object.DestroyImmediate(feetCamera.gameObject);
            if (render != null) UnityEngine.Object.DestroyImmediate(render);
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            File.WriteAllText("Logs/trex-return-foot-audit.csv", csv.ToString());
            if (visual) File.WriteAllText("Logs/ReturnFootVideo/capture-count.txt", imageFrame.ToString());
            if (error != null) Debug.LogException(error); else Debug.Log("RETURN_FOOT_AUDIT_SAVED");
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
