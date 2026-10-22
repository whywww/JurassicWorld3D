using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        private bool freeRecoveryActive, freeRecoveryHasTarget;
        private Vector3 freeRecoveryTarget;
        private float freeRecoveryRetryAt;
        private int freeRecoveryGeometry;
        private Bounds[] freeRecoverySolids;
        private readonly HashSet<int> freeRecoveryAllowed = new HashSet<int>();
        private readonly HashSet<int> freeRecoveryRemaining = new HashSet<int>();
        private readonly HashSet<int> freeRecoveryOverlaps = new HashSet<int>();
        private static readonly float[] RecoveryDistances = { .5f, 1f, 2f, 4f, 8f, 12f, 16f };
        private static readonly float[] RecoveryAngles = { 0f, 15f, -15f, 30f, -30f, 60f, -60f, 90f, -90f, 135f, -135f, 180f };

        private void FinishWanderTurn()
        {
            freeRecoveryActive = !FreeRecoveryRestPose(transform.position, transform.rotation);
            freeRecoveryHasTarget = false;
            freeRecoveryRetryAt = 0f;
            if (freeRecoveryActive)
            {
                interactionSettings.CollectStaticOverlaps(new Vector2(transform.position.x, transform.position.z), CoreForward, interactionBody, freeRecoveryAllowed);
                freeRecoveryGeometry = TerrainTreeObstacleRegistry.GeometryRevision;
                freeRecoverySolids = (Bounds[])interactionSettings.solidObstacles.Clone();
                hasCuriosityPath = false;
                CurrentState = DinosaurState.RandomWalk;
            }
            else if (!PlanCuriosityPath(freeGoal, true)) FreeIdle(2f);
            TraceFreeRoamTurn(freeRecoveryActive ? "Recovery after turn" : "Replanned after turn");
        }

        private bool FreeRecoveryRestPose(Vector3 position, Quaternion rotation)
        {
            Vector3 f = rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
            return interactionSettings.PoseBlockReason(new Vector2(position.x, position.z), new Vector2(f.x, f.z), interactionBody) == null
                && NavMesh.SamplePosition(position, out var hit, .25f, NavigationFilter)
                && FlatDistance(position, hit.position) <= .12f && NavigationRestPoint(hit.position);
        }

        private float FreeRecoveryBoundary(Vector3 position, Quaternion rotation)
        {
            Vector3 f = rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
            Vector2 forward = new Vector2(f.x, f.z), right = new Vector2(f.z, -f.x);
            Rect area = interactionSettings.activityBoundsOverride ?? WorldBounds.DinosaurActivityXZ;
            float outside = 0f;
            foreach (var band in interactionBody.bands)
            {
                Bounds b = band.bounds;
                Vector2 center = new Vector2(position.x, position.z) + right * b.center.x + forward * b.center.z;
                Vector2 extent = new Vector2(Mathf.Abs(right.x) * b.extents.x + Mathf.Abs(forward.x) * b.extents.z,
                    Mathf.Abs(right.y) * b.extents.x + Mathf.Abs(forward.y) * b.extents.z);
                outside = Mathf.Max(outside, area.xMin - center.x + extent.x, center.x + extent.x - area.xMax,
                    area.yMin - center.y + extent.y, center.y + extent.y - area.yMax);
            }
            return outside;
        }

        private float FreeRecoveryNavDistance(Vector3 position)
            => NavMesh.SamplePosition(position, out var hit, 32f, NavigationFilter)
                ? FlatDistance(position, hit.position) : float.PositiveInfinity;

        // A dry run owns a copy. Only accepted runtime steps retire the actual exemptions.
        private bool FreeRecoveryStep(Vector3 from, Vector3 to, Quaternion fromRotation, Quaternion toRotation, bool commit)
        {
            TerrainTreeObstacleRegistry.EnsureInitialized();
            if (TerrainTreeObstacleRegistry.GeometryRevision != freeRecoveryGeometry) return false;
            // Indices must still identify the same rocks; changed geometry never grants new exemptions.
            foreach (int id in freeRecoveryAllowed)
                if (id < 0 && (-id > interactionSettings.solidObstacles.Length
                    || interactionSettings.solidObstacles[-id - 1] != freeRecoverySolids[-id - 1])) return false;
            freeRecoveryRemaining.Clear();
            freeRecoveryRemaining.UnionWith(freeRecoveryAllowed);
            float boundary = FreeRecoveryBoundary(from, fromRotation), offMesh = FreeRecoveryNavDistance(from);
            if (float.IsPositiveInfinity(offMesh)) return false;
            int samples = Mathf.Max(1, Mathf.CeilToInt(FlatDistance(from, to) / .1f),
                Mathf.CeilToInt(Quaternion.Angle(fromRotation, toRotation) / 2f));
            Vector3 previous = from;
            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                Vector3 p = Vector3.Lerp(from, to, t);
                Quaternion q = Quaternion.Slerp(fromRotation, toRotation, t);
                Vector3 f = q * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
                interactionSettings.CollectStaticOverlaps(new Vector2(p.x, p.z), new Vector2(f.x, f.z), interactionBody, freeRecoveryOverlaps);
                if (!freeRecoveryOverlaps.IsSubsetOf(freeRecoveryRemaining)) return false;
                freeRecoveryRemaining.IntersectWith(freeRecoveryOverlaps);
                float nextBoundary = FreeRecoveryBoundary(p, q), nextOffMesh = FreeRecoveryNavDistance(p);
                if (!FreeRecoveryDistancesAllowed(boundary, offMesh, nextBoundary, nextOffMesh)) return false;
                if (offMesh <= .12f && !NavigationStepFits(previous, p)) return false;
                if (!FrontAnimationPoseSafe(previous, p, q, q, interactionBody)
                    || FindBlockingStep(new Vector2(previous.x, previous.z), new Vector2(p.x, p.z)) != null
                    || !SafetySegmentAllowed(new Vector2(previous.x, previous.z), new Vector2(p.x, p.z), IsAvoidingTyrannosaurus)) return false;
                previous = p; boundary = nextBoundary; offMesh = nextOffMesh;
            }
            if (commit)
            {
                freeRecoveryAllowed.Clear();
                freeRecoveryAllowed.UnionWith(freeRecoveryRemaining);
            }
            return true;
        }

        private bool TryFreeRecoveryTarget(Vector3 target)
        {
            target.y = MovementHeight;
            Vector3 delta = target - transform.position;
            if (delta.sqrMagnitude < .01f || delta.sqrMagnitude > 30f * 30f) return false;
            Quaternion rotation = Quaternion.LookRotation(delta) * Quaternion.Euler(0f, -VisualYaw, 0f);
            if (!FreeRecoveryRestPose(target, rotation)) return false;
            // Validate the rotation and translation together so an obstacle exited by the
            // rotation cannot be entered again during translation.
            var saved = new HashSet<int>(freeRecoveryAllowed);
            bool fits = FreeRecoveryStep(transform.position, transform.position, transform.rotation, rotation, true)
                && FreeRecoveryStep(transform.position, target, rotation, rotation, false);
            freeRecoveryAllowed.Clear(); freeRecoveryAllowed.UnionWith(saved);
            if (!fits) return false;
            freeRecoveryTarget = target; freeRecoveryHasTarget = true;
            return true;
        }

        private bool PlanFreeRecovery()
        {
            if (TryFreeRecoveryTarget(freeGoal)) return true;
            Vector3 forward = GetVisualForward();
            foreach (float distance in RecoveryDistances)
                foreach (float angle in RecoveryAngles)
                    if (TryFreeRecoveryTarget(transform.position + Quaternion.Euler(0f, angle, 0f) * forward * distance)) return true;
            return false;
        }

        private void UpdateFreeRecovery()
        {
            actualInteractionSpeed = 0f;
            if (FreeRecoveryRestPose(transform.position, transform.rotation))
            {
                freeRecoveryActive = freeRecoveryHasTarget = false;
                freeRecoveryAllowed.Clear();
                RoamingBlockReason = "";
                if (PlanCuriosityPath(freeGoal, true)) CurrentState = DinosaurState.RandomWalk;
                else FreeIdle(2f);
                return;
            }
            if (!freeRecoveryHasTarget)
            {
                if (Time.time < freeRecoveryRetryAt) { PlayClip(idleClip); return; }
                if (!PlanFreeRecovery()) { WaitForFreeRecovery(); return; }
            }
            Vector3 delta = freeRecoveryTarget - transform.position; delta.y = 0f;
            float angle = Vector3.SignedAngle(GetVisualForward(), delta, Vector3.up);
            if (HasFootPhaseData && Mathf.Abs(angle) >= 40f)
            {
                // Authored turns remain performances even during recovery. Their new
                // endpoint starts a fresh recovery snapshot, just like any wander turn.
                if (!TryBeginSteppingTurn(angle, TurnUse.Wander)) WaitForFreeRecovery();
                return;
            }
            Quaternion desired = Quaternion.LookRotation(delta) * Quaternion.Euler(0f, -VisualYaw, 0f);
            Quaternion rotation = Quaternion.RotateTowards(transform.rotation, desired,
                (gaitProfile != null ? gaitProfile.turnSpeed : 65f) * Time.deltaTime);
            // Align before translating, keeping the executed connector on its checked line.
            bool aligned = Quaternion.Angle(rotation, desired) < .01f;
            Vector3 position = aligned ? Vector3.MoveTowards(transform.position, freeRecoveryTarget, WalkSpeed * Time.deltaTime) : transform.position;
            if (!FreeRecoveryStep(transform.position, position, transform.rotation, rotation, true)) { WaitForFreeRecovery(); return; }
            float distance = FlatDistance(transform.position, position);
            bool turned = Quaternion.Angle(transform.rotation, rotation) > .0001f;
            transform.SetPositionAndRotation(position, rotation);
            interactionMoved |= distance > .00001f;
            actualInteractionSpeed = distance / Mathf.Max(.00001f, Time.deltaTime);
            if (distance > .00001f || gaitProfile != null && turned) PlayMovementClip(WalkSpeed); else PlayClip(idleClip);
            if (FlatDistance(position, freeRecoveryTarget) < .001f) freeRecoveryHasTarget = false;
        }

        private void WaitForFreeRecovery()
        {
            freeRecoveryHasTarget = false;
            freeRecoveryRetryAt = Time.time + 2f;
            RoamingBlockReason = "Waiting for safe recovery exit";
            FreeIdle(2f); // Does not clear the recovery snapshot or retired exemptions.
        }
    }
}
