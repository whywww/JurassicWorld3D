using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class TRexInteractionAcceptance
    {
        private const string RunningKey = "JurassicWorld.TRexCuriosityAcceptance";
        private const string ResultPath = "Logs/trex-interaction-result.txt";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static DinosaurAgent rex;
        private static Camera camera;
        private static DinosaurInteractionSettings settings;
        private static DinosaurCoreBody body;
        private static Vector3 previous, origin, playerPosition;
        private static double deadline;
        private static float started, stageStarted, maximumDeparture, previousTime;
        private static int stage;
        private static bool sawObserve, sawStart, sawFollow, sawStop, sawLook, sawReturn;
        private static float playerShiftedAt;
        private static bool replanVerified;
        private static readonly StringBuilder trace = new StringBuilder();
        private static bool disabledMode;
        private static long clearanceTicks, maximumClearanceTicks;
        private static int clearanceSamples;
        private static readonly System.Collections.Generic.List<float> frameTimes = new System.Collections.Generic.List<float>();

        static TRexInteractionAcceptance()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += PollRequest;
        }
        private static void PollRequest()
        {
            const string requestPath = "Logs/trex-interaction-request.txt";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(requestPath)) return;
            string request = File.ReadAllText(requestPath).Trim(); File.Delete(requestPath);
            try
            {
                if (request == "setup") ConfigureMainScene();
                else if (request == "test") Run();
                else if (request == "routes") DinosaurRouteAcceptance.RunWithoutImages();
                else throw new InvalidOperationException("Unknown interaction request: " + request);
            }
            catch (Exception error) { Finish(error); }
        }
        [MenuItem("Jurassic World/Interaction/Configure T-Rex in Main Scene")]
        public static void ConfigureMainScene() => TRexInteractionBake.ConfigureMainScene();

        public static void CapturePreview()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            foreach (var behaviour in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                if (behaviour.GetType().Namespace == "Crest") behaviour.enabled = false;
            var dinosaur = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex");
            dinosaur.ApplySpawnPlacement(MovementCorridor.PointAt(0, 40f), 0);
            dinosaur.transform.SetPositionAndRotation(new Vector3(23f, 0f, 18f), Quaternion.Euler(0f, -dinosaur.VisualYaw, 0f));
            var animation = dinosaur.GetComponentInChildren<Animation>(true);
            foreach (var renderer in dinosaur.GetComponentsInChildren<Renderer>(true))
            { renderer.enabled = true; if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true; }
            var profile = (DinosaurInteractionProfile)new SerializedObject(dinosaur).FindProperty("interactionProfile").objectReferenceValue;
            profile.Find(InteractionMotionRole.LookForward).clip.SampleAnimation(animation.gameObject, 1f);
            foreach (Canvas canvas in UnityEngine.Object.FindObjectsOfType<Canvas>()) canvas.enabled = false;
            var shotCamera = dinosaur.ViewCamera;
            Vector3 f = dinosaur.transform.rotation * Quaternion.Euler(0f, dinosaur.VisualYaw, 0f) * Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, f);
            Vector3 focus = dinosaur.transform.position + f * 1.5f + Vector3.up * 2.5f;
            RenderPreview(shotCamera, dinosaur.transform.position + right * 18f - f * 3f + Vector3.up * 3f, focus, "trex-curiosity-rainforest.png");
            RenderPreview(shotCamera, new Vector3(-22f, 1.7f, 8f), new Vector3(2f, 2f, 20f), "trex-curiosity-player-forest.png");
        }

        private static void RenderPreview(Camera shotCamera, Vector3 position, Vector3 focus, string name)
        {
            var target = new RenderTexture(1280, 720, 24);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previousActive = RenderTexture.active;
            try
            {
                shotCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position));
                shotCamera.targetTexture = target;
                shotCamera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                Directory.CreateDirectory("Logs");
                File.WriteAllBytes("Logs/" + name, image.EncodeToPNG());
            }
            finally
            {
                shotCamera.targetTexture = null; RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
            }
        }

        [MenuItem("Jurassic World/Acceptance/Verify T-Rex Curiosity")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            disabledMode = SessionState.GetBool(RunningKey + ".Disabled", false);
            if (disabledMode) UnityEngine.Object.FindObjectOfType<DinosaurInteractionSettings>().allowOffRouteInteraction = false;
            if (Application.isBatchMode)
                foreach (var behaviour in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                    if (behaviour is EmotivPlayerInput || (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null
                        && behaviour.GetType().Namespace == "Crest")) behaviour.enabled = false;
            Directory.CreateDirectory("Logs");
            File.WriteAllText(disabledMode ? "Logs/trex-switch-off-result.txt" : ResultPath, "RUNNING");
            SessionState.SetBool(RunningKey, true);
            EditorApplication.isPlaying = true;
        }
        public static void RunDisabled()
        {
            SessionState.SetBool(RunningKey + ".Disabled", true);
            Run();
        }
        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += Begin;
            else if (change == PlayModeStateChange.ExitingPlayMode) { EditorApplication.update -= Tick; SessionState.SetBool(RunningKey, false); Time.timeScale = 1f; }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Set(string field, object value) => typeof(DinosaurAgent).GetField(field, Private).SetValue(rex, value);
        private static bool See() => (bool)typeof(DinosaurAgent).GetMethod("CanSeePlayer", Private).Invoke(rex, null);
        private static Vector3 Forward => rex.transform.rotation * Quaternion.Euler(0f, rex.VisualYaw, 0f) * Vector3.forward;

        private static void Begin()
        {
            try
            {
                rex = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex");
                camera = rex.ViewCamera;
                settings = UnityEngine.Object.FindObjectOfType<DinosaurInteractionSettings>();
                disabledMode = SessionState.GetBool(RunningKey + ".Disabled", false);
                if (disabledMode)
                {
                    Require(!settings.EnabledAtStartup, "OFF was not captured at startup.");
                    Require(!(bool)typeof(DinosaurAgent).GetField("interactionReady", Private).GetValue(rex), "OFF still allowed off-route interaction.");
                    settings.allowOffRouteInteraction = true;
                    Require(!settings.EnabledAtStartup, "Play edit changed startup OFF.");
                    Finish(null); return;
                }
                body = (DinosaurCoreBody)typeof(DinosaurAgent).GetField("interactionBody", Private).GetValue(rex);
                Require(settings.NavigationReady && settings.EnabledAtStartup, "Baked navigation was not registered.");
                Require((bool)typeof(DinosaurAgent).GetField("interactionReady", Private).GetValue(rex), "Curiosity animations did not bind.");
                settings.allowOffRouteInteraction = false;
                Require(settings.EnabledAtStartup, "Play edit changed startup setting.");
                foreach (var other in UnityEngine.Object.FindObjectsOfType<DinosaurAgent>())
                    if (other != rex) { other.enabled = false; other.transform.position = new Vector3(200f, other.MovementHeight, 200f); }
                camera.GetComponent<SmoothExplorerCamera>().enabled = false;
                Set("peers", Array.Empty<DinosaurAgent>());
                Set("stateTimer", 1000f);
                Set("interactionDuration", 20f);
                bool found = false;
                var filter = new NavMeshQueryFilter { agentTypeID = settings.navigationAgentTypeId, areaMask = 1 };
                var path = new NavMeshPath();
                for (int route = 0; route < MovementCorridor.RouteCount && !found; route++)
                    for (float arc = 0f; arc < MovementCorridor.Length(route) && !found; arc += 4f)
                    {
                        rex.ApplySpawnPlacement(MovementCorridor.PointAt(route, arc), route);
                        if (settings.PoseBlocked(new Vector2(rex.transform.position.x, rex.transform.position.z), new Vector2(Forward.x, Forward.z), body)) continue;
                        Vector3 direction = Quaternion.Euler(0f, 35f, 0f) * Forward;
                        playerPosition = rex.transform.position + direction * 23f + Vector3.up * 1.7f;
                        camera.transform.position = playerPosition;
                        Vector3 target = playerPosition - direction * 10f; target.y = 0f;
                        found = See() && NavMesh.CalculatePath(rex.transform.position, target, filter, path) && path.status == NavMeshPathStatus.PathComplete
                            && MovementCorridor.MinDistanceToRoute(new Vector2(target.x, target.z), rex.RouteIndex) > 2f;
                    }
                Require(found, "No reachable off-route curiosity scenario.");
                VerifyCollisionQueries();
                origin = previous = rex.transform.position;
                camera.transform.position = origin - Forward * 18f + Vector3.up * 1.7f;
                Require(!See(), "Player behind the dinosaur was visible.");
                camera.transform.position = origin + Vector3.Cross(Vector3.up, Forward) * 18f + Vector3.up * 1.7f;
                Require(!See(), "Player beside the dinosaur was visible.");
                stage = 0; started = stageStarted = previousTime = Time.time;
                maximumDeparture = 0f;
                sawObserve = sawStart = sawFollow = sawStop = sawLook = sawReturn = false;
                playerShiftedAt = -1f; replanVerified = false;
                trace.Clear(); deadline = EditorApplication.timeSinceStartup + 240;
                clearanceTicks = maximumClearanceTicks = 0; clearanceSamples = 0; frameTimes.Clear();
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Next(int value) { stage = value; stageStarted = Time.time; trace.AppendLine("stage=" + stage + " time=" + Time.time); }
        private static void VerifyCollisionQueries()
        {
            var original = settings.solidObstacles;
            Vector2 from = new Vector2(rex.transform.position.x, rex.transform.position.z);
            Vector2 forward = new Vector2(Forward.x, Forward.z);
            Vector2 right = new Vector2(forward.y, -forward.x);
            try
            {
                float sideReach = body.bands.Max(b => Mathf.Max(Mathf.Abs(b.bounds.min.x), Mathf.Abs(b.bounds.max.x)));
                Vector2 rock = from + right * (sideReach + .7f);
                settings.solidObstacles = new[] { new Bounds(new Vector3(rock.x, 2.5f, rock.y), new Vector3(.6f, 2f, .6f)) };
                Require(!settings.PoseBlocked(from, forward, body), "Synthetic rock overlaps starting pose.");
                Require(settings.StepBlocked(from, from, forward, right, body), "Turning sweep failed to detect rock.");
                Require(settings.StepBlocked(from - right * 10f, from + right * 10f, forward, forward, body), "Large step tunneled through rock.");
                Require(settings.PoseBlocked(new Vector2(WorldBounds.DinosaurActivityXZ.xMax + 1f, 0f), forward, body), "Body boundary check failed.");
                var filter = new NavMeshQueryFilter { agentTypeID = settings.navigationAgentTypeId, areaMask = 1 };
                Require(!NavMesh.SamplePosition(new Vector3(150f, 0f, 150f), out _, 1f, filter), "Navigation escaped activity bounds.");
                var peer = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>().First(a => a != rex);
                Vector3 peerPosition = peer.transform.position;
                try
                {
                    peer.transform.position = rex.transform.position + Forward * 5f;
                    Set("peers", new[] { peer });
                    var blocked = typeof(DinosaurAgent).GetMethod("FindBlockingStep", Private);
                    Require(blocked.Invoke(rex, new object[] { from, from + forward * .1f }) != null, "Peer approach was not blocked.");
                    Require(blocked.Invoke(rex, new object[] { from, from - forward * .1f }) == null, "Peer overlap prevented retreat.");
                }
                finally { peer.transform.position = peerPosition; Set("peers", Array.Empty<DinosaurAgent>()); }
                Debug.Log("CURIOSITY_COLLISION_OK: rotation sweep, large-step rock sweep, body boundary and nonnavigable target.");
            }
            finally { settings.solidObstacles = original; }
        }
        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying || rex == null) return;
                Require(EditorApplication.timeSinceStartup < deadline, "Acceptance timeout at stage " + stage + " phase " + rex.InteractionPhase);
                float dt = Time.time - previousTime;
                if (dt <= 0f) return;
                Vector3 position = rex.transform.position;
                Require(Vector3.Distance(position, previous) <= Mathf.Max(rex.ApproachSpeed, rex.WalkSpeed) * dt + .15f, "Interaction teleported.");
                long queryStart = System.Diagnostics.Stopwatch.GetTimestamp();
                bool poseBlocked = settings.PoseBlocked(new Vector2(position.x, position.z), new Vector2(Forward.x, Forward.z), body);
                long queryTicks = System.Diagnostics.Stopwatch.GetTimestamp() - queryStart;
                clearanceTicks += queryTicks; maximumClearanceTicks = Math.Max(maximumClearanceTicks, queryTicks); clearanceSamples++;
                frameTimes.Add(dt);
                Require(!poseBlocked, "Interaction body penetrated an obstacle or boundary.");
                previous = position; previousTime = Time.time;
                var phase = rex.InteractionPhase;
                string active = (string)typeof(DinosaurAgent).GetField("activeClip", Private).GetValue(rex);
                Require(active == null || (!active.ToLowerInvariant().Contains("bite") && !active.ToLowerInvariant().Contains("attack") && !active.ToLowerInvariant().Contains("roar")), "Attack animation used in curiosity.");
                sawObserve |= phase == DinosaurAgent.PlayerInteractionPhase.Observing;
                sawStart |= phase == DinosaurAgent.PlayerInteractionPhase.Starting;
                sawFollow |= phase == DinosaurAgent.PlayerInteractionPhase.Following;
                sawStop |= phase == DinosaurAgent.PlayerInteractionPhase.Stopping;
                sawLook |= phase == DinosaurAgent.PlayerInteractionPhase.Looking;
                sawReturn |= phase == DinosaurAgent.PlayerInteractionPhase.Returning;
                maximumDeparture = Mathf.Max(maximumDeparture, MovementCorridor.MinDistanceToRoute(new Vector2(position.x, position.z), rex.RouteIndex));
                float elapsed = Time.time - stageStarted;
                if (stage == 0)
                {
                    Require(phase == DinosaurAgent.PlayerInteractionPhase.Inactive, "Side player triggered curiosity.");
                    if (elapsed > .5f) { camera.transform.position = playerPosition; Next(1); }
                }
                else if (stage == 1)
                {
                    if (phase == DinosaurAgent.PlayerInteractionPhase.Following && playerShiftedAt < 0f)
                    {
                        camera.transform.position += Vector3.Cross(Vector3.up, Forward) * 4f;
                        playerShiftedAt = Time.time;
                    }
                    if (playerShiftedAt >= 0f && !replanVerified && Time.time - playerShiftedAt > .8f)
                    {
                        Vector3 planned = (Vector3)typeof(DinosaurAgent).GetField("plannedPlayer", Private).GetValue(rex);
                        Require(Vector3.Distance(planned, camera.transform.position) < .1f, "Player movement did not update the path.");
                        replanVerified = true;
                        camera.transform.position = playerPosition;
                    }
                    if (maximumDeparture > 2f && sawFollow && replanVerified && Time.time - playerShiftedAt > 1.5f)
                    {
                        camera.transform.position = position - Forward * 20f + Vector3.up * 1.7f;
                        Next(2);
                    }
                    Require(Time.time - started < 22f, "Did not leave route. phase=" + phase + " distance=" + maximumDeparture);
                }
                else if (stage == 2)
                {
                    if (elapsed < 2.5f) Require(phase != DinosaurAgent.PlayerInteractionPhase.Stopping, "Lost sight stopped interaction before grace elapsed.");
                    if (phase == DinosaurAgent.PlayerInteractionPhase.Returning) Next(3);
                }
                else if (stage == 3 && phase == DinosaurAgent.PlayerInteractionPhase.Inactive)
                {
                    Require(sawObserve && sawStart && sawFollow && sawStop && sawLook && sawReturn, "Natural state sequence incomplete.");
                    Require(MovementCorridor.MinDistanceToRoute(new Vector2(position.x, position.z), rex.RouteIndex) < .1f, "Return did not connect to original route.");
                    camera.transform.position = position + Forward * 18f + Vector3.up * 1.7f;
                    Next(4);
                }
                else if (stage == 4)
                {
                    Require(phase == DinosaurAgent.PlayerInteractionPhase.Inactive, "Cooldown did not suppress immediate retrigger.");
                    if (elapsed > 1f)
                    {
                        // Isolate the duration cap in a real clearing, with a generous sight grace.
                        rex.transform.position = new Vector3(-22f, 0f, 8f);
                        previous = rex.transform.position; previousTime = Time.time;
                        typeof(DinosaurAgent).GetMethod("BeginIdle", Private).Invoke(rex, null);
                        Set("stateTimer", 1000f); Set("cooldownUntil", 0f);
                        Set("interactionDuration", 12f); Set("lostSightGrace", 100f);
                        camera.transform.position = rex.transform.position + Forward * 20f + Vector3.up * 1.7f;
                        Require(See(), "Duration scenario cannot see player.");
                        Next(5);
                    }
                }
                else if (stage == 5)
                {
                    if (phase == DinosaurAgent.PlayerInteractionPhase.Stopping)
                    {
                        float began = (float)typeof(DinosaurAgent).GetField("interactionStarted", Private).GetValue(rex);
                        Require(Time.time - began >= 11.9f, "Duration scenario stopped before its cap.");
                        Debug.Log("TREX_CURIOSITY_OK: FOV, startup-only switch, moving-player replan, off-route follow, no attacks, lost-sight grace, full-body clearance, continuous return, cooldown, duration cap.");
                        trace.AppendLine("maximum route departure=" + maximumDeparture.ToString("F3"));
                        Finish(null);
                    }
                    Require(elapsed < 16f, "Duration cap did not stop follow.");
                }
                trace.AppendLine($"{Time.time:F2} {phase} position={position} clip={active}");
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Finish(Exception error)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(disabledMode ? "Logs/trex-switch-off-result.txt" : ResultPath, error == null ? (disabledMode ? "TREX_SWITCH_OFF_OK" : "TREX_CURIOSITY_OK") : error.ToString());
            if (!disabledMode) File.WriteAllText("Logs/trex-curiosity-trace.txt", trace.ToString());
            if (!disabledMode && clearanceSamples > 0)
            {
                double milliseconds = 1000d / System.Diagnostics.Stopwatch.Frequency;
                frameTimes.Sort();
                float p95 = frameTimes[Math.Min(frameTimes.Count - 1, (int)(frameTimes.Count * .95f))];
                File.WriteAllText("Logs/trex-curiosity-performance.txt", $"Graphics={SystemInfo.graphicsDeviceType}\nFull-body clearance mean={clearanceTicks * milliseconds / clearanceSamples:F4}ms max={maximumClearanceTicks * milliseconds:F4}ms samples={clearanceSamples}\nObserved Play tick interval mean={frameTimes.Average() * 1000f:F2}ms p95={p95 * 1000f:F2}ms\nEditor acceptance cadence, not a standalone build FPS benchmark. Headless runs omit ocean rendering.\n");
            }
            EditorApplication.update -= Tick;
            SessionState.SetBool(RunningKey, false);
            SessionState.SetBool(RunningKey + ".Disabled", false);
            if (error != null) Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }
    }
}
