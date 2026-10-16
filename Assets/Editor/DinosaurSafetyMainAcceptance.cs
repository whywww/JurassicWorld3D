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
    public static class DinosaurSafetyMainAcceptance
    {
        private const string Key = "JurassicWorld.SafetyMainEncounter";
        private const string Request = "Logs/dinosaur-main-encounter-request.txt";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static DinosaurAgent rex, raptor;
        private static DinosaurInteractionSettings settings;
        private static float began, travelled;
        private static int frame;
        private static bool coupled, sawPeerWait;
        private static Vector3 previous;
        private static readonly StringBuilder Report = new StringBuilder();
        private static object Get(DinosaurAgent agent, string field) => typeof(DinosaurAgent).GetField(field, Private).GetValue(agent);
        private static object Call(DinosaurAgent agent, string method, params object[] args)
            => typeof(DinosaurAgent).GetMethod(method, Private).Invoke(agent, args);

        static DinosaurSafetyMainAcceptance()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                    EditorApplication.delayCall += Begin;
            };
        }
        private static void Poll()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty) return;
            string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
            if (command == "safety") DinosaurSafetyAcceptance.Run();
            else if (command == "integration") TRexMainIntegrationSmoke.Run();
            else if (command == "bake") DinosaurSafetySetup.BakeMain();
            else Run();
        }
        [MenuItem("Jurassic World/Acceptance/Verify Main Raptor Encounter")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save the current scene first.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            foreach (var behaviour in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                if (behaviour is EmotivPlayerInput || behaviour is SmoothExplorerCamera || behaviour.GetType().Namespace == "Crest")
                    behaviour.enabled = false;
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }
        private static void Begin()
        {
            try
            {
                var agents = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>();
                rex = agents.Single(a => a.Species == DinosaurAgent.DinosaurSpecies.Tyrannosaurus);
                raptor = agents.Single(a => a.Species == DinosaurAgent.DinosaurSpecies.Velociraptor);
                foreach (var agent in agents) agent.enabled = false;
                settings = UnityEngine.Object.FindObjectOfType<DinosaurInteractionSettings>();
                // Captured from the reported TurnBack/YieldingToPeer deadlock in Main.
                rex.transform.SetPositionAndRotation(new Vector3(22.518f, 0f, 1.4223f), Quaternion.Euler(0f, -82.47f, 0f));
                raptor.transform.SetPositionAndRotation(new Vector3(30.571f, 0f, 8.6561f), Quaternion.Euler(0f, -101.2f, 0f));
                Call(raptor, "FreeIdle", 100f);
                Report.Clear(); travelled = 0f; previous = raptor.transform.position; coupled = sawPeerWait = false;
                Time.captureDeltaTime = 1f / 60f; began = Time.time; frame = -1;
                Call(raptor, "UpdateFreeRoaming");
                Diagnose();
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Diagnose()
        {
            var filter = (NavMeshQueryFilter)typeof(DinosaurAgent).GetProperty("NavigationFilter", Private).GetValue(raptor);
            Vector3 start = raptor.transform.position;
            var body = (DinosaurCoreBody)Get(raptor, "interactionBody");
            Vector3 f = raptor.transform.rotation * Quaternion.Euler(0f, raptor.VisualYaw, 0f) * Vector3.forward;
            bool onMesh = NavMesh.SamplePosition(start, out var hit, .25f, filter);
            Quaternion turn = Quaternion.RotateTowards(raptor.transform.rotation, Quaternion.LookRotation(awayDirection())
                * Quaternion.Euler(0f, -raptor.VisualYaw, 0f), 2f);
            Vector3 turnedForward = turn * Quaternion.Euler(0f, raptor.VisualYaw, 0f) * Vector3.forward;
            Report.AppendLine($"Turn probe: safe={Call(raptor, "StepPoseSafe", start, start, raptor.transform.rotation, turn, false)} staticStep={settings.StepBlocked(new Vector2(start.x, start.z), new Vector2(start.x, start.z), new Vector2(f.x, f.z), new Vector2(turnedForward.x, turnedForward.z), body)} navRay={NavMesh.Raycast(start, start, out _, filter)}.");
            Report.AppendLine($"Main captured pose: rex={rex.transform.position:F4} raptor={start:F4} distance={Vector3.Distance(start, rex.transform.position):F3}; nav={onMesh} offset={(onMesh ? Vector3.Distance(start, hit.position) : -1f):F4}; static={settings.PoseBlockReason(new Vector2(start.x, start.z), new Vector2(f.x, f.z), body)}; status={raptor.AvoidanceStatus}.");
            var away = (start - rex.transform.position).normalized;
            foreach (float length in new[] { 22f, 8f, 4f, 2f })
            {
                int sampled = 0, paths = 0, monotonic = 0;
                foreach (float angle in new[] { 0f, 30f, -30f, 60f, -60f, 85f, -85f })
                {
                    Vector3 candidate = start + Quaternion.Euler(0f, angle, 0f) * away * length;
                    if (!NavMesh.SamplePosition(candidate, out hit, 2f, filter)) continue;
                    sampled++;
                    var path = new NavMeshPath();
                    if (!NavMesh.CalculatePath(start, hit.position, filter, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                    paths++;
                    if ((bool)Call(raptor, "SafetyPathAllowed", (object)path.corners, true)) monotonic++;
                }
                Report.AppendLine($"Candidate length {length:F0}m: nav samples={sampled}, complete paths={paths}, monotonic={monotonic}.");
            }
            File.WriteAllText("Logs/dinosaur-main-encounter-result.txt", Report.ToString());
        }
        private static Vector3 awayDirection() => (raptor.transform.position - rex.transform.position).normalized;
        private static void Tick()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            try
            {
                Call(raptor, "UpdateFreeRoaming");
                travelled += Vector3.Distance(previous, raptor.transform.position); previous = raptor.transform.position;
                if (coupled)
                {
                    Call(rex, "TickFrontInteraction");
                    sawPeerWait |= rex.FrontActionWaitingForSpace && rex.FrontBlockingReason == "Peer";
                }
                float elapsed = Time.time - began, distance = Vector3.Distance(previous, rex.transform.position);
                if (coupled && !rex.FrontInteractionStage.StartsWith("TurnBack"))
                {
                    Report.AppendLine($"PASS coupled Main return: left TurnBack in {elapsed:F2}s; peerWait={sawPeerWait}; raptor travelled={travelled:F2}m; rexStage={rex.FrontInteractionStage}.");
                    Finish(null); return;
                }
                if (!coupled && distance >= 20f)
                {
                    Report.AppendLine($"PASS Main captured encounter: released in {elapsed:F2}s, travelled={travelled:F2}m, distance={distance:F2}m.");
                    coupled = true; travelled = 0f;
                    rex.transform.SetPositionAndRotation(new Vector3(22.518f, 0f, 1.4223f), Quaternion.Euler(0f, -82.47f, 0f));
                    raptor.transform.SetPositionAndRotation(new Vector3(30.571f, 0f, 8.6561f), Quaternion.Euler(0f, -101.2f, 0f));
                    Call(raptor, "FreeIdle", 100f);
                    Call(rex, "BeginFrontInteraction"); Call(rex, "BeginFrontReturn");
                    began = Time.time; previous = raptor.transform.position;
                    return;
                }
                if (elapsed >= 15f) throw new InvalidOperationException($"Main encounter still blocked: travelled={travelled:F3}m distance={distance:F3}m status={raptor.AvoidanceStatus} hasPath={Get(raptor, "hasCuriosityPath")} rotation={raptor.transform.eulerAngles} blocked={Get(raptor, "curiosityBlockedTime")} corner={Get(raptor, "curiosityCorner")} corners={string.Join(";", ((Vector3[])Get(raptor, "curiosityCorners")).Select(p => p.ToString("F4")))}.");
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            if (error != null) { Report.AppendLine("FAIL: " + error); Debug.LogException(error); }
            File.WriteAllText("Logs/dinosaur-main-encounter-result.txt", Report.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += () => EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            }
        }
    }
}
