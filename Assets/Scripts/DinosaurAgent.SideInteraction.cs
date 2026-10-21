using UnityEngine;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        private bool sideTestSequence, sideFromLeft;
        private static readonly InteractionMotionRole[] SideLeftIntro = { InteractionMotionRole.LookLeft, InteractionMotionRole.SideRoarLeft };
        private static readonly InteractionMotionRole[] SideRightIntro = { InteractionMotionRole.LookRight, InteractionMotionRole.SideRoarRight };
        private InteractionMotionRole[] ActiveIntroSequence => sideTestSequence ? (sideFromLeft ? SideLeftIntro : SideRightIntro) : FrontIntro;
        public bool IsSideTestSequence => sideTestSequence && frontActive;

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
    }
}
