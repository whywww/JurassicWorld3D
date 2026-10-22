using UnityEngine;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        private bool IsContinuousWalker => species == DinosaurSpecies.Baurusuchus;
        private float continuousProbeRemaining, continuousYawRate, continuousGoalRetry;
        private Vector3 continuousProbeTarget;
        private bool continuousArcBlocked;

        private bool UpdateContinuousRoaming(float deltaTime)
        {
            CurrentState = DinosaurState.RandomWalk;
            stateTimer = 0f;
            continuousGoalRetry -= deltaTime;
            bool finished = !hasCuriosityPath || PathFinished();
            if (continuousGoalRetry <= 0f && (finished || FlatDistance(transform.position, freeGoal) < 3f))
            {
                // A failed prefetch must not discard the path the animal is still walking.
                var savedCorners = curiosityCorners; int savedCorner = curiosityCorner;
                bool savedPath = hasCuriosityPath; Vector3 savedGoal = freeGoal;
                if (!PickFreeGoalCore(false))
                {
                    curiosityCorners = savedCorners; curiosityCorner = savedCorner;
                    hasCuriosityPath = savedPath; freeGoal = savedGoal;
                    continuousGoalRetry = .25f;
                }
                else continuousProbeRemaining = 0f;
            }
            bool moved = hasCuriosityPath && !PathFinished() && MoveContinuousPath(deltaTime);
            if (moved) { PlayMovementClip(WalkSpeed); RoamingBlockReason = ""; }
            else
            {
                // Only a geometric/safety obstruction can cause a wait; never a rest timer.
                PlayClip(idleClip);
                RoamingBlockReason = "Waiting for safe walking arc";
                if (continuousGoalRetry <= 0f)
                {
                    var savedCorners = curiosityCorners; int savedCorner = curiosityCorner;
                    bool savedPath = hasCuriosityPath; Vector3 savedGoal = freeGoal;
                    if (!PickFreeGoalCore(false))
                    { curiosityCorners = savedCorners; curiosityCorner = savedCorner; hasCuriosityPath = savedPath; freeGoal = savedGoal; }
                    continuousGoalRetry = .25f; continuousProbeRemaining = 0f;
                }
            }
            return true;
        }
        private Vector3 ContinuousLookAhead()
        {
            Vector3 previous = transform.position;
            float remaining = 2.5f;
            for (int i = curiosityCorner; i < curiosityCorners.Length; i++)
            {
                Vector3 next = curiosityCorners[i]; next.y = MovementHeight;
                float distance = FlatDistance(previous, next);
                if (distance >= remaining) return Vector3.MoveTowards(previous, next, remaining);
                remaining -= distance; previous = next;
            }
            return previous;
        }
        private bool WalkingArcSafe(float yawRate, float duration, out Vector3 end, out Quaternion rotation)
        {
            end = transform.position; rotation = transform.rotation;
            int steps = Mathf.Max(1, Mathf.CeilToInt(duration / .2f));
            float dt = duration / steps;
            for (int i = 0; i < steps; i++)
            {
                Quaternion nextRotation = rotation * Quaternion.Euler(0f, yawRate * dt, 0f);
                Vector3 next = end + nextRotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward * (WalkSpeed * dt);
                if (!StepPoseSafe(end, next, rotation, nextRotation, false)) return false;
                end = next; rotation = nextRotation;
            }
            return true;
        }
        private bool MoveContinuousPath(float deltaTime)
        {
            if (!hasCuriosityPath || PathFinished()) return false;
            Vector3 target = ContinuousLookAhead();
            continuousProbeRemaining -= deltaTime;
            if ((target - continuousProbeTarget).sqrMagnitude > .25f) continuousProbeRemaining = 0f;
            if (continuousProbeRemaining <= 0f)
            {
                continuousProbeRemaining = .25f; continuousProbeTarget = target; continuousArcBlocked = false;
                float maximum = gaitProfile != null ? gaitProfile.turnSpeed : 2f;
                Vector3 desired = target - transform.position; desired.y = 0f;
                float preferred = Mathf.Clamp(Vector3.SignedAngle(GetVisualForward(), desired, Vector3.up), -maximum, maximum);
                float best = float.PositiveInfinity;
                foreach (float rate in new[] { preferred, 0f, maximum, -maximum, maximum * .5f, -maximum * .5f })
                {
                    if (!WalkingArcSafe(rate, 2f, out var end, out var rotation)) continue;
                    Vector3 forward = rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
                    float score = FlatDistance(end, target) + Vector3.Angle(forward, target - end) * .015f;
                    if (score >= best) continue;
                    best = score; continuousYawRate = rate;
                }
                if (float.IsPositiveInfinity(best)) { continuousArcBlocked = true; curiosityBlockedTime += deltaTime; return false; }
            }
            if (continuousArcBlocked) { curiosityBlockedTime += deltaTime; return false; }
            // Recheck the actual step every frame; a moving peer can invalidate a cached arc.
            if (!WalkingArcSafe(continuousYawRate, deltaTime, out var position, out var nextRotation))
            { continuousProbeRemaining = 0f; curiosityBlockedTime += deltaTime; return false; }
            transform.SetPositionAndRotation(position, nextRotation);
            actualInteractionSpeed = WalkSpeed; interactionMoved = true; curiosityBlockedTime = 0f;
            RecordFreeRoamWalk(WalkSpeed * deltaTime);
            return true;
        }
    }
}
