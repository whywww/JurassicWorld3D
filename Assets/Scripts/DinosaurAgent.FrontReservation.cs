using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        private DinosaurInteractionProfile.FootFrame[] frontTurnKeys;
        private DinosaurCoreBody.Band[] frontTurnBands;
        private int frontAheadFailures;
        private float frontAheadFailureAt;
        private float frontSavedWalkTime;
        private readonly Dictionary<int, DinosaurCoreBody.Band[]> frontBlendBands = new Dictionary<int, DinosaurCoreBody.Band[]>();
        private DinosaurCoreBody frontProbeBody;
        private float frontReservationAt, frontRetryAt, frontWaitTime;
        private Vector3 frontReservationOrigin;
        private int frontBiteMask;
        private bool frontSpaceExit, frontWaiting, frontWaitPlaying;
        private string frontBlockReason;
        public string FrontBlockingReason => frontBlockReason ?? "";
        private const string WaitClip = "FrontSpaceWait";
        private const string WaitIdleClip = "FrontSpaceIdle";
        private bool frontStaticPass;
        private float frontWaitBegan, frontSavedReturnTime;
        private int frontWaitReplans;
        private string frontWaitReason;
        private float frontFollowBlockedAt = -1f;
        private bool IsFrontFollowing => frontStage == FrontStage.Follow || IsSideLocomotion;
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
            if (!IsFrontFollowing || frontFollowBlockedAt < 0f || Time.time - frontFollowBlockedAt < 2f) return false;
            if (IsSteppingTurn)
            {
                if (stepTurnState != null) stepTurnState.enabled = false;
                ClearSteppingTurn();
            }
            GetComponentInChildren<DinosaurFootLock>()?.HoldSupportDuringRecovery(.35f);
            frontWaiting = frontWaitPlaying = frontStaticPass = false;
            frontWaitReason = frontBlockReason = null;
            frontSpaceExit = frontExitPending = true; frontNearPending = false;
            actualInteractionSpeed = 0f; interactionMoved = false;
            BeginFrontEnding();
            return true;
        }
        private Terrain[] frontGround;
        private float frontTurnResumeAt = -10f;
        private string frontWaitClip;
        private bool FrontStaticBlock(string reason) => reason == "Tree" || reason == "Rock / solid obstacle" || reason == "NavMesh";
        private bool FrontGroundValid(Vector3 position)
        {
            if (frontGround == null) frontGround = Terrain.activeTerrains;
            foreach (var terrain in frontGround)
            {
                Vector3 local = position - terrain.transform.position, size = terrain.terrainData.size;
                if (local.x < 0f || local.z < 0f || local.x > size.x || local.z > size.z) continue;
                float height = terrain.SampleHeight(position) + terrain.transform.position.y;
                return !float.IsNaN(height) && Mathf.Abs(height - MovementHeight) <= .5f;
            }
            return false;
        }
        private bool FrontClearOfStatic()
            => FrontBodyClear(interactionBody);
        private bool FrontReadyToRoam()
            => FrontBodyClear(frontOriginalBody != null ? frontOriginalBody : interactionBody) && FrontClearOfStatic();
        private bool FrontBodyClear(DinosaurCoreBody body)
        {
            Vector3 f = GetVisualForward();
            return interactionSettings.PoseBlockReason(new Vector2(transform.position.x, transform.position.z), new Vector2(f.x, f.z), body) == null
                && NavMesh.SamplePosition(transform.position, out var hit, .25f, NavigationFilter) && FlatDistance(transform.position, hit.position) <= .12f;
        }
        private bool PlanFrontConnection(Vector3 target, bool completeOnly)
        {
            if (PlanCuriosityPath(target, completeOnly)) return true;
            frontBlockReason = "NavMesh";
            if (!frontStaticPass || !FrontGroundValid(target)) return false;
            curiosityCorners = new[] { transform.position, new Vector3(target.x, MovementHeight, target.z) };
            curiosityCorner = 1; hasCuriosityPath = true;
            return true;
        }
        private bool FindFrontClearReturnPoint()
        {
            Vector3 forward = GetVisualForward();
            for (int ring = 1; ring <= 8; ring++) for (int bearing = 0; bearing < 8; bearing++)
            {
                Vector3 candidate = transform.position + Quaternion.Euler(0f, bearing * 45f, 0f) * forward * (ring * 4f);
                if (!NavMesh.SamplePosition(candidate, out var hit, 2f, NavigationFilter) || !FrontGroundValid(hit.position)) continue;
                Vector3 direction = hit.position - transform.position; direction.y = 0f; direction.Normalize();
                Quaternion heading = Quaternion.LookRotation(direction) * Quaternion.Euler(0f, -VisualYaw, 0f);
                if (interactionSettings.PoseBlockReason(new Vector2(hit.position.x, hit.position.z), new Vector2(direction.x, direction.z), frontOriginalBody != null ? frontOriginalBody : interactionBody) != null
                    || interactionSettings.PoseBlockReason(new Vector2(hit.position.x, hit.position.z), new Vector2(direction.x, direction.z), interactionBody) != null
                    || FrontPeerPoseBlocked(hit.position, heading, interactionBody)) continue;
                frontTrail.Insert(0, hit.position); frontReturnIndex = 0; hasCuriosityPath = false;
                return true;
            }
            return false;
        }

        private DinosaurCoreBody.Band[] FrontBands(DinosaurInteractionProfile.Motion incoming, DinosaurInteractionProfile.Motion outgoing)
        {
            if (outgoing == null || outgoing == incoming) return incoming.bodyBands;
            int key = (int)incoming.role * 100 + (int)outgoing.role;
            if (frontBlendBands.TryGetValue(key, out var cached)) return cached;
            var bounds = new Dictionary<int, Bounds>();
            foreach (var band in outgoing.bodyBands) bounds[band.layer] = band.bounds;
            foreach (var band in incoming.bodyBands)
            {
                if (bounds.TryGetValue(band.layer, out var b)) { b.Encapsulate(band.bounds); bounds[band.layer] = b; }
                else bounds[band.layer] = band.bounds;
            }
            var result = new List<DinosaurCoreBody.Band>();
            foreach (var item in bounds) result.Add(new DinosaurCoreBody.Band { layer = item.Key, bounds = item.Value });
            return frontBlendBands[key] = result.ToArray();
        }
        private bool FrontStepSafe(Vector3 from, Vector3 to, Quaternion fromRotation, Quaternion toRotation, DinosaurCoreBody body,
            DinosaurCoreBody.Band[] playerBands = null)
        {
            // Players do not obstruct this interaction; retained pose data still serves animation calibration.
            int count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / .1f), Mathf.CeilToInt(Quaternion.Angle(fromRotation, toRotation) / 2f));
            for (int i = 0; i <= count; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, (float)i / count);
                Quaternion q = Quaternion.Slerp(fromRotation, toRotation, (float)i / count);
                Vector3 f = q * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
                if (FrontPeerPoseBlocked(p, q, body)) { frontBlockReason = "Peer"; return false; }
                string reason = interactionSettings.PoseBlockReason(new Vector2(p.x, p.z), new Vector2(f.x, f.z), body, frontActive && frontStaticPass);
                if (reason != null) { frontBlockReason = reason; return false; }
                if (frontStaticPass && !FrontGroundValid(p)) { frontBlockReason = "Ground"; return false; }
            }
            if (FindBlockingStep(new Vector2(from.x, from.z), new Vector2(to.x, to.z)) != null)
            { frontBlockReason = "Peer"; return false; }
            if (!frontStaticPass && (!NavMesh.SamplePosition(to, out var hit, .25f, NavigationFilter)
                || FlatDistance(to, hit.position) > .12f || NavMesh.Raycast(from, to, out _, NavigationFilter))
                )
            { frontBlockReason = "NavMesh"; return false; }
            return true;
        }
        private bool FrontPeerPoseBlocked(Vector3 position, Quaternion rotation, DinosaurCoreBody body)
        {
            Vector3 forward3 = rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
            Vector2 forward = new Vector2(forward3.x, forward3.z), right = new Vector2(forward.y, -forward.x);
            foreach (var peer in peers)
            {
                if (peer == null || peer == this || Mathf.Abs(peer.MovementHeight - MovementHeight) > PeerHeightTolerance) continue;
                var peerBody = peer.interactionBody != null ? peer.interactionBody : peer.coreBody;
                if (peerBody == null) continue;
                if (FlatDistance(position, peer.transform.position) > body.BroadRadius + peerBody.BroadRadius + .1f) continue;
                Vector3 peerForward3 = peer.GetVisualForward(); Vector2 pf = new Vector2(peerForward3.x, peerForward3.z), pr = new Vector2(pf.y, -pf.x);
                foreach (var a in body.bands) foreach (var b in peerBody.bands)
                {
                    if (a.bounds.max.y + MovementHeight < b.bounds.min.y + peer.MovementHeight
                        || b.bounds.max.y + peer.MovementHeight < a.bounds.min.y + MovementHeight) continue;
                    Vector2 ac = new Vector2(position.x, position.z) + right * a.bounds.center.x + forward * a.bounds.center.z;
                    Vector2 bc = new Vector2(peer.transform.position.x, peer.transform.position.z) + pr * b.bounds.center.x + pf * b.bounds.center.z;
                    Vector2 d = bc - ac;
                    if (FrontBoxesOverlap(d, right, right, forward, a.bounds.extents, pr, pf, b.bounds.extents)
                        && FrontBoxesOverlap(d, forward, right, forward, a.bounds.extents, pr, pf, b.bounds.extents)
                        && FrontBoxesOverlap(d, pr, right, forward, a.bounds.extents, pr, pf, b.bounds.extents)
                        && FrontBoxesOverlap(d, pf, right, forward, a.bounds.extents, pr, pf, b.bounds.extents)) return true;
                }
            }
            return false;
        }
        private static bool FrontBoxesOverlap(Vector2 delta, Vector2 axis, Vector2 ar, Vector2 af, Vector3 ae, Vector2 br, Vector2 bf, Vector3 be)
            => Mathf.Abs(Vector2.Dot(delta, axis)) < Mathf.Abs(Vector2.Dot(ar, axis)) * ae.x + Mathf.Abs(Vector2.Dot(af, axis)) * ae.z
                + Mathf.Abs(Vector2.Dot(br, axis)) * be.x + Mathf.Abs(Vector2.Dot(bf, axis)) * be.z;
        private void EnsureFrontProbe()
        {
            if (frontProbeBody != null) return;
            var holder = new GameObject("Front reservation probe") { hideFlags = HideFlags.DontSave };
            holder.transform.SetParent(transform, false); frontProbeBody = holder.AddComponent<DinosaurCoreBody>();
        }
        private void PrepareFrontTurnPrediction()
        {
            if (frontTurnKeys != null) return;
            var turn = interactionProfile.Find(InteractionMotionRole.Turn180Look);
            frontTurnKeys = new DinosaurInteractionProfile.FootFrame[6];
            for (int i = 0; i < 6; i++) frontTurnKeys[i] = SampleFootFrame(turn, turn.clip.length * i / 5f);
            frontTurnBands = (DinosaurCoreBody.Band[])turn.bodyBands.Clone();
            for (int i = 0; i < frontTurnBands.Length; i++)
            {
                var bounds = frontTurnBands[i].bounds; bounds.Expand(new Vector3(.6f, 0f, .6f));
                frontTurnBands[i].bounds = bounds;
            }
            EnsureFrontProbe();
        }
        private bool FrontTurnSpace(Vector3 origin, Quaternion heading)
        {
            if (frontStaticPass) return true; // Actual execution still checks peers, boundary and ground.
            PrepareFrontTurnPrediction(); frontProbeBody.bands = frontTurnBands;
            Vector3 previous = origin;
            foreach (var sample in frontTurnKeys)
            {
                Vector3 p = origin + heading * Quaternion.Euler(0f, VisualYaw, 0f) * new Vector3(sample.rootOffset.x, 0f, sample.rootOffset.y);
                p.y = MovementHeight;
                Vector3 forward = Quaternion.Euler(0f, sample.yaw, 0f) * heading * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
                string reason = interactionSettings.PoseBlockReason(new Vector2(p.x, p.z), new Vector2(forward.x, forward.z), frontProbeBody);
                if (reason != null) { frontBlockReason = reason; return false; }
                if (!NavMesh.SamplePosition(p, out var hit, .25f, NavigationFilter) || FlatDistance(p, hit.position) > .12f
                    || NavMesh.Raycast(previous, p, out _, NavigationFilter))
                { frontBlockReason = "NavMesh"; return false; }
                previous = p;
            }
            return true;
        }
        // Kept as a small diagnostic seam for existing editor acceptance fixtures. No bite simulation.
        private int FrontExitMask(Vector3 position, Quaternion rotation) => FrontTurnSpace(position, rotation) ? 3 : 0;
        private bool RefreshFrontReservation(bool force = false)
        {
            if (!force && Time.time < frontReservationAt && FlatDistance(frontReservationOrigin, transform.position) < .5f)
                return frontBiteMask != 0;
            frontReservationAt = Time.time + .5f; frontReservationOrigin = transform.position;
            frontBiteMask = FrontExitMask(transform.position, transform.rotation);
            if (frontBiteMask == 0)
            {
                if (!FrontStaticBlock(frontBlockReason)) return false;
                if (frontWaiting && IsFrontFollowing) return false;
                frontSpaceExit = frontExitPending = true;
                frontBiteMask = 3; // Prediction requests an ending; it no longer freezes walking.
                return true;
            }
            // A temporary blocker gets the full follow retry window before prediction can end it.
            if (frontWaiting && IsFrontFollowing) { frontBlockReason = null; return true; }
            Vector3 position = transform.position; Quaternion heading = transform.rotation;
            float remaining = Mathf.Max(4f, FrontFollowSpeed * 2f);
            if (hasCuriosityPath)
            {
                for (int corner = curiosityCorner; corner < curiosityCorners.Length && remaining > .01f; corner++)
                {
                    Vector3 delta = curiosityCorners[corner] - position; delta.y = 0f;
                    if (delta.magnitude < .01f) continue;
                    float length = Mathf.Min(remaining, delta.magnitude);
                    heading = Quaternion.RotateTowards(heading, Quaternion.LookRotation(delta) * Quaternion.Euler(0f, -VisualYaw, 0f),
                        65f * length / Mathf.Max(.1f, FrontFollowSpeed));
                    position += delta.normalized * length; remaining -= length;
                }
            }
            if (FlatDistance(position, transform.position) > .1f && !FrontTurnSpace(position, heading))
            {
                if (Time.time - frontAheadFailureAt >= .1f)
                {
                    frontAheadFailureAt = Time.time;
                    if (++frontAheadFailures >= 2) frontSpaceExit = frontExitPending = true;
                }
            }
            else frontAheadFailures = 0;
            if (!frontSpaceExit) frontBlockReason = null;
            return true;
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
            if (frontSpaceExit || !PlanFrontFollow()) return false;
            return RefreshFrontReservation(true) && !frontSpaceExit;
        }
        private void BeginFrontSpaceWait()
        {
            NoteFrontFollowBlock();
            if (frontWaiting) return;
            if ((frontStage == FrontStage.Follow || IsSideLocomotion) && frontFollowMotion != null)
                frontSavedWalkTime = legacyAnimation[frontFollowMotion.StateName].time;
            frontSavedReturnTime = moveState != null ? moveState.time : 0f;
            frontWaiting = true; frontRetryAt = Time.time + .5f; frontWaitBegan = Time.time; frontWaitReplans = 0;
            frontWaitReason = frontBlockReason; frontWaitPlaying = false;
            if (frontFollowMotion != null) legacyAnimation[frontFollowMotion.StateName].speed = 0f;
            if (moveState != null) moveState.speed = 0f;
            SelectFrontWaitAnimation();
        }
        private void SelectFrontWaitAnimation()
        {
            var look = interactionProfile.Find(InteractionMotionRole.LookForward);
            EnsureFrontProbe(); frontProbeBody.bands = look.bodyBands;
            string clip = FrontPeerPoseBlocked(transform.position, transform.rotation, frontProbeBody) ? WaitIdleClip : WaitClip;
            if (frontWaitPlaying && frontWaitClip == clip) return;
            if (legacyAnimation[clip] == null) legacyAnimation.AddClip(clip == WaitClip ? look.clip : legacyAnimation[idleClip].clip, clip);
            var state = legacyAnimation[clip]; state.time = 0f; state.speed = 0f; state.wrapMode = WrapMode.Loop;
            legacyAnimation.CrossFade(clip, .2f); frontWaitTime = 0f; frontWaitPlaying = true; frontWaitClip = activeClip = clip;
        }
        private bool FrontWaitCanResume()
        {
            Vector3 p = transform.position; Quaternion q = transform.rotation;
            if (IsSteppingTurn)
            {
                var frame = SampleFootFrame(stepTurnMotion, Mathf.Min(stepTurnEnd, stepTurnTime + 1f / 60f * stepTurnMotion.turnPlaybackSpeed));
                TurnPose(frame, out var next, out var heading);
                frontProbeBody.bands = stepTurnMotion.bodyBands;
                return FrontStepSafe(p, next, q, heading, frontProbeBody, frame.playerBodyBands);
            }
            if (sideTestSequence && IsSideLocomotion)
            {
                if (!hasCuriosityPath || frontWaitReplans++ < 2)
                    if (!PlanFrontFollow()) return false;
                return (!hasCuriosityPath || FrontPathNextSafe()) && FrontStepSafe(p, p, q, q, interactionBody);
            }
            if (frontStage == FrontStage.Follow)
            {
                if (frontWaitReplans < 2) { frontWaitReplans++; if (!PlanFrontFollow()) return false; }
                if (!hasCuriosityPath) { frontBlockReason = "NavMesh"; return false; }
                return RefreshFrontReservation(true) && FrontPathNextSafe();
            }
            if (frontStage == FrontStage.ReturnWalk)
            {
                if (frontReturnIndex < 0 && !FindFrontClearReturnPoint()) { frontBlockReason = "NavMesh"; return false; }
                if (frontWaitReplans < 2)
                { frontWaitReplans++; if (!PlanFrontConnection(frontTrail[frontReturnIndex], true)) return false; }
                if (!hasCuriosityPath) { frontBlockReason = "NavMesh"; return false; }
                return FrontPathNextSafe();
            }
            if (frontStage == FrontStage.TurnBack && frontAction == null && !FrontTurnSpace(p, q)) return false;
            var role = frontStage == FrontStage.TurnBack ? InteractionMotionRole.Turn180Look
                : frontStage == FrontStage.Intro ? ActiveIntroSequence[frontActionIndex] : FrontExitSequence(frontNearBite)[frontActionIndex];
            var motion = frontAction ?? interactionProfile.Find(role);
            var sample = SampleFootFrame(motion, frontAction == null ? 0f : Mathf.Min(FrontActionDuration(motion), frontActionTime + 1f / 60f));
            Vector3 position = frontAction == null ? p : frontActionOrigin + frontActionRotation * Quaternion.Euler(0f, VisualYaw, 0f)
                * new Vector3(sample.rootOffset.x, 0f, sample.rootOffset.y);
            Quaternion rotation = frontAction == null ? q : Quaternion.Euler(0f, sample.yaw, 0f) * frontActionRotation;
            frontProbeBody.bands = motion.bodyBands;
            return FrontStepSafe(p, position, q, rotation, frontProbeBody, sample.playerBodyBands);
        }
        private bool FrontPathNextSafe()
        {
            if (PathFinished()) return true;
            Vector3 direction = curiosityCorners[curiosityCorner] - transform.position; direction.y = 0f;
            float angle = Vector3.SignedAngle(GetVisualForward(), direction, Vector3.up);
            if (Mathf.Abs(angle) >= FrontSteppingTurnAngle && !IsSideLocomotion)
                return TryBeginSteppingTurn(angle, frontStage == FrontStage.ReturnWalk ? TurnUse.FrontReturn : TurnUse.FrontFollow);
            Quaternion rotation = direction.sqrMagnitude < .001f ? transform.rotation
                : Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction) * Quaternion.Euler(0f, -VisualYaw, 0f), 1f);
            return FrontStepSafe(transform.position, transform.position + direction.normalized * .04f, transform.rotation, rotation, interactionBody);
        }
        private bool TickFrontSpaceWait()
        {
            if (EndBlockedFrontFollow()) return true;
            if (frontStage == FrontStage.Near && !CanSeePlayer())
            { invisibleTime += Time.deltaTime; if (invisibleTime >= lostSightGrace) { frontExitPending = true; frontStage = FrontStage.Ending; } }
            if (Time.time >= frontRetryAt)
            {
                frontRetryAt = Time.time + .5f;
                bool resume = FrontWaitCanResume();
                if (!resume) NoteFrontFollowBlock();
                float limit = IsSteppingTurn || frontAction != null || frontStage == FrontStage.TurnBack || frontStage == FrontStage.Intro
                    || frontStage == FrontStage.Near || frontStage == FrontStage.Ending ? 1f : 2f;
                if (!resume && !IsFrontFollowing && Time.time - frontWaitBegan >= limit && FrontStaticBlock(frontBlockReason))
                {
                    frontStaticPass = true; frontWaitReplans = 0;
                    resume = FrontWaitCanResume();
                }
                frontWaitReason = frontBlockReason;
                if (resume)
                {
                    frontWaiting = false; frontBlocked = 0f; frontBlockReason = null;
                    if (IsSteppingTurn)
                    {
                        legacyAnimation.CrossFade(stepTurnMotion.StateName, .2f); frontTurnResumeAt = Time.time;
                        stepTurnState.enabled = true; stepTurnState.time = stepTurnTime;
                        activeClip = stepTurnMotion.StateName;
                    }
                    else if (frontAction != null)
                    {
                        UseFrontEnvelope(frontAction); legacyAnimation.CrossFade(frontAction.StateName, .2f);
                        legacyAnimation[frontAction.StateName].time = frontActionTime; activeClip = frontAction.StateName;
                    }
                    else if (frontStage == FrontStage.Follow || IsSideLocomotion)
                    {
                        if (ResumeSideWalkBlend()) return true;
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
                SelectFrontWaitAnimation();
            }
            if (frontWaitPlaying)
            {
                frontWaitTime += Time.deltaTime;
                legacyAnimation[frontWaitClip].time = Mathf.Repeat(frontWaitTime, legacyAnimation[frontWaitClip].length);
                legacyAnimation.Sample();
            }
            return true;
        }
    }
}

