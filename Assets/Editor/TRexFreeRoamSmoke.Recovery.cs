using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld.Editor
{
    public static partial class TRexFreeRoamSmoke
    {
        private static GameObject turnTree;
        private static readonly FieldInfo ActivityOverride = typeof(DinosaurInteractionSettings).GetField("activityBoundsOverride", Private);
        private static object Invoke(string method, params object[] args)
            => typeof(DinosaurAgent).GetMethod(method, Private).Invoke(rex, args);

        private static void CleanupTurnObstacles()
        {
            if (turnTree != null) UnityEngine.Object.DestroyImmediate(turnTree);
            if (settings != null)
            {
                ActivityOverride.SetValue(settings, null);
            }
            TerrainTreeObstacleRegistry.Invalidate();
        }

        private static void ConfigureTurnObstacles()
        {
            CleanupTurnObstacles();
            Vector3 point = new Vector3(-22f, rex.MovementHeight, 8f);
            settings.solidObstacles = originalObstacles;
            switch (turnCase / 2)
            {
                case 0:
                    settings.solidObstacles = new[] { new Bounds(point, Vector3.one * 40f) };
                    break;
                case 1:
                    turnTree = new GameObject("Free turn acceptance tree");
                    turnTree.transform.position = point;
                    var tree = turnTree.AddComponent<SceneTreeObstacle>();
                    tree.SetRadius(8f);
                    tree.trunkSections = new[] { new TreeTrunkSection { center = Vector2.zero, radius = 8f, bottom = -100f, top = 100f } };
                    TerrainTreeObstacleRegistry.Invalidate();
                    break;
                case 2:
                    ActivityOverride.SetValue(settings, new Rect(point.x - .5f, point.z - .5f, 1f, 1f));
                    break;
            }
        }

        private static void VerifyFreeRecoveryContracts()
        {
            Vector3 origin = new Vector3(300f, rex.MovementHeight, 300f);
            Vector3 savedPosition = rex.transform.position, peerPosition = raptor.transform.position;
            Quaternion savedRotation = rex.transform.rotation, peerRotation = raptor.transform.rotation;
            var body = (DinosaurCoreBody)Get("interactionBody");
            var peerField = typeof(DinosaurAgent).GetField("interactionBody", Private);
            object peerBody = peerField.GetValue(raptor);
            var holder = new GameObject("Recovery acceptance body");
            var probe = holder.AddComponent<DinosaurCoreBody>();
            probe.bands = new[] { new DinosaurCoreBody.Band { layer = 0, bounds = new Bounds(new Vector3(0f, .25f, 0f), new Vector3(.6f, .5f, .6f)) } };
            var build = NavMesh.GetSettingsByID(settings.navigationAgentTypeId);
            var data = NavMeshBuilder.BuildNavMeshData(build, new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(80f, .2f, 80f),
                    transform = Matrix4x4.TRS(origin - Vector3.up * .1f, Quaternion.identity, Vector3.one), area = 0 }
            }, new Bounds(origin, new Vector3(100f, 20f, 100f)), Vector3.zero, Quaternion.identity);
            Require(data != null, "Recovery fixture NavMesh did not bake.");
            var instance = NavMesh.AddNavMeshData(data);
            Quaternion forward = Quaternion.Euler(0f, -rex.VisualYaw, 0f);
            try
            {
                Set("interactionBody", probe);
                ActivityOverride.SetValue(settings, new Rect(250f, 250f, 100f, 100f));
                rex.transform.SetPositionAndRotation(origin, forward);
                Set("freeGoal", origin + Vector3.forward * 2f);
                settings.solidObstacles = new[]
                {
                    new Bounds(origin + Vector3.up * .25f, Vector3.one),
                    new Bounds(origin + Vector3.forward * 3.5f + Vector3.up * .25f, Vector3.one)
                };
                Invoke("FinishWanderTurn");
                Require((bool)Get("freeRecoveryActive"), "Overlap did not activate recovery.");
                Require(Invoke("ForestCollisionFailure", true, float.PositiveInfinity, float.PositiveInfinity) != null,
                    "Birth contract accepted a solid overlap.");
                Require(!(bool)Invoke("SpawnExitFits", origin, Vector3.forward, Vector3.zero), "Birth exit accepted a blocked path.");
                var exemptions = new HashSet<int>((HashSet<int>)Get("freeRecoveryAllowed"));
                Require(Invoke("ForestCollisionFailure", false, float.PositiveInfinity, float.PositiveInfinity) == null,
                    "Observer rejected an original recovery overlap.");
                Require(exemptions.SetEquals((HashSet<int>)Get("freeRecoveryAllowed")), "Read-only query changed recovery exemptions.");
                Require((bool)Invoke("TryFreeRecoveryTarget", origin + Vector3.forward * 2f), "No connector out of original rock.");
                Require((bool)Invoke("FreeRecoveryStep", origin, origin + Vector3.forward * 2f, forward, forward, true), "Original overlap could not be exited.");
                Require(!(bool)Invoke("FreeRecoveryStep", origin + Vector3.forward * 2f, origin, forward, forward, false), "Exited rock could be entered again.");
                Require(!(bool)Invoke("FreeRecoveryStep", origin + Vector3.forward * 2f, origin + Vector3.forward * 4f, forward, forward, false), "Recovery entered a new rock.");
                Require(Invoke("ForestCollisionFailure", false, float.PositiveInfinity, float.PositiveInfinity) != null,
                    "Observer accepted re-entry into the retired rock.");
                rex.transform.position = origin + Vector3.forward * 4f;
                Require(Invoke("ForestCollisionFailure", false, float.PositiveInfinity, float.PositiveInfinity) != null,
                    "Observer accepted a new recovery rock.");
                rex.transform.position = origin;
                Invoke("WaitForFreeRecovery");
                Invoke("FreeIdle", 2f);
                Require((bool)Get("freeRecoveryActive") && (float)Get("freeRecoveryRetryAt") >= Time.time + 1.9f, "Idle lost recovery/retry state.");

                // Run the actual movement loop, checking continuity and restored ordinary gates.
                Invoke("FinishWanderTurn");
                Set("freeRecoveryRetryAt", 0f);
                for (int i = 0; i < 600 && (bool)Get("freeRecoveryActive"); i++)
                {
                    Vector3 before = rex.transform.position;
                    Invoke("UpdateFreeRecovery");
                    Require(Vector3.Distance(before, rex.transform.position) <= rex.WalkSpeed * Time.deltaTime + .001f, "Recovery teleported.");
                }
                Require(!(bool)Get("freeRecoveryActive"), "Recovery never returned to ordinary walking.");
                Vector3 recoveredPosition = rex.transform.position;
                rex.transform.position = origin;
                Require(Invoke("ForestCollisionFailure", false, float.PositiveInfinity, float.PositiveInfinity) != null,
                    "Ordinary walking retained a static clipping exemption after recovery.");
                rex.transform.position = recoveredPosition;
                Require(settings.PoseBlockReason(new Vector2(rex.transform.position.x, rex.transform.position.z), Vector2.up, probe) == null, "Recovery finished inside a solid.");

                settings.solidObstacles = new[] { new Bounds(origin, Vector3.one * 100f) };
                rex.transform.SetPositionAndRotation(origin, forward);
                Invoke("FinishWanderTurn");
                Invoke("UpdateFreeRecovery");
                Require((bool)Get("freeRecoveryActive") && !(bool)Get("freeRecoveryHasTarget")
                    && (float)Get("freeRecoveryRetryAt") >= Time.time + 1.9f, "No-exit recovery did not wait for retry.");
                Invoke("UpdateFreeRecovery");
                Require(rex.transform.position == origin, "Waiting recovery moved without an exit.");

                // Tree identities are whole-tree IDs; old sections may be exited, never re-entered.
                settings.solidObstacles = Array.Empty<Bounds>();
                turnTree = new GameObject("Recovery acceptance tree");
                turnTree.transform.position = origin;
                var tree = turnTree.AddComponent<SceneTreeObstacle>();
                tree.trunkSections = new[] { new TreeTrunkSection { radius = .5f, bottom = -100f, top = 100f } };
                var otherTree = new GameObject("Recovery acceptance tree");
                otherTree.transform.SetParent(turnTree.transform);
                otherTree.transform.position = origin + Vector3.forward * 3.5f;
                otherTree.AddComponent<SceneTreeObstacle>().trunkSections = tree.trunkSections;
                TerrainTreeObstacleRegistry.Invalidate();
                rex.transform.SetPositionAndRotation(origin, forward);
                Invoke("FinishWanderTurn");
                Require((bool)Get("freeRecoveryActive"), "Tree overlap was not captured.");
                Require((bool)Invoke("FreeRecoveryStep", origin, origin + Vector3.forward * 2f, forward, forward, true), "Tree recovery was blocked.");
                Require(!(bool)Invoke("FreeRecoveryStep", origin + Vector3.forward * 2f, origin, forward, forward, false), "Recovery re-entered an exited tree.");
                Require(!(bool)Invoke("FreeRecoveryStep", origin + Vector3.forward * 2f, origin + Vector3.forward * 4f, forward, forward, false), "Recovery entered a new tree with the same display name.");
                CleanupTurnObstacles();
                ActivityOverride.SetValue(settings, new Rect(250f, 250f, 100f, 100f));

                // Outside navigation: walk inward continuously, reject movement farther out.
                var filter = new NavMeshQueryFilter { agentTypeID = settings.navigationAgentTypeId, areaMask = 1 };
                Require(NavMesh.SamplePosition(origin + Vector3.right * 45f, out var edge, 20f, filter), "Missing fixture edge.");
                Vector3 outside = new Vector3(edge.position.x + 1f, rex.MovementHeight, origin.z);
                Vector3 inside = new Vector3(edge.position.x - 2f, rex.MovementHeight, origin.z);
                rex.transform.SetPositionAndRotation(outside, forward);
                Invoke("FinishWanderTurn");
                Require((bool)Get("freeRecoveryActive"), "Off-mesh turn did not activate recovery.");
                Require(Invoke("ForestCollisionFailure", false, float.PositiveInfinity, float.PositiveInfinity) == null,
                    "Observer rejected the captured off-mesh recovery pose.");
                Require(Invoke("ForestCollisionFailure", false, float.PositiveInfinity, 0f) != null,
                    "Observer allowed recovery to move farther off mesh.");
                Require((bool)Invoke("FreeRecoveryStep", outside, inside, forward, forward, false), "Off-mesh recovery could not return inward.");
                Require(!(bool)Invoke("FreeRecoveryStep", outside, outside + Vector3.right, forward, forward, false), "Recovery moved farther off mesh.");

                // Activity boundary recovery follows the same inward-only rule.
                ActivityOverride.SetValue(settings, new Rect(299f, 299f, 2f, 2f));
                rex.transform.SetPositionAndRotation(origin + Vector3.right * 1.2f, forward);
                Invoke("FinishWanderTurn");
                Require((bool)Invoke("FreeRecoveryStep", rex.transform.position, origin, forward, forward, false), "Boundary recovery could not move inward.");
                Require(!(bool)Invoke("FreeRecoveryStep", rex.transform.position, rex.transform.position + Vector3.right, forward, forward, false), "Recovery moved farther outside activity bounds.");

                // Performances and their root corrections must still respect dinosaur bodies.
                ActivityOverride.SetValue(settings, new Rect(250f, 250f, 100f, 100f));
                rex.transform.SetPositionAndRotation(origin, forward);
                raptor.transform.position = origin;
                Set("interactionBody", body);
                peerField.SetValue(raptor, body);
                raptor.transform.rotation = Quaternion.Euler(0f, -raptor.VisualYaw, 0f);
                var use = Enum.Parse(typeof(DinosaurAgent).GetNestedType("TurnUse", BindingFlags.NonPublic), "Wander");
                Require((bool)Invoke("TryBeginSteppingTurn", 90f, use), "Peer fixture turn did not start.");
                Require(Invoke("ForestCollisionFailure", false, float.PositiveInfinity, float.PositiveInfinity) != null,
                    "Observer exempted dinosaur collision during a performance.");
                float clock = rex.StepTurnClipTime;
                Invoke("TickSteppingTurn");
                Require(rex.StepTurnClipTime == clock, "Wander turn ignored another dinosaur.");
                Require(!rex.ApplyFootRootCorrection(Vector3.right * .01f), "Turn foot correction ignored another dinosaur.");
                raptor.transform.position = peerPosition;
                settings.solidObstacles = new[] { new Bounds(origin, Vector3.one * 100f) };
                Require(Invoke("ForestCollisionFailure", false, float.PositiveInfinity, float.PositiveInfinity) == null,
                    "Observer rejected permitted static clipping during a wander turn.");
                Invoke("TickSteppingTurn");
                Require(rex.StepTurnClipTime > clock, "Wander turn did not resume after peer cleared.");
                settings.solidObstacles = new[] { new Bounds(origin, Vector3.one * 100f) };
                Invoke("RecoverInterruptedTurn");
                Require((bool)Get("freeRecoveryActive"), "Interrupted wander turn lost static-overlap recovery.");
                bool savedFront = (bool)Get("frontActive");
                Set("freeRecoveryActive", false);
                Set("frontActive", true);
                try
                {
                    Require(Invoke("ForestCollisionFailure", false, float.PositiveInfinity, float.PositiveInfinity) != null,
                        "Observer exempted ordinary interaction travel from static collision.");
                }
                finally { Set("frontActive", savedFront); }
            }
            finally
            {
                instance.Remove();
                UnityEngine.Object.DestroyImmediate(data);
                Set("interactionBody", body);
                peerField.SetValue(raptor, peerBody);
                raptor.transform.SetPositionAndRotation(peerPosition, peerRotation);
                rex.transform.SetPositionAndRotation(savedPosition, savedRotation);
                Set("freeRecoveryActive", false);
                settings.solidObstacles = originalObstacles;
                CleanupTurnObstacles();
                UnityEngine.Object.DestroyImmediate(holder);
            }
        }
    }
}
