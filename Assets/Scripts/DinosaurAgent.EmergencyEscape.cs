using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        private const float EmergencyStuckSeconds = 8f;
        private const float EmergencyProgressDistance = .25f;
        private bool emergencyEscapeActive, emergencyHasGoal, emergencyMonitoring;
        private Vector3 emergencyAnchor, emergencyOrigin, emergencyGoal;
        private float emergencyStuckTime, emergencyRetry;
        public bool IsEmergencyEscaping => emergencyEscapeActive;
        public float NoMovementDuration => emergencyStuckTime;
        public string EmergencyEscapeStatus { get; private set; } = "Inactive";

        private bool EmergencyMovementExpected => !frontTestWaiting && (trafficLocked || freeRecoveryActive || frontRecoveryActive
            || frontActive && FrontActionWaitingForSpace
            || !frontActive && (CurrentState != DinosaurState.Idle || IsSteppingTurn || routeRejoining
                || freeRetryGoal || freeReplanFailures > 0 || !string.IsNullOrEmpty(RoamingBlockReason)));

        private bool UpdateEmergencyEscape(float deltaTime)
        {
            if (deltaTime <= 0f) return emergencyEscapeActive;
            if (!emergencyEscapeActive)
            {
                if (!EmergencyMovementExpected)
                {
                    emergencyMonitoring = false;
                    emergencyStuckTime = 0f;
                    return false;
                }
                if (!emergencyMonitoring || FlatDistance(transform.position, emergencyAnchor) >= EmergencyProgressDistance)
                {
                    emergencyMonitoring = true;
                    emergencyAnchor = transform.position;
                    emergencyStuckTime = 0f;
                }
                emergencyStuckTime += deltaTime;
                if (emergencyStuckTime < EmergencyStuckSeconds) return false;
                BeginEmergencyEscape();
            }
            TickEmergencyEscape(deltaTime);
            return true;
        }

        private void BeginEmergencyEscape()
        {
            emergencyEscapeActive = true;
            emergencyOrigin = transform.position;
            emergencyHasGoal = false;
            emergencyRetry = 0f;
            // End the old task, rather than resuming its blocked connector on exit.
            FinishGroundTraffic();
            if (frontActive) FinishFrontInteraction();
            ClearFrontRecovery();
            ClearInteractionNavigation();
            freeRecoveryActive = freeRecoveryHasTarget = false;
            freeRecoveryAllowed.Clear();
            IsAvoidingTyrannosaurus = false;
            InteractionPhase = PlayerInteractionPhase.Inactive;
            freeRetryGoal = false;
            freeReplanFailures = 0;
            blockedTime = 0f;
            roamingWalkBlendUntil = 0f;
            waitingAtCrossing = false;
            MovementCorridorCrossings.Release(this);
            frontFeet?.EndReturnHandoff();
            GetComponentInChildren<DinosaurFootLock>()?.HoldSupportDuringRecovery(.35f);
            if (interactionSettings == null) interactionSettings = FindObjectOfType<DinosaurInteractionSettings>();
            if (interactionBody == null) interactionBody = coreBody;
            CurrentState = DinosaurState.RandomWalk;
            EmergencyEscapeStatus = "Escaping: obstacles temporarily ignored";
            Debug.Log($"[Emergency escape] {DisplayName}: no movement for {emergencyStuckTime:F1}s; temporarily ignoring static and peer obstacles.", this);
        }

        private Rect EmergencyBounds => interactionSettings != null
            ? interactionSettings.activityBoundsOverride ?? WorldBounds.DinosaurActivityXZ : WorldBounds.DinosaurActivityXZ;

        // Only endpoints use ordinary collision/nav checks. The connector deliberately
        // ignores trees, rocks and peers, without changing any shared collision data.
        private bool EmergencyOpenPoint(Vector3 point, Quaternion rotation)
        {
            Rect bounds = EmergencyBounds;
            if (!bounds.Contains(new Vector2(point.x, point.z))) return false;
            var body = interactionBody != null ? interactionBody : coreBody;
            Vector3 heading = rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
            Vector2 forward = new Vector2(heading.x, heading.z);
            if (groundObstacleAware)
            {
                if (interactionSettings == null || body == null || !interactionSettings.NavigationReady) return false;
                if (!NavMesh.SamplePosition(point, out var hit, .25f, NavigationFilter)
                    || FlatDistance(point, hit.position) > .12f || !NavigationRestPoint(hit.position)) return false;
                // Leave a .75m clear patch around the complete body, not just its root.
                foreach (Vector2 offset in new[] { Vector2.zero, Vector2.left * .75f, Vector2.right * .75f, Vector2.up * .75f, Vector2.down * .75f })
                    if (interactionSettings.PoseBlocked(new Vector2(point.x, point.z) + offset, forward, body)) return false;
            }
            foreach (var peer in peers)
            {
                if (peer == null || peer == this || !peer.isActiveAndEnabled
                    || groundObstacleAware != peer.groundObstacleAware
                    || Mathf.Abs(MovementHeight - peer.MovementHeight) > PeerHeightTolerance) continue;
                if (FlatDistance(point, peer.transform.position) < collisionRadius + peer.collisionRadius + 1f
                    || PeerPenetration(point, rotation, body, peer) > 0f) return false;
                if (UsesSimpleFreeRoam && peer.GroundSpecies == DinosaurSpecies.Tyrannosaurus
                    && FlatDistance(point, peer.transform.position) < safetyReleaseDistance) return false;
            }
            return true;
        }

        private bool TryEmergencyGoal(Vector3 point)
        {
            if (!IsFreeRoaming)
            {
                MovementCorridor.ProjectToCorridor(routeIndex, new Vector2(point.x, point.z), out float arc, out _);
                MovementCorridor.GetSampleAt(routeIndex, arc, out Vector2 routePoint, out _);
                point = new Vector3(routePoint.x, MovementHeight, routePoint.y);
            }
            point.y = MovementHeight;
            if (FlatDistance(point, emergencyOrigin) < 3f) return false;
            Vector3 delta = point - transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude < .01f) return false;
            Quaternion rotation = Quaternion.LookRotation(delta) * Quaternion.Euler(0f, -VisualYaw, 0f);
            if (!EmergencyOpenPoint(point, rotation)) return false;
            emergencyGoal = point;
            emergencyHasGoal = true;
            return true;
        }

        private bool PlanEmergencyEscape()
        {
            Vector3 forward = GetVisualForward();
            foreach (float distance in new[] { 4f, 8f, 12f, 20f, 32f, 48f, 64f, 96f })
                for (int i = 0; i < 24; i++)
                {
                    float angle = (i + 1) / 2 * 15f * (i % 2 == 0 ? 1f : -1f);
                    if (TryEmergencyGoal(transform.position + Quaternion.Euler(0f, angle, 0f) * forward * distance)) return true;
                }
            // A dense pocket can hide all radial samples. Also search the activity area.
            Rect bounds = EmergencyBounds;
            for (float x = bounds.xMin + 2f; x < bounds.xMax; x += 4f)
                for (float z = bounds.yMin + 2f; z < bounds.yMax; z += 4f)
                    if (TryEmergencyGoal(new Vector3(x, MovementHeight, z))) return true;
            return false;
        }

        private void TickEmergencyEscape(float deltaTime)
        {
            if (emergencyHasGoal)
            {
                Vector3 direction = emergencyGoal - transform.position;
                Quaternion arrival = direction.sqrMagnitude > .000001f
                    ? Quaternion.LookRotation(direction) * Quaternion.Euler(0f, -VisualYaw, 0f) : transform.rotation;
                if (!EmergencyOpenPoint(emergencyGoal, arrival)) emergencyHasGoal = false;
            }
            if (!emergencyHasGoal)
            {
                emergencyRetry -= deltaTime;
                if (emergencyRetry > 0f) { PlayClip(idleClip); return; }
                emergencyRetry = 1f;
                if (!PlanEmergencyEscape())
                {
                    EmergencyEscapeStatus = "Escaping: searching for open ground";
                    RoamingBlockReason = EmergencyEscapeStatus;
                    PlayClip(idleClip);
                    return;
                }
            }
            EmergencyEscapeStatus = "Escaping: obstacles temporarily ignored";
            Vector3 delta = emergencyGoal - transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > .000001f)
            {
                Quaternion desired = Quaternion.LookRotation(delta) * Quaternion.Euler(0f, -VisualYaw, 0f);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, 120f * deltaTime);
                // The exception also bypasses NavMesh walls and traffic priority. Walk
                // continuously toward the checked endpoint; do not teleport or wait to turn.
                Vector3 before = transform.position;
                transform.position = Vector3.MoveTowards(before, emergencyGoal, Mathf.Max(.1f, WalkSpeed) * deltaTime);
                actualInteractionSpeed = FlatDistance(before, transform.position) / deltaTime;
                interactionMoved = true;
                PlayMovementClip(WalkSpeed);
            }
            RoamingBlockReason = EmergencyEscapeStatus;
            if (FlatDistance(transform.position, emergencyGoal) > .05f
                || !EmergencyOpenPoint(transform.position, transform.rotation)) return;
            emergencyEscapeActive = emergencyHasGoal = emergencyMonitoring = false;
            emergencyStuckTime = 0f;
            emergencyAnchor = transform.position;
            EmergencyEscapeStatus = "Inactive";
            RoamingBlockReason = "";
            cooldownUntil = Time.time + interactionCooldown;
            // Route endpoints are sampled on their corridor. Refresh the arc so
            // legacy walkers cannot snap back to their pre-escape position.
            if (!IsFreeRoaming)
                MovementCorridor.ProjectToCorridor(routeIndex, new Vector2(transform.position.x, transform.position.z), out routeArc, out _);
            BeginIdle();
            Debug.Log($"[Emergency escape] {DisplayName}: reached open ground; normal obstacle checks restored.", this);
        }
    }
}
