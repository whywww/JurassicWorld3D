using UnityEngine;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        [SerializeField, Min(0f)] private float sideRoarHandoffDuration = .4f;
        private bool sideRoarHandoffActive, sideRoarHandoffFading, sideHeadPoseApplied;
        private float sideRoarHandoffTime, sideRoarHeadWeight;
        private Transform sideHead, sideNeck;
        private Vector3 sideHeadForward, sideHeadHeading;
        private Quaternion sideHeadBase, sideNeckBase;
        private float sideWalkEntry;
        internal bool SideHandoffMoving => sideRoarHandoffActive && frontActive
            && frontStage == FrontStage.Follow && !frontWaiting;

        private void BindSideHandoffBones()
        {
            if (sideHead != null || legacyAnimation == null) return;
            foreach (var bone in legacyAnimation.GetComponentsInChildren<Transform>(true))
                if (bone.name == "jt_Head_C") { sideHead = bone; break; }
            if (sideHead == null) return;
            sideNeck = sideHead.parent;
            sideHeadForward = sideHead.InverseTransformDirection(GetVisualForward());
        }
        private void RestoreSideHeadPose()
        {
            if (!sideHeadPoseApplied) return;
            if (sideNeck != null) sideNeck.localRotation = sideNeckBase;
            if (sideHead != null) sideHead.localRotation = sideHeadBase;
            sideHeadPoseApplied = false;
        }
        private void ClearSideRoarHandoff()
        {
            RestoreSideHeadPose();
            if (sideRoarHandoffActive && frontEnvelope != null)
                frontEnvelopeBlendUntil = Time.time + (IsSteppingTurn
                    ? Mathf.Max(.15f, (stepTurnEnd - stepTurnTime) / Mathf.Max(.01f, stepTurnMotion.turnPlaybackSpeed)) : .15f);
            sideRoarHandoffActive = sideRoarHandoffFading = false;
            sideRoarHandoffTime = sideRoarHeadWeight = sideWalkEntry = 0f;
            frontFeet?.EndSideHandoff();
        }
        private void BeginSideRoarHandoff()
        {
            if (sideRoarHandoffDuration <= 0f) return;
            BindSideHandoffBones();
            if (sideHead == null || sideNeck == null) return;
            frontFeet = GetComponentInChildren<DinosaurFootLock>();
            if (frontFeet == null) return;
            sideHeadHeading = Vector3.ProjectOnPlane(sideHead.TransformDirection(sideHeadForward), Vector3.up).normalized;
            var walk = interactionProfile.Find(InteractionMotionRole.FollowWalk);
            Quaternion inverse = Quaternion.Inverse(transform.rotation * Quaternion.Euler(0f, VisualYaw, 0f));
            Vector3 left = inverse * (frontFeet.LeftSolePosition - transform.position);
            Vector3 right = inverse * (frontFeet.RightSolePosition - transform.position);
            float best = float.PositiveInfinity;
            foreach (var frame in walk.footFrames)
            {
                if (frame.time >= walk.clip.length) continue;
                float score = (frame.leftContact != frontFeet.LeftPlanted ? 4f : 0f)
                    + (frame.rightContact != frontFeet.RightPlanted ? 4f : 0f)
                    + (frame.leftSole - left).sqrMagnitude * (frontFeet.LeftPlanted ? 4f : 1f)
                    + (frame.rightSole - right).sqrMagnitude * (frontFeet.RightPlanted ? 4f : 1f);
                if (score < best) { best = score; sideWalkEntry = frame.time; }
            }
            sideRoarHandoffActive = true; sideRoarHeadWeight = 1f; sideRoarHandoffTime = 0f;
            sideRoarHandoffFading = false;
            frontFeet.BeginSideHandoff();
        }
        private void AdvanceSideRoarHandoff()
        {
            if (!SideHandoffMoving) return;
            sideRoarHandoffTime += Time.deltaTime;
            Vector3 direction = hasCuriosityPath && curiosityCorner < curiosityCorners.Length
                ? curiosityCorners[curiosityCorner] - transform.position : lastSeenPlayer - transform.position;
            direction.y = 0f;
            float error = Vector3.Angle(GetVisualForward(), direction);
            if (sideRoarHandoffTime >= 2f && error > 10f || sideRoarHandoffTime >= sideRoarHandoffDuration
                && frontFeet.SideHandoffLandings >= 2 && error <= 10f)
                sideRoarHandoffFading = true;
            if (sideRoarHandoffFading)
            {
                sideRoarHeadWeight = Mathf.MoveTowards(sideRoarHeadWeight, 0f, Time.deltaTime / .25f);
                if (sideRoarHeadWeight <= 0f) ClearSideRoarHandoff();
            }
        }
        private float SideHandoffTurnRate(float rate)
        {
            if (!SideHandoffMoving || frontFeet == null) return rate;
            if (frontFeet.LeftPlanted && frontFeet.RightPlanted) return 0f;
            return Mathf.Min(rate, 45f);
        }
        private Vector3 SideHandoffPivotPosition(Vector3 position, Quaternion rotation)
        {
            if (!SideHandoffMoving || frontFeet == null || !frontFeet.TrySideHandoffPivot(out var pivot)) return position;
            Vector3 rotated = pivot + rotation * Quaternion.Inverse(transform.rotation) * (transform.position - pivot);
            Vector3 correction = rotated - transform.position; correction.y = 0f;
            return position + correction;
        }
        private void LateUpdate()
        {
            if (!sideRoarHandoffActive || sideHead == null || sideNeck == null) return;
            sideNeckBase = sideNeck.localRotation; sideHeadBase = sideHead.localRotation;
            Vector3 current = Vector3.ProjectOnPlane(sideHead.TransformDirection(sideHeadForward), Vector3.up);
            float yaw = Mathf.Clamp(Vector3.SignedAngle(current, sideHeadHeading, Vector3.up), -90f, 90f) * sideRoarHeadWeight;
            sideNeck.rotation = Quaternion.AngleAxis(yaw * .65f, Vector3.up) * sideNeck.rotation;
            sideHead.rotation = Quaternion.AngleAxis(yaw * .35f, Vector3.up) * sideHead.rotation;
            sideHeadPoseApplied = true;
        }
        private void OnDisable() => ClearSideRoarHandoff();
    }
}
