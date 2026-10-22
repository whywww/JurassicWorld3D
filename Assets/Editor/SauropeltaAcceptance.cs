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
    public static class SauropeltaAcceptance
    {
        private const string Key = "JurassicWorld.SauropeltaAcceptance";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static DinosaurAgent sauropelta;
        private static Animation animation;
        private static Vector3 previous;
        private static float started, travelled, lastTime, lastAnimationTime, wallStarted;
        private static Quaternion previousRotation, previousFootRotation;
        private static Transform animatedFoot;
        private static float animatedDegrees;
        private static float turned;
        private static int frame, walks, freezes, resumes, loopCrossings;
        private static bool wasFrozen, capturedWalk, capturedStop;
        private static string failure;

        static SauropeltaAcceptance()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                    EditorApplication.delayCall += Begin;
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + ".select", false))
                {
                    SessionState.SetBool(Key + ".select", false);
                    SelectInEditor();
                }
            };
        }

        [MenuItem("Jurassic World/Acceptance/Verify Sauropelta Main Roaming")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save scene edits first.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var preserved = Object.FindObjectsOfType<DinosaurAgent>(true).Where(a => a.name != "Sauropelta")
                .Select(a => (agent: a, position: a.transform.position, rotation: a.transform.rotation, scale: a.transform.localScale)).ToArray();
            var roots = EditorSceneManager.GetActiveScene().GetRootGameObjects().Where(o => o.name != "Sauropelta").ToArray();
            SauropeltaSceneIntegration.AddToMainScene();
            SauropeltaSceneIntegration.AddToMainScene();
            Require(preserved.All(p => p.agent != null && p.agent.transform.position == p.position && p.agent.transform.rotation == p.rotation && p.agent.transform.localScale == p.scale), "Existing dinosaur changed.");
            Require(roots.All(o => o != null), "Existing scene root removed.");
            Require(Object.FindObjectsOfType<DinosaurAgent>().Count(a => a.Species == DinosaurAgent.DinosaurSpecies.Sauropelta) == 1, "Exactly one Sauropelta required.");
            // These changes are test-only; Main was saved before they are applied.
            foreach (var input in Object.FindObjectsOfType<EmotivPlayerInput>()) input.enabled = false;
            Object.FindObjectOfType<DinosaurInteractionSettings>().spawnSeed = 314159;
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        public static void OpenForReview()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            SelectInEditor();
        }

        public static void SelectInEditor()
        {
            var agent = Object.FindObjectsOfType<DinosaurAgent>().FirstOrDefault(a => a.name == "Sauropelta");
            if (agent == null) return;
            Selection.activeGameObject = agent.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        private static void Begin()
        {
            try
            {
                sauropelta = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.Species == DinosaurAgent.DinosaurSpecies.Sauropelta);
                var modelBounds = sauropelta.GetComponentInChildren<SkinnedMeshRenderer>().bounds;
                var body = (DinosaurCoreBody)typeof(DinosaurAgent).GetField("interactionBody", Private).GetValue(sauropelta);
                var bodyBounds = body.bands[0].bounds;
                foreach (var band in body.bands.Skip(1)) bodyBounds.Encapsulate(band.bounds);
                Require(Mathf.Max(bodyBounds.size.x, bodyBounds.size.z) > 4f && Mathf.Max(bodyBounds.size.x, bodyBounds.size.z) < 6f,
                    "Navigation body must match the five metre rendered model: " + bodyBounds.size);
                Require(Mathf.Max(modelBounds.size.x, modelBounds.size.z) > 4f && Mathf.Max(modelBounds.size.x, modelBounds.size.z) < 6f, "Unexpected rendered size.");
                Require(sauropelta.IsFreeRoaming && !sauropelta.UsesRouteScheduling, "Free roaming required.");
                Require(Mathf.Abs(sauropelta.WalkSpeed - .65f) < .001f, "Sauropelta walk speed must be 0.65 m/s.");
                Require((bool)typeof(DinosaurAgent).GetMethod("FreeNavigationReady", Private).Invoke(sauropelta, null), "Navigation/body data missing.");
                animation = sauropelta.GetComponentInChildren<Animation>(true);
                Require(animation.Cast<AnimationState>().Select(s => s.name).SequenceEqual(new[] { "Walk" }), "Only original Walk expected.");
                Require(animation["Walk"].clip.legacy && Mathf.Abs(animation["Walk"].length - 5f) < .1f, "Original 5 second Legacy clip required.");
                Require(sauropelta.GetComponentInChildren<DinosaurFootLock>() == null, "Foot lock must stay disabled.");
                foreach (var renderer in sauropelta.GetComponentsInChildren<SkinnedMeshRenderer>())
                    Require(renderer.sharedMaterials.All(m => m != null && m.shader.isSupported && m.mainTexture != null && m.GetTexture("_BumpMap") != null), "Missing or unsupported material/texture.");
                animatedFoot = sauropelta.GetComponentsInChildren<Transform>().Single(t => t.name == "Bone.L.002");
                previousFootRotation = animatedFoot.localRotation; animatedDegrees = 0f;
                Capture("model");
                previous = sauropelta.transform.position; previousRotation = sauropelta.transform.rotation;
                started = Time.time; wallStarted = (float)EditorApplication.timeSinceStartup;
                travelled = turned = 0f; frame = -1; walks = freezes = resumes = loopCrossings = 0;
                wasFrozen = capturedWalk = capturedStop = false; failure = null;
                lastTime = animation["Walk"].time;
                Application.logMessageReceived += OnLog;
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || message.Contains("Missing navigation/body/foot")) failure = message;
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
            frame = Time.frameCount;
            try
            {
                Require(failure == null, failure);
                var state = animation["Walk"];
                float distance = Vector3.Distance(previous, sauropelta.transform.position);
                float rotation = Quaternion.Angle(previousRotation, sauropelta.transform.rotation);
                travelled += distance; turned += rotation;
                animatedDegrees += Quaternion.Angle(previousFootRotation, animatedFoot.localRotation);
                previousFootRotation = animatedFoot.localRotation;
                bool frozen = state.speed == 0f;
                if (frozen && wasFrozen && distance < .0001f)
                {
                    Require(Mathf.Abs(state.time - lastTime) < .002f, "Stopped pose kept animating.");
                    freezes++;
                    if (!capturedStop) { Capture("stop"); capturedStop = true; }
                }
                if (!frozen && wasFrozen) resumes++;
                if (distance > .0001f)
                {
                    Require(state.enabled && state.speed > 0f, "Moving without animation.");
                    Require(Mathf.Abs(state.speed - sauropelta.WalkSpeed / sauropelta.MoveAnimationGroundSpeed) < .01f, "Animation rate does not match ground speed.");
                    walks++;
                    if (!capturedWalk && Time.time - started > 5f) { Capture("walk"); capturedWalk = true; }
                    float before = Mathf.Repeat(lastTime, state.length), after = Mathf.Repeat(state.time, state.length);
                    if (after < before && !wasFrozen) loopCrossings++;
                }
                previous = sauropelta.transform.position; previousRotation = sauropelta.transform.rotation;
                wasFrozen = frozen; lastTime = state.time;
                if (Time.time - lastAnimationTime >= 5f)
                {
                    File.WriteAllText("Logs/sauropelta-progress.txt", $"{Time.time - started:F1}s; {travelled:F2}m; turns={turned:F1}; movingFrames={walks}; frozenFrames={freezes}; resumes={resumes}; loops={loopCrossings}; {sauropelta.AvoidanceStatus}; {sauropelta.RoamingBlockReason}");
                    lastAnimationTime = Time.time;
                }
                if (Time.time - started < 75f || EditorApplication.timeSinceStartup - wallStarted < 60f) return;
                Require(travelled > 5f && walks > 60, "Insufficient travel.");
                Require(freezes > 20 && resumes > 0, "Stop/resume not observed.");
                Require(animatedDegrees > 30f, "Animation time advances but leg bones do not animate.");
                Require(turned > 10f && loopCrossings > 0, "Turning/animation looping not observed.");
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Capture(string state)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var cameraObject = new GameObject("Sauropelta acceptance camera");
            var camera = cameraObject.AddComponent<Camera>();
            var target = RenderTexture.GetTemporary(960, 640, 24);
            var previousTarget = RenderTexture.active;
            var image = new Texture2D(960, 640, TextureFormat.RGB24, false);
            var originalLayers = sauropelta.GetComponentsInChildren<Transform>(true).Select(t => (node: t, layer: t.gameObject.layer)).ToArray();
            try
            {
                if (Camera.main != null) camera.CopyFrom(Camera.main);
                if (state == "model")
                {
                    foreach (var item in originalLayers) item.node.gameObject.layer = 31;
                    camera.cullingMask = 1 << 31;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = Color.black;
                }
                camera.enabled = false;
                camera.targetTexture = target;
                camera.fieldOfView = 45f;
                Vector3 center = sauropelta.transform.position + Vector3.up;
                Vector3 right = sauropelta.transform.rotation * Quaternion.Euler(0f, sauropelta.VisualYaw, 0f) * Vector3.right;
                var sun = RenderSettings.sun != null ? RenderSettings.sun : Object.FindObjectsOfType<Light>().FirstOrDefault(l => l.type == LightType.Directional);
                if (sun != null && Vector3.Dot(right, -sun.transform.forward) < 0f) right = -right;
                camera.transform.position = center + right * 7f + Vector3.up * 2f;
                camera.transform.LookAt(center);
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 960, 640), 0, 0);
                image.Apply();
                if (state == "model") Require(image.GetPixels32().Count(p => p.r > 10 || p.g > 10 || p.b > 10) > 1000, "Dinosaur is not visible in rendered image.");
                File.WriteAllBytes("Logs/sauropelta-" + state + ".png", image.EncodeToPNG());
            }
            finally
            {
                foreach (var item in originalLayers) if (item.node != null) item.node.gameObject.layer = item.layer;
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
            string result = error == null ? $"PASS: Main; {Time.time - started:F1}s simulation; {(float)EditorApplication.timeSinceStartup - wallStarted:F1}s wall; {travelled:F2}m; turns={turned:F1}; movingFrames={walks}; frozenFrames={freezes}; resumes={resumes}; loops={loopCrossings}; legMotion={animatedDegrees:F1} degrees; walkSpeed={sauropelta.WalkSpeed:F3}m/s; animationRate={sauropelta.WalkSpeed / sauropelta.MoveAnimationGroundSpeed:F3}x." : "FAIL: " + error;
            File.WriteAllText("Logs/sauropelta-acceptance.txt", result);
            if (error == null) Debug.Log(result); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else { SessionState.SetBool(Key + ".select", true); EditorApplication.isPlaying = false; }
        }
    }
}
