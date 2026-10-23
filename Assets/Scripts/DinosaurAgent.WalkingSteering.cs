using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        // Walking cadence and steering are independent. Only the rex has authored turns.
        private bool UsesWalkingSteering => groundObstacleAware && GroundSpecies != DinosaurSpecies.Tyrannosaurus;
        private float walkingSpeed, walkingConnectorRetry;
        private bool walkingRecovery;
        private Vector3 walkingConnectorGoal;
        private float SteeringSpeed => walkingSpeed > 0f ? walkingSpeed : WalkSpeed;
        private bool MoveContinuousPath(float deltaTime) => MovePredictedPath(deltaTime, WalkSpeed);

        private bool MovePredictedTowards(Vector3 destination, float speed, bool recovery = false)
        {
            if (interactionSettings == null) interactionSettings = FindObjectOfType<DinosaurInteractionSettings>();
            if (interactionBody == null) interactionBody = coreBody;
            if (curiosityPath == null) curiosityPath = new NavMeshPath();
            if (interactionSettings == null || !interactionSettings.NavigationReady || interactionBody == null) return false;
            if (FlatDistance(transform.position, destination) <= .08f) return false;
            if (FlatDistance(walkingConnectorGoal, destination) > .5f)
            { hasCuriosityPath = false; walkingConnectorRetry = 0f; }
            if (Time.time >= walkingConnectorRetry && (!hasCuriosityPath || PathFinished()
                || FlatDistance(walkingConnectorGoal, destination) > .5f || curiosityBlockedTime >= .5f))
            {
                walkingConnectorGoal = destination;
                walkingConnectorRetry = Time.time + .5f;
                if (recovery)
                {
                    curiosityCorners = new[] { destination };
                    curiosityCorner = 0; hasCuriosityPath = true;
                }
                else if (!PlanCuriosityPath(destination, true, true)) return false;
                continuousProbeRemaining = 0f;
            }
            walkingRecovery = recovery;
            try { return hasCuriosityPath && !PathFinished() && MovePredictedPath(Time.deltaTime, speed); }
            finally { walkingRecovery = false; }
        }
        private bool MovePredictedCorridor(float speed)
        {
            float lookAhead = Mathf.Min(walkRemaining, Mathf.Max(3f, continuousLookAheadDistance * 2f));
            Vector2 point = MovementCorridor.PointAt(routeIndex, routeArc + lookAhead);
            waitingAtCrossing = !MovementCorridorCrossings.CanAdvance(this, peers, point);
            if (waitingAtCrossing) { PlayClip(idleClip); return false; }
            Vector3 before = transform.position;
            if (!MovePredictedTowards(new Vector3(point.x, MovementHeight, point.y), speed))
            {
                PlayClip(idleClip); blockedTime += Time.deltaTime;
                return HandleBlockedEscalation();
            }
            MovementCorridor.ProjectToCorridor(routeIndex, new Vector2(transform.position.x, transform.position.z), out routeArc, out _);
            walkRemaining -= FlatDistance(before, transform.position);
            blockedTime = 0f; PlayMovementClip(speed);
            return false;
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
            float yawRate = peer.UsesWalkingSteering ? peer.continuousYawRate : peer.continuousObservedYawRate;
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
                    > interactionBody.BroadRadius + other.BroadRadius + SteeringSpeed * endTime + peerSpeed * endTime + 1f) continue;
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
            int steps = Mathf.Max(1, Mathf.CeilToInt(duration / .2f), Mathf.CeilToInt(duration * SteeringSpeed / .15f));
            float dt = duration / steps;
            for (int i = 0; i < steps; i++)
            {
                float nextRate = Mathf.MoveTowards(rate, targetRate, Mathf.Max(1f, continuousYawAcceleration) * dt);
                float angle = (rate + nextRate) * .5f * dt;
                Quaternion nextRotation = rotation * Quaternion.Euler(0f, angle, 0f);
                Vector3 heading = rotation * Quaternion.Euler(0f, angle * .5f + VisualYaw, 0f) * Vector3.forward;
                float half = angle * Mathf.Deg2Rad * .5f;
                float chord = Mathf.Abs(half) < .00001f ? 1f : Mathf.Sin(half) / half;
                Vector3 next = end + heading * (SteeringSpeed * dt * chord);
                // Future sweeps use the peer's future pose, not a second obstacle
                // frozen at its current pose. Actual frame sweeps remain mandatory.
                if (!(walkingRecovery ? FreeRecoveryStep(end, next, rotation, nextRotation, false)
                    : StepPoseSafeCore(end, next, rotation, nextRotation, false, duration <= .2f))
                    || !ContinuousPeerPredictionSafe(end, next, rotation, nextRotation, i * dt, (i + 1) * dt)) return false;
                end = next; rotation = nextRotation; rate = nextRate;
            }
            return true;
        }
        private bool MovePredictedPath(float deltaTime, float speed)
        {
            if (Mathf.Abs(walkingSpeed - speed) > .001f) continuousProbeRemaining = 0f;
            walkingSpeed = speed;
            actualInteractionSpeed = 0f;
            if (speed <= 0f || deltaTime <= 0f || !hasCuriosityPath || PathFinished()) return false;
            Vector3 target = ContinuousLookAhead();
            continuousProbeRemaining -= deltaTime;
            if ((target - continuousProbeTarget).sqrMagnitude > .25f) continuousProbeRemaining = 0f;
            if (continuousProbeRemaining <= 0f)
            {
                continuousProbeRemaining = .25f; continuousProbeTarget = target; continuousArcBlocked = false;
                float maximum = ContinuousMaximumYawRate;
                Vector3 desired = target - transform.position; desired.y = 0f;
                float angle = Vector3.SignedAngle(GetVisualForward(), desired, Vector3.up) * Mathf.Deg2Rad;
                float preferred = Mathf.Clamp(2f * SteeringSpeed * Mathf.Sin(angle) / Mathf.Max(.5f, desired.magnitude) * Mathf.Rad2Deg, -maximum, maximum);
                float best = float.PositiveInfinity;
                bool passing = false;
                float duration = Mathf.Max(3f, continuousProbeDistance) / Mathf.Max(.1f, SteeringSpeed);
                foreach (var peer in peers)
                {
                    if (!IsGroundTrafficPeer(peer)) continue;
                    float turnRadius = SteeringSpeed / Mathf.Max(.001f, maximum * Mathf.Deg2Rad);
                    float passingDistance = collisionRadius + peer.collisionRadius + turnRadius;
                    if (FlatDistance(transform.position, peer.transform.position) < passingDistance * 3f)
                        duration = Mathf.Max(duration, Mathf.Min(passingDistance, Mathf.Max(3f, continuousProbeDistance) * 2f) / Mathf.Max(.1f, SteeringSpeed));
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
            float distance = FlatDistance(transform.position, position);
            if (distance <= .00001f) return false;
            if (walkingRecovery && !FreeRecoveryStep(transform.position, position, transform.rotation, nextRotation, true)) return false;
            transform.SetPositionAndRotation(position, nextRotation); continuousYawRate = nextRate;
            actualInteractionSpeed = distance / deltaTime; interactionMoved = true; curiosityBlockedTime = 0f;
            RecordFreeRoamWalk(distance);
            return true;
        }
    }
}
