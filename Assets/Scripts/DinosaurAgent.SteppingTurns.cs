using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        private enum TurnUse { Return, Route, Wander, FrontFollow, FrontReturn, FrontRecovery, Traffic }
        private TurnUse stepTurnUse;
        private DinosaurInteractionProfile.Motion stepTurnMotion;
        private Vector3 stepTurnOrigin;
        private Quaternion stepTurnRotation;
        private float stepTurnTime, stepTurnEnd, stepTurnBlocked;
        private AnimationState stepTurnState, stepTurnOutgoing;
        private const string TurnOutgoingClip = "SteppingTurn_Outgoing";
        private DinosaurInteractionProfile.Motion stepTurnOutgoingMotion;
        private float stepTurnBlendTime, stepTurnBlendDuration;
        private bool routeRejoining;
        private bool routeJoinRecovering;
        private float routeJoinArc;
        private Vector3 routeJoinPoint;
        public bool IsSteppingTurn => stepTurnMotion != null;
        public bool IsRouteTurnOrJoin => IsSteppingTurn && stepTurnUse == TurnUse.Route || routeRejoining;
        public bool IsRouteTurnPerformance => IsSteppingTurn && stepTurnUse == TurnUse.Route;
        public bool IsRouteStaticRecovery => routeRejoining && routeJoinRecovering;
        public bool HasFootPhaseData => interactionProfile != null && interactionProfile.Find(InteractionMotionRole.Walk)?.footFrames.Length > 1;
        public float StepTurnClipTime => stepTurnTime;
        // Cumulative sampled path length, independent of the applied transform/foot IK.
        // Acceptance uses this to distinguish authored performances from ordinary walking.
        public float AnimationRootTravel { get; private set; }
        // Limit only the actual LateUpdate foot correction, independently of the clip clock.
        private float ForestFootCorrectionBudget => Mathf.Min(2.4f, Mathf.Max(WalkSpeed, ApproachSpeed) * .8f)
            * Mathf.Min(Time.deltaTime, .05f);

        public static DinosaurInteractionProfile.FootFrame SampleFootFrame(DinosaurInteractionProfile.Motion motion, float time)
        {
            var frames = motion.footFrames;
            if (frames.Length == 0) return default;
            float index = Mathf.Clamp01(time / motion.clip.length) * (frames.Length - 1);
            int first = Mathf.FloorToInt(index), next = Mathf.Min(first + 1, frames.Length - 1);
            var result = frames[first];
            result.leftSole = Vector3.Lerp(result.leftSole, frames[next].leftSole, index - first);
            result.rightSole = Vector3.Lerp(result.rightSole, frames[next].rightSole, index - first);
            result.rootOffset = Vector2.Lerp(result.rootOffset, frames[next].rootOffset, index - first);
            result.yaw = Mathf.Lerp(result.yaw, frames[next].yaw, index - first);
            result.authoredYaw = Mathf.Lerp(result.authoredYaw, frames[next].authoredYaw, index - first);
            result.playerBodyBands = PlayerPoseBands(motion, time);
            return result;
        }
        private static DinosaurCoreBody.Band[] PlayerPoseBands(DinosaurInteractionProfile.Motion motion, float time)
        {
            var poses = motion.playerPoses;
            if (poses == null || poses.Length == 0) return null;
            int first = 0, last = poses.Length - 1;
            while (first < last) { int mid = (first + last + 1) / 2; if (poses[mid].time <= time) first = mid; else last = mid - 1; }
            return poses[first].bands;
        }

        public void GetFootPhase(out float left, out float right)
        {
            left = right = 0f;
            if (!HasFootPhaseData || legacyAnimation == null) return;
            if (frontWaitPlaying && legacyAnimation[WaitClip] != null && legacyAnimation[WaitClip].enabled)
            {
                var look = interactionProfile.Find(InteractionMotionRole.LookForward);
                var frame = SampleFootFrame(look, Mathf.Repeat(frontWaitTime, look.clip.length));
                float weight = legacyAnimation[WaitClip].weight;
                if (frame.leftContact) left += weight;
                if (frame.rightContact) right += weight;
            }
            foreach (var motion in interactionProfile.motions)
            {
                if (motion.footFrames.Length < 2) continue;
                var state = legacyAnimation[motion.StateName];
                if (state == null || !state.enabled || state.weight < .01f) state = legacyAnimation[motion.sourceName];
                if (state == null || !state.enabled || state.weight < .01f) continue;
                float time = state.wrapMode == WrapMode.Loop ? Mathf.Repeat(state.time, state.length) : Mathf.Clamp(state.time, 0f, state.length);
                var frame = SampleFootFrame(motion, time);
                if (frame.leftContact) left += state.weight;
                if (frame.rightContact) right += state.weight;
            }
            // A repeated turn keeps its previous pose in a separate animation state.
            if (stepTurnOutgoingMotion != null && stepTurnOutgoing != null
                && stepTurnOutgoing.name == TurnOutgoingClip && stepTurnOutgoing.enabled)
            {
                var frame = SampleFootFrame(stepTurnOutgoingMotion, stepTurnOutgoing.time);
                if (frame.leftContact) left += stepTurnOutgoing.weight;
                if (frame.rightContact) right += stepTurnOutgoing.weight;
            }
        }

        private void TurnPoseAt(Vector3 origin, Quaternion baseRotation, DinosaurInteractionProfile.FootFrame frame,
            out Vector3 position, out Quaternion rotation)
        {
            Quaternion heading = baseRotation * Quaternion.Euler(0f, VisualYaw, 0f);
            position = origin + heading * new Vector3(frame.rootOffset.x, 0f, frame.rootOffset.y);
            position.y = MovementHeight;
            rotation = Quaternion.Euler(0f, frame.yaw, 0f) * baseRotation;
        }

        private void TurnPose(DinosaurInteractionProfile.FootFrame frame, out Vector3 position, out Quaternion rotation)
            => TurnPoseAt(stepTurnOrigin, stepTurnRotation, frame, out position, out rotation);

        private bool StepPoseSafe(Vector3 from, Vector3 to, Quaternion fromRotation, Quaternion toRotation, bool constrainRoute)
        {
            if (interactionSettings == null || !interactionSettings.NavigationReady || interactionBody == null) return false;
            Vector3 f0 = fromRotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
            Vector3 f1 = toRotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
            Vector2 a = new Vector2(from.x, from.z), b = new Vector2(to.x, to.z);
            if (constrainRoute)
            {
                float routeLimit = routeRejoining ? Mathf.Max(2f, MovementCorridor.MinDistanceToRoute(a, routeIndex) + .001f) : 2f;
                if (MovementCorridor.MinDistanceToRoute(b, routeIndex) > routeLimit)
                { LastMovementBlock = "Route corridor"; return false; }
            }
            if (interactionSettings.StepBlocked(a, b, new Vector2(f0.x, f0.z), new Vector2(f1.x, f1.z), interactionBody))
            { LastMovementBlock = "Body sweep"; return false; }
            if (!SafetySegmentAllowed(a, b, IsAvoidingTyrannosaurus)) { LastMovementBlock = "Safety distance"; return false; }
            if (FindBlockingStep(a, b) != null) { LastMovementBlock = "Peer"; return false; }
            if (!GroundPeerSweepSafe(from, to, fromRotation, toRotation, interactionBody))
            { LastMovementBlock = "Peer body"; return false; }
            if (!NavigationStepFits(from, to)) { LastMovementBlock = "Navigation"; return false; }
            return true;
        }

        private bool SelectSteppingTurn(float angle, bool frontTurn, out DinosaurInteractionProfile.Motion motion, out float end)
        {
            motion = interactionProfile.Find(angle < 0f ? InteractionMotionRole.TurnLeft : InteractionMotionRole.TurnRight);
            end = 0f;
            if (motion == null || motion.footFrames.Length < 2)
            { if (frontTurn) frontBlockReason = "Turn animation missing"; return false; }
            float TurnAngle(DinosaurInteractionProfile.FootFrame frame) => Mathf.Abs(frame.yaw + frame.authoredYaw);
            end = 0f;
            foreach (var frame in motion.footFrames)
                if (frame.leftContact && frame.rightContact && TurnAngle(frame) >= 20f && TurnAngle(frame) <= Mathf.Min(90f, Mathf.Abs(angle) + 5f)) end = frame.time;
            // Front bends below 60 degrees keep walking. Larger bends may use the full authored 90,
            // then steer through the remaining small correction without alternating left/right turns.
            if (Mathf.Abs(angle) >= 85f || (frontTurn && end <= 0f && Mathf.Abs(angle) >= FrontSteppingTurnAngle)) end = motion.clip.length;
            if (end <= 0f) { if (frontTurn) frontBlockReason = "Turn segment unavailable"; return false; }
            return true;
        }

        private bool TryBeginSteppingTurn(float angle, TurnUse use)
        {
            bool frontTurn = frontActive && (use == TurnUse.FrontFollow || use == TurnUse.FrontReturn);
            if (IsSteppingTurn || !HasFootPhaseData || Mathf.Abs(angle) < 25f) return false;
            if (!SelectSteppingTurn(angle, frontTurn, out var motion, out float end)) return false;
            if (use == TurnUse.Wander)
            {
                if (!FreeRoamTurnAllowed()) return false;
                TraceFreeRoamTurn("Starting");
            }
            BeginSteppingTurn(motion, end, use);
            return true;
        }

        private void BeginSteppingTurn(DinosaurInteractionProfile.Motion motion, float end, TurnUse use)
        {
            // Route and wander turns are performances; the connecting walk has its own gates.
            stepTurnOrigin = transform.position; stepTurnRotation = transform.rotation;
            stepTurnMotion = motion; stepTurnUse = use; stepTurnTime = stepTurnBlocked = 0f; stepTurnEnd = end;
            stepTurnOutgoing = string.IsNullOrEmpty(activeClip) ? null : legacyAnimation[activeClip];
            float outgoingTime = stepTurnOutgoing != null ? stepTurnOutgoing.time : 0f;
            stepTurnState = legacyAnimation[motion.StateName];
            stepTurnOutgoingMotion = null;
            if (stepTurnOutgoing == stepTurnState)
            {
                // Never fade out the same state that is advancing the incoming turn.
                if (legacyAnimation[TurnOutgoingClip] == null)
                    legacyAnimation.AddClip(motion.clip, TurnOutgoingClip);
                else if (legacyAnimation[TurnOutgoingClip].clip != motion.clip)
                {
                    legacyAnimation.RemoveClip(TurnOutgoingClip);
                    legacyAnimation.AddClip(motion.clip, TurnOutgoingClip);
                }
                stepTurnOutgoing = legacyAnimation[TurnOutgoingClip];
                stepTurnOutgoing.wrapMode = WrapMode.ClampForever;
                stepTurnOutgoingMotion = motion;
            }
            legacyAnimation.Stop();
            if (stepTurnOutgoing != null) { stepTurnOutgoing.enabled = true; stepTurnOutgoing.time = outgoingTime; stepTurnOutgoing.speed = 0f; stepTurnOutgoing.weight = 1f; }
            stepTurnBlendTime = 0f; stepTurnBlendDuration = motion.blendDuration;
            frontWaitHoldingPose = false;
            stepTurnState.enabled = true; stepTurnState.time = 0f; stepTurnState.speed = 0f; stepTurnState.weight = stepTurnOutgoing == null ? 1f : 0f;
            stepTurnState.wrapMode = WrapMode.ClampForever;
            activeClip = motion.StateName;
            if (use != TurnUse.Route && use != TurnUse.Wander && use != TurnUse.Traffic && !frontActive) InteractionPhase = PlayerInteractionPhase.Turning;
            legacyAnimation.Sample();
        }

        private bool TickSteppingTurn()
        {
            float nextTime = Mathf.Min(stepTurnEnd, stepTurnTime + Time.deltaTime * stepTurnMotion.turnPlaybackSpeed);
            var frame = SampleFootFrame(stepTurnMotion, nextTime);
            TurnPose(frame, out var position, out var rotation);
            if (!(stepTurnUse == TurnUse.Traffic
                ? FrontWalkAnimatedStep(stepTurnMotion, stepTurnTime, nextTime, transform.position, position, transform.rotation, rotation)
                : stepTurnUse == TurnUse.Wander || stepTurnUse == TurnUse.Route || stepTurnUse == TurnUse.FrontRecovery
                || stepTurnUse == TurnUse.FrontReturn
                ? FrontAnimatedStep(stepTurnMotion, stepTurnTime, nextTime, transform.position, position, transform.rotation, rotation)
                : frontActive ? FrontWalkAnimatedStep(stepTurnMotion, stepTurnTime, nextTime, transform.position, position, transform.rotation, rotation)
                : StepPoseSafe(transform.position, position, transform.rotation, rotation, stepTurnUse == TurnUse.Route)))
            {
                stepTurnBlocked += Time.deltaTime;
                if (stepTurnUse == TurnUse.Traffic) return false;
                if (frontActive)
                {
                    if (stepTurnUse == TurnUse.FrontRecovery) { WaitFrontRecovery(); return false; }
                    GetComponent<DinosaurFootLock>()?.HoldSupportDuringRecovery(.35f);
                    BeginFrontSpaceWait();
                    return false;
                }
                if (stepTurnBlocked >= blockedReplanDelay && stepTurnUse == TurnUse.Wander)
                {
                    RecoverInterruptedTurn();
                }
                return false; // Both animation time and locomotion remain at the last safe pose.
            }
            stepTurnBlocked = 0f;
            if (stepTurnUse == TurnUse.FrontReturn) frontReturnBlockedAt = -1f;
            if (frontActive && frontEnvelope != null) frontEnvelope.bands = frontProbeBody.bands;
            AnimationRootTravel += SampledRootDistance(stepTurnMotion, stepTurnTime, nextTime);
            transform.SetPositionAndRotation(position, rotation);
            stepTurnTime = nextTime;
            stepTurnState.time = nextTime;
            stepTurnBlendTime += Time.deltaTime;
            float blend = stepTurnOutgoing == null ? 1f : Mathf.Clamp01(stepTurnBlendTime / Mathf.Max(.01f, stepTurnBlendDuration));
            stepTurnState.weight = blend;
            if (stepTurnOutgoing != null) { stepTurnOutgoing.weight = 1f - blend; stepTurnOutgoing.enabled = blend < 1f; }
            legacyAnimation.Sample();
            interactionMoved = true;
            if (nextTime < stepTurnEnd - .0001f) return false;
            TurnUse completed = stepTurnUse;
            var completedMotion = stepTurnMotion;
            stepTurnState.speed = 0f;
            stepTurnMotion = null;
            if (completed == TurnUse.Traffic)
            {
                PlayMovementClip(WalkSpeed);
                hasCuriosityPath = false; // Authored turn translated the root: reconnect from its accepted pose.
                trafficRetryAt = Time.time;
            }
            else if (completed == TurnUse.FrontRecovery)
            {
                BeginMatchedWalkBlend(completedMotion, stepTurnTime, false);
                CaptureFrontRecoveryPose();
                if (!FrontRecoveryConnector(transform.position, transform.rotation, frontRecoveryTarget)) WaitFrontRecovery();
            }
            else if (completed == TurnUse.FrontFollow || completed == TurnUse.FrontReturn)
            {
                if (frontStopPending) BeginFrontEnding();
                else if (completed == TurnUse.FrontFollow)
                {
                    if (frontExitPending) BeginFrontEnding();
                    else if (frontNearPending) BeginFrontNear();
                    else PlayFrontFollow();
                }
                else
                {
                    // The authored turn translates the root. Its old first NavMesh
                    // corner may now be behind us; reconnect from the accepted pose.
                    // The opening walk is guaranteed: static clipping cannot stop it,
                    // so a return cannot alternate turns without covering ground.
                    frontReturnFreeWalkUntil = Time.time + FrontReturnFreeWalkDuration;
                    hasCuriosityPath = false;
                    PlayMovementClip(WalkSpeed);
                }
            }
            else if (completed == TurnUse.Wander)
            {
                RecordFreeRoamTurnCompletion();
                BeginMatchedWalkBlend(completedMotion, stepTurnTime, true);
                FinishWanderTurn();
            }
            else if (completed == TurnUse.Route)
            {
                Vector2 xz = new Vector2(transform.position.x, transform.position.z);
                MovementCorridor.ProjectToCorridor(routeIndex, xz, out float arc, out _);
                routeJoinArc = Mathf.Repeat(arc + 6f, MovementCorridor.Length(routeIndex));
                Vector2 p = MovementCorridor.PointAt(routeIndex, routeJoinArc);
                routeJoinPoint = new Vector3(p.x, MovementHeight, p.y);
                routeRejoining = true;
                routeJoinRecovering = !FreeRecoveryRestPose(transform.position, transform.rotation);
                if (routeJoinRecovering)
                {
                    interactionSettings.CollectStaticOverlaps(xz, CoreForward, interactionBody, freeRecoveryAllowed);
                    freeRecoveryGeometry = TerrainTreeObstacleRegistry.GeometryRevision;
                    freeRecoverySolids = (Bounds[])interactionSettings.solidObstacles.Clone();
                }
                PlayClip(moveClip);
            }
            else if (completed == TurnUse.Return)
            { InteractionPhase = PlayerInteractionPhase.Returning; hasCuriosityPath = false; nextReplan = 0f; }
            return true;
        }

        private static float SampledRootDistance(DinosaurInteractionProfile.Motion motion, float from, float to)
        {
            Vector2 previous = SampleFootFrame(motion, from).rootOffset;
            float distance = 0f;
            foreach (var frame in motion.footFrames)
            {
                if (frame.time <= from) continue;
                if (frame.time >= to) break;
                distance += Vector2.Distance(previous, frame.rootOffset); previous = frame.rootOffset;
            }
            return distance + Vector2.Distance(previous, SampleFootFrame(motion, to).rootOffset);
        }

        private void ClearSteppingTurn()
        {
            stepTurnMotion = null;
            stepTurnState = stepTurnOutgoing = null;
            stepTurnOutgoingMotion = null;
            stepTurnBlendTime = stepTurnBlendDuration = 0f;
            stepTurnTime = stepTurnEnd = stepTurnBlocked = 0f;
            ClearFrontReturnTurnPlan();
        }

        private void RecoverInterruptedTurn()
        {
            // Settle at the current root while retaining existing planted-foot anchors.
            TurnUse interrupted = stepTurnUse;
            GetComponent<DinosaurFootLock>()?.HoldSupportDuringRecovery(.35f);
            ClearSteppingTurn();
            actualInteractionSpeed = 0f;
            if (interrupted == TurnUse.Wander)
            {
                // A peer can interrupt a performance after it has crossed static geometry.
                // Preserve an escape state for that partial pose as well as completed turns.
                FinishWanderTurn();
                FreeIdle(2f);
            }
            else if (InteractionPhase == PlayerInteractionPhase.Inactive) FreeIdle(2f);
            else if (interrupted == TurnUse.FrontFollow || interrupted == TurnUse.FrontReturn)
            {
                if (frontNearPending && !frontExitPending) BeginFrontNear();
                else BeginFrontEnding();
            }
            else { InteractionPhase = PlayerInteractionPhase.Returning; hasCuriosityPath = false; nextReplan = 0f; }
        }

        private void ClearInteractionNavigation()
        {
            ClearSteppingTurn();
            routeRejoining = false;
            routeJoinRecovering = false;
            hasCuriosityPath = false;
            curiosityCorners = System.Array.Empty<Vector3>();
            curiosityCorner = 0;
            curiosityBlockedTime = visibleTime = invisibleTime = 0f;
        }

        public bool ApplyFootRootCorrection(Vector3 correction)
        {
            if (emergencyEscapeActive) return false;
            correction.y = 0f;
            if (trafficLocked && trafficHolding && !trafficMoving) return false;
            if (trafficLocked && trafficMoving)
            {
                correction = Vector3.ClampMagnitude(correction, ForestFootCorrectionBudget);
                Vector3 position = transform.position + correction;
                if (!StepPoseSafe(transform.position, position, transform.rotation, transform.rotation, false)) return false;
                transform.position = position;
                if (IsSteppingTurn) stepTurnOrigin += correction;
                return true;
            }
            if (frontRecoveryActive)
            {
                correction = Vector3.ClampMagnitude(correction, ForestFootCorrectionBudget);
                Vector3 position = transform.position + correction;
                bool turn = IsSteppingTurn && stepTurnUse == TurnUse.FrontRecovery;
                if (!(turn ? FrontAnimationPoseSafe(transform.position, position, transform.rotation, transform.rotation, frontProbeBody)
                    : FreeRecoveryStep(transform.position, position, transform.rotation, transform.rotation, true))) return false;
                transform.position = position;
                if (turn) stepTurnOrigin += correction;
                return true;
            }
            if (ForestPerformanceSupport && !IsSteppingTurn)
            {
                correction = Vector3.ClampMagnitude(correction, ForestFootCorrectionBudget);
                Vector3 position = transform.position + correction;
                frontProbeStage = $"Foot correction {frontStage}";
                if (!FrontAnimationPoseSafe(transform.position, position, transform.rotation, transform.rotation, interactionBody)) return false;
                transform.position = position; frontActionOrigin += correction;
                return true;
            }
            if ((!IsSteppingTurn && SideHandoffMoving) || (ReturnFootHandoffMoving && frontFeet != null && frontFeet.ReturnHandoffActive))
            {
                correction = Vector3.ClampMagnitude(correction, ForestFootCorrectionBudget);
                Vector3 position = transform.position + correction;
                if (!FrontPoseSafe(transform.position, position, transform.rotation, transform.rotation)) return false;
                transform.position = position;
                return true;
            }
            // Blend entry can leave a large support residual. Limit the applied step,
            // rather than rejecting the residual forever and stretching a planted leg.
            if (!IsSteppingTurn) return false;
            if (frontActive)
            {
                correction = Vector3.ClampMagnitude(correction, ForestFootCorrectionBudget);
                Vector3 position = transform.position + correction;
                if (!FrontPoseSafe(transform.position, position, transform.rotation, transform.rotation)) return false;
                transform.position = position; stepTurnOrigin += correction;
                return true;
            }
            correction = Vector3.ClampMagnitude(correction, ForestFootCorrectionBudget);
            Vector3 target = transform.position + correction;
            if (stepTurnUse == TurnUse.Wander || stepTurnUse == TurnUse.Route)
            {
                if (!FrontAnimationPoseSafe(transform.position, target, transform.rotation, transform.rotation, interactionBody)) return false;
            }
            else if (!StepPoseSafe(transform.position, target, transform.rotation, transform.rotation, stepTurnUse == TurnUse.Route)) return false;
            transform.position = target;
            stepTurnOrigin += correction; // Preserve the correction on subsequent sampled steps.
            return true;
        }

        private bool UpdateRouteStepping()
        {
            if (IsSteppingTurn && stepTurnUse == TurnUse.Route) { TickSteppingTurn(); return true; }
            if (!routeRejoining) return false;
            if (routeJoinRecovering && FreeRecoveryRestPose(transform.position, transform.rotation)) routeJoinRecovering = false;
            Vector3 direction = routeJoinPoint - transform.position; direction.y = 0f;
            if (direction.magnitude <= .03f)
            {
                routeArc = routeJoinArc; routeRejoining = false;
                return true;
            }
            float angle = Vector3.SignedAngle(GetVisualForward(), direction, Vector3.up);
            if (Mathf.Abs(angle) >= 40f)
            {
                if (!TryBeginSteppingTurn(angle, TurnUse.Route)) PlayClip(idleClip);
                return true;
            }
            Quaternion rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(direction) * Quaternion.Euler(0f, -VisualYaw, 0f), 40f * Time.deltaTime);
            Vector3 heading = rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
            Vector3 position = transform.position + heading * Mathf.Min(WalkSpeed * Time.deltaTime, direction.magnitude);
            bool canWalk = routeJoinRecovering
                ? MovementCorridor.MinDistanceToRoute(new Vector2(position.x, position.z), routeIndex)
                    <= Mathf.Max(2f, MovementCorridor.MinDistanceToRoute(new Vector2(transform.position.x, transform.position.z), routeIndex)) + .001f
                    && FreeRecoveryStep(transform.position, position, transform.rotation, rotation, true)
                : StepPoseSafe(transform.position, position, transform.rotation, rotation, true);
            if (canWalk)
            { transform.SetPositionAndRotation(position, rotation); PlayMovementClip(WalkSpeed); }
            else PlayClip(idleClip);
            return true;
        }

        private bool TryRouteTurn()
        {
            if (IsFreeRoaming || !HasFootPhaseData || CurrentState != DinosaurState.RandomWalk || IsOffRouteInteraction) return false;
            MovementCorridor.GetSampleAt(routeIndex, Mathf.Repeat(routeArc + 8f, MovementCorridor.Length(routeIndex)), out _, out Vector2 ahead);
            float angle = Vector3.SignedAngle(GetVisualForward(), new Vector3(ahead.x, 0f, ahead.y), Vector3.up);
            if (Mathf.Abs(angle) < 35f) return false;
            GetFootPhase(out float left, out float right);
            if (left < .5f || right < .5f) return false;
            return TryBeginSteppingTurn(angle, TurnUse.Route);
        }
    }
}
