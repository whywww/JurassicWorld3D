using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class DinosaurSafetyAcceptance
    {
        private const string Key = "JurassicWorld.DinosaurSafetyAcceptance";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static DinosaurAgent raptor, rex;
        private static DinosaurInteractionSettings settings;
        private static int scenario, lastFrame;
        private static float began, previousDistance, travelled;
        private static Vector3 previousPosition;
        private static bool moved;
        private static readonly StringBuilder Report = new StringBuilder();
        private static object Call(string method, params object[] args) => typeof(DinosaurAgent).GetMethod(method, Private).Invoke(raptor, args);
        private static void Set(DinosaurAgent agent, string field, object value) => typeof(DinosaurAgent).GetField(field, Private).SetValue(agent, value);
        private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }

        static DinosaurSafetyAcceptance()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                    EditorApplication.delayCall += Begin;
            };
        }

        [MenuItem("Jurassic World/Acceptance/Verify Raptor Safety and Free Roaming")]
        public static void Run() => RunFor(DinosaurAgent.DinosaurSpecies.Velociraptor);

        [MenuItem("Jurassic World/Acceptance/Verify Pachycephalosaurus Safety and Free Roaming")]
        public static void RunPachycephalosaurus() => RunFor(DinosaurAgent.DinosaurSpecies.Pachycephalosaurus);

        private static void RunFor(DinosaurAgent.DinosaurSpecies roamingSpecies)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save the current scene first.");
            var main = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var agents = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>();
            var test = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SessionState.SetInt(Key + ".Species", (int)roamingSpecies);
            foreach (var species in new[] { DinosaurAgent.DinosaurSpecies.Tyrannosaurus, roamingSpecies })
            {
                var source = agents.Single(a => a.Species == species);
                Require(new SerializedObject(source).FindProperty("interactionBody").objectReferenceValue != null, "Missing baked body.");
                var clone = UnityEngine.Object.Instantiate(source.gameObject);
                clone.name = source.name;
                SceneManager.MoveGameObjectToScene(clone, test);
                var data = new SerializedObject(clone.GetComponent<DinosaurAgent>());
                data.FindProperty("viewCamera").objectReferenceValue = null;
                data.FindProperty("playerInteractionEnabled").boolValue = false;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.CloseScene(main, true);
            SceneManager.SetActiveScene(test);
            var configuration = new GameObject("Safety test navigation").AddComponent<DinosaurInteractionSettings>();
            configuration.allowOffRouteInteraction = false;
            var build = NavMesh.GetSettingsByIndex(0);
            build.agentRadius = .5f;
            var sources = new System.Collections.Generic.List<NavMeshBuildSource> {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(120f, .2f, 120f),
                    transform = Matrix4x4.TRS(new Vector3(0f, -.1f, 10f), Quaternion.identity, Vector3.one), area = 0 }
            };
            configuration.navigationAgentTypeId = build.agentTypeID;
            configuration.navigationData = NavMeshBuilder.BuildNavMeshData(build, sources,
                new Bounds(new Vector3(0f, 0f, 10f), new Vector3(130f, 10f, 130f)), Vector3.zero, Quaternion.identity);
            Require(configuration.navigationData != null, "Fixture navigation build failed.");
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        private static void Begin()
        {
            try
            {
                var agents = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>();
                raptor = agents.Single(a => a.Species == (DinosaurAgent.DinosaurSpecies)SessionState.GetInt(Key + ".Species", (int)DinosaurAgent.DinosaurSpecies.Velociraptor));
                rex = agents.Single(a => a.Species == DinosaurAgent.DinosaurSpecies.Tyrannosaurus);
                settings = UnityEngine.Object.FindObjectOfType<DinosaurInteractionSettings>();
                Require(raptor.IsFreeRoaming && !raptor.UsesRouteScheduling && !raptor.HasFootPhaseData,
                    "Raptor must roam without rex foot metadata.");
                raptor.enabled = rex.enabled = false;
                Set(raptor, "peers", new[] { rex });
                Require((bool)Call("FreeNavigationReady"), "Raptor navigation unavailable.");
                Report.Clear(); scenario = 0;
                CheckGeometry(); Prepare();
                lastFrame = -1;
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void CheckGeometry()
        {
            rex.transform.position = Vector3.zero;
            Require(!(bool)Call("SafetySegmentAllowed", new Vector2(-25f, 0f), new Vector2(25f, 0f), false), "Path crossed safety circle.");
            Require((bool)Call("SafetySegmentAllowed", new Vector2(-25f, 16f), new Vector2(25f, 16f), false), "Safe passing path rejected.");
            Require((bool)Call("SafetySegmentAllowed", new Vector2(10f, 0f), new Vector2(22f, 0f), true), "Escape from inside circle rejected.");
            Require(!(bool)Call("SafetySegmentAllowed", new Vector2(10f, 0f), new Vector2(9f, 0f), true), "Retreat moved closer.");
            Require(!(bool)Call("SafetySegmentAllowed", new Vector2(18f, 0f), new Vector2(17f, 10f), true),
                "Retreat segment briefly approached rex despite its farther endpoint.");
            Report.AppendLine("PASS: crossing-path rejection, clear passing path, inside-circle escape, closer-step rejection.");
        }

        private static void Prepare()
        {
            Time.captureDeltaTime = 1f / (scenario == 0 ? 30f : scenario == 1 ? 60f : 120f);
            settings.solidObstacles = Array.Empty<Bounds>();
            rex.transform.position = Vector3.zero;
            raptor.transform.SetPositionAndRotation(new Vector3(14f, 0f, 0f), Quaternion.Euler(0f, -90f - raptor.VisualYaw, 0f));
            Call("FreeIdle", 100f);
            began = Time.time; previousDistance = 14f; previousPosition = raptor.transform.position; travelled = 0f; moved = false;
            if (scenario == 3)
                settings.solidObstacles = new[] { new Bounds(new Vector3(14f, 1f, 0f), new Vector3(20f, 8f, 20f)) };
            if (scenario == 4)
            {
                // A rex approaching an idle raptor must activate the same safety behaviour.
                rex.transform.position = new Vector3(-8f, 0f, 0f);
                Call("UpdateFreeRoaming");
                Require(!raptor.IsAvoidingTyrannosaurus, "Avoidance triggered outside entry distance.");
                rex.transform.position = Vector3.zero;
            }
            if (scenario == 5)
            {
                rex.transform.position = new Vector3(-50f, 0f, -40f);
                Call("FreeIdle", 0f);
            }
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            try
            {
                float elapsed = Time.time - began;
                Call("UpdateFreeRoaming");
                float distance = Vector3.Distance(raptor.transform.position, rex.transform.position);
                if (scenario == 5)
                {
                    travelled += Vector3.Distance(previousPosition, raptor.transform.position);
                    previousPosition = raptor.transform.position;
                    Require(!raptor.UsesRouteScheduling && !raptor.IsAvoidingTyrannosaurus, "Normal roaming retained avoidance/route state.");
                    Vector3 forward = raptor.transform.rotation * Quaternion.Euler(0f, raptor.VisualYaw, 0f) * Vector3.forward;
                    var body = (DinosaurCoreBody)typeof(DinosaurAgent).GetField("interactionBody", Private).GetValue(raptor);
                    Require(!settings.PoseBlocked(new Vector2(previousPosition.x, previousPosition.z), new Vector2(forward.x, forward.z), body),
                        "Normal roaming crossed a body boundary or obstacle.");
                    if (elapsed < 10f) return;
                    Require(travelled > Mathf.Min(8f, raptor.WalkSpeed * 6f), "Normal roaming failed to make progress after retreat.");
                    Report.AppendLine($"PASS: normal free roaming after retreat travelled {travelled:F2}m in 10s; body/boundary checks retained.");
                    Finish(null); return;
                }
                Require(distance + .002f >= previousDistance || !raptor.IsAvoidingTyrannosaurus,
                    "Raptor approached rex during retreat.");
                travelled += Vector3.Distance(previousPosition, raptor.transform.position);
                previousPosition = raptor.transform.position; previousDistance = distance;
                if (scenario == 3 && elapsed <= 2f)
                {
                    Require(travelled < .01f && raptor.IsAvoidingTyrannosaurus, "Blocked fixture moved through obstacle.");
                    return;
                }
                if (scenario == 3) settings.solidObstacles = Array.Empty<Bounds>();
                if (travelled > .1f) moved = true;
                if (elapsed >= 2f && scenario != 3) Require(moved, "No actual retreat within two seconds.");
                Require(elapsed < 15f, "Safety retreat timed out: " + raptor.AvoidanceStatus);
                if (distance < 20f) { Require(raptor.IsAvoidingTyrannosaurus, "Avoidance released below 20m."); return; }
                // Release is evaluated on the next behaviour tick after reaching the boundary.
                Call("UpdateFreeRoaming");
                Require(!raptor.IsAvoidingTyrannosaurus, "Avoidance did not release at 20m.");
                Report.AppendLine($"PASS: case {scenario}, retreat/release in {elapsed:F2}s, distance {distance:F2}m.");
                scenario++; Prepare();
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            if (error != null) { Report.AppendLine("FAIL: " + error); Debug.LogException(error); }
            else Report.AppendLine("DINOSAUR_SAFETY_PASS");
            bool pachy = SessionState.GetInt(Key + ".Species", 2) == (int)DinosaurAgent.DinosaurSpecies.Pachycephalosaurus;
            File.WriteAllText(pachy ? "Logs/pachy-safety-acceptance.txt" : "Logs/dinosaur-safety-acceptance.txt", Report.ToString());
            if (Application.isBatchMode) { EditorApplication.Exit(error == null ? 0 : 1); return; }
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        }
    }
}
