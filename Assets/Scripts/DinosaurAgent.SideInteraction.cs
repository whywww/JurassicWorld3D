using UnityEngine;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        [SerializeField, Min(.01f)] private float sideStartRunPlaybackSpeed = .8f;
        [SerializeField, Min(0f)] private float sideWalkBlendDuration = .3f;
        private DinosaurInteractionProfile.Motion sideWalkMotion;
        private float sideWalkTime, sideWalkBlendTime, sideWalkBlendLength, sideWalkResumeTime, sideWalkResumeLength;
        private bool sideTestSequence, sideFromLeft;
        private static readonly InteractionMotionRole[] SideLeftIntro = { InteractionMotionRole.LookLeft, InteractionMotionRole.SideRoarLeft };
        private static readonly InteractionMotionRole[] SideRightIntro = { InteractionMotionRole.LookRight, InteractionMotionRole.SideRoarRight };
        private InteractionMotionRole[] ActiveIntroSequence => sideTestSequence ? (sideFromLeft ? SideLeftIntro : SideRightIntro) : FrontIntro;
        private bool IsSideLocomotion => frontStage == FrontStage.SideStartRun;
        private bool IsSideChasing => IsSideLocomotion;
        public bool IsSideTestSequence => sideTestSequence && frontActive;

        private void ClearSideWalkBlend()
        {
            sideWalkMotion = null;
            sideWalkTime = sideWalkBlendTime = sideWalkBlendLength = 0f;
        }
        private void BeginSideWalkBlend(AnimationState outgoing, float remaining)
        {
            sideWalkMotion = interactionProfile.Find(InteractionMotionRole.FollowWalk);
            var pose = SampleFootFrame(frontFollowMotion, outgoing.time);
            float best = float.PositiveInfinity;
            sideWalkTime = 0f;
            foreach (var candidate in sideWalkMotion.footFrames)
            {
                if (candidate.time >= sideWalkMotion.clip.length) continue;
                float score = (candidate.leftContact != pose.leftContact ? 4f : 0f)
                    + (candidate.rightContact != pose.rightContact ? 4f : 0f)
                    + (candidate.leftSole - pose.leftSole).sqrMagnitude * (pose.leftContact ? 4f : 1f)
                    + (candidate.rightSole - pose.rightSole).sqrMagnitude * (pose.rightContact ? 4f : 1f);
                if (score < best) { best = score; sideWalkTime = candidate.time; }
            }
            float outgoingTime = outgoing.time;
            legacyAnimation.Stop();
            outgoing.enabled = true; outgoing.time = outgoingTime; outgoing.speed = 0f; outgoing.weight = 1f;
            var incoming = legacyAnimation[sideWalkMotion.StateName];
            incoming.enabled = true; incoming.time = sideWalkTime; incoming.speed = 0f; incoming.weight = 0f;
            incoming.wrapMode = WrapMode.Loop;
            sideWalkBlendTime = 0f; sideWalkBlendLength = Mathf.Max(.001f, remaining);
            sideWalkResumeTime = sideWalkResumeLength = .15f;
            UseFrontEnvelope(sideWalkMotion);
        }
        private bool ResumeSideWalkBlend()
        {
            if (sideWalkMotion == null) return false;
            float outgoingTime = frontSavedWalkTime;
            legacyAnimation.Stop();
            var outgoing = legacyAnimation[frontFollowMotion.StateName];
            outgoing.enabled = true; outgoing.time = outgoingTime; outgoing.speed = 0f; outgoing.weight = 0f;
            var incoming = legacyAnimation[sideWalkMotion.StateName];
            incoming.enabled = true; incoming.time = sideWalkTime; incoming.speed = 0f; incoming.weight = 0f;
            var wait = legacyAnimation[frontWaitClip];
            wait.enabled = true; wait.time = Mathf.Repeat(frontWaitTime, wait.length); wait.speed = 0f; wait.weight = 1f;
            sideWalkResumeTime = 0f;
            sideWalkResumeLength = Mathf.Max(.001f, Mathf.Min(.15f,
                (frontFollowMotion.clip.length - outgoingTime) / Mathf.Max(.01f, sideStartRunPlaybackSpeed)));
            UseFrontEnvelope(sideWalkMotion);
            activeClip = frontFollowMotion.StateName;
            return true;
        }
        private void SampleSideWalkBlend(float outgoingTime, float walkTime, float blendTime, float gameDelta)
        {
            sideWalkTime = walkTime; sideWalkBlendTime = blendTime;
            sideWalkResumeTime = Mathf.Min(sideWalkResumeLength, sideWalkResumeTime + gameDelta);
            float weight = Mathf.SmoothStep(0f, 1f, sideWalkBlendTime / sideWalkBlendLength);
            float resume = Mathf.SmoothStep(0f, 1f, sideWalkResumeTime / sideWalkResumeLength);
            var outgoing = legacyAnimation[frontFollowMotion.StateName];
            var incoming = legacyAnimation[sideWalkMotion.StateName];
            outgoing.time = outgoingTime; outgoing.weight = (1f - weight) * resume;
            incoming.time = sideWalkTime; incoming.weight = weight * resume;
            if (!string.IsNullOrEmpty(frontWaitClip) && legacyAnimation[frontWaitClip] != null)
            {
                var wait = legacyAnimation[frontWaitClip];
                wait.weight = 1f - resume; wait.enabled = resume < 1f;
            }
            legacyAnimation.Sample();
        }

        public bool TriggerSideInteractionTest(bool fromLeft)
        {
            if (!frontTestControlled || !IsWaitingForInteraction || !interactionReady || !SideActionsAvailable()
                || !CanSeePlayer() || PlayerInteractionAngle <= 20f) return false;
            Vector3 direction = viewCamera.transform.position - transform.position;
            if ((Vector3.SignedAngle(GetVisualForward(), direction, Vector3.up) < 0f) != fromLeft) return false;
            frontTestWaiting = false;
            FrontTestInteractionCount++;
            BeginInteractionSequence(true, fromLeft);
            return true;
        }

        private bool SideActionsAvailable()
        {
            if (!FrontActionsAvailable()) return false;
            foreach (var role in new[] { InteractionMotionRole.LookLeft, InteractionMotionRole.LookRight,
                InteractionMotionRole.SideRoarLeft, InteractionMotionRole.SideRoarRight })
                if (interactionProfile.Find(role) == null) return false;
            return true;
        }

        private void PlaySideLocomotion(InteractionMotionRole role, FrontStage stage)
        {
            ClearSideWalkBlend();
            frontStage = stage; frontAction = null;
            frontTimingFollow = true;
            frontFollowMotion = interactionProfile.Find(role);
            UseFrontEnvelope(frontFollowMotion);
            var state = legacyAnimation[frontFollowMotion.StateName];
            state.enabled = false; state.weight = 0f;
            state.time = 0f; state.speed = 0f; state.wrapMode = WrapMode.ClampForever;
            legacyAnimation.CrossFade(frontFollowMotion.StateName, frontFollowMotion.blendDuration);
            state.enabled = true; state.time = 0f;
            activeClip = frontFollowMotion.StateName;
            Debug.Log("[T-Rex side] " + frontFollowMotion.sourceName, this);
        }

        private bool TickSideLocomotion()
        {
            if (IsSideChasing && (frontExitPending || frontStopPending || frontNearPending))
            { if (frontExitPending || frontStopPending) BeginFrontEnding(); else BeginFrontNear(); return true; }
            if (IsSideChasing && Time.time >= nextReplan && (!hasCuriosityPath || FlatDistance(lastSeenPlayer, plannedPlayer) > playerReplanDistance))
            {
                if (!PlanFrontFollow()) { BeginFrontSpaceWait(); return true; }
            }
            var state = legacyAnimation[frontFollowMotion.StateName];
            float rate = Mathf.Max(.01f, sideStartRunPlaybackSpeed);
            float remaining = (frontFollowMotion.clip.length - state.time) / rate;
            if (sideWalkMotion == null && sideWalkBlendDuration > 0f && remaining <= sideWalkBlendDuration + .0001f)
                BeginSideWalkBlend(state, remaining);
            float next = Mathf.Min(frontFollowMotion.clip.length, state.time + Time.deltaTime * rate);
            float gameDelta = (next - state.time) / rate;
            float blendTime = sideWalkBlendTime + gameDelta;
            float blendWeight = sideWalkMotion == null ? 0f : Mathf.SmoothStep(0f, 1f,
                (sideWalkBlendTime + gameDelta * .5f) / sideWalkBlendLength);
            // Scale travel with the accepted clip advance, including the final partial frame.
            float speed = Mathf.Lerp(Mathf.Max(0f, frontFollowMotion.speed.Evaluate((state.time + next) * .5f)) * rate,
                FrontFollowSpeed, blendWeight);
            float walkTime = sideWalkMotion == null ? 0f : Mathf.Repeat(sideWalkTime
                + gameDelta * speed / Mathf.Max(.1f, sideWalkMotion.groundSpeed), sideWalkMotion.clip.length);
            speed *= gameDelta / Mathf.Max(.000001f, Time.deltaTime);
            Vector3 previous = transform.position;
            Quaternion previousRotation = transform.rotation;
            actualInteractionSpeed = 0f; interactionMoved = false;
            bool moving = false;
            if (speed > .001f && hasCuriosityPath && !PathFinished()) moving = MoveFrontPath(speed, false);
            if (frontWaiting || IsSteppingTurn) return true;
            var pose = SampleFootFrame(frontFollowMotion, next);
            if (!FrontWalkAnimatedStep(frontFollowMotion, state.time, next, previous, transform.position, previousRotation, transform.rotation))
            {
                transform.SetPositionAndRotation(previous, previousRotation);
                actualInteractionSpeed = 0f; interactionMoved = false;
                BeginFrontSpaceWait(); return true;
            }
            if (IsSideChasing && !moving && speed > .001f)
            {
                if (PathFinished()) BeginFrontNear();
                else BeginFrontSpaceWait();
                return true;
            }
            if (sideWalkMotion != null) SampleSideWalkBlend(next, walkTime, blendTime, gameDelta);
            else { state.time = next; state.speed = 0f; legacyAnimation.Sample(); }
            if (next < frontFollowMotion.clip.length - .0001f) return true;
            frontStage = FrontStage.Follow;
            if (sideWalkMotion == null) PlayFrontFollow();
            else
            {
                state.enabled = false;
                frontFollowMotion = sideWalkMotion;
                var walk = legacyAnimation[frontFollowMotion.StateName];
                walk.weight = 1f; walk.speed = FrontFollowSpeed / Mathf.Max(.1f, frontFollowMotion.groundSpeed);
                walk.wrapMode = WrapMode.ClampForever;
                activeClip = frontFollowMotion.StateName;
                ClearSideWalkBlend(); UseFrontEnvelope(frontFollowMotion);
                Debug.Log("[T-Rex side blend] walk head low, preserving phase " + walk.time.ToString("F3"), this);
            }
            return true;
        }

    }
}
