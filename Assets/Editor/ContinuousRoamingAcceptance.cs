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
    public static partial class ContinuousRoamingAcceptance
    {
        internal const string Output = "Reports/ContinuousRoaming";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly StringBuilder Report = new StringBuilder();
        private static DinosaurGaitProfile fixtureGait;
        private static DinosaurCoreBody.Band[] fixtureBands;
        private static float fixtureRadius;
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static void Set(DinosaurAgent a, string field, object value) => typeof(DinosaurAgent).GetField(field, Private).SetValue(a, value);
        private static T Get<T>(DinosaurAgent a, string field) => (T)typeof(DinosaurAgent).GetField(field, Private).GetValue(a);
        private static object Call(DinosaurAgent a, string method, params object[] args) => typeof(DinosaurAgent).GetMethod(method, Private).Invoke(a, args);
        private static void WriteReport() => File.WriteAllText(Output + "/Fixtures.md", Report.ToString());

        [MenuItem("Jurassic World/Acceptance/Verify Continuous Free Roaming and Eight Second Escape")]
        public static void Run()
        {
            Directory.CreateDirectory(Output); Report.Clear();
            Report.AppendLine("# Continuous free roaming and eight second escape\n");
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorSceneManager.GetActiveScene().isDirty, "Stop Play and save scene edits first.");
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                Require((int)DinosaurAgent.RoamingMode.Route == 0 && (int)DinosaurAgent.RoamingMode.Free == 1
                    && (int)DinosaurAgent.RoamingMode.ContinuousFree == 2, "Serialized mode values changed.");
                var agents = Object.FindObjectsOfType<DinosaurAgent>(true);
                var original = agents.Single(a => a.name == "Sauropelta");
                var baurus = agents.Single(a => a.name == "Baurusuchus");
                float speed = original.WalkSpeed, native = original.MoveAnimationGroundSpeed;
                float height = original.GetComponent<DinosaurFbxLoader>().ModelRoot.transform.localPosition.y;
                var others = agents.Where(a => a != original && a != baurus).ToArray();
                var json = others.Select(a => EditorJsonUtility.ToJson(a)).ToArray();
                Configure(original, false); Configure(baurus, true);
                original.GaitProfile.turnSpeed = 10f;
                EditorUtility.SetDirty(original.GaitProfile);
                DinosaurGaitAcceptance.VerifyContinuousTurns(original, Output);
                fixtureGait = original.GaitProfile;
                fixtureBands = Get<DinosaurCoreBody>(original, "interactionBody").bands;
                fixtureRadius = original.CollisionRadius;
                RunFixtures();
                var clone = Object.Instantiate(original.gameObject); clone.name = original.name;
                DinosaurGaitProfile probe = null;
                try { probe = DinosaurGaitCalibration.Bake(clone.GetComponent<DinosaurAgent>()); Require(probe.turnSpeed == original.GaitProfile.turnSpeed, "Calibration reset turn rate."); }
                finally { Object.DestroyImmediate(clone); if (probe != null) Object.DestroyImmediate(probe); }
                Require(original.WalkSpeed == speed && original.MoveAnimationGroundSpeed == native
                    && original.GetComponent<DinosaurFbxLoader>().ModelRoot.transform.localPosition.y == height, "Speed, gait or grounding changed.");
                for (int i = 0; i < others.Length; i++) Require(EditorJsonUtility.ToJson(others[i]) == json[i], "Other animal changed: " + others[i].name);
                AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(original.gameObject.scene);
                EditorSceneManager.SaveScene(original.gameObject.scene);
                Report.AppendLine($"\nPASS migration: Sauropelta {speed:F8}m/s, native {native:F8}m/s, height {height:F6}; Baurusuchus remains continuous; all other animals unchanged; calibration preserves turn limit.");
                WriteReport();
                StartMain();
            }
            catch (Exception error)
            {
                Report.AppendLine("\nFAIL " + error); WriteReport(); Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }
        private static void Configure(DinosaurAgent agent, bool retainTurnRadius)
        {
            var data = new SerializedObject(agent);
            data.FindProperty("roamingMode").enumValueIndex = (int)DinosaurAgent.RoamingMode.ContinuousFree;
            if (retainTurnRadius && agent.GaitProfile != null)
                data.FindProperty("continuousMinTurnRadius").floatValue = Mathf.Max(.5f, agent.WalkSpeed / (agent.GaitProfile.turnSpeed * Mathf.Deg2Rad));
            data.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(agent);
        }
        public static void RunSupplemental()
        {
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                var source = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Pachycephalosaurus");
                var root = new GameObject("Reusable continuous mode fixture");
                var settings = root.AddComponent<DinosaurInteractionSettings>();
                settings.activityBoundsOverride = new Rect(960f, 960f, 80f, 80f);
                var build = NavMesh.GetSettingsByIndex(0); build.agentRadius = .2f;
                var data = NavMeshBuilder.BuildNavMeshData(build, new List<NavMeshBuildSource> {
                    new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(84f, .2f, 84f),
                        transform = Matrix4x4.TRS(Origin - Vector3.up * .1f, Quaternion.identity, Vector3.one) }
                }, new Bounds(Origin, new Vector3(90f, 10f, 90f)), Vector3.zero, Quaternion.identity);
                var instance = NavMesh.AddNavMeshData(data);
                settings.navigationData = data; settings.navigationAgentTypeId = build.agentTypeID;
                typeof(DinosaurInteractionSettings).GetField("navigationInstance", Private).SetValue(settings, instance);
                var copy = Object.Instantiate(source.gameObject, root.transform).GetComponent<DinosaurAgent>();
                try
                {
                    copy.enabled = false; copy.transform.SetPositionAndRotation(Origin, Quaternion.Euler(0f, -copy.VisualYaw, 0f));
                    Set(copy, "roamingMode", DinosaurAgent.RoamingMode.ContinuousFree);
                    Set(copy, "startupRoamingMode", DinosaurAgent.RoamingMode.ContinuousFree);
                    Set(copy, "random", new System.Random(42));
                    Set(copy, "curiosityPath", new NavMeshPath());
                    Set(copy, "interactionSettings", settings); Set(copy, "peers", Array.Empty<DinosaurAgent>());
                    Set(copy, "<CurrentState>k__BackingField", DinosaurAgent.DinosaurState.RandomWalk);
                    Path(copy, Origin + Vector3.forward * 30f);
                    for (int i = 0; i < 600; i++) Call(copy, "UpdateContinuousRoaming", 1f / 30f);
                    float distance = Vector3.Distance(Origin, copy.transform.position);
                    Require(distance > copy.WalkSpeed * 19f && copy.IsContinuousRoaming, "Real Pachycephalosaurus copy failed continuous mode reuse.");
                    Set(copy, "trafficLocked", true);
                    for (int i = 0; i < 9; i++) Call(copy, "UpdateEmergencyEscape", 1f);
                    Require(copy.IsEmergencyEscaping, "Reuse fixture did not enter escape.");
                    for (int i = 0; i < 1200 && copy.IsEmergencyEscaping; i++) Call(copy, "UpdateEmergencyEscape", .1f);
                    Require(!copy.IsEmergencyEscaping, "Reuse fixture failed to leave escape.");
                    settings.solidObstacles = new[] { new Bounds(copy.transform.position, Vector3.one * 20f) };
                    object[] args = { 0f, 1f, Vector3.zero, Quaternion.identity };
                    Require(!(bool)Call(copy, "WalkingArcSafe", args), "Ordinary obstacle checks did not restore after escape.");
                    File.WriteAllText(Output + "/Supplemental.md", $"PASS actual Pachycephalosaurus scene copy in ContinuousFree: {distance:F3}m in 20s; no species branch.\nPASS emergency escape completed and ordinary body collision checks restored.\n");
                }
                finally { instance.Remove(); Object.DestroyImmediate(root); Object.DestroyImmediate(data); }
                DinosaurEmergencyEscapeAcceptance.Run();
            }
            catch (Exception error) { Debug.LogException(error); if (Application.isBatchMode) EditorApplication.Exit(1); else throw; }
        }
        private static Vector3 Origin => new Vector3(1000f, 0f, 1000f);
        private static DinosaurAgent Make(GameObject fixture, DinosaurInteractionSettings settings, DinosaurAgent.DinosaurSpecies species, DinosaurAgent.RoamingMode mode)
        {
            var root = new GameObject("Continuous fixture " + species); root.SetActive(false); root.transform.SetParent(fixture.transform);
            root.transform.position = Origin;
            var body = root.AddComponent<DinosaurCoreBody>();
            body.bands = fixtureBands;
            var agent = root.AddComponent<DinosaurAgent>();
            Set(agent, "roamingMode", mode); Set(agent, "species", species);
            Set(agent, "gaitProfile", fixtureGait);
            Set(agent, "coreBody", body); Set(agent, "interactionBody", body); Set(agent, "interactionSettings", settings);
            Set(agent, "collisionRadius", fixtureRadius); Set(agent, "walkSpeed", .44013974f); Set(agent, "displayName", root.name);
            root.SetActive(true); agent.enabled = false;
            Set(agent, "startupRoamingMode", mode); Set(agent, "random", new System.Random(42));
            Set(agent, "peers", Array.Empty<DinosaurAgent>());
            Set(agent, "<CurrentState>k__BackingField", DinosaurAgent.DinosaurState.RandomWalk);
            return agent;
        }
        private static void Path(DinosaurAgent a, params Vector3[] corners)
        {
            Set(a, "curiosityCorners", corners); Set(a, "curiosityCorner", 0); Set(a, "hasCuriosityPath", true);
            Set(a, "freeGoal", corners[corners.Length - 1]); Call(a, "ResetContinuousSteering");
        }
        private static void RunFixtures()
        {
            var fixture = new GameObject("Continuous free roaming acceptance");
            NavMeshData data = null; NavMeshDataInstance instance = default;
            try
            {
                var settings = fixture.AddComponent<DinosaurInteractionSettings>();
                settings.activityBoundsOverride = new Rect(960f, 960f, 80f, 80f);
                var build = NavMesh.GetSettingsByIndex(0); build.agentRadius = .2f; build.agentHeight = 1f;
                data = NavMeshBuilder.BuildNavMeshData(build, new List<NavMeshBuildSource> {
                    new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(84f, .2f, 84f),
                        transform = Matrix4x4.TRS(Origin - Vector3.up * .1f, Quaternion.identity, Vector3.one) }
                }, new Bounds(Origin, new Vector3(90f, 10f, 90f)), Vector3.zero, Quaternion.identity);
                Require(data != null, "Fixture navigation failed."); settings.navigationData = data; settings.navigationAgentTypeId = build.agentTypeID;
                instance = NavMesh.AddNavMeshData(data);
                typeof(DinosaurInteractionSettings).GetField("navigationInstance", Private).SetValue(settings, instance);
                foreach (int fps in new[] { 30, 60, 144 })
                {
                    MotionFixture(fixture, settings, fps, false);
                    MotionFixture(fixture, settings, fps, true);
                    BoundaryAndPeerFixture(fixture, settings, fps);
                    foreach (string scenario in new[] { "rotation", "jitter", "replanning", "traffic" })
                        StallFixture(fixture, settings, fps, scenario);
                }
                var reusable = Make(fixture, settings, DinosaurAgent.DinosaurSpecies.Other, DinosaurAgent.RoamingMode.ContinuousFree);
                Require(reusable.IsFreeRoaming && reusable.IsContinuousRoaming && (bool)Call(reusable, "FreeNavigationReady"), "Continuous mode is still species-bound.");
                Set(reusable, "<CurrentState>k__BackingField", DinosaurAgent.DinosaurState.Idle);
                for (int i = 0; i < 7; i++) Call(reusable, "UpdateEmergencyEscape", 1f);
                Require(!reusable.IsEmergencyEscaping, "Early escape during continuous Idle.");
                Call(reusable, "UpdateEmergencyEscape", 1f);
                Require(reusable.IsEmergencyEscaping, "Continuous Idle disabled eight second fallback.");
                for (int i = 0; i < 1200 && reusable.IsEmergencyEscaping; i++) Call(reusable, "UpdateEmergencyEscape", .1f);
                Require(!reusable.IsEmergencyEscaping && reusable.CurrentState == DinosaurAgent.DinosaurState.RandomWalk
                    && (float)Get<float>(reusable, "stateTimer") == 0f, "Escape failed to return directly to continuous walking.");
                Require((bool)Call(reusable, "EmergencyOpenPoint", reusable.transform.position, reusable.transform.rotation), "Unsafe escape endpoint.");
                Object.DestroyImmediate(reusable.gameObject);
                var stopped = Make(fixture, settings, DinosaurAgent.DinosaurSpecies.Pachycephalosaurus, DinosaurAgent.RoamingMode.Free);
                Call(stopped, "BeginIdle");
                for (int i = 0; i < 30; i++) Call(stopped, "UpdateEmergencyEscape", 1f);
                Require(!stopped.IsContinuousRoaming && stopped.IsFreeRoaming && stopped.NoMovementDuration == 0f, "Stop/go intentional rest regressed.");
                Object.DestroyImmediate(stopped.gameObject);
                var moving = Make(fixture, settings, DinosaurAgent.DinosaurSpecies.Sauropelta, DinosaurAgent.RoamingMode.ContinuousFree);
                for (int i = 0; i < 80; i++)
                {
                    if (i % 4 == 0) moving.transform.position += Vector3.forward * .3f;
                    Call(moving, "UpdateEmergencyEscape", 1f);
                    Require(!moving.IsEmergencyEscaping, "Effective movement failed to reset timer.");
                }
                Object.DestroyImmediate(moving.gameObject);
                Report.AppendLine("PASS reusable mode, stop/go intentional rest, effective movement timer reset, escape endpoint and continuous resume.");
            }
            finally { instance.Remove(); Object.DestroyImmediate(fixture); if (data != null) Object.DestroyImmediate(data); WriteReport(); }
        }
        private static void MotionFixture(GameObject fixture, DinosaurInteractionSettings settings, int fps, bool obstacle)
        {
            settings.solidObstacles = obstacle ? new[] { new Bounds(Origin + Vector3.forward * 5f + Vector3.up * .5f, new Vector3(1f, 1f, 1f)) } : Array.Empty<Bounds>();
            var agent = Make(fixture, settings, DinosaurAgent.DinosaurSpecies.Sauropelta, DinosaurAgent.RoamingMode.ContinuousFree);
            try
            {
                Path(agent, obstacle ? new[] { Origin + Vector3.forward * 30f } : new[] { Origin + Vector3.forward * 4f, Origin + new Vector3(16f, 0f, 4f), Origin + new Vector3(30f, 0f, 4f) });
                float dt = 1f / fps, travel = 0f, turn = 0f; int moved = 0;
                for (int i = 0; i < fps * 55; i++)
                {
                    Vector3 before = agent.transform.position; Quaternion rotation = agent.transform.rotation;
                    Call(agent, "UpdateEmergencyEscape", dt);
                    Require(!agent.IsEmergencyEscaping, $"Ordinary curve depended on emergency escape: fps={fps} obstacle={obstacle} t={i * dt:F2} position={before:F3} travel={travel:F3} block={agent.LastMovementBlock} navigation={settings.NavigationReady}.");
                    Call(agent, "MoveContinuousPath", dt);
                    float distance = Vector3.Distance(before, agent.transform.position); travel += distance;
                    if (distance > .00001f) moved++;
                    float angle = Mathf.Abs(Mathf.DeltaAngle(rotation.eulerAngles.y, agent.transform.eulerAngles.y)); turn += angle;
                    Require(distance > .00001f || angle < .001f, "Continuous walker rotated without moving.");
                    Require(settings.PoseBlockReason(new Vector2(agent.transform.position.x, agent.transform.position.z),
                        new Vector2(agent.transform.forward.x, agent.transform.forward.z), Get<DinosaurCoreBody>(agent, "interactionBody")) == null, "Curve penetrated obstacle or boundary.");
                }
                float fraction = moved / (fps * 55f);
                Require(fraction >= .95f, $"Insufficient continuous motion fps={fps} obstacle={obstacle}: {fraction:P1}");
                if (!obstacle) Require(turn > 45f, "No natural walking bend observed.");
                Report.AppendLine($"PASS motion fps={fps} obstacle={obstacle}: moving={fraction:P2}, travel={travel:F3}m, yaw={turn:F1}deg, no emergency escape."); WriteReport();
            }
            finally { Object.DestroyImmediate(agent.gameObject); settings.solidObstacles = Array.Empty<Bounds>(); }
        }
        private static void BoundaryAndPeerFixture(GameObject fixture, DinosaurInteractionSettings settings, int fps)
        {
            var a = Make(fixture, settings, DinosaurAgent.DinosaurSpecies.Sauropelta, DinosaurAgent.RoamingMode.ContinuousFree);
            DinosaurAgent b = null;
            try
            {
                a.transform.SetPositionAndRotation(Origin + new Vector3(32f, 0f, -15f), Quaternion.Euler(0f, 90f, 0f));
                Path(a, Origin + new Vector3(37f, 0f, -15f), Origin + new Vector3(37f, 0f, 15f));
                float dt = 1f / fps, travel = 0f;
                for (int i = 0; i < fps * 30; i++)
                {
                    Vector3 old = a.transform.position; Call(a, "MoveContinuousPath", dt);
                    travel += Vector3.Distance(old, a.transform.position);
                    Require(settings.PoseBlockReason(new Vector2(a.transform.position.x, a.transform.position.z),
                        new Vector2(a.transform.forward.x, a.transform.forward.z), Get<DinosaurCoreBody>(a, "interactionBody")) == null,
                        "Boundary bend left activity area.");
                }
                Require(travel > a.WalkSpeed * 30f * .95f, "Boundary turn lost continuous progress: " + travel);
                a.transform.SetPositionAndRotation(Origin, Quaternion.identity);
                Call(a, "ResetContinuousSteering");
                Path(a, Origin + Vector3.forward * 30f);
                b = Make(fixture, settings, DinosaurAgent.DinosaurSpecies.Baurusuchus, DinosaurAgent.RoamingMode.ContinuousFree);
                // Allow the measured turn radius and full body clearance to pass
                // proactively, before an already close encounter requires yielding.
                b.transform.SetPositionAndRotation(Origin + Vector3.forward * 16f, Quaternion.Euler(0f, 180f, 0f));
                Path(b, Origin - Vector3.forward * 20f);
                Set(a, "peers", new[] { b }); Set(b, "peers", new[] { a });
                float aTravel = 0f, bTravel = 0f;
                for (int i = 0; i < fps * 30; i++)
                {
                    Vector3 oldA = a.transform.position, oldB = b.transform.position;
                    Call(a, "MoveContinuousPath", dt); Call(b, "MoveContinuousPath", dt);
                    aTravel += Vector3.Distance(oldA, a.transform.position); bTravel += Vector3.Distance(oldB, b.transform.position);
                    float penetration = (float)Call(a, "PeerPenetration", a.transform.position, a.transform.rotation,
                        Get<DinosaurCoreBody>(a, "interactionBody"), b);
                    Require(penetration <= .0001f, "Meeting animals overlap.");
                }
                Require(aTravel > 10f && bTravel > 10f, $"Meeting animals stalled: {aTravel:F3}/{bTravel:F3}m; poses {a.transform.position-Origin}/{b.transform.position-Origin}; yaw {a.transform.eulerAngles.y}/{b.transform.eulerAngles.y}; blocks {a.LastMovementBlock}/{b.LastMovementBlock}.");
                Report.AppendLine($"PASS boundary and head-on passing fps={fps}: boundary={travel:F3}m, peers={aTravel:F3}/{bTravel:F3}m, no body penetration."); WriteReport();
            }
            finally { Object.DestroyImmediate(a.gameObject); if (b != null) Object.DestroyImmediate(b.gameObject); }
        }
        private static void StallFixture(GameObject fixture, DinosaurInteractionSettings settings, int fps, string scenario)
        {
            var agent = Make(fixture, settings, DinosaurAgent.DinosaurSpecies.Sauropelta, DinosaurAgent.RoamingMode.ContinuousFree);
            try
            {
                float dt = 1f / fps, elapsed = 0f;
                for (int frame = 0; frame < fps * 8 + 2 && !agent.IsEmergencyEscaping; frame++)
                {
                    if (scenario == "rotation") agent.transform.rotation *= Quaternion.Euler(0f, 2f * dt, 0f);
                    if (scenario == "jitter") agent.transform.position = Origin + Vector3.right * Mathf.Sin(frame * .2f) * .1f;
                    if (scenario == "replanning")
                    {
                        Set(agent, "<CurrentState>k__BackingField", frame % 2 == 0 ? DinosaurAgent.DinosaurState.Idle : DinosaurAgent.DinosaurState.RandomWalk);
                        Set(agent, "freeRetryGoal", frame % 2 == 0); Set(agent, "<RoamingBlockReason>k__BackingField", "");
                    }
                    if (scenario == "traffic") Set(agent, "trafficLocked", frame % 2 == 0);
                    Call(agent, "UpdateEmergencyEscape", dt); elapsed += dt;
                    if (elapsed < 7.9f) Require(!agent.IsEmergencyEscaping, "Escape occurred before eight seconds.");
                }
                Require(agent.IsEmergencyEscaping && elapsed <= 8f + dt * 1.1f, $"Eight second trigger missed: {scenario} fps={fps} t={elapsed}");
                Report.AppendLine($"PASS eight second stall fps={fps} case={scenario}: trigger={elapsed:F4}s.");
            }
            finally { Object.DestroyImmediate(agent.gameObject); }
        }
    }
}
