using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        private enum FrontStage { Intro, Follow, Near, Ending, TurnBack, ReturnWalk }
        private static readonly InteractionMotionRole[] FrontIntro = { InteractionMotionRole.Investigate,
            InteractionMotionRole.InvestigateOut, InteractionMotionRole.BigRoarStep };
        [Header("Front interaction")]
        [SerializeField, Min(0f)] private float frontDetectionDistance = 32f;
        [SerializeField, Min(0f)] private float frontNearDistance = 10f;
        [SerializeField, Min(0f)] private float frontResumeDistance = 15f;
        [SerializeField, Min(1f)] private float frontFollowDuration = 30f;
        [SerializeField, Min(0f)] private float frontReturnWalkTime = 3f;
        private bool frontActive, frontStopPending;
        private FrontStage frontStage;
        private int frontActionIndex, frontReturnIndex;
        private float frontActionTime, frontBlocked, frontReturnWalked;
        private Vector3 frontActionOrigin;
        private Quaternion frontActionRotation;
        private DinosaurInteractionProfile.Motion frontAction, frontFollowMotion;
        private DinosaurCoreBody frontOriginalBody, frontEnvelope;
        private DinosaurInteractionProfile.Motion frontEnvelopeMotion;
        private bool frontNearPending, frontExitPending;
        private float frontFollowElapsed;
        private bool frontTimingFollow, frontReturnHasHistory;
        private string frontEndReason;
        public string PlayerVisibilityStatus => PlayerVisibilityFailure() ?? "Visible";
        public float PlayerLostSightTime => invisibleTime;
        public string FrontEndReason => frontEndReason ?? "--";
        private bool FrontPlayerNear => CanSeePlayer() && FlatDistance(transform.position, viewCamera.transform.position) <= frontNearDistance;
        private bool FrontPlayerBeyondResume => CanSeePlayer() && FlatDistance(transform.position, viewCamera.transform.position) > frontResumeDistance;
        private InteractionMotionRole frontNearBite;
        private float FrontFollowSpeed => WalkSpeed * (sideRoarHandoffActive
            ? Mathf.SmoothStep(0f, 1f, sideRoarHandoffTime / Mathf.Max(.01f, sideRoarHandoffDuration)) : 1f);
        private const float FrontSteppingTurnAngle = 60f;
        private float frontReturnBlendUntil, frontReturnEntryPhase;
        private bool frontReturnAligning;
        private float frontReturnFreeWalkUntil;
        private const float FrontReturnNearCornerDistance = .45f;
        private const float FrontReturnFreeWalkDuration = 2f;
        private DinosaurInteractionProfile.Motion frontReturnTurnMotion;
        private float frontReturnTurnEnd;
        private Vector3 frontReturnTurnOrigin;
        private Quaternion frontReturnTurnRotation;
        private int frontReturnTurnIndex = -1;
        internal bool ReturnFreeWalkActive => frontActive && frontStage == FrontStage.ReturnWalk && Time.time < frontReturnFreeWalkUntil;
        private DinosaurFootLock frontFeet;
        internal bool FinishingReturnFootsteps => !frontActive && InteractionPhase == PlayerInteractionPhase.Inactive && frontFeet != null && frontFeet.ReturnHandoffActive;
        internal bool ReturnFootHandoffMoving => !frontWaiting && !IsSteppingTurn && (frontActive && frontStage == FrontStage.ReturnWalk && Time.time >= frontReturnBlendUntil
            || FinishingReturnFootsteps && CurrentState == DinosaurState.RandomWalk && InteractionPhase == PlayerInteractionPhase.Inactive);
        internal bool UseReturnSoleHeading => frontActive && frontStage == FrontStage.TurnBack
            || frontFeet != null && frontFeet.ReturnHandoffActive;
        private readonly List<Vector3> frontTrail = new List<Vector3>();
        public string FrontInteractionStage => !frontActive ? "Inactive" : frontStage + (frontRecoveryActive
            ? IsSteppingTurn ? " (RecoveryTurn)" : frontRecoveryWalking ? " (RecoveryWalk)" : " (RecoverySearch)" : frontPeerClearing
            ? frontPeerYielding ? " (YieldingToPeer)" : " (PassingPeer)"
            : frontWaiting ? frontWaitReason == "Peer" ? " (WaitingForPeerClearance)" : " (WaitingForClearance)" : "");
        public float FrontActionPlaybackTime => frontWaiting && frontWaitPlaying ? frontWaitTime : frontAction != null ? frontActionTime
            : frontStage == FrontStage.Follow && frontFollowMotion != null && legacyAnimation != null
                ? legacyAnimation[frontFollowMotion.StateName].time : frontStage == FrontStage.ReturnWalk && moveState != null ? moveState.time : 0f;
        public float CurrentAnimationPlaybackTime => frontWaiting && frontWaitPlaying ? frontWaitTime
            : frontAction != null ? frontActionTime : IsSteppingTurn ? stepTurnTime
            : legacyAnimation != null && !string.IsNullOrEmpty(activeClip) && legacyAnimation[activeClip] != null
                ? legacyAnimation[activeClip].time : 0f;
        public bool FrontActionWaitingForSpace => frontActive && (frontWaiting || frontPeerClearing || frontBlocked > 0f
            || frontRecoveryActive && !frontRecoveryWalking);
        private bool frontTestControlled, frontTestWaiting;
        public bool IsFrontTestControlled => frontTestControlled;
        public int FrontTestInteractionCount { get; private set; }
        public bool IsFrontInteractionActive => frontActive;
        public float InteractionCooldownRemaining => Mathf.Max(0f, cooldownUntil - Time.time);
        public bool IsWaitingForInteraction => !frontActive && InteractionPhase == PlayerInteractionPhase.Inactive && InteractionCooldownRemaining <= 0f;
        public float PlayerInteractionAngle => viewCamera == null ? 0f : Vector3.Angle(
            Vector3.ProjectOnPlane(GetVisualForward(), Vector3.up),
            Vector3.ProjectOnPlane(viewCamera.transform.position - transform.position, Vector3.up));
        public string CurrentAnimationSourceName
        {
            get
            {
                if (activeClip == WaitClip) return "look forward (space wait)";
                if (interactionProfile != null)
                    foreach (var motion in interactionProfile.motions) if (motion.StateName == activeClip) return motion.sourceName;
                return activeClip ?? "";
            }
        }
        public void ArmFrontInteractionTest()
        {
            frontTestControlled = frontTestWaiting = true;
        }
        public bool TriggerFrontInteractionTest()
        {
            if (!frontTestControlled || !IsWaitingForInteraction || !interactionReady || !FrontActionsAvailable()
                || !CanSeePlayer() || PlayerInteractionAngle > 20f + .0001f) return false;
            frontTestWaiting = false;
            FrontTestInteractionCount++;
            BeginFrontInteraction();
            return true;
        }

        private bool FrontActionsAvailable()
        {
            if (interactionProfile == null) return false;
            foreach (var role in FrontIntro) if (interactionProfile.Find(role) == null) return false;
            foreach (var role in new[] { InteractionMotionRole.FollowWalk, InteractionMotionRole.BiteCloseLeft,
                InteractionMotionRole.BiteCloseRight, InteractionMotionRole.LookForwardTwo, InteractionMotionRole.Roar })
                if (interactionProfile.Find(role) == null) return false;
            return interactionProfile.Find(InteractionMotionRole.Turn180Look) != null;
        }
        private void BeginFrontInteraction() => BeginInteractionSequence(false, false);

        private void BeginInteractionSequence(bool fromSide, bool fromLeft)
        {
            ClearFrontRecovery();
            sideTestSequence = fromSide;
            sideFromLeft = fromSide && fromLeft;
            ClearSideRoarHandoff();
            frontFeet?.EndReturnHandoff();
            ClearInteractionNavigation();
            frontOriginalBody = interactionBody;
            if (frontEnvelope == null)
            {
                var holder = new GameObject("Front action envelope") { hideFlags = HideFlags.DontSave };
                holder.transform.SetParent(transform, false);
                frontEnvelope = holder.AddComponent<DinosaurCoreBody>();
            }
            frontActive = true; frontStage = FrontStage.Intro; frontActionIndex = 0;
            ResetFrontExitPlanner();
            frontEndReason = null; frontLastSpaceFailure = null;
            frontStopPending = false; frontAction = null; frontFollowMotion = null;
            frontNearPending = frontExitPending = false;
            frontFollowElapsed = 0f; frontTimingFollow = false;
            frontReturnHasHistory = false; frontReturnWalked = 0f; frontReturnIndex = -1;
            frontFollowBlockedAt = frontReturnBlockedAt = -1f;
            CancelFrontWait();
            CancelFrontPeerClearance();
            frontReturnAligning = false; frontReturnBlendUntil = 0f; frontReturnFreeWalkUntil = 0f;
            ClearFrontReturnTurnPlan();
            frontTrail.Clear(); frontBlocked = invisibleTime = visibleTime = 0f;
            frontTrail.Add(transform.position);
            lastSeenPlayer = viewCamera.transform.position;
            CurrentState = DinosaurState.RandomWalk;
            InteractionPhase = PlayerInteractionPhase.FrontSequence;
            waitingAtCrossing = false; MovementCorridorCrossings.Release(this);
        }
        private void UseFrontEnvelope(DinosaurInteractionProfile.Motion motion)
        {
            frontEnvelopeMotion = motion;
            frontEnvelope.bands = FrontPoseBands(motion, FrontStateTime(motion));
            interactionBody = frontEnvelope;
        }
        private bool FrontPoseSafe(Vector3 from, Vector3 to, Quaternion fromRotation, Quaternion toRotation, DinosaurCoreBody.Band[] playerBands = null)
        {
            RefreshFrontPoseEnvelope();
            frontProbeStage = $"Runtime {frontStage}";
            return FrontStepSafe(from, to, fromRotation, toRotation, interactionBody, playerBands);
        }
        private void FrontActionPose(DinosaurInteractionProfile.FootFrame sample, out Vector3 position, out Quaternion rotation)
        {
            position = frontActionOrigin + frontActionRotation * Quaternion.Euler(0f, VisualYaw, 0f)
                * new Vector3(sample.rootOffset.x, 0f, sample.rootOffset.y);
            position.y = MovementHeight;
            rotation = Quaternion.Euler(0f, sample.yaw, 0f) * frontActionRotation;
        }
        private bool StartFrontAction(InteractionMotionRole role)
        {
            var motion = interactionProfile.Find(role);
            if (motion == null) { frontBlockReason = "Animation missing"; return false; }
            frontAction = motion; frontActionTime = 0f;
            UseFrontEnvelope(motion);
            frontActionOrigin = transform.position; frontActionRotation = transform.rotation;
            frontProbeStage = $"Performance {motion.sourceName}";
            if (!FrontAnimatedStep(motion, 0f, 0f, transform.position, transform.position, transform.rotation, transform.rotation))
            {
                frontAction = null;
                return false;
            }
            frontActionTime = frontBlocked = 0f;
            var state = legacyAnimation[motion.StateName];
            // Re-enter a previously clamped state independently of its previous fade/weight.
            state.enabled = false; state.weight = 0f;
            state.time = 0f; state.speed = 0f; state.wrapMode = WrapMode.ClampForever;
            frontWaitHoldingPose = false;
            legacyAnimation.CrossFade(motion.StateName, motion.blendDuration);
            state.enabled = true; state.time = 0f;
            activeClip = motion.StateName;
            Debug.Log("[T-Rex front] " + motion.sourceName, this);
            return true;
        }
        private bool TickFrontAction()
        {
            float next = Mathf.Min(FrontActionDuration(frontAction), frontActionTime + Time.deltaTime);
            var sample = SampleFootFrame(frontAction, next);
            FrontActionPose(sample, out var position, out var rotation);
            if (!FrontAnimatedStep(frontAction, frontActionTime, next, transform.position, position, transform.rotation, rotation))
            {
                frontBlocked += Time.deltaTime;
                BeginFrontSpaceWait();
                return false;
            }
            frontBlocked = 0f;
            frontEnvelope.bands = frontProbeBody.bands;
            interactionMoved = Vector3.Distance(transform.position, position) > .00001f;
            AnimationRootTravel += SampledRootDistance(frontAction, frontActionTime, next);
            transform.SetPositionAndRotation(position, rotation);
            frontActionTime = next; legacyAnimation[frontAction.StateName].time = next;
            // These states have speed zero: explicitly apply each accepted pose before LateUpdate foot IK.
            legacyAnimation.Sample();
            return next >= FrontActionDuration(frontAction) - .0001f;
        }
        private static float FrontActionDuration(DinosaurInteractionProfile.Motion motion)
            => motion.role == InteractionMotionRole.LookForwardTwo ? Mathf.Min(2f, motion.clip.length) : motion.clip.length;
        private void PlayFrontFollow()
        {
            frontFollowMotion = interactionProfile.Find(InteractionMotionRole.FollowWalk);
            UseFrontEnvelope(frontFollowMotion);
            var state = legacyAnimation[frontFollowMotion.StateName];
            state.time = sideRoarHandoffActive && sideRoarHandoffTime <= 0f ? sideWalkEntry : 0f;
            state.wrapMode = WrapMode.ClampForever;
            state.speed = FrontFollowSpeed / Mathf.Max(.1f, frontFollowMotion.groundSpeed);
            legacyAnimation.CrossFade(frontFollowMotion.StateName, sideRoarHandoffActive && sideRoarHandoffTime <= 0f
                ? sideRoarHandoffDuration : frontFollowMotion.blendDuration);
            activeClip = frontFollowMotion.StateName;
            Debug.Log("[T-Rex front] " + frontFollowMotion.sourceName, this);
        }
        private void BeginFrontFollow(bool resume = false)
        {
            if (frontExitPending) { BeginFrontEnding(); return; }
            if (FrontPlayerNear) { BeginFrontNear(); return; }
            bool handoff = sideTestSequence && !resume && frontStage == FrontStage.Intro;
            ClearSideRoarHandoff();
            if (handoff) BeginSideRoarHandoff();
            frontStage = FrontStage.Follow;
            frontFollowBlockedAt = -1f;
            frontAction = null; frontFollowMotion = null;
            frontTimingFollow = true;
            if (!resume) RecordFrontTrail(true);
            frontStopPending = frontNearPending = frontExitPending = false; hasCuriosityPath = false;
            nextReplan = 0f;
            PlayFrontFollow();
            if (!PlanFrontFollow()) BeginFrontSpaceWait();
        }
        private bool PlanFrontFollow()
        {
            plannedPlayer = lastSeenPlayer; nextReplan = Time.time + Mathf.Max(.5f, replanInterval);
            Vector3 away = transform.position - lastSeenPlayer; away.y = 0f;
            if (away.sqrMagnitude < .01f) away = -GetVisualForward();
            // Leave room for the path's 8cm arrival tolerance and NavMesh projection,
            // so an unobstructed approach can cross the actual 10m Near threshold.
            bool planned = PlanFrontConnection(lastSeenPlayer + away.normalized * Mathf.Max(0f, frontNearDistance - .25f), false);
            if (!planned) NoteFrontFollowBlock();
            return planned;
        }
        // The recorded walk is the return route. Denser sampling at bends keeps the
        // reversed path inside the actual corridor without storing every frame.
        private void RecordFrontTrail(bool force = false)
        {
            if (!force && frontStage != FrontStage.Follow) return;
            Vector3 position = transform.position;
            if (frontTrail.Count == 0) { frontTrail.Add(position); return; }
            Vector3 last = frontTrail[frontTrail.Count - 1];
            Vector3 delta = position - last; delta.y = 0f;
            float distance = delta.magnitude;
            if (distance < .001f) return;
            bool bend = false;
            if (frontTrail.Count >= 2)
            {
                Vector3 previous = last - frontTrail[frontTrail.Count - 2]; previous.y = 0f;
                bend = previous.sqrMagnitude > .0001f && Vector3.Angle(previous, delta) >= 30f;
            }
            if (!force && distance < (bend ? .2f : .45f)) return;
            frontTrail.Add(position);
        }
        private bool MoveFrontPath(float speed, bool returning, bool quiet = false)
        {
            if (UsesWalkingSteering)
            {
                bool moved = MovePredictedPath(Time.deltaTime, speed);
                if (moved && !returning) RecordFrontTrail();
                return moved;
            }
            actualInteractionSpeed = 0f;
            if (!hasCuriosityPath || PathFinished()) return false;
            Vector3 direction = curiosityCorners[curiosityCorner] - transform.position; direction.y = 0f;
            if (direction.sqrMagnitude < .00000001f) return false;
            float angle = Vector3.SignedAngle(GetVisualForward(), direction, Vector3.up);
            bool freeWalk = returning && Time.time < frontReturnFreeWalkUntil;
            if (returning && !freeWalk && Mathf.Abs(angle) >= FrontSteppingTurnAngle)
            {
                if (!FrontReturnSkipNearCorner())
                {
                    if (quiet) frontRetryAt = Time.time + Mathf.Max(.1f, interactionSettings.spaceSearchInterval);
                    else BeginFrontSpaceWait();
                    return false;
                }
                direction = curiosityCorners[curiosityCorner] - transform.position; direction.y = 0f;
                if (direction.sqrMagnitude < .00000001f) return false;
                angle = Vector3.SignedAngle(GetVisualForward(), direction, Vector3.up);
            }
            if (Mathf.Abs(angle) >= FrontSteppingTurnAngle && !freeWalk
                && !(SideHandoffMoving && sideRoarHandoffTime < sideRoarHandoffDuration))
            {
                if (returning)
                {
                    if (!FrontReturnTurnPlanCurrent()) FrontReturnPrepareTurn(angle);
                    if (frontReturnTurnMotion != null)
                    {
                        var motion = frontReturnTurnMotion; float end = frontReturnTurnEnd;
                        ClearFrontReturnTurnPlan();
                        BeginSteppingTurn(motion, end, TurnUse.FrontReturn);
                        return false;
                    }
                }
                else if (TryBeginSteppingTurn(angle, TurnUse.FrontFollow)) return false;
                if (quiet) frontRetryAt = Time.time + Mathf.Max(.1f, interactionSettings.spaceSearchInterval);
                else BeginFrontSpaceWait();
                return false;
            }
            float turnRate = 65f;
            if (!returning) turnRate = SideHandoffTurnRate(turnRate);
            Quaternion rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction) * Quaternion.Euler(0f, -VisualYaw, 0f), turnRate * Time.deltaTime);
            Vector3 heading = rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
            Vector3 position = transform.position + heading * Mathf.Min(direction.magnitude, speed * Time.deltaTime);
            if (!returning) position = SideHandoffPivotPosition(position, rotation);
            // The opening walk after a return turn is guaranteed: static clipping and NavMesh
            // edges are ignored so the actor covers ground before normal collision resumes.
            bool safe = freeWalk
                ? FrontAnimationPoseSafe(transform.position, position, transform.rotation, rotation, interactionBody)
                : FrontPoseSafe(transform.position, position, transform.rotation, rotation);
            if (!safe)
            {
                if (quiet) frontRetryAt = Time.time + Mathf.Max(.1f, interactionSettings.spaceSearchInterval);
                else BeginFrontSpaceWait();
                return false;
            }
            frontBlockReason = frontWaitReason = null;
            actualInteractionSpeed = Vector3.Distance(transform.position, position) / Mathf.Max(.001f, Time.deltaTime);
            transform.SetPositionAndRotation(position, rotation); interactionMoved = actualInteractionSpeed > .001f;
            if (interactionMoved)
            {
                frontBlocked = 0f; frontBlockingPeer = null; frontPeerBlockedTime = 0f;
                if (!returning) { frontFollowBlockedAt = -1f; RecordFrontTrail(); }
                else frontReturnBlockedAt = -1f;
            }
            if (returning && frontReturnAligning && Mathf.Abs(angle) <= 2f) frontReturnAligning = false;
            return interactionMoved;
        }

        private bool FrontReturnTurnPlanCurrent()
            => frontReturnTurnMotion != null && frontReturnTurnIndex == frontReturnIndex
                && FlatDistance(transform.position, frontReturnTurnOrigin) <= .03f
                && Quaternion.Angle(transform.rotation, frontReturnTurnRotation) <= 1f;

        private void ClearFrontReturnTurnPlan()
        {
            frontReturnTurnMotion = null;
            frontReturnTurnEnd = 0f;
            frontReturnTurnOrigin = default;
            frontReturnTurnRotation = default;
            frontReturnTurnIndex = -1;
        }

        // A near corner can be the projected path origin or a sample we have just passed.
        // Skipping it is only allowed when the next corner is reachable in a straight,
        // checked line; otherwise the wait keeps its pose instead of turning to it.
        private bool FrontReturnSkipNearCorner()
        {
            if (!hasCuriosityPath || curiosityCorner >= curiosityCorners.Length) return true;
            if (FlatDistance(transform.position, curiosityCorners[curiosityCorner]) > FrontReturnNearCornerDistance) return true;
            if (curiosityCorner + 1 >= curiosityCorners.Length) return true;
            Vector3 near = curiosityCorners[curiosityCorner] - transform.position; near.y = 0f;
            float nearAngle = Vector3.SignedAngle(GetVisualForward(), near, Vector3.up);
            if (Mathf.Abs(nearAngle) < FrontSteppingTurnAngle) return true;
            Vector3 beyond = curiosityCorners[curiosityCorner + 1] - transform.position; beyond.y = 0f;
            if (Mathf.Abs(Vector3.SignedAngle(GetVisualForward(), beyond, Vector3.up)) >= FrontSteppingTurnAngle) return true;
            if (!FrontReturnConnectorSafe(curiosityCorner + 1))
            {
                frontBlockReason = "Near path corner requires a safe connector";
                return false;
            }
            curiosityCorner++;
            return true;
        }

        private bool FrontReturnConnectorSafe(int next)
        {
            if (next < 0 || next >= curiosityCorners.Length) return false;
            Vector3 target = curiosityCorners[next];
            Vector2 from = new Vector2(transform.position.x, transform.position.z);
            Vector2 to = new Vector2(target.x, target.z);
            Vector2 heading = to - from;
            if (heading.sqrMagnitude < .000001f) return true;
            heading.Normalize();
            if (!NavigationStepFits(transform.position, target)) return false;
            if (interactionSettings.StepBlocked(from, to, CoreForward, heading, interactionBody)) return false;
            if (!SafetySegmentAllowed(from, to, IsAvoidingTyrannosaurus)) return false;
            Vector2 previous = from;
            int samples = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / .1f));
            for (int i = 1; i <= samples; i++)
            {
                Vector2 point = Vector2.Lerp(from, to, (float)i / samples);
                if (FindBlockingStep(previous, point) != null) return false;
                previous = point;
            }
            return true;
        }

        // Search must not start the turn itself. Simulate the authored pose, then prove
        // only that the accepted pose can reach the return target through the NavMesh.
        private bool FrontReturnPrepareTurn(float angle)
        {
            ClearFrontReturnTurnPlan();
            if (!SelectSteppingTurn(angle, true, out var motion, out float end)) return false;
            var finish = SampleFootFrame(motion, end);
            TurnPoseAt(transform.position, transform.rotation, finish, out var position, out _);
            var filter = NavigationFilter;
            if (!NavMesh.SamplePosition(position, out var turned, 1f, filter)) { frontBlockReason = "NavMesh"; return false; }
            if (frontReturnIndex < 0 || frontReturnIndex >= frontTrail.Count) { frontBlockReason = "NavMesh"; return false; }
            if (!NavMesh.SamplePosition(frontTrail[frontReturnIndex], out var target, .75f, filter)) { frontBlockReason = "NavMesh"; return false; }
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(turned.position, target.position, filter, path)
                || path.status != NavMeshPathStatus.PathComplete) { frontBlockReason = "NavMesh"; return false; }
            frontReturnTurnMotion = motion; frontReturnTurnEnd = end;
            frontReturnTurnOrigin = transform.position; frontReturnTurnRotation = transform.rotation;
            frontReturnTurnIndex = frontReturnIndex;
            return true;
        }
        private void BeginFrontEnding()
        {
            ClearFrontRecovery();
            if (frontStage == FrontStage.Ending || frontStage == FrontStage.TurnBack || frontStage == FrontStage.ReturnWalk) return;
            frontStopPending = true;
            // A blocked stepping turn cannot finish, so ending starts at its last accepted pose.
            if (IsSteppingTurn)
            {
                GetComponentInChildren<DinosaurFootLock>()?.HoldSupportDuringRecovery(.35f);
                ClearSteppingTurn();
            }
            if (frontStage == FrontStage.Follow)
                Debug.Log($"[T-Rex front] Follow -> Ending: walked={frontFollowElapsed:F3}s, lostSight={invisibleTime:F3}s, reason={frontEndReason ?? frontBlockReason}, path={hasCuriosityPath}", this);
            ClearSideRoarHandoff();
            CancelFrontWait();
            CancelFrontPeerClearance();
            frontTimingFollow = false;
            ChooseFrontBite();
            RecordFrontTrail(true);
            frontStage = FrontStage.Ending; frontActionIndex = 0; frontAction = null;
            frontFollowBlockedAt = -1f;
            frontBlocked = 0f; hasCuriosityPath = false;
            frontStopPending = true;
        }
        private void BeginFrontNear()
        {
            if (!FrontPlayerNear) { frontNearPending = false; return; }
            ClearSideRoarHandoff();
            CancelFrontWait();
            CancelFrontPeerClearance();
            ChooseFrontBite();
            RecordFrontTrail(true);
            frontStage = FrontStage.Near; frontActionIndex = 0; frontAction = null;
            frontFollowBlockedAt = -1f;
            frontNearPending = false; frontBlocked = 0f; hasCuriosityPath = false;
        }
        private void BeginFrontReturn()
        {
            ClearSideRoarHandoff();
            CancelFrontWait(); CancelFrontPeerClearance();
            frontStopPending = false;
            frontTimingFollow = false;
            frontReturnHasHistory = false; frontReturnWalked = 0f; frontReturnIndex = -1;
            frontReturnBlockedAt = -1f; frontReturnFreeWalkUntil = 0f;
            ClearFrontReturnTurnPlan();
            hasCuriosityPath = false;
            frontStage = FrontStage.TurnBack; frontActionIndex = 0; frontAction = null; frontBlocked = 0f;
        }
        private void BeginFrontReturnWalk()
        {
            frontStage = FrontStage.ReturnWalk;
            frontReturnWalked = frontBlocked = 0f; hasCuriosityPath = false;
            frontReturnBlockedAt = -1f;
            frontReturnHasHistory = frontTrail.Count > 1;
            frontReturnIndex = frontTrail.Count - 1;
            while (frontReturnIndex >= 0 && FlatDistance(transform.position, frontTrail[frontReturnIndex]) < 1.2f) frontReturnIndex--;
            ClearFrontReturnTurnPlan();
            BeginFrontReturnWalkBlend();
            frontReturnFreeWalkUntil = Mathf.Max(frontReturnBlendUntil, Time.time) + FrontReturnFreeWalkDuration;
        }
        private void AdvanceFrontReturnTarget()
        {
            while (frontReturnIndex >= 0 && FlatDistance(transform.position, frontTrail[frontReturnIndex]) <= 1.2f)
                frontReturnIndex--;
            if (frontReturnIndex <= 0) return;
            // Dense trail samples are smaller than a walking turn's radius. Aim along
            // the same straight section instead of orbiting a nearby sample after a turn.
            Vector3 start = frontTrail[frontReturnIndex];
            Vector3 tangent = start - frontTrail[frontReturnIndex - 1]; tangent.y = 0f;
            float lookAhead = Mathf.Max(3f, WalkSpeed * 2f);
            while (frontReturnIndex > 0 && FlatDistance(transform.position, frontTrail[frontReturnIndex]) < lookAhead)
            {
                Vector3 next = frontTrail[frontReturnIndex - 1];
                Vector3 segment = frontTrail[frontReturnIndex] - next; segment.y = 0f;
                Vector3 span = start - next; span.y = 0f;
                if (Vector3.Angle(tangent, segment) > 15f || Vector3.Angle(tangent, span) > 15f) break;
                frontReturnIndex--;
            }
        }
        private void BeginFrontReturnWalkBlend()
            => BeginMatchedWalkBlend(interactionProfile.Find(InteractionMotionRole.Turn180Look), -1f, false);

        private void BeginMatchedWalkBlend(DinosaurInteractionProfile.Motion turn, float sourceTime, bool roaming)
        {
            var walk = interactionProfile.Find(InteractionMotionRole.Walk);
            var finish = SampleFootFrame(turn, sourceTime >= 0f ? sourceTime : turn.clip.length);
            Vector3 left = finish.leftSole, right = finish.rightSole;
            Vector3 leftHeading = finish.leftHeading, rightHeading = finish.rightHeading;
            Transform leftFoot = null, rightFoot = null;
            Quaternion inverse = Quaternion.Inverse(transform.rotation * Quaternion.Euler(0f, VisualYaw, 0f));
            foreach (var bone in legacyAnimation.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == "jt_ClawMiddle_L") left = inverse * (bone.position - transform.position);
                else if (bone.name == "jt_ClawMiddle_R") right = inverse * (bone.position - transform.position);
                else if (bone.name == "jt_Foot_L") leftFoot = bone;
                else if (bone.name == "jt_Foot_R") rightFoot = bone;
            }
            if (leftFoot != null) leftHeading = left - inverse * (leftFoot.position - transform.position);
            if (rightFoot != null) rightHeading = right - inverse * (rightFoot.position - transform.position);
            frontFeet = GetComponentInChildren<DinosaurFootLock>();
            if (frontFeet != null)
            {
                frontFeet.GetReturnSoleHeadings(out var lh, out var rh); leftHeading = inverse * lh; rightHeading = inverse * rh;
            }
            leftHeading.y = rightHeading.y = 0f;
            float start = Mathf.Max(0f, moveClipLoopStart), end = moveClipLoopEnd > start ? moveClipLoopEnd : walk.clip.length;
            float best = float.PositiveInfinity; frontReturnEntryPhase = start;
            for (int i = 0; i + 1 < walk.footFrames.Length; i++)
            {
                var sample = walk.footFrames[i]; var next = walk.footFrames[i + 1];
                if (sample.time < start || sample.time >= end) continue;
                bool liftingLeft = sample.leftContact && !next.leftContact && sample.rightContact;
                bool liftingRight = sample.rightContact && !next.rightContact && sample.leftContact;
                if (!liftingLeft && !liftingRight) continue;
                // Reject authored knee/sole ordering reversals over the next two steps, including the loop seam.
                bool valid = true; int landings = 0; var previous = sample;
                for (int j = 1; j <= walk.footFrames.Length && landings < 2; j++)
                {
                    float time = start + Mathf.Repeat(sample.time - start + j / 60f, end - start);
                    var future = SampleFootFrame(walk, time);
                    if (future.leftSole.x >= future.rightSole.x || future.leftKnee.x >= future.rightKnee.x) { valid = false; break; }
                    if (!previous.leftContact && future.leftContact) landings++;
                    if (!previous.rightContact && future.rightContact) landings++;
                    previous = future;
                }
                if (!valid || landings < 2) continue;
                float score = (sample.leftSole - left).sqrMagnitude + (sample.rightSole - right).sqrMagnitude;
                score += Mathf.Pow(Vector3.Angle(sample.leftHeading, leftHeading) / 30f, 2f) + Mathf.Pow(Vector3.Angle(sample.rightHeading, rightHeading) / 30f, 2f);
                var swing = liftingLeft ? next.leftSole - sample.leftSole : next.rightSole - sample.rightSole;
                if (swing.z + MoveAnimationGroundSpeed * (next.time - sample.time) < 0f) score += 4f;
                if (sample.leftContact != finish.leftContact) score += 4f;
                if (sample.rightContact != finish.rightContact) score += 4f;
                if (score >= best) continue;
                best = score; frontReturnEntryPhase = sample.time;
            }
            if (roaming) roamingWalkBlendUntil = Time.time + .25f;
            else
            {
                UseFrontEnvelope(walk);
                frontReturnBlendUntil = Time.time + .25f;
                frontReturnAligning = true;
            }
            moveState = legacyAnimation[moveClip];
            moveState.time = frontReturnEntryPhase; moveState.speed = 0f; moveState.wrapMode = WrapMode.Loop;
            legacyAnimation.CrossFade(moveClip, .25f); activeClip = moveClip;
            frontFeet?.BeginReturnHandoff();
            Debug.Log($"[T-Rex return blend] phase={frontReturnEntryPhase:F3}s, foot score={best:F4}, heading held for 0.25s; normal walk speed and steering resume after blend.", this);
        }
        private void FinishFrontInteraction()
        {
            ClearFrontRecovery();
            ClearSideRoarHandoff();
            CancelFrontWait();
            CancelFrontPeerClearance();
            frontActive = frontStopPending = false; frontAction = frontFollowMotion = null;
            frontNearPending = frontExitPending = false; frontTimingFollow = false;
            frontFollowBlockedAt = -1f; frontReturnBlockedAt = -1f;
            frontReturnAligning = false; frontReturnBlendUntil = 0f; frontReturnFreeWalkUntil = 0f;
            ClearFrontReturnTurnPlan();
            frontReturnHasHistory = false; frontReturnWalked = 0f; frontReturnIndex = -1;
            // A short trail may end mid-stride. Preserve this return's first-two-step handoff
            // through the following walk; it clears itself at the second landing.
            if (legacyAnimation[WaitClip] != null) legacyAnimation[WaitClip].enabled = false;
            interactionBody = frontOriginalBody;
            frontTrail.Clear();
            if (IsFreeRoaming) FinishFreeInteraction();
            else
            {
                // Route comparison mode must reconnect continuously instead of snapping to its centreline.
                ClearInteractionNavigation();
                InteractionPhase = PlayerInteractionPhase.Returning;
                cooldownUntil = Time.time + interactionCooldown;
                freeReplanFailures = 0;
                hasCuriosityPath = false; nextReplan = 0f;
            }
        }
        private void UpdateFrontVisibility(float deltaTime)
        {
            string failure = PlayerVisibilityFailure();
            if (failure != null || !FrontPlayerNear) frontNearPending = false;
            if (failure == null) { lastSeenPlayer = viewCamera.transform.position; invisibleTime = 0f; }
            // Opening and closing performances finish at their authored pose; only following,
            // near and side travel lose the interaction when the player leaves the sight cone.
            if (frontStage != FrontStage.Follow && frontStage != FrontStage.Near)
            { invisibleTime = 0f; return; }
            if (failure != null) invisibleTime += deltaTime;
            if (failure != null && invisibleTime >= lostSightGrace && !frontExitPending)
            { frontEndReason = failure; frontExitPending = true; frontNearPending = false; }
        }

        private void TickFrontFollowClock(float deltaTime)
        {
            if (!frontTimingFollow) return;
            frontFollowElapsed += deltaTime;
            if (frontFollowElapsed >= frontFollowDuration && !frontExitPending)
            { frontEndReason = "FollowTimeout"; frontExitPending = true; }
        }
        private bool TickFrontInteraction()
        {
            UpdateFrontReturnDiagnostics();
            UpdateFrontVisibility(Time.deltaTime);
            // Follow time includes Near, waiting, blocking and turns; resuming does not reset it.
            TickFrontFollowClock(Time.deltaTime);
            if (frontRecoveryActive)
            {
                // Finish an accepted turn before honouring a pending interaction exit.
                if (frontExitPending && !IsSteppingTurn && frontStage == FrontStage.Follow)
                { BeginFrontEnding(); return true; }
                TickFrontRecovery();
                return true;
            }
            // A blocked action cannot finish its blend. Leave the wait at its last accepted pose
            // before selecting a checked ending, rather than waiting forever after sight loss.
            if (frontExitPending && (frontWaiting || frontPeerClearing)
                && (frontStage == FrontStage.Intro || frontStage == FrontStage.Follow || frontStage == FrontStage.Near
                    || frontStage == FrontStage.Ending))
            { CancelFrontWait(); CancelFrontPeerClearance(); BeginFrontEnding(); return true; }
            if (frontPeerClearing) return TickFrontPeerClearance();
            if (frontWaiting) return TickFrontSpaceWait();
            if (EndBlockedFrontFollow()) return true;
            AdvanceSideRoarHandoff();
            RefreshFrontPoseEnvelope();
            if (frontStage == FrontStage.Follow)
            {
                if (FrontPlayerNear) frontNearPending = true;
            }
            if (IsSteppingTurn)
            {
                TickSteppingTurn();
                if (interactionMoved && IsFrontFollowing) frontFollowBlockedAt = -1f;
                return true;
            }
            if (frontStage == FrontStage.Intro || frontStage == FrontStage.Near || frontStage == FrontStage.Ending || frontStage == FrontStage.TurnBack)
            {
                var sequence = frontStage == FrontStage.Intro ? ActiveIntroSequence
                    : FrontExitSequence(frontNearBite);
                var role = frontStage == FrontStage.TurnBack ? InteractionMotionRole.Turn180Look : sequence[frontActionIndex];
                if (frontStage == FrontStage.Near)
                {
                    if (frontExitPending) { frontStage = FrontStage.Ending; frontTimingFollow = false; } // Keep the same action and progress.
                    if (role == InteractionMotionRole.LookForwardTwo && !frontExitPending
                        && FrontPlayerBeyondResume && CanResumeFrontFollow())
                    { BeginFrontFollow(true); return true; }
                }
                if (frontAction == null && !StartFrontAction(role))
                {
                    BeginFrontSpaceWait();
                    return true;
                }
                if (TickFrontAction())
                {
                    if (!frontActive) return true;
                    if (frontStage == FrontStage.Intro && frontExitPending)
                    { frontAction = null; BeginFrontEnding(); return true; }
                    frontAction = null;
                    if (frontStage == FrontStage.Near)
                    {
                        if (!frontExitPending && FrontPlayerBeyondResume && CanResumeFrontFollow())
                        { BeginFrontFollow(true); return true; }
                        if (++frontActionIndex == sequence.Length) BeginFrontReturn();
                        return true;
                    }
                    if (frontStage == FrontStage.TurnBack) BeginFrontReturnWalk();
                    else if (++frontActionIndex == sequence.Length)
                    {
                        frontActionIndex = 0;
                        if (frontStage == FrontStage.Intro) BeginFrontFollow();
                        else BeginFrontReturn();
                    }
                }
                return true;
            }
            if (frontStage == FrontStage.Follow)
            {
                if (frontStopPending || frontExitPending) { BeginFrontEnding(); return true; }
                if (frontNearPending)
                {
                    GetFootPhase(out float left, out float right);
                    if (left > .5f || right > .5f)
                    {
                        GetComponentInChildren<DinosaurFootLock>()?.HoldSupportDuringRecovery(.15f);
                        BeginFrontNear();
                        return true;
                    }
                }
                if (!frontNearPending && !frontExitPending && Time.time >= nextReplan
                    && (!hasCuriosityPath || FlatDistance(lastSeenPlayer, plannedPlayer) > playerReplanDistance))
                    PlanFrontFollow();
                bool moved = MoveFrontPath(FrontFollowSpeed, false);
                if (IsSteppingTurn) return true;
                if (frontWaiting || frontPeerClearing) return true;
                if (!moved)
                {
                    // Freeze the chase pose instead of running on the spot or inserting look forward.
                    legacyAnimation[frontFollowMotion.StateName].speed = 0f;
                    if (PathFinished())
                    {
                        if (FrontPlayerNear) frontNearPending = true;
                        else if (!frontExitPending)
                        {
                            // Reaching the last seen point is not a new sight or space failure.
                            frontBlockReason = "NavMesh / approach ends outside Near";
                            BeginFrontSpaceWait();
                        }
                        return true;
                    }
                    frontBlocked += Time.deltaTime;
                    BeginFrontSpaceWait();
                    return true;
                }
                var state = legacyAnimation[frontFollowMotion.StateName];
                state.speed = FrontFollowSpeed / Mathf.Max(.1f, frontFollowMotion.groundSpeed);
                if (!frontNearPending && !frontExitPending && state.time + Time.deltaTime * state.speed >= state.length)
                {
                    var end = SampleFootFrame(frontFollowMotion, state.length);
                    var start = SampleFootFrame(frontFollowMotion, 0f);
                    GetComponentInChildren<DinosaurFootLock>()?.BlendWalkSeam(end.leftSole - start.leftSole, end.rightSole - start.rightSole, .18f);
                    float advance = Time.deltaTime * state.speed;
                    state.time = Mathf.Repeat(state.time + advance, state.length) - advance;
                }
                if (state.time >= state.length - .0001f)
                {
                    if (frontExitPending) BeginFrontEnding();
                    else if (frontNearPending) BeginFrontNear();
                    else state.time = 0f;
                }
                return true;
            }
            // ReturnWalk: follow the recorded route backwards and leave when the walking
            // time is spent or the oldest recorded point is reached.
            if (Time.time < frontReturnBlendUntil)
            {
                actualInteractionSpeed = 0f;
                if (moveState != null) moveState.speed = 0f;
                // The guaranteed opening walk also covers the entry blend, so static scenery
                // cannot turn it back into a wait before the actor has covered ground.
                bool safe = Time.time < frontReturnFreeWalkUntil
                    ? FrontAnimationPoseSafe(transform.position, transform.position, transform.rotation, transform.rotation, interactionBody)
                    : FrontPoseSafe(transform.position, transform.position, transform.rotation, transform.rotation);
                if (!safe) BeginFrontSpaceWait();
                return true;
            }
            if (frontReturnWalked >= frontReturnWalkTime) { FinishFrontInteraction(); return true; }
            if (frontReturnIndex < 0)
            {
                if (frontReturnHasHistory) { FinishFrontInteraction(); return true; }
                if (!FindFrontClearReturnPoint()) { if (moveState != null) moveState.speed = 0f; BeginFrontSpaceWait(); return true; }
                frontReturnHasHistory = true;
            }
            // A body-sized connector must not launch another turn to chase the final sub-step distance.
            if (hasCuriosityPath && frontReturnIndex >= 0 && FlatDistance(transform.position, frontTrail[frontReturnIndex]) <= 1.2f)
            { frontReturnIndex--; hasCuriosityPath = false; }
            if (!hasCuriosityPath || PathFinished())
            {
                AdvanceFrontReturnTarget();
                if (frontReturnIndex < 0)
                {
                    if (frontReturnHasHistory) { FinishFrontInteraction(); return true; }
                    if (!FindFrontClearReturnPoint()) { if (moveState != null) moveState.speed = 0f; BeginFrontSpaceWait(); return true; }
                    frontReturnHasHistory = true;
                }
                if (!PlanFrontConnection(frontTrail[frontReturnIndex], true))
                {
                    if (moveState != null) moveState.speed = 0f;
                    BeginFrontSpaceWait();
                    return true;
                }
            }
            bool walking = MoveFrontPath(WalkSpeed, true);
            if (walking) { frontReturnWalked += Time.deltaTime; PlayMovementClip(WalkSpeed); }
            else if (!IsSteppingTurn && !frontWaiting && !frontPeerClearing)
            {
                if (moveState != null) moveState.speed = 0f;
                if (frontBlocked >= 2f) { hasCuriosityPath = false; BeginFrontSpaceWait(); }
            }
            return true;
        }
    }
}
