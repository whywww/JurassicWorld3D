using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        private enum FrontStage { Intro, Follow, Near, Ending, TurnBack, ReturnWalk, SideStartRun }
        private static readonly InteractionMotionRole[] FrontIntro = { InteractionMotionRole.Investigate,
            InteractionMotionRole.InvestigateOut, InteractionMotionRole.BigRoarStep };
        [Header("Front interaction")]
        [SerializeField, Min(0f)] private float frontDetectionDistance = 32f;
        [SerializeField, Min(0f)] private float frontNearDistance = 10f;
        [SerializeField, Min(0f)] private float frontResumeDistance = 15f;
        [SerializeField, Min(1f)] private float frontFollowDuration = 30f;
        [SerializeField, Min(0f)] private float frontReturnWalkTime = 10f;
        private bool frontActive, frontStopPending, frontActionHeld;
        private FrontStage frontStage;
        private int frontActionIndex, frontReturnIndex, frontFailures;
        private float frontActionTime, frontBlocked, frontReturnWalked;
        private Vector3 frontActionOrigin;
        private Quaternion frontActionRotation;
        private DinosaurInteractionProfile.Motion frontAction, frontFollowMotion;
        private DinosaurCoreBody frontOriginalBody, frontEnvelope;
        private DinosaurInteractionProfile.Motion frontEnvelopeMotion;
        private float frontEnvelopeBlendUntil;
        private bool frontEnvelopeSettled;
        private bool frontNearPending, frontExitPending;
        private float frontFollowElapsed;
        private InteractionMotionRole frontNearBite;
        private float FrontFollowSpeed => WalkSpeed * (sideRoarHandoffActive
            ? Mathf.SmoothStep(0f, 1f, sideRoarHandoffTime / Mathf.Max(.01f, sideRoarHandoffDuration)) : 1f);
        private const float FrontSteppingTurnAngle = 60f;
        private float frontReturnBlendUntil, frontReturnEntryPhase;
        private bool frontReturnAligning;
        private DinosaurFootLock frontFeet;
        internal bool FinishingReturnFootsteps => !frontActive && InteractionPhase == PlayerInteractionPhase.Inactive && frontFeet != null && frontFeet.ReturnHandoffActive;
        internal bool ReturnFootHandoffMoving => !frontWaiting && !IsSteppingTurn && (frontActive && frontStage == FrontStage.ReturnWalk && Time.time >= frontReturnBlendUntil
            || FinishingReturnFootsteps && CurrentState == DinosaurState.RandomWalk && InteractionPhase == PlayerInteractionPhase.Inactive);
        internal bool UseReturnSoleHeading => frontActive && frontStage == FrontStage.TurnBack
            || frontFeet != null && frontFeet.ReturnHandoffActive;
        private readonly List<Vector3> frontTrail = new List<Vector3>();
        public string FrontInteractionStage => frontActive ? frontStage + (frontWaiting
            ? frontWaitReason == "Peer" ? " (WaitingForPeerClearance)" : " (TryingToDetour)"
            : frontStaticPass ? " (PassingStaticObstacles)" : "") : "Inactive";
        public float FrontActionPlaybackTime => frontWaiting && frontWaitPlaying ? frontWaitTime : frontAction != null ? frontActionTime
            : (frontStage == FrontStage.Follow || IsSideLocomotion) && frontFollowMotion != null && legacyAnimation != null
                ? legacyAnimation[frontFollowMotion.StateName].time : frontStage == FrontStage.ReturnWalk && moveState != null ? moveState.time : 0f;
        public bool FrontActionWaitingForSpace => frontActive && (frontWaiting || frontBlocked > 0f);
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
                if (activeClip == WaitIdleClip) return idleClip + " (peer wait)";
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
            if (!frontTestControlled || !IsWaitingForInteraction || !interactionReady || !FrontActionsAvailable()) return false;
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
            sideTestSequence = fromSide;
            sideFromLeft = fromSide && fromLeft;
            ClearSideRoarHandoff();
            ClearSideWalkBlend();
            frontFeet?.EndReturnHandoff();
            ClearInteractionNavigation();
            frontOriginalBody = interactionBody;
            if (frontEnvelope == null)
            {
                var holder = new GameObject("Front action envelope") { hideFlags = HideFlags.DontSave };
                holder.transform.SetParent(transform, false);
                frontEnvelope = holder.AddComponent<DinosaurCoreBody>();
            }
            frontActive = true; frontStage = FrontStage.Intro; frontActionIndex = frontFailures = 0;
            frontStopPending = false; frontAction = null; frontFollowMotion = null;
            frontNearPending = frontExitPending = false; frontFollowElapsed = 0f;
            frontFollowBlockedAt = -1f;
            frontSpaceExit = frontWaiting = false; frontAheadFailures = 0; frontReservationAt = 0f;
            frontStaticPass = frontWaitPlaying = false; frontWaitReason = frontBlockReason = null;
            frontReturnAligning = false; frontReturnBlendUntil = 0f;
            frontAheadFailureAt = -1f;
            PrepareFrontTurnPrediction();
            frontTrail.Clear(); frontBlocked = invisibleTime = visibleTime = 0f;
            lastSeenPlayer = viewCamera.transform.position;
            CurrentState = DinosaurState.RandomWalk;
            InteractionPhase = PlayerInteractionPhase.FrontSequence;
            waitingAtCrossing = false; MovementCorridorCrossings.Release(this);
        }
        private void UseFrontEnvelope(DinosaurInteractionProfile.Motion motion)
        {
            var bands = new Dictionary<int, Bounds>();
            foreach (var b in motion.bodyBands) bands[b.layer] = b.bounds;
            // Include the outgoing pose during blending, without constraining all roaming by every performance.
            foreach (var candidate in interactionProfile.motions)
            {
                var state = legacyAnimation[candidate.StateName];
                if (state == null || !state.enabled || state.weight < .01f) state = legacyAnimation[candidate.sourceName];
                if (state == null || !state.enabled || state.weight < .01f) continue;
                foreach (var b in candidate.bodyBands)
                {
                    if (bands.TryGetValue(b.layer, out var bounds)) { bounds.Encapsulate(b.bounds); bands[b.layer] = bounds; }
                    else bands[b.layer] = b.bounds;
                }
            }
            var result = new List<DinosaurCoreBody.Band>();
            foreach (var b in bands) result.Add(new DinosaurCoreBody.Band { layer = b.Key, bounds = b.Value });
            frontEnvelope.bands = result.ToArray();
            interactionBody = frontEnvelope;
            frontEnvelopeMotion = motion; frontEnvelopeSettled = false;
            frontEnvelopeBlendUntil = Time.time + (motion.role == InteractionMotionRole.Walk ? .35f : motion.blendDuration);
            if (sideRoarHandoffActive)
            {
                foreach (var role in new[] { InteractionMotionRole.SideRoarLeft, InteractionMotionRole.SideRoarRight })
                {
                    var extra = interactionProfile.Find(role);
                    if (extra == null) continue;
                    foreach (var b in extra.bodyBands)
                    {
                        if (bands.TryGetValue(b.layer, out var bounds)) { bounds.Encapsulate(b.bounds); bands[b.layer] = bounds; }
                        else bands[b.layer] = b.bounds;
                    }
                }
                result.Clear();
                foreach (var b in bands) result.Add(new DinosaurCoreBody.Band { layer = b.Key, bounds = b.Value });
                frontEnvelope.bands = result.ToArray();
                frontEnvelopeBlendUntil = float.PositiveInfinity;
            }
        }
        private bool FrontPoseSafe(Vector3 from, Vector3 to, Quaternion fromRotation, Quaternion toRotation, DinosaurCoreBody.Band[] playerBands = null)
        {
            return FrontStepSafe(from, to, fromRotation, toRotation, interactionBody, playerBands);
        }
        private void FrontActionPose(DinosaurInteractionProfile.FootFrame sample, out Vector3 position, out Quaternion rotation)
        {
            position = frontActionOrigin + frontActionRotation * Quaternion.Euler(0f, VisualYaw, 0f)
                * new Vector3(sample.rootOffset.x, 0f, sample.rootOffset.y);
            position.y = MovementHeight;
            rotation = Quaternion.Euler(0f, sample.yaw, 0f) * frontActionRotation;
            if (frontActionHeld) position = frontActionOrigin;
        }
        private bool StartFrontAction(InteractionMotionRole role)
        {
            var motion = interactionProfile.Find(role);
            if (role == InteractionMotionRole.Turn180Look && !FrontTurnSpace(transform.position, transform.rotation)) return false;
            frontAction = motion;
            UseFrontEnvelope(motion);
            frontActionOrigin = transform.position; frontActionRotation = transform.rotation; frontActionHeld = false;
            Vector3 previous = transform.position; Quaternion previousRotation = transform.rotation;
            bool safe = true;
            var initial = SampleFootFrame(motion, 0f);
            safe = FrontPoseSafe(previous, previous, previousRotation, previousRotation, initial.playerBodyBands);
            if (!safe)
            {
                // Stationary performances may hold their root; the full animated body envelope remains checked.
                if (frontStage != FrontStage.Intro || Mathf.Abs(motion.turnDegrees) > 1f || !FrontPoseSafe(transform.position, transform.position, transform.rotation, transform.rotation))
                { frontAction = null; return false; }
                frontActionHeld = true;
            }
            frontAction = motion; frontActionTime = frontBlocked = 0f;
            var state = legacyAnimation[motion.StateName];
            // Re-enter a previously clamped state independently of its previous fade/weight.
            state.enabled = false; state.weight = 0f;
            state.time = 0f; state.speed = 0f; state.wrapMode = WrapMode.ClampForever;
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
            if (!FrontPoseSafe(transform.position, position, transform.rotation, rotation, sample.playerBodyBands))
            {
                frontBlocked += Time.deltaTime;
                BeginFrontSpaceWait();
                return false;
            }
            frontBlocked = 0f;
            interactionMoved = Vector3.Distance(transform.position, position) > .00001f;
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
            bool handoff = sideTestSequence && !resume && frontStage == FrontStage.Intro;
            ClearSideRoarHandoff();
            if (handoff) BeginSideRoarHandoff();
            ClearSideWalkBlend();
            frontStage = FrontStage.Follow;
            frontStaticPass = false; frontFollowBlockedAt = -1f;
            frontAction = null; frontFollowMotion = null; frontFailures = 0;
            if (!resume) { frontTrail.Clear(); frontTrail.Add(transform.position); }
            frontStopPending = frontNearPending = frontExitPending = false; hasCuriosityPath = false;
            invisibleTime = 0f; nextReplan = 0f;
            PlayFrontFollow();
            if (!PlanFrontFollow() || !RefreshFrontReservation(true)) BeginFrontSpaceWait();
        }
        private bool PlanFrontFollow()
        {
            plannedPlayer = lastSeenPlayer; nextReplan = Time.time + Mathf.Max(.5f, replanInterval);
            Vector3 away = transform.position - lastSeenPlayer; away.y = 0f;
            if (away.sqrMagnitude < .01f) away = -GetVisualForward();
            bool planned = PlanFrontConnection(lastSeenPlayer + away.normalized * frontNearDistance, false);
            if (planned) RefreshFrontReservation(true);
            else NoteFrontFollowBlock();
            return planned;
        }
        private void RecordFrontTrail()
        {
            if (frontTrail.Count == 0 || Vector3.Distance(frontTrail[frontTrail.Count - 1], transform.position) >= .5f) frontTrail.Add(transform.position);
        }
        private bool MoveFrontPath(float speed, bool returning)
        {
            actualInteractionSpeed = 0f;
            if (!hasCuriosityPath) { frontBlockReason = "NavMesh"; BeginFrontSpaceWait(); return false; }
            if (PathFinished()) return false;
            Vector3 direction = curiosityCorners[curiosityCorner] - transform.position; direction.y = 0f;
            float angle = Vector3.SignedAngle(GetVisualForward(), direction, Vector3.up);
            if (Mathf.Abs(angle) >= FrontSteppingTurnAngle && !IsSideLocomotion
                && !(SideHandoffMoving && sideRoarHandoffTime < sideRoarHandoffDuration))
            {
                if (!TryBeginSteppingTurn(angle, returning ? TurnUse.FrontReturn : TurnUse.FrontFollow)) BeginFrontSpaceWait();
                return false;
            }
            float turnRate = 65f;
            if (!returning) turnRate = SideHandoffTurnRate(turnRate);
            Quaternion rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction) * Quaternion.Euler(0f, -VisualYaw, 0f), turnRate * Time.deltaTime);
            Vector3 heading = rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
            Vector3 position = transform.position + heading * Mathf.Min(direction.magnitude, speed * Time.deltaTime);
            if (!returning) position = SideHandoffPivotPosition(position, rotation);
            if (!FrontPoseSafe(transform.position, position, transform.rotation, rotation)) { BeginFrontSpaceWait(); return false; }
            actualInteractionSpeed = Vector3.Distance(transform.position, position) / Mathf.Max(.001f, Time.deltaTime);
            transform.SetPositionAndRotation(position, rotation); interactionMoved = actualInteractionSpeed > .001f;
            if (!returning && interactionMoved) frontFollowBlockedAt = -1f;
            if (returning && frontReturnAligning && Mathf.Abs(angle) <= 2f) frontReturnAligning = false;
            frontBlocked = 0f;
            if (frontStaticPass && FrontClearOfStatic()) frontStaticPass = false;
            if (!returning) RecordFrontTrail();
            return interactionMoved;
        }
        private void BeginFrontEnding()
        {
            if (frontStage == FrontStage.Ending || frontStage == FrontStage.TurnBack || frontStage == FrontStage.ReturnWalk) return;
            frontStopPending = true;
            if (IsSteppingTurn) return;
            ClearSideRoarHandoff();
            ClearSideWalkBlend();
            if (!ChooseFrontBite()) { BeginFrontSpaceWait(); return; }
            RecordFrontTrail(); frontTrail.Add(transform.position);
            frontStage = FrontStage.Ending; frontActionIndex = 0; frontAction = null;
            frontStaticPass = false; frontFollowBlockedAt = -1f;
            frontBlocked = 0f; frontFailures = 0; hasCuriosityPath = false;
        }
        private void BeginFrontNear()
        {
            ClearSideRoarHandoff();
            ClearSideWalkBlend();
            if (!ChooseFrontBite()) { BeginFrontSpaceWait(); return; }
            RecordFrontTrail();
            frontStage = FrontStage.Near; frontActionIndex = 0; frontAction = null;
            frontStaticPass = false; frontFollowBlockedAt = -1f;
            frontNearPending = false; frontBlocked = 0f; frontFailures = 0; hasCuriosityPath = false;
        }
        private void BeginFrontReturn()
        {
            ClearSideRoarHandoff();
            ClearSideWalkBlend();
            frontStage = FrontStage.TurnBack; frontAction = null; frontTrail.Add(transform.position);
            frontStaticPass = false; frontFollowBlockedAt = -1f;
            frontStopPending = frontNearPending = frontExitPending = false;
            frontBlocked = 0f; frontFailures = 0; hasCuriosityPath = false;
        }
        private void BeginFrontReturnWalkBlend()
        {
            var walk = interactionProfile.Find(InteractionMotionRole.Walk);
            var turn = interactionProfile.Find(InteractionMotionRole.Turn180Look);
            var finish = SampleFootFrame(turn, turn.clip.length);
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
            UseFrontEnvelope(walk);
            frontReturnBlendUntil = frontEnvelopeBlendUntil = Time.time + .25f;
            frontReturnAligning = true;
            moveState = legacyAnimation[moveClip];
            moveState.time = frontReturnEntryPhase; moveState.speed = 0f; moveState.wrapMode = WrapMode.Loop;
            legacyAnimation.CrossFade(moveClip, .25f); activeClip = moveClip;
            frontFeet?.BeginReturnHandoff();
            Debug.Log($"[T-Rex return blend] phase={frontReturnEntryPhase:F3}s, foot score={best:F4}, heading held for 0.25s; normal walk speed and steering resume after blend.", this);
        }
        private void FinishFrontInteraction()
        {
            ClearSideRoarHandoff();
            ClearSideWalkBlend();
            frontActive = frontStopPending = false; frontAction = frontFollowMotion = null;
            frontNearPending = frontExitPending = false;
            frontWaiting = frontSpaceExit = false;
            frontStaticPass = frontWaitPlaying = false; frontWaitReason = frontBlockReason = null;
            frontFollowBlockedAt = -1f;
            frontReturnAligning = false; frontReturnBlendUntil = 0f;
            // A short trail may end mid-stride. Preserve this return's first-two-step handoff
            // through the following walk; it clears itself at the second landing.
            if (legacyAnimation[WaitClip] != null) legacyAnimation[WaitClip].enabled = false;
            if (legacyAnimation[WaitIdleClip] != null) legacyAnimation[WaitIdleClip].enabled = false;
            interactionBody = frontOriginalBody;
            frontTrail.Clear(); ClearInteractionNavigation();
            InteractionPhase = PlayerInteractionPhase.Inactive;
            cooldownUntil = Time.time + interactionCooldown;
            isReturning = false; freeReplanFailures = 0;
            // No second idle period after the return walk.
            if (IsFreeRoaming) { if (!PickFreeGoal()) FreeIdle(2f); }
            else
            {
                // Route comparison mode must reconnect continuously instead of snapping to its centreline.
                InteractionPhase = PlayerInteractionPhase.Returning;
                isReturning = true; hasCuriosityPath = false; nextReplan = 0f;
            }
        }
        private bool TickFrontInteraction()
        {
            if (frontWaiting) return TickFrontSpaceWait();
            if (EndBlockedFrontFollow()) return true;
            AdvanceSideRoarHandoff();
            if (!frontEnvelopeSettled && frontEnvelopeMotion != null && Time.time >= frontEnvelopeBlendUntil)
            {
                frontEnvelope.bands = frontEnvelopeMotion.bodyBands;
                frontEnvelopeSettled = true;
            }
            if (frontStage == FrontStage.Follow || sideTestSequence && IsSideChasing)
            {
                if (CanSeePlayer()) { lastSeenPlayer = viewCamera.transform.position; invisibleTime = 0f; }
                else invisibleTime += Time.deltaTime;
                if (invisibleTime >= lostSightGrace || frontFollowElapsed >= frontFollowDuration)
                    frontExitPending = true;
                if (FlatDistance(transform.position, viewCamera.transform.position) <= frontNearDistance)
                    frontNearPending = true;
            }
            if (IsSteppingTurn)
            {
                if (frontExitPending || frontNearPending) stopAfterStepTurn = true;
                TickSteppingTurn();
                if (interactionMoved && IsFrontFollowing) frontFollowBlockedAt = -1f;
                return true;
            }
            if (sideTestSequence && IsSideLocomotion) return TickSideLocomotion();
            if (frontStage == FrontStage.Intro || frontStage == FrontStage.Near || frontStage == FrontStage.Ending || frontStage == FrontStage.TurnBack)
            {
                var sequence = frontStage == FrontStage.Intro ? ActiveIntroSequence
                    : FrontExitSequence(frontNearBite);
                var role = frontStage == FrontStage.TurnBack ? InteractionMotionRole.Turn180Look : sequence[frontActionIndex];
                if (frontStage == FrontStage.Near)
                {
                    if (CanSeePlayer()) { lastSeenPlayer = viewCamera.transform.position; invisibleTime = 0f; }
                    else invisibleTime += Time.deltaTime;
                    if (invisibleTime >= lostSightGrace) frontExitPending = true;
                    if (frontExitPending) frontStage = FrontStage.Ending; // Keep the same action and progress.
                    if (role == InteractionMotionRole.LookForwardTwo && !frontExitPending
                        && FlatDistance(transform.position, viewCamera.transform.position) > frontResumeDistance && CanResumeFrontFollow())
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
                    frontAction = null; frontFailures = 0;
                    if (frontStage == FrontStage.Near)
                    {
                        if (FlatDistance(transform.position, viewCamera.transform.position) > frontResumeDistance && CanResumeFrontFollow())
                        { BeginFrontFollow(true); return true; }
                        if (++frontActionIndex == sequence.Length) BeginFrontReturn();
                        return true;
                    }
                    if (frontStage == FrontStage.TurnBack)
                    {
                        frontStage = FrontStage.ReturnWalk; frontReturnIndex = frontTrail.Count - 2;
                        frontStaticPass = false;
                        frontReturnWalked = frontBlocked = 0f; hasCuriosityPath = false;
                        BeginFrontReturnWalkBlend();
                    }
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
                if (!RefreshFrontReservation()) { BeginFrontSpaceWait(); return true; }
                if (frontStopPending) { BeginFrontEnding(); return true; }
                if (frontNearPending || frontExitPending)
                {
                    GetFootPhase(out float left, out float right);
                    if (left > .5f || right > .5f)
                    {
                        GetComponentInChildren<DinosaurFootLock>()?.HoldSupportDuringRecovery(.15f);
                        if (frontExitPending) BeginFrontEnding(); else BeginFrontNear();
                        return true;
                    }
                }
                if (!frontNearPending && !frontExitPending && Time.time >= nextReplan
                    && (!hasCuriosityPath || FlatDistance(lastSeenPlayer, plannedPlayer) > playerReplanDistance))
                    if (!PlanFrontFollow()) frontFailures++;
                bool moved = MoveFrontPath(FrontFollowSpeed, false);
                if (IsSteppingTurn) return true;
                if (!moved)
                {
                    bool blockedExitReady = frontBlocked >= 2f;
                    // Freeze the chase pose instead of running on the spot or inserting look forward.
                    legacyAnimation[frontFollowMotion.StateName].speed = 0f;
                    if (PathFinished())
                    {
                        if (FlatDistance(transform.position, viewCamera.transform.position) <= frontResumeDistance) frontNearPending = true;
                        else frontExitPending = true;
                        frontBlocked += Time.deltaTime;
                    }
                    else if (frontBlocked >= 2f)
                    {
                        frontBlocked = 0f;
                        if (frontNearPending || frontExitPending)
                            GetComponentInChildren<DinosaurFootLock>()?.HoldSupportDuringRecovery(.35f);
                        else if (++frontFailures >= 2 || !PlanFrontFollow()) frontExitPending = true;
                    }
                    if (frontNearPending || frontExitPending)
                    {
                        GetFootPhase(out float left, out float right);
                        if (left > .5f || right > .5f || blockedExitReady)
                        {
                            if (left <= .5f && right <= .5f) GetComponentInChildren<DinosaurFootLock>()?.HoldSupportDuringRecovery(.35f);
                            if (frontExitPending) BeginFrontEnding(); else BeginFrontNear();
                        }
                    }
                }
                else
                {
                    var state = legacyAnimation[frontFollowMotion.StateName];
                    frontFollowElapsed += Time.deltaTime;
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
                }
                return true;
            }
            if (Time.time < frontReturnBlendUntil)
            {
                actualInteractionSpeed = 0f;
                if (moveState != null) moveState.speed = 0f;
                if (!FrontPoseSafe(transform.position, transform.position, transform.rotation, transform.rotation)) BeginFrontSpaceWait();
                return true;
            }
            if ((frontReturnWalked >= frontReturnWalkTime || frontReturnIndex < 0) && FrontReadyToRoam()) { FinishFrontInteraction(); return true; }
            if (frontReturnIndex < 0)
            {
                if (!FindFrontClearReturnPoint()) { frontBlockReason = "NavMesh"; BeginFrontSpaceWait(); return true; }
            }
            // A body-sized connector must not launch another turn to chase the final sub-step distance.
            if (hasCuriosityPath && frontReturnIndex >= 0 && FlatDistance(transform.position, frontTrail[frontReturnIndex]) <= 1.2f)
            { frontReturnIndex--; hasCuriosityPath = false; }
            if (!hasCuriosityPath || PathFinished())
            {
                while (frontReturnIndex >= 0 && FlatDistance(transform.position, frontTrail[frontReturnIndex]) < 1.2f) frontReturnIndex--;
                if (frontReturnIndex < 0)
                {
                    if (FrontReadyToRoam()) { FinishFrontInteraction(); return true; }
                    if (!FindFrontClearReturnPoint()) { frontBlockReason = "NavMesh"; BeginFrontSpaceWait(); return true; }
                }
                // Dense recorded samples are a path, not individual stopping/turning destinations.
                // Look back several metres so the 180 turn's lateral step joins smoothly.
                while (frontReturnIndex > 0)
                {
                    Vector3 aim = frontTrail[frontReturnIndex] - transform.position; aim.y = 0f;
                    if (aim.magnitude >= 4f) break;
                    frontReturnIndex--;
                }
                // Connect to the next earlier recorded point, retaining current obstacle checks.
                if (!PlanFrontConnection(frontTrail[frontReturnIndex], true))
                {
                    if (moveState != null) moveState.speed = 0f;
                    BeginFrontSpaceWait();
                    return true;
                }
            }
            bool walking = MoveFrontPath(WalkSpeed, true);
            if (walking) { frontReturnWalked += Time.deltaTime; PlayMovementClip(WalkSpeed); }
            else if (!IsSteppingTurn)
            {
                if (moveState != null) moveState.speed = 0f;
                if (frontBlocked >= 2f) { hasCuriosityPath = false; BeginFrontSpaceWait(); }
            }
            return true;
        }
    }
}

