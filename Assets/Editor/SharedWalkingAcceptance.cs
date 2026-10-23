using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    // A short 60 Hz smoke check; never saves or regenerates the user's Main scene.
    [InitializeOnLoad]
    public static class SharedWalkingAcceptance
    {
        private const string Key = "JurassicWorld.SharedWalkingAcceptance";
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private const string Output = "Reports/SharedWalking";
        private static readonly StringBuilder Report = new StringBuilder();
        private static DinosaurAgent[] agents;
        private static Vector3[] previous;
        private static Quaternion[] rotations;
        private static float[] travel;
        private static int frame, idleFrames, curvedFrames, lastFrame;
        private static Bounds[] solids;
        private static DinosaurInteractionSettings settings;
        private static DinosaurAgent pachy;
        private static bool failed;
        private static void Set(object a, string n, object v) => a.GetType().GetField(n, Private).SetValue(a, v);
        private static T Get<T>(object a, string n) => (T)a.GetType().GetField(n, Private).GetValue(a);
        private static object Call(object a, string n, params object[] args) => a.GetType().GetMethod(n, Private).Invoke(a, args);
        private static void Require(bool v, string message) { if (!v) throw new InvalidOperationException(message); }

        static SharedWalkingAcceptance()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                    EditorApplication.delayCall += Begin;
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + ".done", false))
                {
                    SessionState.SetBool(Key + ".done", false);
                    if (Application.isBatchMode) EditorApplication.Exit(SessionState.GetInt(Key + ".exit", 1));
                }
            };
        }

        [MenuItem("Jurassic World/Acceptance/Shared Predictive Walking (60 FPS Smoke)")]
        public static void Run()
        {
            Directory.CreateDirectory(Output);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            foreach (var input in Object.FindObjectsOfType<EmotivPlayerInput>()) input.enabled = false;
            Object.FindObjectOfType<DinosaurInteractionSettings>().spawnSeed = 20261023;
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        private static void Begin()
        {
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                agents = Object.FindObjectsOfType<DinosaurAgent>().Where(a => Get<bool>(a, "groundObstacleAware")).ToArray();
                pachy = agents.Single(a => a.Species == DinosaurAgent.DinosaurSpecies.Pachycephalosaurus);
                settings = Object.FindObjectOfType<DinosaurInteractionSettings>();
                solids = settings.solidObstacles;
                previous = agents.Select(a => a.transform.position).ToArray();
                rotations = agents.Select(a => a.transform.rotation).ToArray();
                travel = new float[agents.Length];
                frame = idleFrames = curvedFrames = 0; failed = false;
                Report.Clear(); Report.AppendLine("# Shared predictive walking — 60 FPS smoke\n");
                var rex = agents.Single(a => a.Species == DinosaurAgent.DinosaurSpecies.Tyrannosaurus);
                Require(!(bool)typeof(DinosaurAgent).GetProperty("UsesWalkingSteering", Private).GetValue(rex), "Rex lost authored turns.");
                foreach (var a in agents.Where(a => a != rex))
                    Require((bool)typeof(DinosaurAgent).GetProperty("UsesWalkingSteering", Private).GetValue(a), a.name + " did not select walking steering.");
                CheckWalkingFixtures();
                lastFrame = -1;
                EditorApplication.update += Tick;
            }
            catch (Exception e) { Finish(e); }
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            try
            {
                frame++;
                if (frame < 3) return;
                for (int i = 0; i < agents.Length; i++)
                {
                    var a = agents[i];
                    float distance = Vector3.Distance(previous[i], a.transform.position);
                    float angle = Quaternion.Angle(rotations[i], a.transform.rotation);
                    if (a.Species != DinosaurAgent.DinosaurSpecies.Tyrannosaurus)
                    {
                        Require(distance > .000005f || angle < .002f, a.name + " rotated without displacement.");
                        Require(!a.IsSteppingTurn, a.name + " entered authored turn.");
                        if (a == pachy)
                        {
                            if (distance > .000005f && angle > .002f) curvedFrames++;
                            if (distance < .000005f && Get<string>(a, "activeClip") == Get<string>(a, "idleClip")) idleFrames++;
                        }
                    }
                    travel[i] += distance;
                    previous[i] = a.transform.position; rotations[i] = a.transform.rotation;
                }
                if (frame % 300 == 0) File.WriteAllText(Output + "/progress.txt", $"{frame / 60f:F1}s; Pachy={travel[Array.IndexOf(agents, pachy)]:F3}m; Idle={idleFrames}; curve={curvedFrames}; escape={pachy.IsEmergencyEscaping}; block={pachy.RoamingBlockReason}; traffic={pachy.GroundTrafficStatus}");
                if (frame == 300 || frame == 900 || frame == 1500) CapturePachy();
                if (frame < 1800) return;
                Require(travel[Array.IndexOf(agents, pachy)] > 1f, "Pachy did not walk.");
                Require(idleFrames > 0, "Main Idle was not exercised.");
                for (int i = 0; i < agents.Length; i++) Report.AppendLine($"- {agents[i].name}: {travel[i]:F3} m; {agents[i].CurrentState}; {agents[i].RoamingBlockReason}");
                Report.AppendLine($"\nPachy: Idle frames={idleFrames}; moving-turn frames={curvedFrames}.");
                Finish(null);
            }
            catch (Exception e) { Finish(e); }
        }

        private static void CapturePachy()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var cameraObject = new GameObject("Walking smoke camera");
            var camera = cameraObject.AddComponent<Camera>();
            if (Camera.main != null) camera.CopyFrom(Camera.main);
            camera.enabled = false;
            var target = new RenderTexture(960, 540, 24);
            var pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
            var previousTarget = RenderTexture.active;
            try
            {
                Vector3 forward = pachy.transform.rotation * Quaternion.Euler(0f, pachy.VisualYaw, 0f) * Vector3.forward;
                camera.transform.position = pachy.transform.position + Vector3.Cross(Vector3.up, forward) * 8f + Vector3.up * 2.5f;
                camera.transform.LookAt(pachy.transform.position + Vector3.up * 1.2f);
                camera.fieldOfView = 45f;
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); pixels.Apply();
                File.WriteAllBytes(Output + $"/Main-{frame / 60}s.png", pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousTarget; camera.targetTexture = null;
                Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(pixels); Object.DestroyImmediate(target);
            }
        }

        private static void CheckWalkingFixtures()
        {
            var root = new GameObject("Shared walking smoke fixture");
            var fixtureSettings = root.AddComponent<DinosaurInteractionSettings>();
            Vector3 origin = new Vector3(1000f, 0f, 1000f);
            fixtureSettings.activityBoundsOverride = new Rect(960f, 960f, 80f, 80f);
            var build = NavMesh.GetSettingsByIndex(0); build.agentRadius = .2f; build.agentHeight = 1f;
            var data = NavMeshBuilder.BuildNavMeshData(build, new List<NavMeshBuildSource> {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(84f, .2f, 84f),
                    transform = Matrix4x4.TRS(origin - Vector3.up * .1f, Quaternion.identity, Vector3.one) }
            }, new Bounds(origin, new Vector3(90f, 10f, 90f)), Vector3.zero, Quaternion.identity);
            var instance = NavMesh.AddNavMeshData(data);
            Set(fixtureSettings, "navigationInstance", instance);
            fixtureSettings.navigationAgentTypeId = build.agentTypeID;
            try
            {
                foreach (var mode in new[] { DinosaurAgent.RoamingMode.Free, DinosaurAgent.RoamingMode.ContinuousFree })
                {
                    var animal = new GameObject("Walking fixture"); animal.SetActive(false); animal.transform.SetParent(root.transform);
                    animal.transform.position = origin;
                    var body = animal.AddComponent<DinosaurCoreBody>();
                    body.bands = new[] { new DinosaurCoreBody.Band { bounds = new Bounds(Vector3.up * .5f, new Vector3(.3f, .8f, .6f)) } };
                    var a = animal.AddComponent<DinosaurAgent>();
                    Set(a, "interactionBody", body); Set(a, "coreBody", body); Set(a, "interactionSettings", fixtureSettings);
                    Set(a, "species", DinosaurAgent.DinosaurSpecies.Pachycephalosaurus);
                    Set(a, "startupRoamingMode", mode); Set(a, "walkSpeed", 1f); Set(a, "collisionRadius", .2f);
                    Set(a, "peers", Array.Empty<DinosaurAgent>());
                    Set(a, "random", new System.Random(42));
                    Set(a, "curiosityPath", new NavMeshPath());
                    Set(a, "curiosityCorners", new[] { origin + new Vector3(8f, 0f, 18f) });
                    Set(a, "curiosityCorner", 0); Set(a, "hasCuriosityPath", true);
                    float turn = 0f, distance = 0f;
                    for (int j = 0; j < 300; j++)
                    {
                        var before = a.transform.position; var rotation = a.transform.rotation;
                        Require((bool)Call(a, "MovePredictedPath", 1f / 60f, 1f), "Open walking arc rejected.");
                        float step = Vector3.Distance(before, a.transform.position);
                        float yaw = Quaternion.Angle(rotation, a.transform.rotation);
                        Require(step > .00001f, "Steering moved without translation.");
                        distance += step; turn += yaw;
                    }
                    Require(distance > 4.9f && turn > 5f, "Walking curve was not exercised.");
                    var stopped = a.transform.position; var stoppedRotation = a.transform.rotation;
                    fixtureSettings.solidObstacles = new[] { new Bounds(stopped + Vector3.up, new Vector3(20f, 8f, 20f)) };
                    Call(a, "ResetContinuousSteering");
                    Require(!(bool)Call(a, "MovePredictedPath", 1f / 60f, 1f), "Enclosed arc accepted.");
                    Require(a.transform.position == stopped && a.transform.rotation == stoppedRotation, "Blocked pose changed.");
                    fixtureSettings.solidObstacles = Array.Empty<Bounds>();
                    Call(a, "ResetContinuousSteering");
                    Require((bool)Call(a, "MovePredictedPath", 1f / 60f, 1f), "Walking did not resume.");
                    Report.AppendLine($"- {mode}: 60 Hz curve {distance:F3}m/{turn:F2} degrees, enclosed stop and resume PASS.");
                    // A blocked connector must escape after eight seconds, even though
                    // the source pose is inside a static obstacle. Destination stays checked.
                    Set(a, "<CurrentState>k__BackingField", DinosaurAgent.DinosaurState.RandomWalk);
                    var trapped = a.transform.position;
                    fixtureSettings.solidObstacles = new[] { new Bounds(trapped + Vector3.up, new Vector3(2f, 8f, 2f)) };
                    for (int j = 0; j < 479; j++)
                    {
                        Call(a, "UpdateEmergencyEscape", 1f / 60f);
                        Require(!a.IsEmergencyEscaping, "Escape triggered before eight seconds.");
                    }
                    for (int j = 0; j < 3 && !a.IsEmergencyEscaping; j++) Call(a, "UpdateEmergencyEscape", 1f / 60f);
                    Require(a.IsEmergencyEscaping, "Eight second escape did not activate.");
                    for (int j = 0; j < 600 && a.IsEmergencyEscaping; j++)
                    {
                        var before = a.transform.position; var rotation = a.transform.rotation;
                        Call(a, "UpdateEmergencyEscape", 1f / 60f);
                        Require(Vector3.Distance(before, a.transform.position) > .000005f
                            || Quaternion.Angle(rotation, a.transform.rotation) < .002f, "Escape rotated without movement.");
                    }
                    Require(!a.IsEmergencyEscaping && Vector3.Distance(trapped, a.transform.position) > 3f, "Emergency escape remained blocked.");
                    // Reintroduce a local enclosure after escape: ordinary checks must apply again.
                    Set(a, "curiosityCorners", new[] { a.transform.position + a.transform.forward * 20f });
                    Set(a, "curiosityCorner", 0); Set(a, "hasCuriosityPath", true);
                    fixtureSettings.solidObstacles = new[] { new Bounds(a.transform.position + Vector3.up, new Vector3(2f, 8f, 2f)) };
                    Call(a, "ResetContinuousSteering");
                    Require(!(bool)Call(a, "MovePredictedPath", 1f / 60f, 1f), "Ordinary collisions were not restored.");
                    fixtureSettings.solidObstacles = Array.Empty<Bounds>();
                    if (mode == DinosaurAgent.RoamingMode.Free)
                    {
                        Call(a, "FreeIdle", 2f);
                        for (int j = 0; j < 600; j++) Call(a, "UpdateEmergencyEscape", 1f / 60f);
                        Require(!a.IsEmergencyEscaping, "Intentional rest triggered escape.");
                    }
                    Report.AppendLine($"- {mode}: eight second escape crossed blocking geometry, reached checked open ground, restored collisions PASS.");
                    Object.DestroyImmediate(animal);
                }
            }
            finally { instance.Remove(); Object.DestroyImmediate(root); Object.DestroyImmediate(data); }
        }

        private static void Finish(Exception error)
        {
            if (failed) return;
            failed = true;
            EditorApplication.update -= Tick;
            if (settings != null && solids != null) settings.solidObstacles = solids;
            Time.captureDeltaTime = 0f;
            Report.AppendLine(error == null ? "\nPASS" : "\nFAIL: " + error);
            Directory.CreateDirectory(Output); File.WriteAllText(Output + "/Smoke.md", Report.ToString());
            if (error != null) Debug.LogException(error);
            SessionState.SetBool(Key, false); SessionState.SetBool(Key + ".done", true);
            SessionState.SetInt(Key + ".exit", error == null ? 0 : 1);
            EditorApplication.isPlaying = false;
        }
    }
}
