using System;
using System.Collections.Generic;
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
    public static class StegosaurusSceneAcceptance
    {
        private const string Running = "JurassicWorld.StegosaurusAcceptance.Running";
        private const string RunIndex = "JurassicWorld.StegosaurusAcceptance.RunIndex";
        private const string ExitOnFinish = "JurassicWorld.StegosaurusAcceptance.ExitOnFinish";
        private static DinosaurAgent[] agents;
        private static Vector3[] previous;
        private static float[] travelled, lastProgress;
        private static readonly HashSet<DinosaurAgent.DinosaurState> States = new HashSet<DinosaurAgent.DinosaurState>();
        private static double deadline;
        private static float started;
        private static bool previewSaved;
        private static readonly FieldInfo ActiveClip = typeof(DinosaurAgent).GetField("activeClip", BindingFlags.NonPublic | BindingFlags.Instance);

        static StegosaurusSceneAcceptance()
        {
            if (SessionState.GetBool(Running, false)) EditorApplication.playModeStateChanged += OnPlayState;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        public static void VerifyEditMode()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var terrainHashes = Object.FindObjectsOfType<Terrain>().Select(t => AssetDatabase.GetAssetPath(t.terrainData)).Distinct()
                .ToDictionary(p => p, p => File.ReadAllBytes(p));
            var existing = Object.FindObjectsOfType<DinosaurAgent>().Where(a => a.name != "Stegosaurus").ToArray();
            var snapshots = existing.SelectMany(a => a.GetComponentsInChildren<Transform>(true).Cast<Object>()
                .Concat(a.GetComponents<Component>().Cast<Object>())).Distinct().ToDictionary(o => o, o => EditorJsonUtility.ToJson(o));
            for (int repeat = 0; repeat < 2; repeat++) StegosaurusSceneIntegration.AddToMainScene();
            foreach (var item in snapshots) Require(item.Key != null && EditorJsonUtility.ToJson(item.Key) == item.Value,
                "Incremental update changed an existing dinosaur: " + item.Key.name);
            foreach (var item in terrainHashes) Require(item.Value.SequenceEqual(File.ReadAllBytes(item.Key)), "Terrain changed: " + item.Key);
            var all = Object.FindObjectsOfType<DinosaurAgent>();
            Require(all.Length == 3 && all.Count(a => a.name == "Stegosaurus") == 1, "Expected exactly three dinosaurs and one Stegosaurus.");
            DinosaurAgent stego = all.Single(a => a.name == "Stegosaurus");
            var loader = stego.GetComponent<DinosaurFbxLoader>();
            Require(loader != null && loader.ModelRoot != null, "Missing Stegosaurus binding.");
            Require(stego.CollisionRadius == 3f, "Incorrect Stegosaurus clearance radius.");
            Require(stego.GetComponent<DinosaurCoreBody>()?.bands.Length > 0, "Missing trunk clearance profile.");
            var animation = loader.ModelRoot.GetComponentInChildren<Animation>(true);
            Require(animation != null && animation.Cast<AnimationState>().Count() == 2, "Expected two legacy clips.");
            foreach (AnimationState state in animation)
                Require(state.clip.legacy && state.clip.wrapMode == WrapMode.Loop && AnimationUtility.GetCurveBindings(state.clip).Length > 20,
                    "Invalid legacy animation: " + state.name);
            Require(loader.ModelRoot.GetComponentsInChildren<Animator>(true).Length == 0, "Animator conflicts with legacy playback.");
            var lod = loader.ModelRoot.GetComponentInChildren<LODGroup>(true);
            Require(lod != null && lod.GetLODs().Length == 6 && lod.GetLODs().All(l => l.renderers.Length > 0 && l.renderers.All(r => r != null)), "Missing LOD renderers.");
            foreach (Renderer renderer in loader.ModelRoot.GetComponentsInChildren<Renderer>(true))
                Require(renderer.sharedMaterials.All(m => m != null && AssetDatabase.GetAssetPath(m).StartsWith("Assets/FerociousIndustries/")), "Source materials not preserved.");
            if (File.Exists(FoliageInteractionPreparation.EnabledPath))
            {
                Require(stego.GetComponent<DinosaurFoliageInteractor>()?.capsules.Length > 0, "Missing foliage contact capsules.");
                FoliageInteractionPreparation.ValidateCurrent();
            }
            foreach (HudController hud in Object.FindObjectsOfType<HudController>())
                Require(new SerializedObject(hud).FindProperty("expectedDinosaurCount").intValue == 3, "Incorrect HUD count.");
            Debug.Log("STEGOSAURUS_EDIT_OK: idempotent add; existing dinosaurs and terrain unchanged; materials, six LODs, legacy clips, core and foliage profiles, HUD verified.");
        }

        public static void Run()
        {
            try
            {
                VerifyEditMode();
                SessionState.SetInt(RunIndex, 0);
                SessionState.SetBool(Running, true);
                EditorApplication.playModeStateChanged -= OnPlayState;
                EditorApplication.playModeStateChanged += OnPlayState;
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Finish(error); }
        }

        public static void RunAndExit()
        {
            SessionState.SetBool(ExitOnFinish, true);
            Run();
        }

        public static void InspectTrafficClearance()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var traffic = new[] { Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex"),
                Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Stegosaurus"),
                Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Velociraptor") };
            int[] routes = { 0, 2, 4 };
            for (int i = 0; i < traffic.Length; i++)
                for (int j = 0; j < traffic.Length; j++)
                {
                    if (i == j) continue;
                    float radius = traffic[i].CollisionRadius + traffic[j].CollisionRadius + .5f;
                    int clear = 0, outsideZones = 0; float maximum = 0f;
                    for (int k = 0; k < MovementCorridor.SampleCount(routes[i]); k++)
                    {
                        Vector2 point = MovementCorridor.GetSample(routes[i], k).Position;
                        float distance = MovementCorridor.MinDistanceToRoute(point, routes[j]);
                        maximum = Mathf.Max(maximum, distance);
                        if (distance >= radius) clear++;
                        if (!MovementCorridorCrossings.IsInside(routes[i], point, routes[j])) outsideZones++;
                    }
                    Debug.Log($"TRAFFIC_CLEARANCE route={routes[i]} other={routes[j]} radius={radius:F1} maxDistance={maximum:F1} safeSamples={clear} outsideZoneSamples={outsideZones}");
                }
        }

        private static void OnPlayState(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Running, false)) return;
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                if (SessionState.GetInt(RunIndex, 0) >= 2) { Finish(null); return; }
                EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
                return;
            }
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            try
            {
                agents = Object.FindObjectsOfType<DinosaurAgent>().OrderBy(a => a.name).ToArray();
                Require(agents.Length == 3 && agents.Select(a => a.RouteIndex).Distinct().Count() == 3, "Randomizer did not give three distinct routes.");
                previous = agents.Select(a => a.transform.position).ToArray();
                travelled = new float[agents.Length]; lastProgress = new float[agents.Length];
                started = Time.time; deadline = EditorApplication.timeSinceStartup + 180;
                States.Clear(); previewSaved = false;
                Time.timeScale = 4f;
                Debug.Log("STEGOSAURUS_PLAY_START " + SessionState.GetInt(RunIndex, 0) + ": " + string.Join(", ", agents.Select(a => a.name + " route=" + a.RouteIndex)));
                EditorApplication.update -= VerifyFrame;
                EditorApplication.update += VerifyFrame;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void VerifyFrame()
        {
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Stegosaurus play verification timeout.");
                if (Time.time - started < 1f) return;
                for (int i = 0; i < agents.Length; i++)
                {
                    DinosaurAgent agent = agents[i]; Vector3 position = agent.transform.position;
                    Require(agent.GetComponent<DinosaurFbxLoader>().IsLoaded, "Animation binding failed: " + agent.name);
                    float step = Vector3.Distance(position, previous[i]);
                    Require(step < 5f, "Unexpected teleport: " + agent.name);
                    travelled[i] += step; previous[i] = position;
                    if (step > .001f) lastProgress[i] = Time.time;
                    Require(Time.time - lastProgress[i] < 60f, "Dinosaur stuck: " + agent.name);
                    Require(MovementCorridor.MinDistanceToRoute(new Vector2(position.x, position.z), agent.RouteIndex) < .03f, "Dinosaur left route: " + agent.name);
                    Require(Mathf.Abs(position.y - agent.MovementHeight) < .001f, "Root height drifted: " + agent.name);
                    var core = agent.GetComponent<DinosaurCoreBody>();
                    if (core != null)
                    {
                        Vector3 forward = agent.transform.rotation * Quaternion.Euler(0f, agent.VisualYaw, 0f) * Vector3.forward;
                        Require(!TerrainTreeObstacleRegistry.IsCoreBlocked(new Vector2(position.x, position.z), new Vector2(forward.x, forward.z).normalized, core), "Core intersects trunk: " + agent.name);
                    }
                    for (int j = 0; j < i; j++)
                        Require(Vector3.Distance(position, agents[j].transform.position) >= agent.CollisionRadius + agents[j].CollisionRadius - .05f, "Dinosaurs overlap.");
                }
                DinosaurAgent stego = agents.Single(a => a.name == "Stegosaurus");
                States.Add(stego.CurrentState);
                Animation animation = stego.GetComponent<DinosaurFbxLoader>().ModelRoot.GetComponentInChildren<Animation>(true);
                // RandomWalk also plays Idle while yielding at a crossing or waiting for a peer.
                string playing = (string)ActiveClip.GetValue(stego);
                Require((playing == "Idle" || playing == "Walk") && animation.IsPlaying(playing),
                    "Stegosaurus effective movement state does not play its matching animation: " + playing);
                if (!previewSaved && Time.time - started > 5f && stego.CurrentState == DinosaurAgent.DinosaurState.RandomWalk)
                {
                    SavePreview(stego); previewSaved = true;
                }
                if (Time.time - started < 125f) return;
                Require(States.Contains(DinosaurAgent.DinosaurState.Idle) && States.Contains(DinosaurAgent.DinosaurState.RandomWalk), "No idle/walk transitions observed.");
                for (int i = 0; i < agents.Length; i++)
                {
                    Require(travelled[i] > 30f, "Insufficient movement: " + agents[i].name);
                    Debug.Log($"STEGOSAURUS_PLAY_OK run={SessionState.GetInt(RunIndex, 0)} dinosaur={agents[i].name} route={agents[i].RouteIndex} travelled={travelled[i]:F1}m duration={Time.time - started:F1}s");
                }
                EditorApplication.update -= VerifyFrame;
                SessionState.SetInt(RunIndex, SessionState.GetInt(RunIndex, 0) + 1);
                Time.timeScale = 1f;
                EditorApplication.isPlaying = false;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void SavePreview(DinosaurAgent stego)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            Camera camera = Camera.main;
            var target = new RenderTexture(1280, 720, 24);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
            Vector3 oldPosition = camera.transform.position; Quaternion oldRotation = camera.transform.rotation;
            LODGroup lod = stego.GetComponentInChildren<LODGroup>(true);
            try
            {
                Vector3 aim = stego.transform.position + Vector3.up * 2.3f;
                MovementCorridor.GetSampleAt(stego.RouteIndex, stego.RouteArc + 11f, out Vector2 cameraPoint, out Vector2 cameraTangent);
                Vector3 eye = new Vector3(cameraPoint.x, stego.MovementHeight + 3.5f, cameraPoint.y);
                eye += new Vector3(cameraTangent.y, 0f, -cameraTangent.x) * 2f;
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
                camera.targetTexture = target;
                Directory.CreateDirectory("Logs");
                foreach (int level in new[] { 0, 5 })
                {
                    lod.ForceLOD(level); camera.Render(); RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                    File.WriteAllBytes($"Logs/stegosaurus-lod{level}.png", image.EncodeToPNG());
                }
            }
            finally
            {
                lod.ForceLOD(-1); camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
            }
        }

        private static void Finish(Exception error)
        {
            SessionState.SetBool(Running, false);
            EditorApplication.update -= VerifyFrame;
            EditorApplication.playModeStateChanged -= OnPlayState;
            Time.timeScale = 1f;
            if (error != null) Debug.LogException(error);
            else Debug.Log("STEGOSAURUS_ACCEPTANCE_OK: two randomized Play sessions, each 125 simulated seconds.");
            bool exit = Application.isBatchMode || SessionState.GetBool(ExitOnFinish, false);
            SessionState.SetBool(ExitOnFinish, false);
            if (exit) EditorApplication.Exit(error == null ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}
