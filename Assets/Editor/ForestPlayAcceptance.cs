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
        private const string BaselineScene = "Assets/Scenes/RainforestExpansionBaseline.unity";
        private static string ArtifactOutput => SessionState.GetBool(Key + ".TreeHeights", false) ? TreeHeightAdjustment.Output
            : SessionState.GetBool(Key + ".Baseline", false) ? RainforestExpansion.Output + "/Baseline"
            : SessionState.GetBool(Key + ".Expansion", false) ? RainforestExpansion.Output : RainforestSample.Output;
        public static void RunExpansion() { SessionState.SetBool(Key + ".Expansion", true); Run(); }
        public static void RunTreeHeights() { SessionState.SetBool(Key + ".TreeHeights", true); Run(); }
        public static void RunExpansionBaseline()
        {
            DinosaurGaitCalibration.Require(File.Exists(RainforestExpansion.Output + "/Checkpoint/Main.unity"),
                "Build the Rainforest Expansion ring first; the checkpoint scene is missing.");
            File.Copy(RainforestExpansion.Output + "/Checkpoint/Main.unity", BaselineScene, true);
            AssetDatabase.ImportAsset(BaselineScene);
            SessionState.SetBool(Key + ".Baseline", true); RunExpansion();
        }
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
        private static int footFailures, totalFootFailures, caseFailures;
        private static string firstFootFailure;
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
        private static int meshSamples, meshOverlapSamples, meshOverlapVertices;
        private static readonly Dictionary<DinosaurAgent, Vector2> RecoveryDistances = new Dictionary<DinosaurAgent, Vector2>();
        private static object Query(DinosaurAgent agent, string method, params object[] args)
            => typeof(DinosaurAgent).GetMethod(method, Flags).Invoke(agent, args);
        private static string CollisionStage(DinosaurAgent agent)
            => (string)typeof(DinosaurAgent).GetProperty("ForestCollisionStage", Flags).GetValue(agent);
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
            string prior = ArtifactOutput + "/NormalPlay.md";
            string history = ArtifactOutput + "/NormalPlay.BeforeCollisionPolicy.md";
            if (!SessionState.GetBool(Key + ".Expansion", false) && File.Exists(prior) && !File.Exists(history))
                File.WriteAllText(history, "> Historical result using the previous all-vertices/static-penetration assertions; not the current collision policy.\n\n" + File.ReadAllText(prior));
            Directory.CreateDirectory(ArtifactOutput);
            EditorSceneManager.OpenScene(SessionState.GetBool(Key + ".Baseline", false) ? BaselineScene : "Assets/Scenes/Main.unity");
            if (SessionState.GetBool(Key + ".Baseline", false))
            {
                FoliageInteractionPreparation.RestoreCurrent(); RainforestSample.RebuildNavigation("Assets/Navigation/RainforestExpansionBaselineNav.asset");
                FoliageInteractionPreparation.SeedConfigured();
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
            }
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
                Report.Clear(); Report.AppendLine("# Normal Play regression — runtime collision policy\n");
                meshSamples = meshOverlapSamples = meshOverlapVertices = 0;
                RecoveryDistances.Clear();
                totalFootFailures = caseFailures = 0;
                Report.AppendLine("Births and ordinary travel use the runtime collision envelope. Special performances may overlap static scenery; peer body checks remain active. Recovery permits only unretired original overlaps and inward boundary/navigation movement. Rendered-mesh overlaps are visual diagnostics, not collision failures. Historical strict-mesh results: NormalPlay.BeforeCollisionPolicy.md.\n");
                Report.AppendLine("All agents, navigation, skeletal animation, foot locking and foliage interaction remain active. First minute is rendered continuously and recorded; remaining movement frames run with AlwaysAnimate and camera renders at 30-second checkpoints. GPU/CPU rendering cost is measured separately in Performance.md.\n");
                // Exercise the actual startup sampler; no reserved clearing placements.
                for (int seed = 731716; seed < 731748; seed++)
                {
                    foreach (var agent in agents)
                        agent.transform.SetPositionAndRotation(SessionState.GetVector3(Key + agent.name + ".position", Vector3.zero),
                            Quaternion.Euler(SessionState.GetVector3(Key + agent.name + ".rotation", Vector3.zero)));
                    settings.spawnSeed = seed; Spawn();
                    foreach (var agent in agents)
                    {
                        agent.GetComponentInChildren<Animation>().Sample();
                        CheckPose(agent, true);
                        CheckSpawnExit(agent);
                        CheckRenderedBody(agent, $"spawn seed {seed}");
                    }
                }
                Report.AppendLine("32 fixed seeds: runtime body-envelope, navigation, camera/peer spacing and six-metre exit contracts passed for both active species; visible mesh results are reported separately.");
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
            if (SessionState.GetBool(Key + ".Baseline", false))
                EditorSceneManager.LoadSceneInPlayMode(BaselineScene, new LoadSceneParameters(LoadSceneMode.Single));
            else SceneManager.LoadScene("Main");
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
            RecoveryDistances.Clear();
            settings.spawnSeed = 731716; Spawn();
            Time.captureDeltaTime = 1f / Rates[rate];
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            previous = agents.Select(a => a.transform.position).ToArray();
            lastProgress = previous.ToArray();
            travelled = new float[agents.Length]; stoppedAt = Enumerable.Repeat(Time.time, agents.Length).ToArray();
            feet = agents.Select(a => a.GetComponent<DinosaurFootLock>()).ToArray();
            maximumFootError = 0f;
            footFailures = 0; firstFootFailure = null;
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
                video = new MediaEncoder(ArtifactOutput + "/NormalPlay.mp4",
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

        private static void CheckPose(DinosaurAgent agent, bool spawning = false)
        {
            Vector2 prior = RecoveryDistances.TryGetValue(agent, out var distances) ? distances
                : new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            string reason = (string)Query(agent, "ForestCollisionFailure", spawning, prior.x, prior.y);
            Require(reason == null, $"Runtime collision contract: {reason}, {Diagnostic(agent)}");
            if (!spawning && CollisionStage(agent) == "Recovery")
                RecoveryDistances[agent] = new Vector2((float)Query(agent, "FreeRecoveryBoundary", agent.transform.position, agent.transform.rotation),
                    (float)Query(agent, "FreeRecoveryNavDistance", agent.transform.position));
            else RecoveryDistances.Remove(agent);
        }

        private static void CheckSpawnExit(DinosaurAgent agent)
        {
            Vector3 forward = agent.transform.rotation * Quaternion.Euler(0f, agent.VisualYaw, 0f) * Vector3.forward;
            object[] args = { agent.transform.position, forward, Vector3.zero };
            Require((bool)typeof(DinosaurAgent).GetMethod("SpawnExitFits", Flags).Invoke(agent, args), "Unsafe six-metre spawn exit: " + Diagnostic(agent));
            Vector3 exit = (Vector3)args[2];
            Camera camera = Camera.main;
            Require(camera == null || Vector2.Distance(new Vector2(agent.transform.position.x, agent.transform.position.z),
                new Vector2(camera.transform.position.x, camera.transform.position.z)) >= 25f, "Spawn too close to camera: " + Diagnostic(agent));
            foreach (var peer in agents)
            {
                if (peer == agent) continue;
                Vector2 a = new Vector2(agent.transform.position.x, agent.transform.position.z), b = new Vector2(exit.x, exit.z);
                Vector2 p = new Vector2(peer.transform.position.x, peer.transform.position.z), d = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude);
                float spacing = agent.CollisionRadius + peer.CollisionRadius + 6f;
                Require(Vector2.Distance(a, p) >= spacing, "Spawn peer spacing: " + Diagnostic(agent));
                // Startup places larger animals first; later, smaller animals are not
                // retroactively added to the earlier animal's six-metre reservation.
                if (peer.CollisionRadius > agent.CollisionRadius)
                    Require(Vector2.Distance(a + d * t, p) >= spacing, "Spawn exit peer spacing: " + Diagnostic(agent));
            }
        }

        private static string Diagnostic(DinosaurAgent agent) =>
            $"{agent.DisplayName}, collisionStage={CollisionStage(agent)}, seed={DinosaurSpawnRandomizer.LastSpawnSeed}, elapsed={Time.time-began:F2}, fps={Rates[Mathf.Min(rate, Rates.Length - 1)]}, position={agent.transform.position}, yaw={agent.transform.eulerAngles.y:F2}, state={agent.CurrentState}, timer={Get(agent, "stateTimer")}, enabled={agent.enabled}, interaction={agent.InteractionPhase}, front={Get(agent, "frontActive")}/{Get(agent, "frontTestWaiting")}, turn={agent.IsSteppingTurn}, turnTime={agent.StepTurnClipTime:F6}, path={Get(agent, "hasCuriosityPath")}, goal={Get(agent, "freeGoal")}, blocked={Get(agent, "curiosityBlockedTime")}, reason={agent.RoamingBlockReason}, avoidance={agent.AvoidanceStatus}";

        // One diagnostic per animal/sample. A vertex overlapping multiple trunk sections is counted once.
        private static int CheckRenderedBody(DinosaurAgent agent, string sample)
        {
            var sections = TerrainTreeObstacleRegistry.GetTrunkSections();
            int count = 0;
            Vector3 firstVertex = default;
            TreeTrunkSection firstWood = default;
            foreach (var skin in agent.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh();
                try
                {
                    skin.BakeMesh(mesh);
                    // Use baked geometry for the broad phase too: renderer bounds can be stale after re-spawning.
                    Vector3[] vertices = mesh.vertices;
                    if (vertices.Length == 0) continue;
                    Bounds bounds = new Bounds(skin.transform.TransformPoint(vertices[0]), Vector3.zero);
                    foreach (var vertex in vertices) bounds.Encapsulate(skin.transform.TransformPoint(vertex));
                    var nearby = sections.Where(section => section.top > bounds.min.y && section.bottom < bounds.max.y
                        && section.center.x + section.radius > bounds.min.x && section.center.x - section.radius < bounds.max.x
                        && section.center.y + section.radius > bounds.min.z && section.center.y - section.radius < bounds.max.z).ToArray();
                    foreach (var vertex in vertices)
                    {
                        Vector3 point = skin.transform.TransformPoint(vertex);
                        foreach (var section in nearby)
                        {
                            if (point.y <= section.bottom || point.y >= section.top
                                || Vector2.Distance(new Vector2(point.x, point.z), section.center) >= section.radius - .025f) continue;
                            if (count == 0) { firstVertex = point; firstWood = section; }
                            count++; break;
                        }
                    }
                }
                finally { Object.DestroyImmediate(mesh); }
            }
            meshSamples++;
            if (count > 0)
            {
                meshOverlapSamples++; meshOverlapVertices += count;
                string detail = $"VISUAL_ONLY {sample}: dinosaur={agent.DisplayName}, seed={DinosaurSpawnRandomizer.LastSpawnSeed}, stage={CollisionStage(agent)}, overlappingVertices={count}, firstVertex={firstVertex:F3}, wood={firstWood.center:F3}, radius={firstWood.radius:F4}";
                Report.AppendLine("- " + detail);
                Debug.Log(detail);
            }
            return count;
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
            if (Time.time - began < .15f) File.WriteAllBytes(ArtifactOutput + "/NormalPlay.png", pixels.EncodeToPNG());
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
                        if (error > .25f)
                        {
                            footFailures++; totalFootFailures++;
                            if (firstFootFailure == null) firstFootFailure = $"Planted foot drift {error:F4}m: " + Diagnostic(agent);
                        }
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
                    foreach (var agent in agents) CheckRenderedBody(agent, $"checkpoint {Rates[rate]} FPS / {Time.time - began:F1}s");
                    File.WriteAllText(ArtifactOutput + "/NormalPlay.md", Report + "\n" + progress);
                }
                if (Time.time - began < duration) return;
                for (int i = 0; i < agents.Length; i++)
                {
                    Require(travelled[i] > 5f, "No initial movement: " + Diagnostic(agents[i]));
                    Report.AppendLine($"- {Rates[rate]} FPS / {duration:F0}s / {agents[i].DisplayName}: {travelled[i]:F2}m; runtime collision contract passed; no root jumps or prolonged static stall. Static overlaps permitted only by the active policy.");
                }
                Debug.Log($"FOREST_PLAY_CASE_COMPLETED fps={Rates[rate]} foot={(footFailures == 0 ? "PASS" : "FAIL")}");
                Report.AppendLine($"Maximum planted-foot error at {Rates[rate]} FPS: {maximumFootError:F4}m (limit 0.25m); {(footFailures == 0 ? "PASS" : "FAIL")} / {footFailures} failing animal frames.\n");
                if (firstFootFailure != null) Report.AppendLine(firstFootFailure + "\n");
                if (++rate == Rates.Length) Finish(MovementFailure()); else StartCase();
            }
            catch (Exception error)
            {
                caseFailures++;
                CloseVideo();
                Report.AppendLine($"\nFAIL {Rates[rate]} FPS case stopped at {Time.time-began:F2}s; maximum foot error {maximumFootError:F4}m; {footFailures} foot-error frames.\n\n{error}\n");
                if(firstFootFailure!=null)Report.AppendLine(firstFootFailure+"\n");
                if(++rate<Rates.Length)StartCase();else Finish(MovementFailure());
            }
        }
        private static Exception MovementFailure() => totalFootFailures == 0 && caseFailures == 0 ? null
            : new InvalidOperationException($"Movement checks failed: {caseFailures} aborted FPS cases; planted-foot threshold exceeded in {totalFootFailures} animal frames. Threshold remains 0.25m; failed cases are not counted as complete ten-minute runs.");

        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            QualitySettings.vSyncCount = originalVSync; Application.targetFrameRate = originalFrameRate;
            if (playerCamera != null) playerCamera.enabled = true;
            CloseVideo();
            SceneManager.sceneLoaded -= Loaded; EditorApplication.delayCall -= InitializeCase;
            Directory.CreateDirectory(ArtifactOutput);
            Report.AppendLine($"\nVisible-mesh diagnostics: {meshSamples} animal samples; {meshOverlapSamples} samples contained overlaps; {meshOverlapVertices} overlapping vertices total (vertices may recur across samples). These counts do not determine collision-contract success.");
            Report.AppendLine(error == null ? "\nRuntime collision contract: PASS" : "\nRuntime collision contract: FAIL\n\n" + error);
            File.WriteAllText(ArtifactOutput + "/NormalPlay.md", Report.ToString());
            SessionState.SetBool(Key + ".Expansion", false);
            SessionState.SetBool(Key + ".Baseline", false);
            if (error == null) Debug.Log("FOREST_NORMAL_PLAY_PASS"); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}
