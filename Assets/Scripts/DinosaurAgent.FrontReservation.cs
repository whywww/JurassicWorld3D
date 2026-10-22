using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        private float frontSavedWalkTime, frontSavedReturnTime;
        private DinosaurCoreBody frontProbeBody;
        private float frontRetryAt, frontWaitTime;
        private bool frontWaiting, frontWaitPlaying;
        private string frontBlockReason;
        public string FrontBlockingReason => frontBlockReason ?? frontWaitReason ?? "";
        public string LastFrontSpaceFailure => frontLastSpaceFailure ?? "--";
        private const string WaitClip = "FrontSpaceWait";
        private float frontWaitBegan;
        private int frontWaitReplans;
        private string frontWaitReason;
        private float frontFollowBlockedAt = -1f;
        private float frontReturnBlockedAt = -1f;
        private DinosaurAgent frontBlockingPeer;
        private bool frontPeerClearing, frontPeerYielding;
        private float frontPeerBlockedTime;
        private bool IsFrontFollowing => frontStage == FrontStage.Follow;

        private void NoteFrontFollowBlock()
        {
            if (!IsFrontFollowing) return;
            if (FrontStaticBlock(frontBlockReason))
            {
                if (frontFollowBlockedAt < 0f) frontFollowBlockedAt = Time.time;
            }
            else if (frontBlockReason != null) frontFollowBlockedAt = -1f;
        }
        private bool EndBlockedFrontFollow()
        {
            // Persistent static travel blocks now search for an animated escape first.
            if (frontRecoveryActive || frontWaiting && FrontStaticBlock(frontWaitReason)) return false;
            if (!IsFrontFollowing || frontFollowBlockedAt < 0f || Time.time - frontFollowBlockedAt < interactionSettings.spaceFailureTimeout) return false;
            // One last attempt from the actual pose. A peer yield is not static unreachability.
            bool planned = PlanFrontFollow();
            if (planned)
            {
                frontFollowBlockedAt = -1f; frontWaitReplans = 0;
                return false;
            }
            if (IsSteppingTurn)
            {
                if (stepTurnState != null) stepTurnState.enabled = false;
                ClearSteppingTurn();
            }
            GetComponentInChildren<DinosaurFootLock>()?.HoldSupportDuringRecovery(.35f);
            frontWaiting = frontWaitPlaying = false;
            frontPeerClearing = frontPeerYielding = false; frontBlockingPeer = null;
            Debug.Log($"[T-Rex front] Follow blocked: reason={frontBlockReason ?? frontWaitReason}, position={transform.position:F3}, walked={frontFollowElapsed:F3}s", this);
            frontEndReason = "Static path unreachable";
            frontExitPending = true; frontNearPending = false;
            actualInteractionSpeed = 0f; interactionMoved = false;
            BeginFrontEnding();
            return true;
        }
        private string frontWaitClip;
        private bool frontWaitHoldingPose;
        private bool FrontWaitBlendActive => frontWaitPlaying && !frontWaitHoldingPose && !string.IsNullOrEmpty(frontWaitClip)
            && legacyAnimation[frontWaitClip] != null && legacyAnimation[frontWaitClip].enabled && legacyAnimation[frontWaitClip].weight > .01f;
        private bool FrontStaticBlock(string reason) => reason != null && reason != "Peer";

        private bool PlanFrontConnection(Vector3 target, bool completeOnly)
        {
            frontExitQueriesTotal++; frontExitQueriesThisFrame++;
            frontReturnConnection = transform.position;
            if (PlanCuriosityPath(target, completeOnly, true)) { frontExitFailure = null; return true; }
            frontExitSearches++;
            frontExitFailure = string.IsNullOrEmpty(RoamingBlockReason) ? "NavMesh" : RoamingBlockReason;
            frontBlockReason = string.IsNullOrEmpty(RoamingBlockReason) ? "NavMesh" : RoamingBlockReason;
            return false;
        }
        private bool FindFrontClearReturnPoint()
        {
            Vector3 forward = GetVisualForward();
            var body = frontOriginalBody != null ? frontOriginalBody : interactionBody;
            for (int ring = 1; ring <= 8; ring++) for (int bearing = 0; bearing < 8; bearing++)
            {
                Vector3 candidate = transform.position + Quaternion.Euler(0f, bearing * 45f, 0f) * forward * (ring * 4f);
                if (!NavMesh.SamplePosition(candidate, out var hit, 2f, NavigationFilter)) continue;
                Vector3 direction = hit.position - transform.position; direction.y = 0f;
                if (direction.sqrMagnitude < .01f) continue;
                direction.Normalize();
                Quaternion heading = Quaternion.LookRotation(direction) * Quaternion.Euler(0f, -VisualYaw, 0f);
                if (interactionSettings.PoseBlocked(new Vector2(hit.position.x, hit.position.z), new Vector2(direction.x, direction.z), body)
                    || interactionSettings.PoseBlocked(new Vector2(hit.position.x, hit.position.z), new Vector2(direction.x, direction.z), interactionBody)
                    || FrontPeerPoseBlocked(hit.position, heading, body)) continue;
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(transform.position, hit.position, NavigationFilter, path)
                    || path.status != NavMeshPathStatus.PathComplete) continue;
                // Fallback routes are only useful if the whole walk already passes the static
                // clearance test; otherwise the first step stalls at the same obstruction.
                if (!PlanCuriosityPath(hit.position, true)) continue;
                frontTrail.Insert(0, hit.position); frontReturnIndex = 0;
                frontReturnConnection = transform.position;
                return true;
            }
            return false;
        }

        /// <summary>Walking gate: trees, solid obstacles, activity bounds, NavMesh and peer bodies.</summary>
        private bool FrontStepSafe(Vector3 from, Vector3 to, Quaternion fromRotation, Quaternion toRotation, DinosaurCoreBody body,
            DinosaurCoreBody.Band[] playerBands = null)
        {
            // Players do not obstruct this interaction; retained pose data still serves animation calibration.
            if (trafficMoving && !GroundPeerSweepSafe(from, to, fromRotation, toRotation, body))
            { frontBlockReason = "Peer"; return false; }
            int count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / .1f), Mathf.CeilToInt(Quaternion.Angle(fromRotation, toRotation) / 2f));
            for (int i = 0; i <= count; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, (float)i / count);
                Quaternion q = Quaternion.Slerp(fromRotation, toRotation, (float)i / count);
                Vector3 f = q * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
                if (!trafficMoving && FrontPeerPoseBlocked(p, q, body, out var blocker))
                { frontBlockingPeer = blocker; frontBlockReason = "Peer"; return false; }
                string reason = interactionSettings.PoseBlockReason(new Vector2(p.x, p.z), new Vector2(f.x, f.z), body);
                if (reason != null) { RememberFrontSpaceFailure(reason, p); return false; }
            }
            var blockingStep = FindBlockingStep(new Vector2(from.x, from.z), new Vector2(to.x, to.z));
            if (blockingStep != null) { frontBlockingPeer = blockingStep; frontBlockReason = "Peer"; return false; }
            // Performances may end with the root slightly outside the baked mesh;
            // front walking may step back onto it but never farther away.
            if (!NavigationStepFits(from, to, frontActive))
            { frontBlockReason = "NavMesh"; return false; }
            return true;
        }

        /// <summary>Animation gate: other dinosaur bodies only. Static clipping is accepted for performances.</summary>
        private bool FrontAnimationPoseSafe(Vector3 from, Vector3 to, Quaternion fromRotation, Quaternion toRotation, DinosaurCoreBody body)
        {
            int count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / .1f), Mathf.CeilToInt(Quaternion.Angle(fromRotation, toRotation) / 2f));
            for (int i = 0; i <= count; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, (float)i / count);
                Quaternion q = Quaternion.Slerp(fromRotation, toRotation, (float)i / count);
                if (FrontPeerPoseBlocked(p, q, body, out var blocker))
                { frontBlockingPeer = blocker; frontBlockReason = "Peer"; return false; }
            }
            return true;
        }

        private bool FrontPeerPoseBlocked(Vector3 position, Quaternion rotation, DinosaurCoreBody body)
            => FrontPeerPoseBlocked(position, rotation, body, out _);
        private bool FrontPeerPoseBlocked(Vector3 position, Quaternion rotation, DinosaurCoreBody body, out DinosaurAgent blocker)
        {
            blocker = null;
            if (body == null) return false;
            foreach (var peer in peers)
            {
                if (!IsCollisionPeer(peer) || blocker != null && !peer.HasRightOfWay(blocker)) continue;
                if (PeerPenetration(position, rotation, body, peer) > 0f) blocker = peer;
            }
            return blocker != null;
        }
        private void EnsureFrontProbe()
        {
            if (frontProbeBody != null) return;
            var holder = new GameObject("Front interaction probe") { hideFlags = HideFlags.DontSave };
            holder.transform.SetParent(transform, false); frontProbeBody = holder.AddComponent<DinosaurCoreBody>();
        }

        private bool ChooseFrontBite()
        {
            frontNearBite = Random.value < .5f ? InteractionMotionRole.BiteCloseLeft : InteractionMotionRole.BiteCloseRight;
            return true;
        }
        private static readonly InteractionMotionRole[] FrontLeftExit =
            { InteractionMotionRole.Roar, InteractionMotionRole.LookForwardTwo, InteractionMotionRole.BiteCloseLeft, InteractionMotionRole.LookForwardTwo };
        private static readonly InteractionMotionRole[] FrontRightExit =
            { InteractionMotionRole.Roar, InteractionMotionRole.LookForwardTwo, InteractionMotionRole.BiteCloseRight, InteractionMotionRole.LookForwardTwo };
        private InteractionMotionRole[] FrontExitSequence(InteractionMotionRole bite)
            => bite == InteractionMotionRole.BiteCloseLeft ? FrontLeftExit : FrontRightExit;
        private bool CanResumeFrontFollow()
        {
            if (frontExitPending || frontStopPending || !CanSeePlayer() || !PlanFrontFollow()) return false;
            return true;
        }

        private void BeginFrontSpaceWait()
        {
            NoteFrontFollowBlock();
            if (frontWaiting) return;
            // A lower-priority dinosaur actively yields instead of holding the interaction.
            if (frontBlockReason == "Peer" && frontBlockingPeer != null && !HasRightOfWay(frontBlockingPeer))
            { BeginFrontPeerClearance(frontBlockingPeer, true); return; }
            if (frontStage == FrontStage.Follow && frontFollowMotion != null)
                frontSavedWalkTime = legacyAnimation[frontFollowMotion.StateName].time;
            frontSavedReturnTime = moveState != null ? moveState.time : 0f;
            frontWaiting = true; frontRetryAt = Time.time + Mathf.Max(.05f, interactionSettings.spaceSearchInterval);
            frontWaitBegan = Time.time; frontWaitReplans = 0;
            frontWaitReason = frontBlockReason;
            if (frontStage == FrontStage.ReturnWalk && FrontStaticBlock(frontWaitReason) && frontReturnBlockedAt < 0f)
                frontReturnBlockedAt = Time.time;
            frontWaitPlaying = false; frontPeerBlockedTime = 0f;
            if (frontFollowMotion != null) legacyAnimation[frontFollowMotion.StateName].speed = 0f;
            if (moveState != null) moveState.speed = 0f;
            if (frontAction != null || IsSteppingTurn)
            {
                // Hold the last accepted performance pose; its clock does not advance.
                foreach (AnimationState frozen in legacyAnimation) frozen.speed = 0f;
                legacyAnimation.Sample();
                frontWaitHoldingPose = true; frontWaitClip = null;
            }
            else
            {
                frontWaitHoldingPose = false;
                SelectFrontWaitAnimation();
            }
        }
        private void CancelFrontWait()
        {
            frontWaiting = false; frontWaitPlaying = false; frontWaitHoldingPose = false;
            frontWaitReason = frontBlockReason = null; frontBlocked = 0f; frontPeerBlockedTime = 0f;
            if (!string.IsNullOrEmpty(frontWaitClip) && legacyAnimation != null && legacyAnimation[frontWaitClip] != null)
                legacyAnimation[frontWaitClip].enabled = false;
            frontWaitClip = null;
        }
        private void SelectFrontWaitAnimation()
        {
            frontWaitHoldingPose = false;
            if (frontWaitPlaying && frontWaitClip == WaitClip) return;
            var look = interactionProfile.Find(InteractionMotionRole.LookForward);
            if (look == null) { frontWaitPlaying = false; return; }
            if (legacyAnimation[WaitClip] == null) legacyAnimation.AddClip(look.clip, WaitClip);
            var state = legacyAnimation[WaitClip]; state.time = 0f; state.speed = 0f; state.wrapMode = WrapMode.Loop;
            legacyAnimation.CrossFade(WaitClip, .2f); frontWaitTime = 0f; frontWaitPlaying = true; frontWaitClip = activeClip = WaitClip;
        }

        private void BeginFrontPeerClearance(DinosaurAgent blocker, bool yielding)
        {
            if (blocker == null) return;
            RegisterGroundConflict(blocker);
            if (!trafficMoving || frontPeerClearing) return;
            frontWaiting = false; frontWaitPlaying = false; frontWaitHoldingPose = false;
            frontWaitReason = frontBlockReason = null; frontPeerBlockedTime = 0f;
            if (frontStage == FrontStage.Follow && frontFollowMotion != null)
                frontSavedWalkTime = legacyAnimation[frontFollowMotion.StateName].time;
            frontSavedReturnTime = moveState != null ? moveState.time : 0f;
            if (IsSteppingTurn)
            {
                GetComponentInChildren<DinosaurFootLock>()?.HoldSupportDuringRecovery(.35f);
                ClearSteppingTurn();
            }
            frontPeerClearing = true; frontPeerYielding = yielding; frontBlockingPeer = blocker;
            frontWaitReason = yielding ? "PeerYield" : "PeerPassAround";
            hasCuriosityPath = false;
        }
        private void CancelFrontPeerClearance()
        {
            frontPeerClearing = false; frontPeerYielding = false; frontBlockingPeer = null;
            frontPeerBlockedTime = 0f;
            frontBlocked = 0f; frontBlockReason = frontWaitReason = null; hasCuriosityPath = false;
        }
        private void FinishFrontPeerClearance()
        {
            bool stageFollow = frontStage == FrontStage.Follow;
            bool stageReturn = frontStage == FrontStage.ReturnWalk;
            CancelFrontPeerClearance();
            if (moveState != null) moveState.speed = 0f;
            if (stageReturn)
            {
                PlayMovementClip(WalkSpeed);
                if (moveState != null) moveState.speed = 0f;
                return;
            }
            if (frontAction != null)
            {
                RebaseFrontActionOrigin();
                UseFrontEnvelope(frontAction);
                var state = legacyAnimation[frontAction.StateName];
                state.enabled = false; state.weight = 0f;
                state.time = frontActionTime; state.speed = 0f; state.wrapMode = WrapMode.ClampForever;
                legacyAnimation.CrossFade(frontAction.StateName, .2f);
                state.enabled = true; state.time = frontActionTime;
                activeClip = frontAction.StateName;
                return;
            }
            if (stageFollow && frontFollowMotion != null)
            {
                UseFrontEnvelope(frontFollowMotion);
                var state = legacyAnimation[frontFollowMotion.StateName];
                state.enabled = true; state.time = frontSavedWalkTime;
                state.speed = 0f; state.wrapMode = WrapMode.ClampForever;
                legacyAnimation.CrossFade(frontFollowMotion.StateName, .15f);
                activeClip = frontFollowMotion.StateName;
                if (!PlanFrontFollow()) BeginFrontSpaceWait();
            }
        }
        private bool TickFrontPeerClearance() => UpdateGroundTraffic();
        private void RebaseFrontActionOrigin()
        {
            if (frontAction == null) return;
            var sample = SampleFootFrame(frontAction, frontActionTime);
            frontActionRotation = Quaternion.Euler(0f, -sample.yaw, 0f) * transform.rotation;
            Vector3 offset = frontActionRotation * Quaternion.Euler(0f, VisualYaw, 0f)
                * new Vector3(sample.rootOffset.x, 0f, sample.rootOffset.y);
            frontActionOrigin = transform.position - offset;
            frontActionOrigin.y = MovementHeight;
        }

        private bool FrontWaitCanResume()
        {
            Vector3 p = transform.position; Quaternion q = transform.rotation;
            if (IsSteppingTurn)
            {
                float nextTime = Mathf.Min(stepTurnEnd, stepTurnTime + Time.deltaTime * stepTurnMotion.turnPlaybackSpeed);
                var frame = SampleFootFrame(stepTurnMotion, nextTime);
                TurnPose(frame, out var next, out var heading);
                // Return turns are performances: once started they play through static clipping.
                return stepTurnUse == TurnUse.FrontReturn
                    ? FrontAnimatedStep(stepTurnMotion, stepTurnTime, nextTime, p, next, q, heading)
                    : FrontWalkAnimatedStep(stepTurnMotion, stepTurnTime, nextTime, p, next, q, heading);
            }
            if (frontStage == FrontStage.Follow)
            {
                if (frontWaitReplans < 2) { frontWaitReplans++; if (!PlanFrontFollow()) return false; }
                if (!hasCuriosityPath) { frontBlockReason = "NavMesh"; return false; }
                return FrontPathNextSafe();
            }
            if (frontStage == FrontStage.ReturnWalk) return FrontReturnWaitCanResume();
            if (frontStage == FrontStage.TurnBack && frontAction == null) return true;
            var role = frontStage == FrontStage.TurnBack ? InteractionMotionRole.Turn180Look
                : frontStage == FrontStage.Intro ? ActiveIntroSequence[frontActionIndex] : FrontExitSequence(frontNearBite)[frontActionIndex];
            var motion = frontAction ?? interactionProfile.Find(role);
            if (motion == null) return false;
            var sample = SampleFootFrame(motion, frontAction == null ? 0f
                : Mathf.Min(FrontActionDuration(motion), frontActionTime + Time.deltaTime));
            Vector3 position = frontAction == null ? p : frontActionOrigin + frontActionRotation * Quaternion.Euler(0f, VisualYaw, 0f)
                * new Vector3(sample.rootOffset.x, 0f, sample.rootOffset.y);
            position.y = MovementHeight;
            Quaternion rotation = frontAction == null ? q : Quaternion.Euler(0f, sample.yaw, 0f) * frontActionRotation;
            frontProbeBody.bands = FrontPoseBands(motion, sample.time);
            return FrontAnimationPoseSafe(p, position, q, rotation, frontProbeBody);
        }
        private bool FrontPathNextSafe()
        {
            if (PathFinished()) return true;
            Vector3 direction = curiosityCorners[curiosityCorner] - transform.position; direction.y = 0f;
            float angle = Vector3.SignedAngle(GetVisualForward(), direction, Vector3.up);
            if (Mathf.Abs(angle) >= FrontSteppingTurnAngle)
                return TryBeginSteppingTurn(angle, frontStage == FrontStage.ReturnWalk ? TurnUse.FrontReturn : TurnUse.FrontFollow);
            Quaternion rotation = direction.sqrMagnitude < .001f ? transform.rotation
                : Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction) * Quaternion.Euler(0f, -VisualYaw, 0f), 1f);
            return FrontStepSafe(transform.position, transform.position + direction.normalized * .04f, transform.rotation, rotation, interactionBody);
        }

        // Waiting only proves the route and prepares the action. The pose must not change
        // here; MoveFrontPath starts the prepared turn on the next tick.
        private bool FrontReturnWaitCanResume()
        {
            if (frontReturnIndex < 0 && !FindFrontClearReturnPoint()) { frontBlockReason = "NavMesh"; return false; }
            if (frontWaitReplans < 2)
            { frontWaitReplans++; if (!PlanFrontConnection(frontTrail[frontReturnIndex], true)) return false; }
            if (!hasCuriosityPath) { frontBlockReason = "NavMesh"; return false; }
            if (PathFinished()) return true;
            // The guaranteed walk ignores static scenery, so a peer wait released inside its
            // window resumes immediately instead of waiting for the static gate again.
            if (Time.time < frontReturnFreeWalkUntil) return true;
            if (!FrontReturnSkipNearCorner()) return false;
            Vector3 direction = curiosityCorners[curiosityCorner] - transform.position; direction.y = 0f;
            float angle = Vector3.SignedAngle(GetVisualForward(), direction, Vector3.up);
            if (Mathf.Abs(angle) < FrontSteppingTurnAngle) return FrontPathNextSafe();
            return FrontReturnPrepareTurn(angle);
        }
        private bool TickFrontSpaceWait()
        {
            if ((frontStage == FrontStage.Follow || frontStage == FrontStage.ReturnWalk)
                && FrontStaticBlock(frontWaitReason) && Time.time - frontWaitBegan >= interactionSettings.spaceFailureTimeout)
            {
                BeginFrontRecovery();
                return true;
            }
            if (EndBlockedFrontFollow()) return true;
            // The reversed trail is only the preferred route. When a static block persists,
            // drop the stuck connector/turn and continue from the nearest checked clearing point.
            if (frontStage == FrontStage.ReturnWalk && frontReturnBlockedAt >= 0f
                && Time.time - frontReturnBlockedAt >= interactionSettings.spaceFailureTimeout)
            {
                GetComponentInChildren<DinosaurFootLock>()?.HoldSupportDuringRecovery(.35f);
                if (IsSteppingTurn) ClearSteppingTurn();
                hasCuriosityPath = false; frontWaitReplans = 0; frontReturnBlockedAt = Time.time;
                bool fallback = FindFrontClearReturnPoint();
                Debug.Log($"[T-Rex front] Return blocked: reason={frontWaitReason ?? frontBlockReason}, fallback={fallback}, position={transform.position:F3}", this);
                if (fallback)
                {
                    frontWaiting = false; frontWaitPlaying = false; frontWaitHoldingPose = false;
                    frontBlockReason = frontWaitReason = null; frontBlocked = 0f;
                    if (moveState != null) moveState.speed = 0f;
                    return true;
                }
                frontBlockReason = "NavMesh";
            }
            if (frontWaitReason == "Peer" && frontBlockingPeer != null)
            {
                frontPeerBlockedTime += Time.deltaTime;
                if (frontPeerBlockedTime >= RightOfWayGraceSeconds)
                {
                    frontPeerBlockedTime = 0f;
                    BeginFrontPeerClearance(frontBlockingPeer, !HasRightOfWay(frontBlockingPeer));
                    return true;
                }
            }
            if (Time.time >= frontRetryAt)
            {
                frontRetryAt = Time.time + Mathf.Max(.1f, interactionSettings.spaceSearchInterval);
                bool resume = FrontWaitCanResume();
                if (!resume) NoteFrontFollowBlock();
                frontWaitReason = frontBlockReason;
                if (resume)
                {
                    frontWaitHoldingPose = false;
                    frontWaiting = false; frontBlocked = 0f; frontBlockReason = null;
                    frontBlockingPeer = null; frontPeerBlockedTime = 0f;
                    if (IsSteppingTurn)
                    {
                        if (!string.IsNullOrEmpty(frontWaitClip))
                        {
                            stepTurnOutgoing = legacyAnimation[frontWaitClip];
                            float waitTime = stepTurnOutgoing.time;
                            legacyAnimation.Stop();
                            stepTurnOutgoing.enabled = true; stepTurnOutgoing.time = waitTime;
                            stepTurnOutgoing.speed = 0f; stepTurnOutgoing.weight = 1f;
                            stepTurnOutgoingMotion = null;
                            stepTurnBlendTime = 0f; stepTurnBlendDuration = .2f;
                            stepTurnState.weight = 0f;
                        }
                        // A held performance already retains both states and its blend clock.
                        stepTurnState.enabled = true; stepTurnState.time = stepTurnTime;
                        stepTurnState.speed = 0f;
                        activeClip = stepTurnMotion.StateName;
                    }
                    else if (frontAction != null)
                    {
                        UseFrontEnvelope(frontAction); legacyAnimation.CrossFade(frontAction.StateName, .2f);
                        legacyAnimation[frontAction.StateName].time = frontActionTime; activeClip = frontAction.StateName;
                    }
                    else if (frontStage == FrontStage.Follow)
                    {
                        UseFrontEnvelope(frontFollowMotion);
                        var state = legacyAnimation[frontFollowMotion.StateName];
                        legacyAnimation.CrossFade(frontFollowMotion.StateName, .15f);
                        state.time = frontSavedWalkTime; state.speed = 0f;
                        activeClip = frontFollowMotion.StateName;
                    }
                    else if (frontStage == FrontStage.ReturnWalk)
                    {
                        PlayMovementClip(WalkSpeed);
                        if (moveState != null) { moveState.time = frontSavedReturnTime; moveState.speed = 0f; }
                    }
                    return true;
                }
                if (frontAction == null && !IsSteppingTurn) SelectFrontWaitAnimation();
            }
            if (frontWaitPlaying && !frontWaitHoldingPose && frontWaitClip != null && legacyAnimation[frontWaitClip] != null)
            {
                frontWaitTime += Time.deltaTime;
                legacyAnimation[frontWaitClip].time = Mathf.Repeat(frontWaitTime, legacyAnimation[frontWaitClip].length);
                legacyAnimation.Sample();
            }
            return true;
        }
    }
}
