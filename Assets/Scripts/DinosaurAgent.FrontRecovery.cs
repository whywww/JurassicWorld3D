using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        private bool frontRecoveryActive, frontRecoveryWalking;
        private Vector3 frontRecoveryTarget, frontRecoveryAttemptPosition;
        private float frontRecoveryRetryAt;
        private int frontRecoverySceneStamp;
        private readonly HashSet<InteractionMotionRole> frontRecoveryTried = new HashSet<InteractionMotionRole>();
        private static readonly InteractionMotionRole[] RecoveryTurns =
            { InteractionMotionRole.TurnLeft, InteractionMotionRole.TurnRight, InteractionMotionRole.Turn180Look };
        private static readonly float[] RecoverySteering = { 0f, -15f, 15f, -30f, 30f, -45f, 45f };

        private void CaptureFrontRecoveryPose()
        {
            interactionBody = frontOriginalBody;
            TerrainTreeObstacleRegistry.EnsureInitialized();
            interactionSettings.CollectStaticOverlaps(new Vector2(transform.position.x, transform.position.z), CoreForward,
                interactionBody, freeRecoveryAllowed);
            freeRecoveryGeometry = TerrainTreeObstacleRegistry.GeometryRevision;
            freeRecoverySolids = (Bounds[])interactionSettings.solidObstacles.Clone();
        }

        private void BeginFrontRecovery()
        {
            CancelFrontWait();
            if (IsSteppingTurn) ClearSteppingTurn();
            frontRecoveryActive = true;
            frontRecoveryWalking = false;
            frontRecoveryRetryAt = 0f;
            frontRecoveryAttemptPosition = transform.position;
            frontRecoveryTried.Clear();
            hasCuriosityPath = false;
            GetComponentInChildren<DinosaurFootLock>()?.HoldSupportDuringRecovery(.35f);
            CaptureFrontRecoveryPose();
            TickFrontRecovery();
        }

        private void ClearFrontRecovery()
        {
            frontReturnFreeWalkUntil = 0f;
            ClearFrontReturnTurnPlan();
            if (!frontRecoveryActive) return;
            if (IsSteppingTurn && stepTurnUse == TurnUse.FrontRecovery) ClearSteppingTurn();
            frontRecoveryActive = frontRecoveryWalking = false;
            frontRecoveryTried.Clear();
            freeRecoveryAllowed.Clear();
            frontBlockReason = frontWaitReason = null;
        }

        private void WaitFrontRecovery()
        {
            if (IsSteppingTurn)
            {
                GetComponentInChildren<DinosaurFootLock>()?.HoldSupportDuringRecovery(.35f);
                ClearSteppingTurn();
                // A peer may stop a performance after it has crossed static scenery.
                CaptureFrontRecoveryPose();
            }
            frontRecoveryWalking = false;
            frontRecoveryRetryAt = Time.time + 2f;
            actualInteractionSpeed = 0f;
            frontBlockReason = "Waiting for safe recovery exit";
            SelectFrontWaitAnimation();
        }

        private Vector3 FrontRecoveryGoal
        {
            get
            {
                if (frontStage != FrontStage.Follow)
                    return frontReturnIndex >= 0 && frontReturnIndex < frontTrail.Count ? frontTrail[frontReturnIndex]
                        : frontTrail.Count > 0 ? frontTrail[0] : transform.position;
                Vector3 away = transform.position - lastSeenPlayer; away.y = 0f;
                if (away.sqrMagnitude < .01f) away = -GetVisualForward();
                return lastSeenPlayer + away.normalized * Mathf.Max(0f, frontNearDistance - .25f);
            }
        }

        private int FrontRecoverySceneStamp()
        {
            unchecked
            {
                int hash = TerrainTreeObstacleRegistry.GeometryRevision;
                foreach (var obstacle in interactionSettings.solidObstacles) hash = hash * 31 + obstacle.GetHashCode();
                hash = hash * 31 + interactionSettings.activityBoundsOverride.GetHashCode();
                foreach (var peer in peers)
                    if (peer != null) hash = hash * 31 + peer.transform.position.GetHashCode() + peer.transform.rotation.GetHashCode();
                return hash;
            }
        }

        // Simulation never retires the runtime overlap exemptions.
        private bool FrontRecoveryConnector(Vector3 from, Quaternion rotation, Vector3 target)
        {
            Vector3 direction = target - from; direction.y = 0f;
            if (direction.sqrMagnitude < .01f) return false;
            Quaternion heading = Quaternion.LookRotation(direction) * Quaternion.Euler(0f, -VisualYaw, 0f);
            if (!FreeRecoveryRestPose(target, heading)) return false;
            var saved = new HashSet<int>(freeRecoveryAllowed);
            try
            {
                return FreeRecoveryStep(from, from, rotation, heading, true)
                    && FreeRecoveryStep(from, target, heading, heading, false);
            }
            finally { freeRecoveryAllowed.Clear(); freeRecoveryAllowed.UnionWith(saved); }
        }

        private bool FrontRecoveryConnectsGoal(Vector3 point, Quaternion heading)
        {
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(point, FrontRecoveryGoal, NavigationFilter, path)
                || path.status != NavMeshPathStatus.PathComplete) return false;
            Vector3 previous = point;
            foreach (var corner in path.corners)
            {
                Vector3 direction = corner - previous; direction.y = 0f;
                if (direction.sqrMagnitude < .0001f) continue;
                Quaternion next = Quaternion.LookRotation(direction) * Quaternion.Euler(0f, -VisualYaw, 0f);
                if (!FrontStepSafe(previous, corner, heading, next, interactionBody)) return false;
                previous = corner; heading = next;
            }
            return true;
        }

        private bool PlanFrontRecovery()
        {
            int stamp = FrontRecoverySceneStamp();
            if (stamp != frontRecoverySceneStamp) { frontRecoveryTried.Clear(); frontRecoverySceneStamp = stamp; }
            var saved = new HashSet<int>(freeRecoveryAllowed);
            var savedSolids = freeRecoverySolids;
            int savedGeometry = freeRecoveryGeometry;
            var savedBands = frontProbeBody != null ? frontProbeBody.bands : null;
            DinosaurInteractionProfile.Motion selected = null;
            float best = float.PositiveInfinity;
            Vector3 target = default;
            try
            {
                // Evaluate the straight exit and all full authored turns from their actual root endpoint.
                for (int choice = -1; choice < RecoveryTurns.Length; choice++)
                {
                    var motion = choice < 0 ? null : interactionProfile.Find(RecoveryTurns[choice]);
                    if (choice >= 0 && (motion == null || motion.footFrames.Length < 2
                        || frontRecoveryTried.Contains(motion.role))) continue;
                    Vector3 origin = transform.position;
                    Quaternion rotation = transform.rotation;
                    if (motion != null)
                    {
                        bool fits = true;
                        Vector3 previous = origin; Quaternion previousRotation = rotation;
                        float previousTime = 0f;
                        // Include animation-dependent body envelopes and the complete root trajectory.
                        for (float time = 0f; ; time = Mathf.Min(motion.clip.length, time + 1f / 30f))
                        {
                            var frame = SampleFootFrame(motion, time);
                            Vector3 position = transform.position + transform.rotation * Quaternion.Euler(0f, VisualYaw, 0f)
                                * new Vector3(frame.rootOffset.x, 0f, frame.rootOffset.y);
                            position.y = MovementHeight;
                            Quaternion q = Quaternion.Euler(0f, frame.yaw, 0f) * transform.rotation;
                            if (!FrontAnimatedStep(motion, previousTime, time, previous, position, previousRotation, q, false))
                            { fits = false; break; }
                            previous = position; previousRotation = q; previousTime = time;
                            if (time >= motion.clip.length) { origin = position; rotation = q; break; }
                        }
                        if (!fits) continue;
                        Vector3 f = rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
                        interactionSettings.CollectStaticOverlaps(new Vector2(origin.x, origin.z), new Vector2(f.x, f.z),
                            interactionBody, freeRecoveryAllowed);
                        freeRecoveryGeometry = TerrainTreeObstacleRegistry.GeometryRevision;
                        freeRecoverySolids = (Bounds[])interactionSettings.solidObstacles.Clone();
                    }
                    else
                    {
                        freeRecoveryAllowed.Clear(); freeRecoveryAllowed.UnionWith(saved);
                        freeRecoveryGeometry = savedGeometry; freeRecoverySolids = savedSolids;
                    }
                    Vector3 forward = rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
                    foreach (float distance in RecoveryDistances)
                    foreach (float angle in RecoverySteering)
                    {
                        Vector3 candidate = origin + Quaternion.Euler(0f, angle, 0f) * forward * distance;
                        if (!FrontRecoveryConnector(origin, rotation, candidate)) continue;
                        Quaternion heading = Quaternion.Euler(0f, angle, 0f) * rotation;
                        bool connects = FrontRecoveryConnectsGoal(candidate, heading);
                        float score = (connects ? 0f : 10000f) + distance
                            + Quaternion.Angle(transform.rotation, heading) * .01f;
                        if (score >= best) continue;
                        best = score; target = candidate; selected = motion;
                    }
                }
            }
            finally
            {
                freeRecoveryAllowed.Clear(); freeRecoveryAllowed.UnionWith(saved);
                freeRecoveryGeometry = savedGeometry; freeRecoverySolids = savedSolids;
                if (frontProbeBody != null && savedBands != null) frontProbeBody.bands = savedBands;
            }
            if (float.IsPositiveInfinity(best)) return false;
            frontRecoveryTarget = target;
            frontRecoveryWalking = true;
            frontWaitPlaying = false;
            if (selected != null)
            {
                frontRecoveryTried.Add(selected.role);
                BeginSteppingTurn(selected, selected.clip.length, TurnUse.FrontRecovery);
            }
            else PlayMovementClip(WalkSpeed);
            frontBlockReason = null;
            return true;
        }

        private void TickFrontRecovery()
        {
            actualInteractionSpeed = 0f;
            if (IsSteppingTurn) { TickSteppingTurn(); return; }
            interactionBody = frontOriginalBody;
            if (!frontRecoveryWalking)
            {
                if (Time.time < frontRecoveryRetryAt)
                {
                    if (frontWaitPlaying && legacyAnimation[WaitClip] != null)
                    {
                        frontWaitTime += Time.deltaTime;
                        legacyAnimation[WaitClip].time = Mathf.Repeat(frontWaitTime, legacyAnimation[WaitClip].length);
                        legacyAnimation.Sample();
                    }
                    return;
                }
                if (!PlanFrontRecovery())
                {
                    // Failed turns at the same pose stay excluded until scenery/peers change
                    // or a checked walk makes progress. Waiting alone must not cause spinning.
                    WaitFrontRecovery();
                }
                return;
            }
            if (Time.time < frontReturnBlendUntil) return;
            Vector3 delta = frontRecoveryTarget - transform.position; delta.y = 0f;
            if (delta.sqrMagnitude <= .0001f)
            {
                if (!FreeRecoveryRestPose(transform.position, transform.rotation)) { WaitFrontRecovery(); return; }
                ClearFrontRecovery();
                hasCuriosityPath = false;
                frontFollowBlockedAt = frontReturnBlockedAt = -1f;
                if (frontStage == FrontStage.Follow)
                {
                    PlayFrontFollow();
                    if (!PlanFrontFollow()) BeginFrontSpaceWait();
                }
                else { PlayMovementClip(WalkSpeed); frontReturnBlendUntil = 0f; }
                return;
            }
            if (UsesWalkingSteering)
            {
                if (MovePredictedTowards(frontRecoveryTarget, WalkSpeed, true))
                { PlayMovementClip(WalkSpeed); if (frontStage == FrontStage.Follow) RecordFrontTrail(); }
                else WaitFrontRecovery();
                return;
            }
            Quaternion desired = Quaternion.LookRotation(delta) * Quaternion.Euler(0f, -VisualYaw, 0f);
            Quaternion rotation = Quaternion.RotateTowards(transform.rotation, desired, 65f * Time.deltaTime);
            Vector3 next = Quaternion.Angle(rotation, desired) < .01f
                ? Vector3.MoveTowards(transform.position, frontRecoveryTarget, WalkSpeed * Time.deltaTime) : transform.position;
            if (!FreeRecoveryStep(transform.position, next, transform.rotation, rotation, true)) { WaitFrontRecovery(); return; }
            float travelled = FlatDistance(transform.position, next);
            transform.SetPositionAndRotation(next, rotation);
            interactionMoved = travelled > .00001f;
            actualInteractionSpeed = travelled / Mathf.Max(.00001f, Time.deltaTime);
            if (interactionMoved)
            {
                PlayMovementClip(WalkSpeed);
                if (frontStage == FrontStage.Follow) RecordFrontTrail();
                if (FlatDistance(transform.position, frontRecoveryAttemptPosition) > 1f)
                { frontRecoveryTried.Clear(); frontRecoveryAttemptPosition = transform.position; }
            }
            else if (moveState != null) moveState.speed = 0f;
        }
    }
}
