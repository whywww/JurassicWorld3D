using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        private enum TurnUse { Follow, Return, Route, Wander, FrontFollow, FrontReturn }
        private TurnUse stepTurnUse;
        private DinosaurInteractionProfile.Motion stepTurnMotion;
        private Vector3 stepTurnOrigin;
        private Quaternion stepTurnRotation;
        private float stepTurnTime, stepTurnEnd, stepTurnBlocked;
        private AnimationState stepTurnState, stepTurnOutgoing;
        private PlayerInteractionPhase stepTurnResume;
        private bool routeRejoining, stopAfterStepTurn;
        private float routeJoinArc;
        private Vector3 routeJoinPoint;
        public bool IsSteppingTurn => stepTurnMotion != null;
        public bool IsRouteTurnOrJoin => IsSteppingTurn && stepTurnUse == TurnUse.Route || routeRejoining;
        public bool HasFootPhaseData => interactionProfile != null && interactionProfile.Find(InteractionMotionRole.Walk)?.footFrames.Length > 1;
        public float StepTurnClipTime => stepTurnTime;

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
        }

        private void TurnPose(DinosaurInteractionProfile.FootFrame frame, out Vector3 position, out Quaternion rotation)
        {
            Quaternion heading = stepTurnRotation * Quaternion.Euler(0f, VisualYaw, 0f);
            position = stepTurnOrigin + heading * new Vector3(frame.rootOffset.x, 0f, frame.rootOffset.y);
            position.y = MovementHeight;
            rotation = Quaternion.Euler(0f, frame.yaw, 0f) * stepTurnRotation;
        }

        private bool StepPoseSafe(Vector3 from, Vector3 to, Quaternion fromRotation, Quaternion toRotation, bool constrainRoute)
        {
            if (interactionSettings == null || !interactionSettings.NavigationReady || interactionBody == null) return false;
            Vector3 f0 = fromRotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
            Vector3 f1 = toRotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
            Vector2 a = new Vector2(from.x, from.z), b = new Vector2(to.x, to.z);
            if (constrainRoute && MovementCorridor.MinDistanceToRoute(b, routeIndex) > 2f) return false;
            if (interactionSettings.StepBlocked(a, b, new Vector2(f0.x, f0.z), new Vector2(f1.x, f1.z), interactionBody)
                || FindBlockingStep(a, b) != null) return false;
            var filter = NavigationFilter;
            return NavMesh.SamplePosition(to, out var hit, .25f, filter)
                && Vector2.Distance(b, new Vector2(hit.position.x, hit.position.z)) <= .12f
                && !NavMesh.Raycast(from, to, out _, filter);
        }

        private bool TryBeginSteppingTurn(float angle, TurnUse use)
        {
            bool frontTurn = frontActive && (use == TurnUse.FrontFollow || use == TurnUse.FrontReturn);
            if (IsSteppingTurn || !HasFootPhaseData || Mathf.Abs(angle) < 25f) return false;
            var motion = interactionProfile.Find(angle < 0f ? InteractionMotionRole.TurnLeft : InteractionMotionRole.TurnRight);
            if (motion == null || motion.footFrames.Length < 2)
            { if (frontTurn) frontBlockReason = "Turn animation missing"; return false; }
            float TurnAngle(DinosaurInteractionProfile.FootFrame frame) => Mathf.Abs(frame.yaw + (frontTurn ? frame.authoredYaw : 0f));
            float end = 0f;
            foreach (var frame in motion.footFrames)
                if (frame.leftContact && frame.rightContact && TurnAngle(frame) >= 20f && TurnAngle(frame) <= Mathf.Min(90f, Mathf.Abs(angle) + 5f)) end = frame.time;
            // Front bends below 60 degrees keep walking. Larger bends may use the full authored 90,
            // then steer through the remaining small correction without alternating left/right turns.
            if (Mathf.Abs(angle) >= 85f || (frontTurn && end <= 0f && Mathf.Abs(angle) >= FrontSteppingTurnAngle)) end = motion.clip.length;
            if (end <= 0f) { if (frontTurn) frontBlockReason = "Turn segment unavailable"; return false; }
            stepTurnOrigin = transform.position; stepTurnRotation = transform.rotation;
            // If a full step cannot fit, use an earlier double-support exit, never compress its yaw.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                bool safe = true; Vector3 previous = transform.position; Quaternion previousRotation = transform.rotation;
                foreach (var frame in motion.footFrames)
                {
                    if (frame.time > end + .001f) break;
                    TurnPose(frame, out var position, out var rotation);
                    if (!(frontActive && (use == TurnUse.FrontFollow || use == TurnUse.FrontReturn)
                        ? FrontStepSafe(previous, position, previousRotation, rotation, interactionBody)
                        : StepPoseSafe(previous, position, previousRotation, rotation, use == TurnUse.Route))) { safe = false; break; }
                    previous = position; previousRotation = rotation;
                }
                if (safe) break;
                float shorter = 0f;
                foreach (var frame in motion.footFrames)
                    if (frame.time < end - .1f && frame.leftContact && frame.rightContact && TurnAngle(frame) >= 20f) shorter = frame.time;
                end = shorter;
                if (end <= 0f || attempt == 2) return false;
            }
            stepTurnMotion = motion; stepTurnUse = use; stepTurnTime = stepTurnBlocked = 0f; stepTurnEnd = end;
            if (frontActive && use == TurnUse.FrontFollow)
            {
                TurnPose(SampleFootFrame(motion, end), out var finish, out var finishRotation);
                if (FrontExitMask(finish, finishRotation) == 0)
                { stepTurnMotion = null; frontSpaceExit = frontExitPending = true; return false; }
            }
            stepTurnResume = InteractionPhase;
            stopAfterStepTurn = false;
            stepTurnOutgoing = string.IsNullOrEmpty(activeClip) ? null : legacyAnimation[activeClip];
            float outgoingTime = stepTurnOutgoing != null ? stepTurnOutgoing.time : 0f;
            legacyAnimation.Stop();
            if (stepTurnOutgoing != null) { stepTurnOutgoing.enabled = true; stepTurnOutgoing.time = outgoingTime; stepTurnOutgoing.speed = 0f; stepTurnOutgoing.weight = 1f; }
            stepTurnState = legacyAnimation[motion.StateName];
            stepTurnState.enabled = true; stepTurnState.time = 0f; stepTurnState.speed = 0f; stepTurnState.weight = 0f;
            stepTurnState.wrapMode = WrapMode.ClampForever;
            activeClip = motion.StateName;
            if (use != TurnUse.Route && use != TurnUse.Wander && !frontActive) InteractionPhase = PlayerInteractionPhase.Turning;
            legacyAnimation.Sample();
            return true;
        }

        private bool FreeTurnFitsAt(Vector3 origin, Quaternion rotation, float angle)
        {
            var motion = interactionProfile.Find(angle < 0f ? InteractionMotionRole.TurnLeft : InteractionMotionRole.TurnRight);
            if (motion == null || motion.footFrames.Length < 2) return false;
            Vector3 savedOrigin = stepTurnOrigin;
            Quaternion savedRotation = stepTurnRotation;
            stepTurnOrigin = origin; stepTurnRotation = rotation;
            try
            {
                Vector3 previous = origin; Quaternion previousRotation = rotation;
                foreach (var frame in motion.footFrames)
                {
                    TurnPose(frame, out var position, out var heading);
                    if (!StepPoseSafe(previous, position, previousRotation, heading, false)) return false;
                    previous = position; previousRotation = heading;
                }
                return true;
            }
            finally { stepTurnOrigin = savedOrigin; stepTurnRotation = savedRotation; }
        }

        private bool TickSteppingTurn()
        {
            float nextTime = Mathf.Min(stepTurnEnd, stepTurnTime + Time.deltaTime * stepTurnMotion.turnPlaybackSpeed);
            var frame = SampleFootFrame(stepTurnMotion, nextTime);
            TurnPose(frame, out var position, out var rotation);
            if (!(frontActive ? FrontStepSafe(transform.position, position, transform.rotation, rotation, interactionBody)
                : StepPoseSafe(transform.position, position, transform.rotation, rotation, stepTurnUse == TurnUse.Route)))
            {
                stepTurnBlocked += Time.deltaTime;
                if (frontActive)
                {
                    GetComponent<DinosaurFootLock>()?.HoldSupportDuringRecovery(.35f);
                    BeginFrontSpaceWait();
                    return false;
                }
                if (stepTurnBlocked >= blockedReplanDelay && (stopAfterStepTurn || stepTurnUse == TurnUse.Wander))
                {
                    RecoverInterruptedTurn();
                }
                return false; // Both animation time and locomotion remain at the last safe pose.
            }
            stepTurnBlocked = 0f;
            transform.SetPositionAndRotation(position, rotation);
            stepTurnTime = nextTime;
            stepTurnState.time = nextTime;
            float blend = Mathf.Clamp01(nextTime / Mathf.Max(.01f, stepTurnMotion.blendDuration));
            if (frontActive && Time.time - frontTurnResumeAt < .2f)
            {
                blend = Mathf.Min(blend, Mathf.Clamp01((Time.time - frontTurnResumeAt) / .2f));
                if (!string.IsNullOrEmpty(frontWaitClip)) legacyAnimation[frontWaitClip].weight = 1f - blend;
            }
            stepTurnState.weight = blend;
            if (stepTurnOutgoing != null) { stepTurnOutgoing.weight = 1f - blend; stepTurnOutgoing.enabled = blend < 1f; }
            legacyAnimation.Sample();
            interactionMoved = true;
            if (nextTime < stepTurnEnd - .0001f) return false;
            TurnUse completed = stepTurnUse;
            stepTurnState.speed = 0f;
            stepTurnMotion = null;
            if (completed == TurnUse.FrontFollow || completed == TurnUse.FrontReturn)
            {
                if (frontStopPending) BeginFrontEnding();
                else if (completed == TurnUse.FrontFollow)
                {
                    if (frontExitPending) BeginFrontEnding();
                    else if (frontNearPending) BeginFrontNear();
                    else PlayFrontFollow();
                }
                else PlayMovementClip(WalkSpeed);
            }
            else if (completed == TurnUse.Wander)
            {
                PlayMovementClip(WalkSpeed);
                if (!PlanCuriosityPath(freeGoal, true)) FreeIdle(2f);
            }
            else if (completed == TurnUse.Route)
            {
                Vector2 xz = new Vector2(transform.position.x, transform.position.z);
                MovementCorridor.ProjectToCorridor(routeIndex, xz, out float arc, out _);
                routeJoinArc = Mathf.Repeat(arc + 6f, MovementCorridor.Length(routeIndex));
                Vector2 p = MovementCorridor.PointAt(routeIndex, routeJoinArc);
                routeJoinPoint = new Vector3(p.x, MovementHeight, p.y);
                routeRejoining = true;
                PlayClip(moveClip);
            }
            else if (completed == TurnUse.Return)
            { InteractionPhase = PlayerInteractionPhase.Returning; hasCuriosityPath = false; nextReplan = 0f; }
            else if (stopAfterStepTurn) BeginCuriosityStop();
            else if (stepTurnResume == PlayerInteractionPhase.Observing) BeginCuriosityRun();
            else { InteractionPhase = PlayerInteractionPhase.Following; curiosityMotion = null; }
            return true;
        }

        private void ClearSteppingTurn()
        {
            stepTurnMotion = null;
            stepTurnState = stepTurnOutgoing = null;
            stepTurnTime = stepTurnEnd = stepTurnBlocked = 0f;
            stopAfterStepTurn = false;
        }

        private void RecoverInterruptedTurn()
        {
            // Settle at the current root while retaining existing planted-foot anchors.
            TurnUse interrupted = stepTurnUse;
            GetComponent<DinosaurFootLock>()?.HoldSupportDuringRecovery(.35f);
            ClearSteppingTurn();
            currentInteractionSpeed = actualInteractionSpeed = 0f;
            if (interrupted == TurnUse.Wander || InteractionPhase == PlayerInteractionPhase.Inactive) FreeIdle(2f);
            else if (interrupted == TurnUse.FrontFollow || interrupted == TurnUse.FrontReturn)
            {
                if (frontNearPending && !frontExitPending) BeginFrontNear();
                else BeginFrontEnding();
            }
            else BeginCuriosityStop();
        }

        private void ClearInteractionNavigation()
        {
            ClearSteppingTurn();
            routeRejoining = false;
            hasCuriosityPath = false;
            curiosityCorners = System.Array.Empty<Vector3>();
            curiosityCorner = failedReplans = 0;
            curiosityBlockedTime = visibleTime = invisibleTime = 0f;
            curiosityMotion = null;
        }

        public bool ApplyFootRootCorrection(Vector3 correction)
        {
            correction.y = 0f;
            if (!IsSteppingTurn || correction.magnitude > .4f) return false;
            Vector3 target = transform.position + correction;
            if (!StepPoseSafe(transform.position, target, transform.rotation, transform.rotation, stepTurnUse == TurnUse.Route)) return false;
            transform.position = target;
            stepTurnOrigin += correction; // Preserve the correction on subsequent sampled steps.
            return true;
        }

        private bool UpdateRouteStepping()
        {
            if (IsSteppingTurn && stepTurnUse == TurnUse.Route) { TickSteppingTurn(); return true; }
            if (!routeRejoining) return false;
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
            if (StepPoseSafe(transform.position, position, transform.rotation, rotation, true))
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
