using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class PachycephalosaurusAcceptance
    {
        private const string Key = "JurassicWorld.PachycephalosaurusAcceptance";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static DinosaurAgent pachy;
        private static Animation animation;
        private static Vector3 previous;
        private static float started, travelled;
        private static float nextProgress;
        private static float lastMoved;
        private static int frame, walks, idles;
        private static bool walking;
        private static bool capturedWalk, capturedIdle;
        private static string failure;

        static PachycephalosaurusAcceptance()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                    EditorApplication.delayCall += Begin;
            };
        }

        [MenuItem("Jurassic World/Acceptance/Verify Pachycephalosaurus Main Roaming")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save the scene first.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var preserved = Object.FindObjectsOfType<DinosaurAgent>(true).Where(a => a.Species != DinosaurAgent.DinosaurSpecies.Pachycephalosaurus)
                .Select(a => (agent: a, position: a.transform.position, rotation: a.transform.rotation, scale: a.transform.localScale)).ToArray();
            var roots = EditorSceneManager.GetActiveScene().GetRootGameObjects().Where(o => o.name != "Pachycephalosaurus").ToArray();
            PachycephalosaurusSceneIntegration.AddToMainScene();
            Require(preserved.All(p => p.agent != null && p.agent.transform.position == p.position && p.agent.transform.rotation == p.rotation && p.agent.transform.localScale == p.scale),
                "Updating Pachycephalosaurus changed an existing dinosaur.");
            Require(roots.All(o => o != null), "Updating Pachycephalosaurus removed an existing scene root.");
            Require(Object.FindObjectsOfType<DinosaurAgent>().Count(a => a.Species == DinosaurAgent.DinosaurSpecies.Pachycephalosaurus) == 1,
                "Main must contain exactly one Pachycephalosaurus.");
            foreach (var input in Object.FindObjectsOfType<EmotivPlayerInput>()) input.enabled = false;
            var settings = Object.FindObjectOfType<DinosaurInteractionSettings>();
            settings.spawnSeed = 314159;
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        private static void Begin()
        {
            try
            {
                pachy = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.Species == DinosaurAgent.DinosaurSpecies.Pachycephalosaurus);
                Require(pachy.IsFreeRoaming && !pachy.UsesRouteScheduling, "Pachycephalosaurus must freely roam.");
                Require((bool)typeof(DinosaurAgent).GetMethod("FreeNavigationReady", Private).Invoke(pachy, null), "Missing navigation/body data.");
                animation = pachy.GetComponentInChildren<Animation>(true);
                Require(animation.Cast<AnimationState>().Select(s => s.name).OrderBy(n => n).SequenceEqual(new[] { "Idle", "Walk" }), "Unexpected animations.");
                Require(animation["Idle"].clip.legacy && animation["Walk"].clip.legacy, "Legacy animation required.");
                Require(animation["Idle"].clip.events.Length == 0 && animation["Walk"].clip.events.Length == 0, "Vendor events remain.");
                Require(pachy.GetComponentsInChildren<Animator>(true).Length == 0, "Competing Animator remains.");
                previous = pachy.transform.position;
                started = Time.time; travelled = 0f; walks = idles = 0; walking = false; frame = -1; failure = null;
                nextProgress = 0f;
                lastMoved = Time.time;
                capturedWalk = capturedIdle = false;
                Time.captureDeltaTime = 1f / 30f;
                Application.logMessageReceived += OnLog;
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || message.Contains("Missing navigation/body/foot")) failure = message;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
            frame = Time.frameCount;
            try
            {
                Require(failure == null, failure);
                if (Vector3.Distance(previous, pachy.transform.position) > .001f) lastMoved = Time.time;
                Require(pachy.IsAvoidingTyrannosaurus || Time.time - lastMoved < 30f, "Normal roaming remained stationary for 30 seconds: " + pachy.RoamingBlockReason);
                travelled += Vector3.Distance(previous, pachy.transform.position);
                previous = pachy.transform.position;
                bool nowWalking = pachy.CurrentState == DinosaurAgent.DinosaurState.RandomWalk && animation.IsPlaying("Walk");
                if (nowWalking && !walking) walks++;
                if (!nowWalking && walking && animation.IsPlaying("Idle")) idles++;
                if (!capturedWalk && nowWalking && Time.time - started > 5f) { Capture("walk"); capturedWalk = true; }
                if (!capturedIdle && capturedWalk && !nowWalking && animation.IsPlaying("Idle")) { Capture("idle"); capturedIdle = true; }
                walking = nowWalking;
                if (Time.time - started >= nextProgress)
                {
                    File.WriteAllText("Logs/pachy-main-progress.txt", $"{Time.time - started:F1}s, {travelled:F2}m, walks={walks}, idles={idles}; {pachy.AvoidanceStatus}; {pachy.RoamingBlockReason}");
                    nextProgress += 30f;
                }
                if (Time.time - started < 180f) return;
                Require(travelled > 20f && walks >= 3 && idles >= 2, $"Insufficient roaming: {travelled:F2}m, walks={walks}, idles={idles}; {pachy.RoamingBlockReason}");
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Capture(string state)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var cameraObject = new GameObject("Pachy acceptance camera");
            var camera = cameraObject.AddComponent<Camera>();
            var target = RenderTexture.GetTemporary(960, 640, 24);
            var previousTarget = RenderTexture.active;
            var image = new Texture2D(960, 640, TextureFormat.RGB24, false);
            try
            {
                if (Camera.main != null) camera.CopyFrom(Camera.main);
                camera.enabled = false;
                camera.targetTexture = target;
                camera.fieldOfView = 45f;
                Vector3 center = pachy.transform.position + Vector3.up;
                Vector3 right = pachy.transform.rotation * Quaternion.Euler(0f, pachy.VisualYaw, 0f) * Vector3.right;
                var sun = RenderSettings.sun != null ? RenderSettings.sun : Object.FindObjectsOfType<Light>().FirstOrDefault(l => l.type == LightType.Directional);
                if (sun != null && Vector3.Dot(right, -sun.transform.forward) < 0f) right = -right;
                camera.transform.position = center + right * 7f + Vector3.up * 2f;
                camera.transform.LookAt(center);
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 960, 640), 0, 0);
                image.Apply();
                File.WriteAllBytes("Logs/pachy-" + state + ".png", image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousTarget;
                camera.targetTexture = null;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(cameraObject);
            }
        }

        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(Key, false);
            Time.captureDeltaTime = 0f;
            string result = error == null ? $"PASS: Main, 180 simulated seconds, {travelled:F2}m, walks={walks}, idles={idles}; Legacy Idle/Walk only; navigation ready." : "FAIL: " + error;
            File.WriteAllText("Logs/pachy-main-acceptance.txt", result);
            if (error == null) Debug.Log(result); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}
