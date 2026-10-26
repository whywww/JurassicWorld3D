using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class DinosaurEmergencyEscapeAcceptance
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Set(DinosaurAgent agent, string field, object value)
            => typeof(DinosaurAgent).GetField(field, Private).SetValue(agent, value);
        private static object Call(DinosaurAgent agent, string method, params object[] args)
            => typeof(DinosaurAgent).GetMethod(method, Private).Invoke(agent, args);
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }

        [MenuItem("Jurassic World/Acceptance/Verify Eight Second Emergency Escape")]
        public static void Run()
        {
            var fixture = new GameObject("Emergency escape acceptance");
            NavMeshData data = null;
            NavMeshDataInstance instance = default;
            try
            {
                var settings = fixture.AddComponent<DinosaurInteractionSettings>();
                settings.activityBoundsOverride = new Rect(-30f, -30f, 60f, 60f);
                var build = NavMesh.GetSettingsByIndex(0);
                build.agentRadius = .2f; build.agentHeight = 1f;
                data = NavMeshBuilder.BuildNavMeshData(build, new List<NavMeshBuildSource> {
                    new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(64f, .2f, 64f),
                        transform = Matrix4x4.TRS(new Vector3(0f, -.1f, 0f), Quaternion.identity, Vector3.one) }
                }, new Bounds(Vector3.zero, new Vector3(70f, 10f, 70f)), Vector3.zero, Quaternion.identity);
                Require(data != null, "Could not build fixture navigation.");
                settings.navigationData = data; settings.navigationAgentTypeId = build.agentTypeID;
                instance = NavMesh.AddNavMeshData(data);
                typeof(DinosaurInteractionSettings).GetField("navigationInstance", Private).SetValue(settings, instance);
                // A solid enclosure makes ordinary clearance impossible, while the
                // fallback can walk through it toward a verified clear endpoint.
                settings.solidObstacles = new[] { new Bounds(new Vector3(0f, .5f, 0f), new Vector3(4f, 1f, 4f)) };
                foreach (DinosaurAgent.DinosaurSpecies species in Enum.GetValues(typeof(DinosaurAgent.DinosaurSpecies)))
                {
                    if (species == DinosaurAgent.DinosaurSpecies.Other) continue;
                    var agent = Make(fixture, settings, species);
                    Set(agent, "trafficLocked", true);
                    for (int second = 0; second < 7; second++)
                    {
                        // Replanning/traffic clocks must not reset the global timer.
                        Set(agent, "trafficProgressAt", Time.time);
                        agent.transform.rotation *= Quaternion.Euler(0f, 2f, 0f);
                        Call(agent, "UpdateEmergencyEscape", 1f);
                        Require(!agent.IsEmergencyEscaping, species + " escaped before eight seconds.");
                        Require(agent.EmergencyEscapeMoveCount == 0, "Waiting/rotation was counted as an escape move.");
                    }
                    Call(agent, "UpdateEmergencyEscape", 1f);
                    Require(agent.IsEmergencyEscaping, species + " failed to escape traffic after eight seconds.");
                    Require(!(bool)typeof(DinosaurAgent).GetField("trafficLocked", Private).GetValue(agent), "Traffic lock survived escape.");
                    Require(!agent.ApplyFootRootCorrection(Vector3.forward), "Foot IK moved escape root.");
                    Require(agent.transform.position.magnitude > .1f, "Escape did not translate through the enclosure.");
                    Require(agent.EmergencyEscapeMoveCount == 1, "First committed escape move was not counted exactly once.");
                    for (int frame = 0; frame < 400 && agent.IsEmergencyEscaping; frame++) Call(agent, "UpdateEmergencyEscape", .1f);
                    Require(!agent.IsEmergencyEscaping, species + " did not resume normal roaming.");
                    Require((bool)Call(agent, "EmergencyOpenPoint", agent.transform.position, agent.transform.rotation), "Restored collisions inside an obstacle.");
                    Require(agent.NoMovementDuration == 0f, "Stall timer was not reset on exit.");
                    Object.DestroyImmediate(agent.gameObject);
                    Debug.Log("EMERGENCY_ESCAPE_PASS: " + species);
                }
                settings.solidObstacles = Array.Empty<Bounds>();
                var resting = Make(fixture, settings, DinosaurAgent.DinosaurSpecies.Pachycephalosaurus);
                Call(resting, "BeginIdle");
                float idle = (float)typeof(DinosaurAgent).GetField("stateTimer", Private).GetValue(resting);
                Require(idle >= 1f && idle <= 2f, "Pachy rest must be 1-2 seconds.");
                for (int i = 0; i < 30; i++) Call(resting, "UpdateEmergencyEscape", 1f);
                Require(!resting.IsEmergencyEscaping && resting.NoMovementDuration == 0f, "Intentional rest triggered escape.");
                typeof(DinosaurAgent).GetProperty("CurrentState").SetValue(resting, DinosaurAgent.DinosaurState.RandomWalk);
                for (int i = 0; i < 20; i++) { resting.transform.position += Vector3.forward; Call(resting, "UpdateEmergencyEscape", 1f); }
                Require(!resting.IsEmergencyEscaping, "Walking was mistaken for a stall.");
                Object.DestroyImmediate(resting.gameObject);

                var trapped = Make(fixture, settings, DinosaurAgent.DinosaurSpecies.Tyrannosaurus);
                Set(trapped, "trafficLocked", true);
                settings.solidObstacles = new[] { new Bounds(Vector3.zero, new Vector3(200f, 20f, 200f)) };
                for (int i = 0; i < 9; i++) Call(trapped, "UpdateEmergencyEscape", 1f);
                Require(trapped.IsEmergencyEscaping && trapped.transform.position == Vector3.zero, "No-open-ground fallback restored collisions prematurely.");
                Require(trapped.EmergencyEscapeMoveCount == 0, "Searching without open ground granted a movement exception.");
                settings.solidObstacles = Array.Empty<Bounds>();
                Call(trapped, "UpdateEmergencyEscape", 1f);
                Vector3 originalGoal = (Vector3)typeof(DinosaurAgent).GetField("emergencyGoal", Private).GetValue(trapped);
                var blocker = Make(fixture, settings, DinosaurAgent.DinosaurSpecies.Sauropelta);
                blocker.transform.position = originalGoal;
                Set(trapped, "peers", new[] { blocker });
                Call(trapped, "UpdateEmergencyEscape", 1f);
                Require((Vector3)typeof(DinosaurAgent).GetField("emergencyGoal", Private).GetValue(trapped) != originalGoal,
                    "An occupied endpoint was not replaced.");
                for (int i = 0; i < 400 && trapped.IsEmergencyEscaping; i++) Call(trapped, "UpdateEmergencyEscape", .1f);
                Require(!trapped.IsEmergencyEscaping, "Escape did not recover after open ground became available.");
                Object.DestroyImmediate(trapped.gameObject);
                Object.DestroyImmediate(blocker.gameObject);
                VerifyPeerException(fixture, settings);
                Debug.Log("EMERGENCY_ESCAPE_ALL_PASS: species, traffic retry, rotation-only stall, rest, walking, no-open-ground retry, moving peer endpoint, committed-move exception, final-step sampling, collision restoration.");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
            finally
            {
                instance.Remove();
                Object.DestroyImmediate(fixture);
                if (data != null) Object.DestroyImmediate(data);
            }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void ExpectOrdinaryIntersectionFailure(ulong previousA, ulong currentA, ulong previousB, ulong currentB)
        {
            bool rejected = false;
            try { DinosaurGroundTrafficAcceptance.ValidateBodySample(0f, .1f, previousA, currentA, previousB, currentB); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Ordinary intersection was exempted without a new emergency move.");
        }

        private static void VerifyPeerException(GameObject fixture, DinosaurInteractionSettings settings)
        {
            var agent = Make(fixture, settings, DinosaurAgent.DinosaurSpecies.Sauropelta);
            var peer = Make(fixture, settings, DinosaurAgent.DinosaurSpecies.Velociraptor);
            peer.transform.position = new Vector3(0f, 0f, 1.2f);
            Set(agent, "peers", new[] { peer });
            Call(agent, "BeginEmergencyEscape");
            Require(agent.IsEmergencyEscaping && agent.EmergencyEscapeMoveCount == 0, "Starting escape should not grant a movement exception.");
            ExpectOrdinaryIntersectionFailure(0, 0, 0, 0);
            Require((bool)Call(agent, "TryEmergencyGoal", new Vector3(0f, 0f, 4f)), "Peer-crossing escape endpoint was not clear.");
            var body = agent.GetComponent<DinosaurCoreBody>();
            float previousDepth = (float)Call(agent, "PeerPenetration", agent.transform.position, agent.transform.rotation, body, peer);
            bool sawException = false, sawFinalStep = false;
            for (int i = 0; i < 100 && agent.IsEmergencyEscaping; i++)
            {
                ulong before = agent.EmergencyEscapeMoveCount;
                Vector3 position = agent.transform.position;
                Call(agent, "TickEmergencyEscape", .1f);
                Require(agent.transform.position != position && agent.EmergencyEscapeMoveCount == before + 1,
                    "Each committed escape move must increment the counter exactly once.");
                float depth = (float)Call(agent, "PeerPenetration", agent.transform.position, agent.transform.rotation, body, peer);
                sawException |= DinosaurGroundTrafficAcceptance.ValidateBodySample(previousDepth, depth, before, agent.EmergencyEscapeMoveCount, 0, 0);
                // Pair order must not change which participant can grant the exception.
                if (depth > previousDepth + .0001f)
                    Require(DinosaurGroundTrafficAcceptance.ValidateBodySample(previousDepth, depth, 0, 0, before, agent.EmergencyEscapeMoveCount), "Second participant's escape move was ignored.");
                previousDepth = depth;
                if (!agent.IsEmergencyEscaping)
                {
                    sawFinalStep = true;
                    Require(DinosaurGroundTrafficAcceptance.ValidateBodySample(0f, .1f, before, agent.EmergencyEscapeMoveCount, 0, 0),
                        "Final escape step lost its sampling exception when escape mode ended.");
                }
            }
            Require(sawException && sawFinalStep, "Peer-crossing/final-step escape fixture did not execute.");
            Require((bool)Call(agent, "EmergencyOpenPoint", agent.transform.position, agent.transform.rotation), "Escape ended inside an occupied endpoint.");
            Require(!(bool)Call(agent, "StepPoseSafe", agent.transform.position, peer.transform.position, agent.transform.rotation, agent.transform.rotation, false),
                "Normal movement accepted a peer-body crossing after escape.");
            ulong finished = agent.EmergencyEscapeMoveCount;
            Call(agent, "UpdateEmergencyEscape", .1f);
            Require(agent.EmergencyEscapeMoveCount == finished, "Normal idle was counted as an escape move.");
            ExpectOrdinaryIntersectionFailure(finished, finished, 0, 0);
            ExpectOrdinaryIntersectionFailure(0, 0, finished, finished);
            Require(!DinosaurGroundTrafficAcceptance.ValidateBodySample(.1f, .05f, finished, finished, 0, 0), "Non-deepening overlap required an exception.");
            Debug.Log("EMERGENCY_PEER_EXCEPTION_PASS: crossing, pair order, final committed step, no exception during search/idle, normal collision checks restored.");
            Object.DestroyImmediate(agent.gameObject);
            Object.DestroyImmediate(peer.gameObject);
        }

        private static DinosaurAgent Make(GameObject fixture, DinosaurInteractionSettings settings, DinosaurAgent.DinosaurSpecies species)
        {
            var root = new GameObject(species.ToString()); root.transform.SetParent(fixture.transform); root.SetActive(false);
            var body = root.AddComponent<DinosaurCoreBody>();
            body.bands = new[] { new DinosaurCoreBody.Band { layer = 0, bounds = new Bounds(new Vector3(0f, .4f, 0f), new Vector3(.6f, .8f, 1f)) } };
            var agent = root.AddComponent<DinosaurAgent>();
            Set(agent, "species", species); Set(agent, "startupRoamingMode", DinosaurAgent.RoamingMode.Free);
            Set(agent, "coreBody", body); Set(agent, "interactionBody", body); Set(agent, "interactionSettings", settings);
            Set(agent, "collisionRadius", .75f); Set(agent, "walkSpeed", 2f); Set(agent, "displayName", species.ToString());
            Set(agent, "random", new System.Random(42));
            root.SetActive(true);
            return agent;
        }
    }
}
