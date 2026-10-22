using UnityEngine;

namespace JurassicWorld
{
    public sealed partial class DinosaurFootLock
    {
        private sealed class SupportFoot
        {
            public Transform foot, sole, thigh, knee;
            public bool planted;
            public Vector3 anchor, heading;
            public float positionError, headingError, correction;
            public bool releasing;
            public float releaseTime, lastYaw;
            public Vector3 lastOffset;
            public Vector3 seamOffset;
            public float seamTime = .1f;
            public float seamDuration = .1f;
            public bool handoffReleased;
            public Vector3 releaseHeading;
            public Vector3 soleForward;
            public bool stableHeading;
        }
        private SupportFoot leftSupport, rightSupport;
        private float supportRecoveryUntil;
        private bool recoverLeftSupport, recoverRightSupport;
        private bool returnHandoff;
        private int returnLandings;
        private bool sideHandoff, sideLeftReleased, sideRightReleased;
        private int sideLandings;
        public int SideHandoffLandings => sideLandings;
        public Vector3 LeftSolePosition => leftSupport != null ? leftSupport.sole.position : transform.position;
        public Vector3 RightSolePosition => rightSupport != null ? rightSupport.sole.position : transform.position;
        public void BeginSideHandoff()
        {
            sideHandoff = true; sideLandings = 0;
            sideLeftReleased = sideRightReleased = false;
            supportRecoveryUntil = 0f;
        }
        public void EndSideHandoff() => sideHandoff = false;
        public bool TrySideHandoffPivot(out Vector3 pivot)
        {
            pivot = Vector3.zero;
            if (!sideHandoff || LeftPlanted == RightPlanted) return false;
            pivot = LeftPlanted ? leftSupport.anchor : rightSupport.anchor;
            return true;
        }
        public bool ReturnHandoffActive => returnHandoff;
        public void GetReturnSoleHeadings(out Vector3 left, out Vector3 right)
        {
            left = leftSupport.planted ? leftSupport.heading : leftSupport.foot.TransformDirection(leftSupport.soleForward);
            right = rightSupport.planted ? rightSupport.heading : rightSupport.foot.TransformDirection(rightSupport.soleForward);
        }
        public void BeginReturnHandoff()
        {
            returnHandoff = true; returnLandings = 0; supportRecoveryUntil = 0f;
            if (leftSupport != null) leftSupport.handoffReleased = false;
            if (rightSupport != null) rightSupport.handoffReleased = false;
        }
        public void EndReturnHandoff()
        {
            returnHandoff = false;
            if (leftSupport != null) leftSupport.handoffReleased = false;
            if (rightSupport != null) rightSupport.handoffReleased = false;
        }
        public bool LeftPlanted => leftSupport != null && leftSupport.planted;
        public bool RightPlanted => rightSupport != null && rightSupport.planted;
        public Vector3 LeftAnchor => leftSupport != null ? leftSupport.anchor : Vector3.zero;
        public Vector3 RightAnchor => rightSupport != null ? rightSupport.anchor : Vector3.zero;
        public float LeftPlantError => leftSupport != null ? leftSupport.positionError : 0f;
        public float RightPlantError => rightSupport != null ? rightSupport.positionError : 0f;
        public float LeftYawError => leftSupport != null ? leftSupport.headingError : 0f;
        public float RightYawError => rightSupport != null ? rightSupport.headingError : 0f;
        public float MaximumLegCorrection => Mathf.Max(leftSupport?.correction ?? 0f, rightSupport?.correction ?? 0f);

        public void ResetSupportAnchors()
        {
            ResetGaitFeet();
            EndReturnHandoff();
            EndSideHandoff();
            supportRecoveryUntil = 0f;
            recoverLeftSupport = recoverRightSupport = false;
            foreach (var support in new[] { leftSupport, rightSupport })
                if (support != null) { support.planted = support.releasing = false; support.lastOffset = support.seamOffset = Vector3.zero; support.lastYaw = 0f; support.seamTime = support.seamDuration; }
        }

        public void BlendWalkSeam(Vector3 leftOffset, Vector3 rightOffset, float duration = .1f)
        {
            Quaternion heading = agent.transform.rotation * Quaternion.Euler(0f, agent.VisualYaw, 0f);
            if (leftSupport != null) { leftSupport.seamOffset = heading * leftOffset; leftSupport.seamTime = 0f; leftSupport.seamDuration = duration; }
            if (rightSupport != null) { rightSupport.seamOffset = heading * rightOffset; rightSupport.seamTime = 0f; rightSupport.seamDuration = duration; }
        }

        public void HoldSupportDuringRecovery(float duration)
        {
            recoverLeftSupport = LeftPlanted;
            recoverRightSupport = RightPlanted;
            supportRecoveryUntil = Time.time + duration;
        }

        private static Transform NamedBone(Transform root, string name)
        {
            foreach (var node in root.GetComponentsInChildren<Transform>(true)) if (node.name == name) return node;
            return null;
        }
        private void BindTRexFeet(Transform visual)
        {
            if (agent == null || !agent.HasFootPhaseData) return;
            leftSupport = CreateFoot(visual, "L"); rightSupport = CreateFoot(visual, "R");
        }
        private static SupportFoot CreateFoot(Transform visual, string side)
        {
            var foot = NamedBone(visual, "jt_Foot_" + side);
            var sole = NamedBone(visual, "jt_ClawMiddle_" + side);
            if (foot == null || sole == null) return null;
            Transform knee = foot.parent;
            while (knee != null && !knee.name.Contains("Knee")) knee = knee.parent;
            if (knee == null || knee.parent == null) return null;
            return new SupportFoot { foot = foot, sole = sole, knee = knee, thigh = knee.parent,
                soleForward = foot.InverseTransformDirection(sole.position - foot.position).normalized };
        }
        private static Vector3 GroundHeading(SupportFoot support)
        {
            Vector3 direction = support.stableHeading ? support.foot.TransformDirection(support.soleForward)
                : support.sole.position - support.foot.position; direction.y = 0f;
            return direction.sqrMagnitude > .00001f ? direction.normalized : Vector3.forward;
        }
        private static void UpdateContact(SupportFoot support, float weight)
        {
            if (support.planted && weight < .25f)
            {
                support.planted = false;
                support.releasing = true;
                support.releaseTime = 0f;
            }
            if (!support.planted && !support.releasing && weight >= .65f)
            {
                support.anchor = support.sole.position;
                support.heading = GroundHeading(support);
                support.planted = true;
            }
        }
        private void UpdateTRexFeet()
        {
            SetReturnHeadingMode(leftSupport, agent.UseReturnSoleHeading || sideHandoff);
            SetReturnHeadingMode(rightSupport, agent.UseReturnSoleHeading || sideHandoff);
            agent.GetFootPhase(out float left, out float right);
            if (Time.time < supportRecoveryUntil)
            {
                if (recoverLeftSupport) left = 1f;
                if (recoverRightSupport) right = 1f;
            }
            bool wasLeft = leftSupport.planted, wasRight = rightSupport.planted;
            UpdateContact(leftSupport, left); UpdateContact(rightSupport, right);
            if (sideHandoff && agent.SideHandoffMoving)
            {
                if (wasLeft && !leftSupport.planted) sideLeftReleased = true;
                if (wasRight && !rightSupport.planted) sideRightReleased = true;
                if (!wasLeft && leftSupport.planted && sideLeftReleased) { sideLandings++; sideLeftReleased = false; }
                if (!wasRight && rightSupport.planted && sideRightReleased) { sideLandings++; sideRightReleased = false; }
            }
            if (returnHandoff && agent.ReturnFootHandoffMoving)
            {
                if (wasLeft && !leftSupport.planted) { leftSupport.handoffReleased = true; leftSupport.releaseHeading = leftSupport.heading; }
                if (wasRight && !rightSupport.planted) { rightSupport.handoffReleased = true; rightSupport.releaseHeading = rightSupport.heading; }
                if (!wasLeft && leftSupport.planted && leftSupport.handoffReleased) { returnLandings++; leftSupport.handoffReleased = false; }
                if (!wasRight && rightSupport.planted && rightSupport.handoffReleased) { returnLandings++; rightSupport.handoffReleased = false; }
            }
            if (agent.IsSteppingTurn || agent.ForestPerformanceSupport || sideHandoff && agent.SideHandoffMoving || returnHandoff && agent.ReturnFootHandoffMoving)
            {
                Vector3 correction = Vector3.zero; int count = 0;
                if (leftSupport.planted) { correction += leftSupport.anchor - leftSupport.sole.position; count++; }
                if (rightSupport.planted) { correction += rightSupport.anchor - rightSupport.sole.position; count++; }
                if (count != 0) agent.ApplyFootRootCorrection(correction / count);
            }
            SolveSupport(leftSupport); SolveSupport(rightSupport);
            if (returnHandoff && returnLandings >= 2) EndReturnHandoff();
        }
        private static void SetReturnHeadingMode(SupportFoot support, bool stable)
        {
            if (support.stableHeading == stable) return;
            Vector3 previous = GroundHeading(support);
            support.stableHeading = stable;
            float delta = Vector3.SignedAngle(previous, GroundHeading(support), Vector3.up);
            // Changing the measurement axis must not rotate an already planted ankle.
            support.heading = Quaternion.AngleAxis(delta, Vector3.up) * support.heading;
            support.releaseHeading = Quaternion.AngleAxis(delta, Vector3.up) * support.releaseHeading;
        }

        private static void SolveSupport(SupportFoot support)
        {
            bool seam = support.seamTime < support.seamDuration;
            if (!support.planted && !support.releasing && !seam) return;
            Quaternion authoredRotation = support.foot.rotation;
            Vector3 authoredSole = support.sole.position;
            Vector3 localSole = support.foot.InverseTransformPoint(support.sole.position);
            float yaw = support.planted ? Vector3.SignedAngle(GroundHeading(support), support.heading, Vector3.up) : 0f;
            Vector3 soleTarget = support.planted ? support.anchor : authoredSole;
            if (support.releasing)
            {
                support.releaseTime += Time.deltaTime;
                float amount = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(support.releaseTime / (support.handoffReleased ? .2f : .1f)));
                soleTarget = authoredSole + support.lastOffset * amount;
                yaw = support.lastYaw * amount;
                if (support.handoffReleased)
                {
                    // Blend world headings rather than carrying a correction relative to the old torso pose.
                    Vector3 authoredHeading = GroundHeading(support);
                    Vector3 desired = Vector3.Slerp(support.releaseHeading, authoredHeading, 1f - amount);
                    yaw = Vector3.SignedAngle(authoredHeading, desired, Vector3.up);
                }
                if (amount <= 0f)
                {
                    support.releasing = false;
                    support.positionError = support.headingError = support.correction = 0f;
                    if (!seam) return;
                }
            }
            if (seam)
            {
                support.seamTime += Time.deltaTime;
                if (!support.planted)
                    soleTarget += support.seamOffset * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(support.seamTime / support.seamDuration)));
            }
            Quaternion targetRotation = Quaternion.AngleAxis(yaw, Vector3.up) * authoredRotation;
            // Preserve authored pitch/roll. Only the ground heading is anchored.
            Vector3 offset = targetRotation * Vector3.Scale(localSole, support.foot.lossyScale);
            Vector3 target = soleTarget - offset;
            support.correction = Vector3.Distance(target, support.foot.position);
            Vector3 root = support.thigh.position;
            float upper = Vector3.Distance(root, support.knee.position);
            float lower = Vector3.Distance(support.knee.position, support.foot.position);
            Vector3 delta = target - root;
            float distance = delta.magnitude;
            if (distance < .001f || upper < .001f || lower < .001f) return;
            Vector3 direction = delta / distance;
            // Rotation-only two-bone IK keeps limb lengths unchanged and never moves the pelvis.
            float reach = Mathf.Clamp(distance, Mathf.Abs(upper - lower) + .0001f, upper + lower - .0001f);
            float along = (upper * upper + reach * reach - lower * lower) / (2f * reach);
            float bend = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            Vector3 pole = Vector3.ProjectOnPlane(support.knee.position - root, direction).normalized;
            if (pole.sqrMagnitude < .01f) pole = Vector3.ProjectOnPlane(support.thigh.forward, direction).normalized;
            Vector3 kneeTarget = root + direction * along + pole * bend;
            support.thigh.rotation = Quaternion.FromToRotation(support.knee.position - root, kneeTarget - root) * support.thigh.rotation;
            support.knee.rotation = Quaternion.FromToRotation(support.foot.position - support.knee.position, target - support.knee.position) * support.knee.rotation;
            support.foot.rotation = targetRotation;
            if (support.planted)
            {
                support.lastOffset = support.sole.position - authoredSole;
                support.lastYaw = yaw;
                support.positionError = Vector3.Distance(support.sole.position, support.anchor);
                support.headingError = Vector3.Angle(GroundHeading(support), support.heading);
            }
        }
    }
}
