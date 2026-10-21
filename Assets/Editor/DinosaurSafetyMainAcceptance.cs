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
        private static Vector3 returnOrigin;
        private static Vector3 previous;
        private static readonly StringBuilder Report = new StringBuilder();
        private static object Get(DinosaurAgent agent, string field) => typeof(DinosaurAgent).GetField(field, Private).GetValue(agent);
        private static object Call(DinosaurAgent agent, string method, params object[] args)
            => typeof(DinosaurAgent).GetMethod(method, Private).Invoke(agent, args);
        private static bool PeerPoseBlocked(DinosaurAgent agent, Vector3 position, Quaternion rotation, DinosaurCoreBody body)
            => (bool)typeof(DinosaurAgent).GetMethod("FrontPeerPoseBlocked", Private, null,
                new[] { typeof(Vector3), typeof(Quaternion), typeof(DinosaurCoreBody) }, null)
                .Invoke(agent, new object[] { position, rotation, body });

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
                Report.Clear();
                // Captured from the reported TurnBack/YieldingToPeer deadlock in Main.
                PlaceEncounter();
                Call(raptor, "FreeIdle", 100f);
                travelled = 0f; previous = raptor.transform.position; coupled = sawPeerWait = false;
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
        private static bool StaticPoseClear(DinosaurAgent agent)
        {
            Vector3 forward = agent.transform.rotation * Quaternion.Euler(0f, agent.VisualYaw, 0f) * Vector3.forward;
            return !settings.PoseBlocked(new Vector2(agent.transform.position.x, agent.transform.position.z),
                new Vector2(forward.x, forward.z), (DinosaurCoreBody)Get(agent, "interactionBody"));
        }
        private static void PlaceEncounter()
        {
            Vector3 oldRex = new Vector3(22.518f, 0f, 1.4223f), oldRaptor = new Vector3(30.571f, 0f, 8.6561f);
            Quaternion oldRotation = Quaternion.Euler(0f, -82.47f, 0f), oldPeerRotation = Quaternion.Euler(0f, -101.2f, 0f);
            rex.transform.SetPositionAndRotation(oldRex, oldRotation);
            raptor.transform.SetPositionAndRotation(oldRaptor, oldPeerRotation);
            if (!StaticPoseClear(rex) || !StaticPoseClear(raptor))
            {
                var layout = UnityEngine.Object.FindObjectOfType<RainforestSampleLayout>();
                bool found = false;
                if (layout != null)
                for (int i = 0; i < layout.clearingPositions.Length && !found; i++)
                {
                    var origin = layout.clearingPositions[i]; origin.y = rex.MovementHeight;
                    var rotation = layout.clearingRotations[i];
                    rex.transform.SetPositionAndRotation(origin, rotation);
                    if (!StaticPoseClear(rex)) continue;
                    for (int bearing = 0; bearing < 8; bearing++)
                    {
                        Quaternion delta = Quaternion.Euler(0f, bearing * 45f, 0f) * rotation * Quaternion.Inverse(oldRotation);
                        Vector3 peer = origin + delta * (oldRaptor - oldRex); peer.y = raptor.MovementHeight;
                        raptor.transform.SetPositionAndRotation(peer, delta * oldPeerRotation);
                        if (!StaticPoseClear(raptor)) continue;
                        if (!NavMesh.SamplePosition(peer, out var hit, .25f, new NavMeshQueryFilter
                            { agentTypeID = settings.navigationAgentTypeId, areaMask = 1 })
                            || Vector2.Distance(new Vector2(peer.x, peer.z), new Vector2(hit.position.x, hit.position.z)) > .12f) continue;
                        if (PeerPoseBlocked(rex, origin, rotation, (DinosaurCoreBody)Get(rex, "interactionBody"))) continue;
                        found = true; break;
                    }
                }
                if (!found) throw new InvalidOperationException("Captured encounter is occupied and no equivalent safe clearing fixture exists.");
                Report.AppendLine("Captured coordinates are occupied by the restored forest; replaying the same encounter distance/orientations in a validated clearing.");
            }
            rex.GetComponent<DinosaurFootLock>()?.ResetSupportAnchors();
            raptor.GetComponent<DinosaurFootLock>()?.ResetSupportAnchors();
            returnOrigin = rex.transform.position;
        }
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
                    if (rex.IsOffRouteInteraction) Call(rex, "TickFrontInteraction");
                    else Call(rex, "Update");
                    sawPeerWait |= rex.FrontActionWaitingForSpace && rex.FrontBlockingReason == "Peer";
                    if (!StaticPoseClear(rex) || !StaticPoseClear(raptor))
                        throw new InvalidOperationException("Encounter entered a hard obstacle.");
                }
                float elapsed = Time.time - began, distance = Vector3.Distance(previous, rex.transform.position);
                if (coupled && !rex.IsOffRouteInteraction && Vector3.Distance(returnOrigin, rex.transform.position) > 1f)
                {
                    Report.AppendLine($"PASS coupled Main return: resumed actual roaming in {elapsed:F2}s; rex moved={Vector3.Distance(returnOrigin, rex.transform.position):F2}m; peerWait={sawPeerWait}; raptor travelled={travelled:F2}m; rexStage={rex.FrontInteractionStage}.");
                    Finish(null); return;
                }
                if (!coupled && distance >= 20f)
                {
                    Report.AppendLine($"PASS Main captured encounter: released in {elapsed:F2}s, travelled={travelled:F2}m, distance={distance:F2}m.");
                    coupled = true; travelled = 0f;
                    PlaceEncounter();
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
