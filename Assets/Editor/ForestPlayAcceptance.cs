using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.Media;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    // Unlike the interaction fixtures this keeps the normal spawn, all dinosaurs,
    // normal goals, peer avoidance and animation updates running together.
    [InitializeOnLoad]
    public static class ForestPlayAcceptance
    {
        private const string Key = "JurassicWorld.ForestPlayAcceptance";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly int[] Rates = { 30, 60, 144 };
        private static readonly StringBuilder Report = new StringBuilder();
        private static DinosaurAgent[] agents;
        private static DinosaurInteractionSettings settings;
        private static Vector3[] previous, lastProgress;
        private static float[] travelled, stoppedAt;
        private static int rate, frame;
        private static float began;
        private static float lastCheckpoint, maximumFootError;
        private static DinosaurFootLock[] feet;
        private static int originalVSync, originalFrameRate;
        private static bool loading;
        private static double deadline;
        private static MediaEncoder video;
        private static Camera capture;
        private static RenderTexture target;
        private static Texture2D pixels;
        private static float nextCapture;
        private static Bounds[] vegetationBounds;
        private static Vector3 captureOffset;
        private static Camera playerCamera;
        private static object Get(DinosaurAgent agent, string field) => typeof(DinosaurAgent).GetField(field, Flags).GetValue(agent);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Spawn() => typeof(DinosaurSpawnRandomizer).GetMethod("RandomizeAll", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

        static ForestPlayAcceptance()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) EditorApplication.delayCall += Begin;
            };
        }

        [MenuItem("Jurassic World/Acceptance/Verify Normal Forest Play")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the scene and stop Play before acceptance.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            foreach (var agent in Object.FindObjectsOfType<DinosaurAgent>())
            {
                SessionState.SetVector3(Key + agent.name + ".position", agent.transform.position);
                SessionState.SetVector3(Key + agent.name + ".rotation", agent.transform.eulerAngles);
            }
            Object.FindObjectOfType<DinosaurInteractionSettings>().spawnSeed = 731716;
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        private static void Bind()
        {
            agents = Object.FindObjectsOfType<DinosaurAgent>().Where(a => a.IsFreeRoaming).OrderBy(a => a.name).ToArray();
            Require(agents.Length >= 2, "Both normal free-roaming dinosaurs must be active.");
            settings = Object.FindObjectOfType<DinosaurInteractionSettings>();
        }

        private static void Begin()
        {
            try
            {
                Bind();
                originalVSync = QualitySettings.vSyncCount; originalFrameRate = Application.targetFrameRate;
                Report.Clear(); Report.AppendLine("# Normal Play regression\n");
                Report.AppendLine("All agents, navigation, skeletal animation, foot locking and foliage interaction remain active. First minute is rendered continuously and recorded; remaining movement frames run with AlwaysAnimate and camera renders at 30-second checkpoints. GPU/CPU rendering cost is measured separately in Performance.md.\n");
                // Exercise the actual startup sampler; no reserved clearing placements.
                for (int seed = 731716; seed < 731748; seed++)
                {
                    foreach (var agent in agents)
                        agent.transform.SetPositionAndRotation(SessionState.GetVector3(Key + agent.name + ".position", Vector3.zero),
                            Quaternion.Euler(SessionState.GetVector3(Key + agent.name + ".rotation", Vector3.zero)));
                    settings.spawnSeed = seed; Spawn();
                    foreach (var agent in agents) { CheckPose(agent); CheckRenderedBody(agent); }
                }
                Report.AppendLine("32 fixed seeds: full-body spawn and six-metre exit checks passed for both active species.");
                rate = 0; deadline = EditorApplication.timeSinceStartup + 1800;
                StartCase();
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void StartCase()
        {
            // Reload resets every normal runtime state, animation and foot anchor.
            loading = true;
            SceneManager.sceneLoaded += Loaded;
            SceneManager.LoadScene("Main");
        }

        private static void Loaded(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= Loaded;
            EditorApplication.delayCall += InitializeCase;
        }

        private static void InitializeCase()
        {
            try
            {
            Bind();
            settings.spawnSeed = 731716; Spawn();
            Time.captureDeltaTime = 1f / Rates[rate];
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            previous = agents.Select(a => a.transform.position).ToArray();
            lastProgress = previous.ToArray();
            travelled = new float[agents.Length]; stoppedAt = Enumerable.Repeat(Time.time, agents.Length).ToArray();
            feet = agents.Select(a => a.GetComponent<DinosaurFootLock>()).ToArray();
            maximumFootError = 0f;
            began = Time.time; frame = -1;
            lastCheckpoint = began;
            foreach (var agent in agents) CheckPose(agent);
            playerCamera = Camera.main;
            foreach (var agent in agents)
                Require(agent.GetComponentInChildren<Animation>().cullingType == AnimationCullingType.AlwaysAnimate,
                    "Movement sampling requires animation to update without camera rendering.");
            if (rate > 0) playerCamera.enabled = false;
            if (rate == 0)
            {
                target = new RenderTexture(640, 360, 24); target.Create();
                capture = new GameObject("Normal Play recording").AddComponent<Camera>();
                capture.CopyFrom(Camera.main); capture.enabled = false; capture.targetTexture = target;
                pixels = new Texture2D(640, 360, TextureFormat.RGBA32, false);
                video = new MediaEncoder(RainforestSample.Output + "/NormalPlay.mp4",
                    new VideoTrackAttributes { frameRate = new MediaRational(10), width = 640, height = 360, includeAlpha = false });
                nextCapture = Time.time;
                vegetationBounds = Object.FindObjectsOfType<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy
                    && r.GetComponentInParent<DinosaurAgent>() == null && r.bounds.max.y > 1f && r.bounds.size.magnitude < 100f)
                    .Select(r => r.bounds).ToArray();
                var rex = agents.First(a => a.Species == DinosaurAgent.DinosaurSpecies.Tyrannosaurus);
                float best = float.PositiveInfinity;
                for (int direction = 0; direction < 16; direction++)
                {
                    Vector3 offset = Quaternion.Euler(0f, direction * 22.5f, 0f) * new Vector3(0f, 7f, 16f);
                    Vector3 view = rex.transform.position + offset, focus = rex.transform.position + Vector3.up * 2f;
                    var ray = new Ray(focus, (view - focus).normalized);
                    float distance = Vector3.Distance(view, focus), score = 0f;
                    foreach (var bounds in vegetationBounds)
                    {
                        if (bounds.Contains(view)) score += 100f;
                        if (bounds.IntersectRay(ray, out float hit) && hit < distance) score += 1f;
                    }
                    if (score < best) { best = score; captureOffset = offset; }
                }
            }
            Debug.Log($"FOREST_PLAY_START fps={Rates[rate]} seed={DinosaurSpawnRandomizer.LastSpawnSeed}");
            loading = false;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void CheckPose(DinosaurAgent agent)
        {
            var body = (DinosaurCoreBody)Get(agent, "interactionBody");
            Vector3 visual = agent.transform.rotation * Quaternion.Euler(0f, agent.VisualYaw, 0f) * Vector3.forward;
            string reason = settings.PoseBlockReason(new Vector2(agent.transform.position.x, agent.transform.position.z),
                new Vector2(visual.x, visual.z), body);
            Require(reason == null, $"Hard penetration: {agent.DisplayName}, {reason}, {Diagnostic(agent)}");
        }

        private static string Diagnostic(DinosaurAgent agent) =>
            $"{agent.DisplayName}, seed={DinosaurSpawnRandomizer.LastSpawnSeed}, elapsed={Time.time-began:F2}, fps={Rates[Mathf.Min(rate, Rates.Length - 1)]}, position={agent.transform.position}, yaw={agent.transform.eulerAngles.y:F2}, state={agent.CurrentState}, timer={Get(agent, "stateTimer")}, enabled={agent.enabled}, interaction={agent.InteractionPhase}, front={Get(agent, "frontActive")}/{Get(agent, "frontTestWaiting")}, turn={agent.IsSteppingTurn}, turnTime={agent.StepTurnClipTime:F6}, path={Get(agent, "hasCuriosityPath")}, goal={Get(agent, "freeGoal")}, blocked={Get(agent, "curiosityBlockedTime")}, reason={agent.RoamingBlockReason}, avoidance={agent.AvoidanceStatus}";

        private static void CheckRenderedBody(DinosaurAgent agent)
        {
            var sections = TerrainTreeObstacleRegistry.GetTrunkSections();
            foreach (var skin in agent.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                Bounds bounds = skin.bounds;
                var nearby = sections.Where(s => s.top > bounds.min.y && s.bottom < bounds.max.y
                    && s.center.x + s.radius > bounds.min.x && s.center.x - s.radius < bounds.max.x
                    && s.center.y + s.radius > bounds.min.z && s.center.y - s.radius < bounds.max.z).ToArray();
                if (nearby.Length == 0) continue;
                var mesh = new Mesh();
                try
                {
                    skin.BakeMesh(mesh);
                    foreach (var vertex in mesh.vertices)
                    {
                        Vector3 p = skin.transform.TransformPoint(vertex);
                        foreach (var section in nearby)
                            Require(p.y <= section.bottom || p.y >= section.top
                                || Vector2.Distance(new Vector2(p.x, p.z), section.center) >= section.radius - .025f,
                                $"Rendered mesh inside hard wood at {p}, wood={section.center}, radius={section.radius}: " + Diagnostic(agent));
                    }
                }
                finally { Object.DestroyImmediate(mesh); }
            }
        }

        private static void Record()
        {
            if (video == null || Time.time < nextCapture) return;
            if (Time.time - began >= 60f) { CloseVideo(); playerCamera.enabled = false; return; }
            nextCapture += .1f;
            var rex = agents.First(a => a.Species == DinosaurAgent.DinosaurSpecies.Tyrannosaurus);
            capture.transform.position = rex.transform.position + captureOffset;
            capture.transform.LookAt(rex.transform.position + Vector3.up * 2f);
            capture.Render(); var old = RenderTexture.active; RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); pixels.Apply(); RenderTexture.active = old;
            video.AddFrame(pixels);
            if (Time.time - began < .15f) File.WriteAllBytes(RainforestSample.Output + "/NormalPlay.png", pixels.EncodeToPNG());
        }

        private static void CloseVideo()
        {
            video?.Dispose(); video = null;
            if (capture != null) Object.DestroyImmediate(capture.gameObject);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (pixels != null) Object.DestroyImmediate(pixels);
        }

        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying || loading || frame == Time.frameCount) return;
                Require(EditorApplication.timeSinceStartup < deadline, "Normal Play wall-clock timeout.");
                frame = Time.frameCount;
                Record();
                for (int i = 0; i < agents.Length; i++)
                {
                    var agent = agents[i]; CheckPose(agent);
                    float step = Vector3.Distance(previous[i], agent.transform.position);
                    Require(step < Mathf.Max(.15f, 12f / Rates[rate]), "Root jump: " + Diagnostic(agent));
                    travelled[i] += step; previous[i] = agent.transform.position;
                    if (feet[i] != null)
                    {
                        float error = Mathf.Max(feet[i].LeftPlanted ? feet[i].LeftPlantError : 0f,
                            feet[i].RightPlanted ? feet[i].RightPlantError : 0f);
                        maximumFootError = Mathf.Max(maximumFootError, error);
                        Require(error <= .25f, $"Planted foot drift {error:F4}m: " + Diagnostic(agent));
                    }
                    if (Vector3.Distance(lastProgress[i], agent.transform.position) > .5f)
                    { lastProgress[i] = agent.transform.position; stoppedAt[i] = Time.time; }
                    Require(Time.time - stoppedAt[i] < 30f || agent.IsOffRouteInteraction || agent.IsAvoidingTyrannosaurus,
                        $"No roaming progress for 30s: lastMoveBlock={agent.LastMovementBlock}, travelled={travelled[i]:F2}, " + Diagnostic(agent));
                }
                float duration = rate == 0 ? 600f : 60f;
                if (Time.time - lastCheckpoint >= 30f)
                {
                    lastCheckpoint = Time.time;
                    string progress = $"RUNNING {Rates[rate]} FPS / {Time.time - began:F1}s: "
                        + string.Join("; ", agents.Select((a, i) => $"{a.DisplayName}={travelled[i]:F2}m"));
                    Debug.Log("FOREST_PLAY_PROGRESS " + progress);
                    if (playerCamera != null && !playerCamera.enabled) playerCamera.Render();
                    foreach (var agent in agents) CheckRenderedBody(agent);
                    File.WriteAllText(RainforestSample.Output + "/NormalPlay.md", Report + "\n" + progress);
                }
                if (Time.time - began < duration) return;
                for (int i = 0; i < agents.Length; i++)
                {
                    Require(travelled[i] > 5f, "No initial movement: " + Diagnostic(agents[i]));
                    Report.AppendLine($"- {Rates[rate]} FPS / {duration:F0}s / {agents[i].DisplayName}: {travelled[i]:F2}m; no hard penetration, jumps or prolonged static stall.");
                }
                Debug.Log($"FOREST_PLAY_CASE_PASS fps={Rates[rate]}");
                Report.AppendLine($"Maximum planted-foot error at {Rates[rate]} FPS: {maximumFootError:F4}m (limit 0.25m).\n");
                if (++rate == Rates.Length) Finish(null); else StartCase();
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            QualitySettings.vSyncCount = originalVSync; Application.targetFrameRate = originalFrameRate;
            if (playerCamera != null) playerCamera.enabled = true;
            CloseVideo();
            SceneManager.sceneLoaded -= Loaded; EditorApplication.delayCall -= InitializeCase;
            Directory.CreateDirectory(RainforestSample.Output);
            Report.AppendLine(error == null ? "\nPASS" : "\nFAIL\n\n" + error);
            File.WriteAllText(RainforestSample.Output + "/NormalPlay.md", Report.ToString());
            if (error == null) Debug.Log("FOREST_NORMAL_PLAY_PASS"); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}
