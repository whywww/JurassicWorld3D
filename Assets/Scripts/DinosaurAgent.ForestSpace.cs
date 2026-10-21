using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        // Special performances may clip through trees and static scenery, but never
        // through another dinosaur's body; foot corrections follow the same gate.
        internal bool ForestPerformanceSupport => frontActive && frontAction != null && !frontWaiting && !frontPeerClearing;

        // Acceptance queries observe the same gates without advancing locomotion, animation,
        // retry clocks or the recovery exemptions. Kept private to avoid a public debug API.
        private bool ForestStaticClippingAllowed => IsSteppingTurn && stepTurnUse == TurnUse.Wander
            || frontActive && frontAction != null && !frontPeerClearing;
        private string ForestCollisionStage => ForestStaticClippingAllowed ? (IsSteppingTurn ? "Wander performance" : "Interaction performance")
            : freeRecoveryActive && !frontActive ? "Recovery" : frontActive ? "Interaction travel / wait" : "Ordinary travel / idle";

        private string ForestCollisionFailure(bool spawning, float previousBoundary, float previousNavDistance)
        {
            if (interactionSettings == null || interactionBody == null) return "Missing collision data";
            bool performance = !spawning && ForestStaticClippingAllowed;
            bool recovery = !spawning && !performance && freeRecoveryActive && !frontActive;
            var body = !spawning && (performance || frontActive) && frontProbeBody != null ? frontProbeBody : interactionBody;
            if (FrontPeerPoseBlocked(transform.position, transform.rotation, body)) return "Peer body";
            if (performance) return null;
            if (recovery) return FreeRecoveryPoseFailure(previousBoundary, previousNavDistance);
            string reason = interactionSettings.PoseBlockReason(new Vector2(transform.position.x, transform.position.z), CoreForward, interactionBody);
            if (reason != null) return reason;
            if (!NavMesh.SamplePosition(transform.position, out var hit, .25f, NavigationFilter)
                || FlatDistance(transform.position, hit.position) > .12f) return "NavMesh endpoint";
            return null;
        }

        private string FreeRecoveryPoseFailure(float previousBoundary, float previousNavDistance)
        {
            TerrainTreeObstacleRegistry.EnsureInitialized();
            if (freeRecoveryGeometry != TerrainTreeObstacleRegistry.GeometryRevision) return "Recovery geometry changed";
            var overlaps = new HashSet<int>();
            interactionSettings.CollectStaticOverlaps(new Vector2(transform.position.x, transform.position.z), CoreForward, interactionBody, overlaps);
            if (!overlaps.IsSubsetOf(freeRecoveryAllowed)) return "New or retired recovery obstacle";
            foreach (int id in freeRecoveryAllowed)
                if (id < 0 && (-id > interactionSettings.solidObstacles.Length
                    || interactionSettings.solidObstacles[-id - 1] != freeRecoverySolids[-id - 1])) return "Recovery solid changed";
            float boundary = FreeRecoveryBoundary(transform.position, transform.rotation), nav = FreeRecoveryNavDistance(transform.position);
            if (float.IsPositiveInfinity(nav)) return "Recovery has no nearby NavMesh";
            return FreeRecoveryDistancesAllowed(previousBoundary, previousNavDistance, boundary, nav) ? null : "Recovery moved farther outside bounds / NavMesh";
        }

        private static bool FreeRecoveryDistancesAllowed(float boundary, float offMesh, float nextBoundary, float nextOffMesh)
            => nextBoundary <= boundary + .0001f && nextOffMesh <= Mathf.Max(.12f, offMesh) + .0001f;
    }
}
