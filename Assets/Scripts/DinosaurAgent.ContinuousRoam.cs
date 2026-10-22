using UnityEngine;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        private bool IsContinuousWalker => IsContinuousRoaming;
        [Header("Continuous free roaming")]
        [SerializeField, Min(.5f), InspectorName("行走前视距离（米）")] private float continuousLookAheadDistance = 3f;
        [SerializeField, Min(.5f), InspectorName("最小转弯半径（米）")] private float continuousMinTurnRadius = 2.5f;
        [SerializeField, Min(3f), InspectorName("提前绕障距离（米）")] private float continuousProbeDistance = 3f;
        [SerializeField, Min(1f), InspectorName("转速变化上限（度/秒²）")] private float continuousYawAcceleration = 30f;
        private float continuousProbeRemaining, continuousYawRate, continuousTargetYawRate, continuousGoalRetry;
        private Vector3 continuousProbeTarget;
        private bool continuousArcBlocked;
        public string ContinuousRoamingStatus { get; private set; } = "Inactive";

        private void ResetContinuousSteering()
        {
            continuousProbeRemaining = continuousYawRate = continuousTargetYawRate = continuousGoalRetry = 0f;
            continuousArcBlocked = false;
        }
        private float ContinuousMaximumYawRate => Mathf.Min(gaitProfile != null ? gaitProfile.turnSpeed : 45f,
            WalkSpeed / Mathf.Max(.5f, continuousMinTurnRadius) * Mathf.Rad2Deg);

        private float ContinuousPathRemaining()
        {
            float distance = 0f; Vector3 previous = transform.position;
            for (int i = curiosityCorner; i < curiosityCorners.Length; i++)
            { distance += FlatDistance(previous, curiosityCorners[i]); previous = curiosityCorners[i]; }
            return distance;
        }
        private bool ContinuousGoalHasExit(Vector3[] corners)
        {
            if (corners.Length < 2) return false;
            Vector3 end = corners[corners.Length - 1];
            Vector3 forward = end - corners[corners.Length - 2]; forward.y = 0f;
            if (forward.sqrMagnitude < .001f) return false;
            Quaternion rotation = Quaternion.LookRotation(forward) * Quaternion.Euler(0f, -VisualYaw, 0f);
            Vector3 next = end + forward.normalized * Mathf.Max(3f, continuousLookAheadDistance);
            return StepPoseSafe(end, next, rotation, rotation, false);
        }
        private bool TryContinueGoal()
        {
            var corners = curiosityCorners; int corner = curiosityCorner;
            bool path = hasCuriosityPath; Vector3 goal = freeGoal;
            if (PickFreeGoalCore(false)) { continuousProbeRemaining = 0f; freeRetryGoal = false; return true; }
            curiosityCorners = corners; curiosityCorner = corner; hasCuriosityPath = path; freeGoal = goal;
            return false;
        }
        private bool UpdateContinuousRoaming(float deltaTime)
        {
            CurrentState = DinosaurState.RandomWalk;
            stateTimer = 0f;
            continuousGoalRetry -= deltaTime;
            // Spawning already validated a short forward exit; use it before
            // searching farther through the forest for a new roaming goal.
            if (freeRetryGoal && !hasCuriosityPath)
            {
                freeRetryGoal = false;
                if (PlanCuriosityPath(freeGoal, true)) continuousProbeRemaining = 0f;
            }
            bool finished = !hasCuriosityPath || PathFinished();
            if (continuousGoalRetry <= 0f && (finished || ContinuousPathRemaining() < continuousLookAheadDistance))
            {
                TryContinueGoal();
                continuousGoalRetry = .25f;
            }
            bool moved = hasCuriosityPath && !PathFinished() && MoveContinuousPath(deltaTime);
            if (moved)
            {
                PlayMovementClip(WalkSpeed); RoamingBlockReason = "";
                ContinuousRoamingStatus = Mathf.Abs(continuousYawRate) > .1f ? "Walking curve" : "Walking";
            }
            else
            {
                PlayClip(idleClip);
                ContinuousRoamingStatus = "Waiting for safe walking arc";
                RoamingBlockReason = ContinuousRoamingStatus;
                if (continuousGoalRetry <= 0f)
                { TryContinueGoal(); continuousGoalRetry = .25f; continuousProbeRemaining = 0f; }
            }
            return true;
        }
        private void AdvanceContinuousCorners()
        {
            // A rounded bend need not touch its original polygon vertex. Advance only
            // after crossing the outgoing segment's start and checking the connector.
            while (hasCuriosityPath && curiosityCorner + 1 < curiosityCorners.Length)
            {
                Vector3 corner = curiosityCorners[curiosityCorner], next = curiosityCorners[curiosityCorner + 1];
                Vector3 outgoing = next - corner; outgoing.y = 0f;
                Vector3 offset = transform.position - corner; offset.y = 0f;
                if (Vector3.Dot(offset, outgoing) <= .0001f || !NavigationStepFits(transform.position, next)) break;
                curiosityCorner++;
            }
        }
        private Vector3 ContinuousLookAhead()
        {
            Vector3 previous = transform.position;
            float remaining = Mathf.Max(.5f, continuousLookAheadDistance);
            for (int i = curiosityCorner; i < curiosityCorners.Length; i++)
            {
                Vector3 next = curiosityCorners[i]; next.y = MovementHeight;
                float distance = FlatDistance(previous, next);
                if (distance >= remaining) return Vector3.MoveTowards(previous, next, remaining);
                remaining -= distance; previous = next;
            }
            return previous;
        }
        private Vector3 continuousObservedPosition;
        private Quaternion continuousObservedRotation;
        private bool continuousObservationStarted, continuousMotionObserved;
        private float continuousObservedSpeed, continuousObservedYawRate;
        private void ObserveContinuousPeerMotion(float deltaTime)
        {
            if (deltaTime <= 0f) return;
            if (continuousObservationStarted)
            {
                continuousObservedSpeed = FlatDistance(transform.position, continuousObservedPosition) / deltaTime;
                continuousObservedYawRate = Mathf.DeltaAngle(continuousObservedRotation.eulerAngles.y, transform.eulerAngles.y) / deltaTime;
                continuousMotionObserved = true;
            }
            continuousObservedPosition = transform.position; continuousObservedRotation = transform.rotation;
            continuousObservationStarted = true;
        }
        private float ContinuousPeerSpeed(DinosaurAgent peer) => peer.CurrentState == DinosaurState.Idle || Application.isPlaying && !peer.isActiveAndEnabled ? 0f
            : peer.continuousMotionObserved ? peer.continuousObservedSpeed : peer.actualInteractionSpeed;
        private void PredictedContinuousPeerPose(DinosaurAgent peer, float seconds, out Vector3 position, out Quaternion rotation)
        {
            float speed = ContinuousPeerSpeed(peer);
            if (peer.trafficMoving && peer.trafficReverse) speed = -speed;
            float yawRate = peer.IsContinuousWalker ? peer.continuousYawRate : peer.continuousObservedYawRate;
            float angle = yawRate * seconds;
            rotation = peer.transform.rotation * Quaternion.Euler(0f, angle, 0f);
            Vector3 heading = peer.transform.rotation * Quaternion.Euler(0f, angle * .5f + peer.VisualYaw, 0f) * Vector3.forward;
            float half = angle * Mathf.Deg2Rad * .5f;
            float chord = Mathf.Abs(half) < .00001f ? 1f : Mathf.Sin(half) / half;
            position = peer.transform.position + heading * (speed * seconds * chord);
        }
        private bool ContinuousPeerPredictionSafe(Vector3 from, Vector3 to, Quaternion fromRotation, Quaternion toRotation, float startTime, float endTime)
        {
            foreach (var peer in peers)
            {
                if (!IsGroundTrafficPeer(peer)) continue;
                float peerSpeed = ContinuousPeerSpeed(peer);
                var other = peer.interactionBody != null ? peer.interactionBody : peer.coreBody;
                if (other == null || FlatDistance(from, peer.transform.position)
                    > interactionBody.BroadRadius + other.BroadRadius + WalkSpeed * endTime + peerSpeed * endTime + 1f) continue;
                PredictedContinuousPeerPose(peer, startTime, out var peerFrom, out var peerFromRotation);
                PredictedContinuousPeerPose(peer, endTime, out var peerTo, out var peerToRotation);
                Vector2 relativeStart = new Vector2(from.x - peerFrom.x, from.z - peerFrom.z);
                Vector2 relativeEnd = new Vector2(to.x - peerTo.x, to.z - peerTo.z);
                Vector2 relativeStep = relativeEnd - relativeStart;
                float closest = relativeStep.sqrMagnitude > 1e-10f
                    ? Mathf.Clamp01(-Vector2.Dot(relativeStart, relativeStep) / relativeStep.sqrMagnitude) : 0f;
                float radius = collisionRadius + peer.collisionRadius;
                if ((relativeStart + relativeStep * closest).sqrMagnitude + .00001f < Mathf.Min(relativeStart.sqrMagnitude, radius * radius))
                { LastMovementBlock = "Predicted peer"; return false; }
                foreach (var a in interactionBody.bands) foreach (var b in other.bands)
                {
                    float before = BandPenetration(from, fromRotation, a, peerFrom, peerFromRotation, peer.VisualYaw, peer.MovementHeight, b);
                    float after = BandPenetration(to, toRotation, a, peerTo, peerToRotation, peer.VisualYaw, peer.MovementHeight, b);
                    if (after > before + .00001f) return false;
                }
            }
            return true;
        }
        private bool WalkingArcSafe(float yawRate, float duration, out Vector3 end, out Quaternion rotation)
            => SimulateWalkingArc(yawRate, duration, out end, out rotation, out _);

        private bool SimulateWalkingArc(float targetRate, float duration, out Vector3 end, out Quaternion rotation, out float rate)
        {
            end = transform.position; rotation = transform.rotation; rate = continuousYawRate;
            int steps = Mathf.Max(1, Mathf.CeilToInt(duration / .2f), Mathf.CeilToInt(duration * WalkSpeed / .15f));
            float dt = duration / steps;
            for (int i = 0; i < steps; i++)
            {
                float nextRate = Mathf.MoveTowards(rate, targetRate, Mathf.Max(1f, continuousYawAcceleration) * dt);
                float angle = (rate + nextRate) * .5f * dt;
                Quaternion nextRotation = rotation * Quaternion.Euler(0f, angle, 0f);
                Vector3 heading = rotation * Quaternion.Euler(0f, angle * .5f + VisualYaw, 0f) * Vector3.forward;
                float half = angle * Mathf.Deg2Rad * .5f;
                float chord = Mathf.Abs(half) < .00001f ? 1f : Mathf.Sin(half) / half;
                Vector3 next = end + heading * (WalkSpeed * dt * chord);
                // Future sweeps use the peer's future pose, not a second obstacle
                // frozen at its current pose. Actual frame sweeps remain mandatory.
                if (!StepPoseSafeCore(end, next, rotation, nextRotation, false, duration <= .2f)
                    || !ContinuousPeerPredictionSafe(end, next, rotation, nextRotation, i * dt, (i + 1) * dt)) return false;
                end = next; rotation = nextRotation; rate = nextRate;
            }
            return true;
        }
        private bool MoveContinuousPath(float deltaTime)
        {
            actualInteractionSpeed = 0f;
            if (deltaTime <= 0f || !hasCuriosityPath || PathFinished()) return false;
            Vector3 target = ContinuousLookAhead();
            continuousProbeRemaining -= deltaTime;
            if ((target - continuousProbeTarget).sqrMagnitude > .25f) continuousProbeRemaining = 0f;
            if (continuousProbeRemaining <= 0f)
            {
                continuousProbeRemaining = .25f; continuousProbeTarget = target; continuousArcBlocked = false;
                float maximum = ContinuousMaximumYawRate;
                Vector3 desired = target - transform.position; desired.y = 0f;
                float angle = Vector3.SignedAngle(GetVisualForward(), desired, Vector3.up) * Mathf.Deg2Rad;
                float preferred = Mathf.Clamp(2f * WalkSpeed * Mathf.Sin(angle) / Mathf.Max(.5f, desired.magnitude) * Mathf.Rad2Deg, -maximum, maximum);
                float best = float.PositiveInfinity;
                bool passing = false;
                float duration = Mathf.Max(3f, continuousProbeDistance) / Mathf.Max(.1f, WalkSpeed);
                foreach (var peer in peers)
                {
                    if (!IsGroundTrafficPeer(peer)) continue;
                    float turnRadius = WalkSpeed / Mathf.Max(.001f, maximum * Mathf.Deg2Rad);
                    float passingDistance = collisionRadius + peer.collisionRadius + turnRadius;
                    if (FlatDistance(transform.position, peer.transform.position) < passingDistance * 3f)
                        duration = Mathf.Max(duration, passingDistance / Mathf.Max(.1f, WalkSpeed));
                    Vector3 offset = peer.transform.position - transform.position; offset.y = 0f;
                    Vector3 heading = GetVisualForward();
                    Vector3 peerHeading = peer.GetVisualForward();
                    // Both oncoming walkers choose their own right. Hold the side
                    // lane until the peer passes rather than steering back early.
                    if (Vector3.Dot(heading, peerHeading) < -.5f && Vector3.Dot(offset, heading) > 0f
                        && offset.magnitude < passingDistance * 2f)
                    {
                        float lateral = Mathf.Abs(Vector3.Dot(offset, Vector3.Cross(Vector3.up, heading)));
                        preferred = lateral < collisionRadius + peer.collisionRadius + .75f ? maximum : 0f;
                        passing = true;
                    }
                }
                foreach (float candidate in new[] { preferred, 0f, maximum, -maximum, maximum * .5f, -maximum * .5f })
                {
                    if (!WalkingArcSafe(candidate, duration, out var end, out var rotation)) continue;
                    Vector3 forward = rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
                    float score = FlatDistance(end, target) + Vector3.Angle(forward, target - end) * .02f;
                    if (passing) score += Mathf.Abs(candidate - preferred) * 2f;
                    if (score >= best) continue;
                    best = score; continuousTargetYawRate = candidate;
                }
                if (float.IsPositiveInfinity(best)) { continuousArcBlocked = true; curiosityBlockedTime += deltaTime; return false; }
            }
            if (continuousArcBlocked) { curiosityBlockedTime += deltaTime; return false; }
            // Moving peers can invalidate a cached preview; validate the actual sweep too.
            if (!SimulateWalkingArc(continuousTargetYawRate, deltaTime, out var position, out var nextRotation, out var nextRate))
            { continuousProbeRemaining = 0f; curiosityBlockedTime += deltaTime; return false; }
            transform.SetPositionAndRotation(position, nextRotation); continuousYawRate = nextRate;
            actualInteractionSpeed = WalkSpeed; interactionMoved = true; curiosityBlockedTime = 0f;
            RecordFreeRoamWalk(WalkSpeed * deltaTime);
            return true;
        }
    }
}
