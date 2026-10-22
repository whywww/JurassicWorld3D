using UnityEngine;

namespace JurassicWorld
{
    public sealed partial class DinosaurFootLock
    {
        private sealed class GaitFoot
        {
            public Transform foot, knee, hip;
            public Vector3 soleOffset, anchor, releaseOffset;
            public Quaternion anchorRotation, releaseRotation;
            public bool planted;
            public float release = 1f, releaseDuration = .12f;
            public Vector3 Sole => foot.TransformPoint(soleOffset);
        }
        private GaitFoot[] gaitFeet;
        private Animation gaitAnimation;
        public int GaitFootCount => gaitFeet != null ? gaitFeet.Length : 0;
        public bool GaitPlanted(int index) => gaitFeet[index].planted;
        public Vector3 GaitSole(int index) => gaitFeet[index].Sole;
        public Vector3 GaitAnchor(int index) => gaitFeet[index].anchor;
        public float GaitMaximumReachError { get; private set; }

        private void BindGaitFeet()
        {
            gaitFeet = null;
            if (agent == null || agent.GaitProfile == null) return;
            gaitAnimation = agent.GetComponentInChildren<Animation>(true);
            var profile = agent.GaitProfile;
            var bound = new GaitFoot[profile.feet.Length];
            for (int i = 0; i < bound.Length; i++)
            {
                Transform foot = gaitAnimation.transform.Find(profile.feet[i].path);
                if (foot == null || foot.parent == null || foot.parent.parent == null)
                { Debug.LogError("Missing baked gait leg: " + profile.feet[i].path, this); return; }
                Transform knee = gaitAnimation.transform.Find(profile.feet[i].kneePath);
                Transform hip = gaitAnimation.transform.Find(profile.feet[i].hipPath);
                if (knee == null || hip == null) { Debug.LogError("Missing baked knee/hip", this); return; }
                bound[i] = new GaitFoot { foot = foot, knee = knee, hip = hip,
                    soleOffset = profile.feet[i].soleOffset };
            }
            gaitFeet = bound;
        }
        private void ResetGaitFeet()
        {
            if (gaitFeet == null) return;
            foreach (var foot in gaitFeet) { foot.planted = false; foot.release = 1f; }
        }

        // Also used by the deterministic editor acceptance, after exactly one animation sample.
        public void UpdateGaitFeet(float deltaTime)
        {
            if (gaitFeet == null) return;
            var profile = agent.GaitProfile;
            var state = gaitAnimation[profile.clip.name];
            bool walking = state != null && state.enabled && state.weight > .5f
                && (agent.GaitMotionActive || agent.GaitHoldsStoppedPose && state.speed == 0f);
            GaitMaximumReachError = 0f;
            for (int i = 0; i < gaitFeet.Length; i++)
            {
                var foot = gaitFeet[i];
                Vector3 rawSole = foot.Sole;
                Quaternion rawRotation = foot.foot.rotation;
                float contact = walking ? profile.Contact(i, state.time) : 0f;
                if (foot.planted && contact < .5f)
                {
                    foot.planted = false; foot.release = 0f;
                    foot.releaseOffset = foot.anchor - rawSole;
                    foot.releaseDuration = Mathf.Max(.12f, foot.releaseOffset.magnitude / 1.5f);
                    foot.releaseRotation = foot.anchorRotation * Quaternion.Inverse(rawRotation);
                }
                if (!foot.planted && contact >= .5f)
                {
                    foot.planted = true; foot.release = 1f;
                    foot.anchor = rawSole;
                    foot.anchor.y = agent.MovementHeight;
                    foot.anchorRotation = rawRotation;
                }
                if (foot.planted) SolveGaitLeg(foot, foot.anchor, foot.anchorRotation);
                else if (foot.release < 1f)
                {
                    foot.release = Mathf.Min(1f, foot.release + deltaTime / foot.releaseDuration);
                    float weight = 1f - Mathf.SmoothStep(0f, 1f, foot.release);
                    Vector3 target = rawSole + foot.releaseOffset * weight;
                    target.y = Mathf.Max(agent.MovementHeight, target.y);
                    SolveGaitLeg(foot, target, Quaternion.Slerp(Quaternion.identity, foot.releaseRotation, weight) * rawRotation);
                }
                else if (rawSole.y < agent.MovementHeight)
                {
                    Vector3 target = rawSole; target.y = agent.MovementHeight;
                    SolveGaitLeg(foot, target, rawRotation);
                }
                if (foot.planted) GaitMaximumReachError = Mathf.Max(GaitMaximumReachError,
                    Vector3.Distance(foot.Sole, foot.anchor));
            }
        }

        // Two-bone IK preserves segment lengths and the animated bend plane. Only this leg is rotated.
        private static void SolveGaitLeg(GaitFoot leg, Vector3 soleTarget, Quaternion rotation)
        {
            Vector3 offset = rotation * Quaternion.Inverse(leg.foot.rotation) * leg.foot.TransformVector(leg.soleOffset);
            Vector3 target = soleTarget - offset;
            Vector3 hip = leg.hip.position, knee = leg.knee.position, ankle = leg.foot.position;
            float upper = Vector3.Distance(hip, knee), lower = Vector3.Distance(knee, ankle);
            Vector3 aim = target - hip;
            if (upper < .0001f || lower < .0001f || aim.sqrMagnitude < .0000001f) return;
            float distance = Mathf.Clamp(aim.magnitude, Mathf.Abs(upper - lower) + .00001f, upper + lower - .00001f);
            Vector3 direction = aim.normalized;
            Vector3 bend = Vector3.ProjectOnPlane(knee - hip, direction);
            if (bend.sqrMagnitude < .0000001f) bend = Vector3.ProjectOnPlane(leg.knee.up, direction);
            if (bend.sqrMagnitude < .0000001f) bend = Vector3.ProjectOnPlane(Vector3.forward, direction);
            float along = (upper * upper + distance * distance - lower * lower) / (2f * distance);
            Vector3 desiredKnee = hip + direction * along + bend.normalized * Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            leg.hip.rotation = Quaternion.FromToRotation(knee - hip, desiredKnee - hip) * leg.hip.rotation;
            leg.knee.rotation = Quaternion.FromToRotation(leg.foot.position - leg.knee.position,
                hip + direction * distance - leg.knee.position) * leg.knee.rotation;
            leg.foot.rotation = rotation;
        }
    }
}
