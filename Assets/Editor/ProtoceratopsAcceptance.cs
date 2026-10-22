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
    public static class ProtoceratopsAcceptance
    {
        private const string Key = "JurassicWorld.ProtoceratopsAcceptance";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static DinosaurAgent protoceratops;
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
        private static DinosaurInteractionSettings settings;
        private static DinosaurCoreBody navigationBody;
        private static int poseChecks;
        private static float lowestContact, highestContact;
        private static Vector3 previousGoal;
        private static int goalHandoffs, turningWalkFrames, idleFrames;
        private static double deadline;
        private static bool curveGoalInstalled;
        private static float nextCurveProbe;

        static ProtoceratopsAcceptance()
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

        [MenuItem("Jurassic World/Acceptance/Verify Protoceratops Main Roaming")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save scene edits first.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Require(Object.FindObjectsOfType<DinosaurAgent>().Count(a => a.Species == DinosaurAgent.DinosaurSpecies.Protoceratops) == 1, "Exactly one Protoceratops required.");
            ProtoceratopsEyeAcceptance.VerifyMain();
            // These changes are test-only; Main was saved before they are applied.
            foreach (var input in Object.FindObjectsOfType<EmotivPlayerInput>()) input.enabled = false;
            Object.FindObjectOfType<DinosaurInteractionSettings>().spawnSeed = 314159;
            File.WriteAllText("Logs/protoceratops-acceptance.txt", "RUNNING: Main shared continuous roaming, 120 seconds.");
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
            var agent = Object.FindObjectsOfType<DinosaurAgent>().FirstOrDefault(a => a.name == "Protoceratops");
            if (agent == null) return;
            Selection.activeGameObject = agent.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        private static void Begin()
        {
            try
            {
                protoceratops = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.Species == DinosaurAgent.DinosaurSpecies.Protoceratops);
                // World AABBs shrink along both axes at diagonal spawn headings.
                // Measure deformed vertices in the animal's own frame instead.
                var modelBounds = new Bounds();
                bool firstVertex = true;
                var sampledMesh = new Mesh();
                try
                {
                    foreach (var renderer in protoceratops.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        SauropeltaMeshSampling.Bake(renderer, sampledMesh);
                        foreach (var vertex in sampledMesh.vertices)
                        {
                            Vector3 point = protoceratops.transform.InverseTransformPoint(renderer.transform.TransformPoint(vertex));
                            if (firstVertex) { modelBounds = new Bounds(point, Vector3.zero); firstVertex = false; }
                            else modelBounds.Encapsulate(point);
                        }
                    }
                }
                finally { Object.DestroyImmediate(sampledMesh); }
                var body = (DinosaurCoreBody)typeof(DinosaurAgent).GetField("interactionBody", Private).GetValue(protoceratops);
                var bodyBounds = body.bands[0].bounds;
                foreach (var band in body.bands.Skip(1)) bodyBounds.Encapsulate(band.bounds);
                Require(Mathf.Max(bodyBounds.size.x, bodyBounds.size.z) > 1.7f && Mathf.Max(bodyBounds.size.x, bodyBounds.size.z) < 2.5f,
                    "Navigation body must match the two metre rendered model: " + bodyBounds.size);
                Require(Mathf.Max(modelBounds.size.x, modelBounds.size.z) > 1.7f && Mathf.Max(modelBounds.size.x, modelBounds.size.z) < 2.5f, "Unexpected rendered size: " + modelBounds.size);
                Require(protoceratops.IsContinuousRoaming && protoceratops.IsFreeRoaming && !protoceratops.UsesRouteScheduling,
                    "Shared continuous free roaming required.");
                Require(Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.Species == DinosaurAgent.DinosaurSpecies.Sauropelta).IsContinuousRoaming,
                    "Sauropelta reference must use the same ContinuousFree mode.");
                Require(protoceratops.WalkSpeed > .05f && protoceratops.WalkSpeed < 2f, "Protoceratops walk speed must match the sampled gait.");
                Require((bool)typeof(DinosaurAgent).GetMethod("FreeNavigationReady", Private).Invoke(protoceratops, null), "Navigation/body data missing.");
                animation = protoceratops.GetComponentInChildren<Animation>(true);
                Require(animation.Cast<AnimationState>().Select(s => s.name).SequenceEqual(new[] { "WalkCycle" }), "Only original Walk expected.");
                Require(animation["WalkCycle"].clip.legacy && Mathf.Abs(animation["WalkCycle"].length - 2f) < .1f, "Original 2 second Legacy clip required.");
                Require(protoceratops.GetComponentInChildren<DinosaurFootLock>() == null, "Foot lock must stay disabled.");
                foreach (var renderer in protoceratops.GetComponentsInChildren<SkinnedMeshRenderer>())
                    Require(renderer.sharedMaterials.All(m => m != null && m.shader.isSupported && (m.mainTexture != null || m.name == "Lens")), "Missing or unsupported material/texture.");
                navigationBody = body;
                settings = Object.FindObjectOfType<DinosaurInteractionSettings>();
                poseChecks = 0; lowestContact = float.MaxValue; highestContact = float.MinValue;
                var rex = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.Species == DinosaurAgent.DinosaurSpecies.Tyrannosaurus);
                Vector2 rexPosition = new Vector2(rex.transform.position.x, rex.transform.position.z);
                var safety = typeof(DinosaurAgent).GetMethod("SafetySegmentAllowed", Private);
                bool Allowed(float from, float to, bool retreat) => (bool)safety.Invoke(protoceratops,
                    new object[] { rexPosition + Vector2.right * from, rexPosition + Vector2.right * to, retreat });
                Require(!Allowed(16f, 14f, false) && Allowed(16f, 18f, false)
                    && Allowed(14f, 16f, true) && !Allowed(14f, 13f, true), "Tyrannosaurus safety boundary not enforced.");
                var config = new SerializedObject(protoceratops);
                Require(config.FindProperty("safetyDistance").floatValue == 15f
                    && config.FindProperty("safetyReleaseDistance").floatValue == 20f, "Unexpected safety distances.");
                Require(protoceratops.MinimapPortrait != null, "Minimap portrait missing.");
                Require(settings.solidObstacles.Any(obstacle => settings.PoseBlocked(
                    new Vector2(obstacle.center.x, obstacle.center.z), Vector2.up, body)), "Rock blocking not detected.");
                animatedFoot = protoceratops.GetComponentsInChildren<Transform>().Single(t => t.name == "protoceratops_foot.L_13");
                previousFootRotation = animatedFoot.localRotation; animatedDegrees = 0f;
                Capture("model");
                previous = protoceratops.transform.position; previousRotation = protoceratops.transform.rotation;
                started = Time.time; wallStarted = (float)EditorApplication.timeSinceStartup;
                previousGoal = (Vector3)typeof(DinosaurAgent).GetField("freeGoal", Private).GetValue(protoceratops);
                goalHandoffs = turningWalkFrames = idleFrames = 0;
                curveGoalInstalled = false; nextCurveProbe = 10f;
                deadline = EditorApplication.timeSinceStartup + 600;
                travelled = turned = 0f; frame = -1; walks = freezes = resumes = loopCrossings = 0;
                wasFrozen = capturedWalk = capturedStop = false; failure = null;
                lastTime = animation["WalkCycle"].time;
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
                Require(EditorApplication.timeSinceStartup < deadline, "Continuous roaming acceptance timed out.");
                var state = animation["WalkCycle"];
                if (frame % 15 == 0)
                {
                    Vector3 f = protoceratops.transform.rotation * Quaternion.Euler(0f, protoceratops.VisualYaw, 0f) * Vector3.forward;
                    Require(!settings.PoseBlocked(new Vector2(protoceratops.transform.position.x, protoceratops.transform.position.z),
                        new Vector2(f.x, f.z), navigationBody), "Roaming crossed a tree, rock, or activity boundary.");
                    var mesh = new Mesh();
                    float contact = float.MaxValue;
                    try
                    {
                        foreach (var renderer in protoceratops.GetComponentsInChildren<SkinnedMeshRenderer>())
                        {
                            SauropeltaMeshSampling.Bake(renderer, mesh);
                            foreach (Vector3 vertex in mesh.vertices) contact = Mathf.Min(contact, renderer.transform.TransformPoint(vertex).y);
                        }
                    }
                    finally { Object.DestroyImmediate(mesh); }
                    lowestContact = Mathf.Min(lowestContact, contact); highestContact = Mathf.Max(highestContact, contact);
                    Require(contact > -.04f && contact < .1f, "Foot contact outside ground tolerance: " + contact);
                    poseChecks++;
                }
                float distance = Vector3.Distance(previous, protoceratops.transform.position);
                float rotation = Quaternion.Angle(previousRotation, protoceratops.transform.rotation);
                travelled += distance; turned += rotation;
                float elapsed = Time.time - started;
                bool normal = !protoceratops.IsEmergencyEscaping && protoceratops.GroundTrafficStatus == "Inactive";
                if (elapsed > .25f && normal)
                {
                    if (protoceratops.CurrentState == DinosaurAgent.DinosaurState.Idle) idleFrames++;
                    Require(idleFrames == 0, "Normal continuous roaming entered an idle rest.");
                    if (rotation > .001f)
                    {
                        Require(distance > .000001f, "Continuous roaming turned in place.");
                        turningWalkFrames++;
                        if (turningWalkFrames == 20) Capture("curve");
                    }
                    if (distance < .000001f)
                        Require(protoceratops.ContinuousRoamingStatus.Contains("Waiting"), "Stopped without a safety obstruction.");
                }
                Vector3 goal = (Vector3)typeof(DinosaurAgent).GetField("freeGoal", Private).GetValue(protoceratops);
                if (Vector3.Distance(goal, previousGoal) > .01f)
                {
                    if (normal && distance > .000001f) goalHandoffs++;
                    previousGoal = goal;
                }
                if (normal && !curveGoalInstalled && elapsed >= nextCurveProbe)
                {
                    nextCurveProbe = elapsed + 1f;
                    curveGoalInstalled = TryInstallCurveGoal();
                }
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
                    Require(Mathf.Abs(state.speed - protoceratops.WalkSpeed / protoceratops.MoveAnimationGroundSpeed) < .01f, "Animation rate does not match ground speed.");
                    walks++;
                    if (!capturedWalk && Time.time - started > 5f) { Capture("walk"); capturedWalk = true; }
                    float before = Mathf.Repeat(lastTime, state.length), after = Mathf.Repeat(state.time, state.length);
                    if (after < before && !wasFrozen) loopCrossings++;
                }
                previous = protoceratops.transform.position; previousRotation = protoceratops.transform.rotation;
                wasFrozen = frozen; lastTime = state.time;
                if (Time.time - lastAnimationTime >= 5f)
                {
                    File.WriteAllText("Logs/protoceratops-progress.txt", $"{Time.time - started:F1}s; {travelled:F2}m; turns={turned:F1}; movingFrames={walks}; frozenFrames={freezes}; resumes={resumes}; loops={loopCrossings}; handoffs={goalHandoffs}; curveFrames={turningWalkFrames}; idleFrames={idleFrames}; {protoceratops.ContinuousRoamingStatus}; {protoceratops.AvoidanceStatus}; {protoceratops.RoamingBlockReason}");
                    lastAnimationTime = Time.time;
                }
                if (elapsed < 120f || EditorApplication.timeSinceStartup - wallStarted < 60f) return;
                Require(travelled > 5f && walks > 60, "Insufficient travel.");
                Require(curveGoalInstalled && goalHandoffs > 1 && turningWalkFrames > 20 && idleFrames == 0,
                    "Continuous goal handoff or walking turns not observed.");
                Require(animatedDegrees > 30f, "Animation time advances but leg bones do not animate.");
                Require(poseChecks > 30, "Insufficient obstacle/contact samples.");
                Require(turned > 10f && loopCrossings > 0, "Turning/animation looping not observed.");
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        // Only the test supplies a side goal. Steering, safety checks, speed and
        // obstacle handling all remain the same shared controller used in Main.
        private static bool TryInstallCurveGoal()
        {
            var type = typeof(DinosaurAgent);
            float maximum = (float)type.GetProperty("ContinuousMaximumYawRate", Private).GetValue(protoceratops);
            string[] fields = { "curiosityCorners", "curiosityCorner", "hasCuriosityPath", "freeGoal" };
            object[] saved = fields.Select(f => type.GetField(f, Private).GetValue(protoceratops)).ToArray();
            foreach (float sign in new[] { 1f, -1f })
            {
                object[] arc = { maximum * sign, 6f, Vector3.zero, Quaternion.identity };
                if (!(bool)type.GetMethod("WalkingArcSafe", Private).Invoke(protoceratops, arc)) continue;
                Vector3 end = (Vector3)arc[2]; Quaternion heading = (Quaternion)arc[3];
                Vector3 target = end + heading * Quaternion.Euler(0f, protoceratops.VisualYaw, 0f) * Vector3.forward * 4f;
                if ((bool)type.GetMethod("PlanCuriosityPath", Private).Invoke(protoceratops, new object[] { target, true, false }))
                {
                    type.GetField("freeGoal", Private).SetValue(protoceratops, target);
                    type.GetField("continuousProbeRemaining", Private).SetValue(protoceratops, 0f);
                    File.WriteAllText("Logs/protoceratops-curve-goal.txt", $"Safe test-only curve goal installed at {Time.time - started:F2}s; target={target:F3}. Main scene is not saved.\n");
                    return true;
                }
                for (int i = 0; i < fields.Length; i++) type.GetField(fields[i], Private).SetValue(protoceratops, saved[i]);
            }
            return false;
        }

        private static void Capture(string state)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var cameraObject = new GameObject("Protoceratops acceptance camera");
            var camera = cameraObject.AddComponent<Camera>();
            var target = RenderTexture.GetTemporary(960, 640, 24);
            var previousTarget = RenderTexture.active;
            var image = new Texture2D(960, 640, TextureFormat.RGB24, false);
            var originalLayers = protoceratops.GetComponentsInChildren<Transform>(true).Select(t => (node: t, layer: t.gameObject.layer)).ToArray();
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
                Vector3 center = protoceratops.transform.position + Vector3.up * .5f;
                Vector3 right = protoceratops.transform.rotation * Quaternion.Euler(0f, protoceratops.VisualYaw, 0f) * Vector3.right;
                var sun = RenderSettings.sun != null ? RenderSettings.sun : Object.FindObjectsOfType<Light>().FirstOrDefault(l => l.type == LightType.Directional);
                if (sun != null && Vector3.Dot(right, -sun.transform.forward) < 0f) right = -right;
                camera.transform.position = center + right * 3f + Vector3.up * .7f;
                camera.transform.LookAt(center);
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 960, 640), 0, 0);
                image.Apply();
                if (state == "model") Require(image.GetPixels32().Count(p => p.r > 10 || p.g > 10 || p.b > 10) > 1000, "Dinosaur is not visible in rendered image.");
                File.WriteAllBytes("Logs/protoceratops-" + state + ".png", image.EncodeToPNG());
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
            string result = error == null ? $"PASS: Main ContinuousFree (same mode as Sauropelta); {Time.time - started:F1}s simulation; {(float)EditorApplication.timeSinceStartup - wallStarted:F1}s wall; {travelled:F2}m; turns={turned:F1}; movingFrames={walks}; frozenFrames={freezes}; resumes={resumes}; loops={loopCrossings}; legMotion={animatedDegrees:F1} degrees; walkSpeed={protoceratops.WalkSpeed:F3}m/s; animationRate={protoceratops.WalkSpeed / protoceratops.MoveAnimationGroundSpeed:F3}x; obstacleChecks={poseChecks}; groundContact=[{lowestContact:F3},{highestContact:F3}]m; safetyBoundary=PASS; handoffs={goalHandoffs}; curveFrames={turningWalkFrames}; idleFrames={idleFrames}." : "FAIL: " + error;
            File.WriteAllText("Logs/protoceratops-acceptance.txt", result);
            if (error == null) Debug.Log(result); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else { SessionState.SetBool(Key + ".select", true); EditorApplication.isPlaying = false; }
        }
    }
}
