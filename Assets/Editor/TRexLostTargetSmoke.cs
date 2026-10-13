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
    public static class TRexLostTargetSmoke
    {
        private const string Key = "JurassicWorld.LostTargetSmoke";
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static DinosaurAgent rex;
        private static DinosaurInteractionSettings settings;
        private static Bounds[] obstacles;
        private static DinosaurFootLock feet;
        private static int frame, scenario, stage;
        private static float began, exited, hiddenAt;
        private static Vector3 exitPosition;
        private static bool sawStop, sawLook;
        private static string lastRequestStatus;
        private static readonly StringBuilder trace = new StringBuilder();
        static TRexLostTargetSmoke()
        {
            EditorApplication.update += PollRequest;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) EditorApplication.delayCall += Begin;
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + ".Restore", false))
                {
                    SessionState.SetBool(Key + ".Restore", false);
                    EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                }
            };
        }
        private static void PollRequest()
        {
            const string request = "Logs/trex-lost-target-request.txt";
            if (!File.Exists(request)) return;
            string status = EditorApplication.isPlayingOrWillChangePlaymode ? "WAITING_FOR_PLAY_TO_STOP"
                : EditorApplication.isCompiling || EditorApplication.isUpdating ? "WAITING_FOR_IMPORT"
                : UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty ? "WAITING_FOR_SCENE_SAVE" : "STARTING";
            if (status != lastRequestStatus) { File.WriteAllText("Logs/trex-lost-target-status.txt", status); lastRequestStatus = status; }
            if (status != "STARTING") return;
            File.Delete(request);
            try { Run(); } catch (Exception error) { Finish(error); }
        }
        [MenuItem("Jurassic World/Acceptance/Verify T-Rex Lost Target Recovery (Short)")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            foreach (var behaviour in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                if (behaviour is EmotivPlayerInput || behaviour.GetType().Namespace == "Crest") behaviour.enabled = false;
            SessionState.SetBool(Key, true);
            SessionState.SetBool(Key + ".Restore", !Application.isBatchMode);
            EditorApplication.isPlaying = true;
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static object Get(string field) => typeof(DinosaurAgent).GetField(field, Private).GetValue(rex);
        private static void Set(string field, object value) => typeof(DinosaurAgent).GetField(field, Private).SetValue(rex, value);
        private static object Call(string method, params object[] values) => typeof(DinosaurAgent).GetMethod(method, Private).Invoke(rex, values);
        private static Vector3 Forward => rex.transform.rotation * Quaternion.Euler(0f, rex.VisualYaw, 0f) * Vector3.forward;
        private static void Begin()
        {
            try
            {
                rex = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex");
                settings = UnityEngine.Object.FindObjectOfType<DinosaurInteractionSettings>();
                obstacles = settings.solidObstacles;
                feet = rex.GetComponent<DinosaurFootLock>();
                foreach (var other in UnityEngine.Object.FindObjectsOfType<DinosaurAgent>())
                    if (other != rex) { other.enabled = false; other.transform.position = new Vector3(200f, 0f, 200f); }
                rex.ViewCamera.GetComponent<SmoothExplorerCamera>().enabled = false;
                Time.captureDeltaTime = 1f / 60f;
                scenario = frame = 0; trace.Clear(); Prepare();
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Prepare()
        {
            settings.solidObstacles = obstacles;
            Call("ClearInteractionNavigation");
            typeof(DinosaurAgent).GetProperty("InteractionPhase").SetValue(rex, DinosaurAgent.PlayerInteractionPhase.Inactive);
            Call("BeginIdle");
            Set("stateTimer", 1000f); Set("cooldownUntil", 0f); Set("interactionDuration", 40f); Set("lostSightGrace", 3f);
            bool found = false;
            foreach (var point in new[] { new Vector3(-22f, 0f, 8f), new Vector3(23f, 0f, 18f), new Vector3(0f, 0f, 49f) })
            {
                for (int yaw = 0; yaw < 360 && !found; yaw += 30)
                {
                    rex.transform.SetPositionAndRotation(point, Quaternion.Euler(0f, yaw - rex.VisualYaw, 0f));
                    feet.ResetSupportAnchors();
                    Vector3 sideDirection = Quaternion.Euler(0f, 35f, 0f) * Forward;
                    rex.ViewCamera.transform.position = point + sideDirection * 23f + Vector3.up * 1.7f;
                    var filter = new NavMeshQueryFilter { agentTypeID = settings.navigationAgentTypeId, areaMask = 1 };
                    var path = new NavMeshPath();
                    found = (bool)Call("CanSeePlayer") && NavMesh.CalculatePath(point, point + sideDirection * 13f, filter, path)
                        && path.status == NavMeshPathStatus.PathComplete
                        && (bool)Call("FreeTurnFitsAt", point, rex.transform.rotation, 90f);
                }
                if (found) break;
            }
            Require(found, "No visible-player/reachable-follow fixture with turn space.");
            stage = 0; began = Time.time; hiddenAt = -1f; sawStop = sawLook = false;
            trace.AppendLine($"case={scenario} start={began:F2} position={rex.transform.position}");
        }
        private static void HidePlayer()
        {
            rex.ViewCamera.transform.position = new Vector3(200f, 2f, 200f);
            hiddenAt = Time.time;
        }
        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying || rex == null || frame == Time.frameCount) return;
                frame = Time.frameCount;
                var phase = rex.InteractionPhase;
                trace.AppendLine($"{Time.time:F3},{scenario},{stage},{phase},{rex.IsSteppingTurn},{Get("stopAfterStepTurn")},{rex.transform.position}");
                if (stage == 0 && phase == DinosaurAgent.PlayerInteractionPhase.Following && (float)Get("actualInteractionSpeed") > .1f)
                {
                    if (scenario == 0) { HidePlayer(); stage = 2; }
                    else if (scenario == 3) { Set("interactionDuration", Time.time - (float)Get("interactionStarted") + 1f); stage = 2; }
                    else
                    {
                        // Alter the path, while detection/following/exit continue through the real controller.
                        Set("curiosityCorners", new[] { rex.transform.position + Quaternion.Euler(0f, 90f, 0f) * Forward * 12f });
                        Set("curiosityCorner", 0); Set("hasCuriosityPath", true); Set("nextReplan", Time.time + 30f);
                        stage = 1;
                    }
                }
                if (stage == 1 && rex.IsSteppingTurn)
                {
                    HidePlayer();
                    if (scenario == 2)
                        settings.solidObstacles = obstacles.Concat(new[] { new Bounds(rex.transform.position + Vector3.up, new Vector3(2f, 3f, 2f)) }).ToArray();
                    stage = 2;
                }
                if (stage == 2)
                {
                    if (phase == DinosaurAgent.PlayerInteractionPhase.Stopping)
                    {
                        sawStop = true;
                        if (scenario == 0)
                        {
                            Set("curiosityCorners", new[] { rex.transform.position + Quaternion.Euler(0f, 90f, 0f) * Forward * 12f });
                            Set("curiosityCorner", 0); Set("hasCuriosityPath", true);
                            Require(!rex.IsSteppingTurn, "Stopping restarted a turn.");
                        }
                        settings.solidObstacles = obstacles;
                    }
                    if (phase == DinosaurAgent.PlayerInteractionPhase.Looking) sawLook = true;
                    if (sawStop && sawLook && phase == DinosaurAgent.PlayerInteractionPhase.Inactive)
                    {
                        Require(!rex.IsSteppingTurn && !(bool)Get("hasCuriosityPath") && !(bool)Get("stopAfterStepTurn"), "Exit left stale turn/path state.");
                        exitPosition = rex.transform.position; exited = Time.time; stage = 3;
                        rex.ViewCamera.transform.position = new Vector3(200f, 2f, 200f);
                    }
                    if (hiddenAt > 0f) Require(Time.time - hiddenAt < 22f, "Lost-target exit did not complete.");
                }
                if (stage == 3)
                {
                    if (Vector3.Distance(exitPosition, rex.transform.position) > 1f)
                    {
                        trace.AppendLine($"CASE_PASS {scenario} resumed after {Time.time - exited:F2}s");
                        if (++scenario == 4) { Finish(null); return; }
                        Prepare(); return;
                    }
                    Require(Time.time - exited < 20f, "Did not resume wandering after exit.");
                }
                Require(Time.time - began < 65f, "Recovery scenario timed out before completing.");
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            if (settings != null && obstacles != null) settings.solidObstacles = obstacles;
            Directory.CreateDirectory("Logs");
            string result = error == null ? "PASS actual discovery/follow/lost sight/resume; stopping with a bent old path; lost sight during turn; blocked turn exit; duration exit. All four scenarios resumed free roaming without returning to a route. Short smoke; visual Play feedback required." : error.ToString();
            File.WriteAllText("Logs/trex-lost-target-result.txt", result);
            File.WriteAllText("Logs/trex-lost-target-trace.txt", trace.ToString());
            if (error == null) Debug.Log(result); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
