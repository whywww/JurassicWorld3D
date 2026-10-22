using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Media;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static partial class ContinuousRoamingAcceptance
    {
        private const string Key = "JurassicWorld.ContinuousRoamingAcceptance";
        private static DinosaurAgent live;
        private static Vector3 previous;
        private static Quaternion previousRotation;
        private static float began, travelled, yaw, maxFootError, nextVideo, nextProgress;
        private static int mainFrame, movingFrames, totalFrames, escapeCount;
        private static bool escaping;
        private static string lastStatus;
        private static readonly StringBuilder MainReport = new StringBuilder();
        private static Camera videoCamera;
        private static RenderTexture videoTarget;
        private static Texture2D videoPixels;
        private static MediaEncoder video;
        private static double deadline;
        static ContinuousRoamingAcceptance()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                    EditorApplication.delayCall += BeginMain;
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + ".finish", false))
                {
                    SessionState.SetBool(Key + ".finish", false);
                    EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                    SauropeltaAcceptance.SelectInEditor();
                    if (Application.isBatchMode) EditorApplication.Exit(SessionState.GetInt(Key + ".exit", 1));
                }
            };
        }
        private static void StartMain()
        {
            // Tests change only unsaved input/seed settings after the accepted scene is saved.
            foreach (var input in Object.FindObjectsOfType<EmotivPlayerInput>()) input.enabled = false;
            Object.FindObjectOfType<DinosaurInteractionSettings>().spawnSeed = 20261022;
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }
        private static void BeginMain()
        {
            try
            {
                live = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Sauropelta");
                Require(live.IsContinuousRoaming && live.IsFreeRoaming, "Sauropelta did not enter continuous mode.");
                var baurus = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Baurusuchus");
                Require(baurus.IsContinuousRoaming, "Baurusuchus migration failed.");
                Require(live.GetComponent<DinosaurFootLock>()?.GaitFootCount == 4, "Four support feet did not bind.");
                previous = live.transform.position; previousRotation = live.transform.rotation;
                began = Time.time; travelled = yaw = maxFootError = nextVideo = nextProgress = 0f;
                mainFrame = -1; movingFrames = totalFrames = escapeCount = 0; escaping = false; lastStatus = "";
                deadline = EditorApplication.timeSinceStartup + 600;
                MainReport.Clear(); MainReport.AppendLine("# Main continuous roaming, 120 seconds\n\nAll dinosaurs, normal navigation and peer traffic remain active.\n");
                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                {
                    videoCamera = new GameObject("Continuous roaming review camera").AddComponent<Camera>();
                    if (Camera.main != null) videoCamera.CopyFrom(Camera.main);
                    videoCamera.enabled = false; videoCamera.fieldOfView = 45f;
                    videoTarget = new RenderTexture(640, 360, 24); videoCamera.targetTexture = videoTarget;
                    videoPixels = new Texture2D(640, 360, TextureFormat.RGBA32, false);
                    video = new MediaEncoder(Output + "/Main-continuous.mp4", new VideoTrackAttributes {
                        width = 640, height = 360, frameRate = new MediaRational(8), includeAlpha = false, bitRateMode = VideoBitrateMode.High });
                }
                EditorApplication.update += TickMain;
            }
            catch (Exception error) { FinishMain(error); }
        }
        private static void TickMain()
        {
            if (!EditorApplication.isPlaying || mainFrame == Time.frameCount) return;
            mainFrame = Time.frameCount;
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Main play test timed out.");
                float distance = Vector3.Distance(previous, live.transform.position);
                travelled += distance; yaw += Mathf.Abs(Mathf.DeltaAngle(previousRotation.eulerAngles.y, live.transform.eulerAngles.y));
                if (distance > .00001f) movingFrames++; totalFrames++;
                previous = live.transform.position; previousRotation = live.transform.rotation;
                var feet = live.GetComponent<DinosaurFootLock>();
                maxFootError = Mathf.Max(maxFootError, feet.GaitMaximumReachError);
                Require(feet.GaitMaximumReachError <= .02f, $"Main foot reach exceeded 2cm: {feet.GaitMaximumReachError:F4}; {live.GroundTrafficStatus}");
                if (live.IsEmergencyEscaping && !escaping) escapeCount++;
                escaping = live.IsEmergencyEscaping;
                string status = live.ContinuousRoamingStatus + "; traffic=" + live.GroundTrafficStatus + "; escape=" + live.EmergencyEscapeStatus;
                if (status != lastStatus)
                { MainReport.AppendLine($"{Time.time - began:F2}s: {status}; no movement={live.NoMovementDuration:F2}s; {live.RoamingBlockReason}"); lastStatus = status; }
                float elapsed = Time.time - began;
                if (video != null && elapsed >= nextVideo)
                {
                    Vector3 center = live.transform.position + Vector3.up;
                    Vector3 side = live.transform.rotation * Vector3.right;
                    videoCamera.transform.position = center + side * 7f + Vector3.up * 2f;
                    videoCamera.transform.LookAt(center);
                    var old = RenderTexture.active;
                    try
                    {
                        videoCamera.Render(); RenderTexture.active = videoTarget;
                        videoPixels.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); videoPixels.Apply(); video.AddFrame(videoPixels);
                    }
                    finally { RenderTexture.active = old; }
                    nextVideo += .125f;
                }
                if (elapsed >= nextProgress)
                {
                    File.WriteAllText(Output + "/Progress.txt", $"{elapsed:F1}s; travel={travelled:F2}m; yaw={yaw:F1}; moving={movingFrames}/{totalFrames}; foot={maxFootError:F5}m; escapes={escapeCount}; {status}");
                    nextProgress += 10f;
                }
                if (elapsed < 120f) return;
                Require(travelled > 20f && yaw > 45f, $"Insufficient Main walking/turning: {travelled:F2}m, {yaw:F1}deg.");
                FinishMain(null);
            }
            catch (Exception error) { FinishMain(error); }
        }
        private static void FinishMain(Exception error)
        {
            EditorApplication.update -= TickMain;
            SessionState.SetBool(Key, false);
            video?.Dispose(); video = null;
            if (videoCamera != null) { videoCamera.targetTexture = null; Object.DestroyImmediate(videoCamera.gameObject); }
            if (videoTarget != null) Object.DestroyImmediate(videoTarget);
            if (videoPixels != null) Object.DestroyImmediate(videoPixels);
            MainReport.AppendLine($"\n{(error == null ? "PASS" : "FAIL")}: {Time.time - began:F2}s, travel={travelled:F3}m, yaw={yaw:F1}deg, moving={movingFrames}/{totalFrames}, max foot error={maxFootError:F5}m, emergency escapes={escapeCount}.");
            if (error != null) { MainReport.AppendLine(error.ToString()); Debug.LogException(error); }
            File.WriteAllText(Output + "/Main.md", MainReport.ToString());
            SessionState.SetBool(Key + ".finish", true); SessionState.SetInt(Key + ".exit", error == null ? 0 : 1);
            EditorApplication.isPlaying = false;
        }
    }
}
