using System.Collections.Generic;
using UnityEngine;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        private readonly Dictionary<long, Bounds> frontPoseUnion = new Dictionary<long, Bounds>();
        private readonly List<DinosaurCoreBody.Band> frontPoseResult = new List<DinosaurCoreBody.Band>();
        private readonly Dictionary<int, Bounds> frontBlendParts = new Dictionary<int, Bounds>();
        private string frontLastSpaceFailure;
        private string frontProbeStage = "Runtime";

        private static DinosaurCoreBody.Band[] ObstacleBands(DinosaurInteractionProfile.Motion motion, float time)
        {
            if (motion == null) return System.Array.Empty<DinosaurCoreBody.Band>();
            var poses = motion.obstaclePoses;
            if (poses == null || poses.Length == 0) return motion.bodyBands;
            int first = 0, last = poses.Length - 1;
            time = Mathf.Clamp(time, 0f, motion.clip.length);
            while (first < last) { int mid = (first + last + 1) / 2; if (poses[mid].time <= time) first = mid; else last = mid - 1; }
            return poses[first].bands;
        }

        private float FrontStateTime(DinosaurInteractionProfile.Motion motion, float advance = 0f)
        {
            if (motion == null || legacyAnimation == null) return 0f;
            var state = legacyAnimation[motion.StateName];
            if (state == null || !state.enabled || state.weight < .01f) state = legacyAnimation[motion.sourceName];
            if (state == null) return 0f;
            float time = state.time + advance;
            return state.wrapMode == WrapMode.Loop ? Mathf.Repeat(time, state.length) : Mathf.Clamp(time, 0f, state.length);
        }

        private void AddFrontPose(DinosaurCoreBody.Band[] bands)
        {
            foreach (var band in bands)
            {
                long key = ((long)band.part << 32) | (uint)band.layer;
                if (frontPoseUnion.TryGetValue(key, out var b)) { b.Encapsulate(band.bounds); frontPoseUnion[key] = b; }
                else frontPoseUnion[key] = band.bounds;
            }
        }

        // Union corresponding body parts, so an animation blend also covers the space
        // between the outgoing and incoming vertices without filling unrelated gaps.
        private DinosaurCoreBody.Band[] FrontPoseBands(DinosaurInteractionProfile.Motion motion, float time,
            DinosaurInteractionProfile.Motion outgoing = null, float elapsed = 0f, bool activeBlend = true, float previousTime = -1f)
        {
            var current = ObstacleBands(motion, time);
            bool blend = outgoing != null && outgoing != motion && elapsed <= Mathf.Max(.35f, motion.blendDuration);
            if (activeBlend && legacyAnimation != null)
            {
                foreach (var candidate in interactionProfile.motions)
                {
                    if (candidate == motion) continue;
                    var state = legacyAnimation[candidate.StateName];
                    if (state == null || !state.enabled || state.weight < .01f) state = legacyAnimation[candidate.sourceName];
                    blend |= state != null && state.enabled && state.weight >= .01f && elapsed <= Mathf.Max(.35f, motion.blendDuration);
                }
                blend |= FrontWaitBlendActive && elapsed <= .2f;
            }
            if (!blend && (previousTime < 0f || ReferenceEquals(current, ObstacleBands(motion, previousTime)))) return current;
            frontPoseUnion.Clear(); frontPoseResult.Clear();
            AddFrontPose(current);
            if (previousTime >= 0f) AddFrontPose(ObstacleBands(motion, previousTime));
            if (outgoing != null && outgoing != motion && elapsed <= Mathf.Max(.35f, motion.blendDuration))
                AddFrontPose(ObstacleBands(outgoing, FrontStateTime(outgoing, elapsed * FrontStateSpeed(outgoing))));
            if (activeBlend && legacyAnimation != null)
            {
                foreach (var candidate in interactionProfile.motions)
                {
                    if (candidate == motion) continue;
                    var state = legacyAnimation[candidate.StateName];
                    if (state == null || !state.enabled || state.weight < .01f) state = legacyAnimation[candidate.sourceName];
                    if (state == null || !state.enabled || state.weight < .01f || elapsed > Mathf.Max(.35f, motion.blendDuration)) continue;
                    AddFrontPose(ObstacleBands(candidate, FrontStateTime(candidate, elapsed * state.speed)));
                }
                if (FrontWaitBlendActive && elapsed <= .2f)
                {
                    var look = interactionProfile.Find(InteractionMotionRole.LookForward);
                    AddFrontPose(ObstacleBands(look, Mathf.Repeat(frontWaitTime + elapsed, look.clip.length)));
                }
            }
            if (blend)
            {
                // A head can cross height layers during a blend. Fill that part's
                // interpolated travel as well, rather than checking only both endpoints.
                frontBlendParts.Clear();
                foreach (var item in frontPoseUnion)
                {
                    int part = (int)(item.Key >> 32);
                    if (frontBlendParts.TryGetValue(part, out var b)) { b.Encapsulate(item.Value); frontBlendParts[part] = b; }
                    else frontBlendParts[part] = item.Value;
                }
                foreach (var item in frontBlendParts)
                {
                    var bounds = item.Value;
                    for (int layer = Mathf.FloorToInt(bounds.min.y / .5f); layer <= Mathf.FloorToInt(bounds.max.y / .5f); layer++)
                    {
                        var b = bounds; float bottom = Mathf.Max(bounds.min.y, layer * .5f), top = Mathf.Min(bounds.max.y, (layer + 1) * .5f);
                        b.center = new Vector3(b.center.x, (bottom + top) * .5f, b.center.z);
                        b.size = new Vector3(b.size.x, top - bottom, b.size.z);
                        frontPoseResult.Add(new DinosaurCoreBody.Band { layer = layer, part = item.Key, bounds = b });
                    }
                }
            }
            else foreach (var item in frontPoseUnion)
                frontPoseResult.Add(new DinosaurCoreBody.Band { layer = (int)item.Key, part = (int)(item.Key >> 32), bounds = item.Value });
            return frontPoseResult.ToArray();
        }

        private float FrontStateSpeed(DinosaurInteractionProfile.Motion motion)
        {
            if (legacyAnimation == null) return 0f;
            var state = legacyAnimation[motion.StateName];
            if (state == null || !state.enabled) state = legacyAnimation[motion.sourceName];
            return state != null ? state.speed : 0f;
        }

        // Special animation gate: other dinosaur bodies only, static clipping allowed.
        private bool FrontAnimatedStep(DinosaurInteractionProfile.Motion motion, float fromTime, float toTime,
            Vector3 from, Vector3 to, Quaternion fromRotation, Quaternion toRotation, bool activeBlend = true,
            DinosaurInteractionProfile.Motion outgoing = null)
        {
            EnsureFrontProbe();
            if (activeBlend && outgoing == null) frontProbeStage = $"Runtime {motion.sourceName} {toTime:F2}s";
            frontProbeBody.bands = FrontPoseBands(motion, toTime, outgoing, activeBlend && outgoing == null ? 0f : toTime,
                activeBlend, fromTime);
            return FrontAnimationPoseSafe(from, to, fromRotation, toRotation, frontProbeBody);
        }

        // Walking gate for navigation stepping turns and side locomotion, which are real travel.
        private bool FrontWalkAnimatedStep(DinosaurInteractionProfile.Motion motion, float fromTime, float toTime,
            Vector3 from, Vector3 to, Quaternion fromRotation, Quaternion toRotation, bool activeBlend = true,
            DinosaurInteractionProfile.Motion outgoing = null)
        {
            EnsureFrontProbe();
            if (activeBlend && outgoing == null) frontProbeStage = $"Runtime {motion.sourceName} {toTime:F2}s";
            frontProbeBody.bands = FrontPoseBands(motion, toTime, outgoing, activeBlend && outgoing == null ? 0f : toTime,
                activeBlend, fromTime);
            return FrontStepSafe(from, to, fromRotation, toRotation, frontProbeBody);
        }

        private void RefreshFrontPoseEnvelope()
        {
            if (!frontActive || frontEnvelope == null || frontEnvelopeMotion == null) return;
            var motion = IsSteppingTurn ? stepTurnMotion : frontAction ?? frontEnvelopeMotion;
            float time = IsSteppingTurn ? stepTurnTime : frontAction != null ? frontActionTime : FrontStateTime(motion);
            frontEnvelope.bands = FrontPoseBands(motion, time);
        }

        private void RememberFrontSpaceFailure(string reason, Vector3 position)
        {
            frontBlockReason = reason;
            string obstacle = interactionSettings.LastPoseObstacle;
            frontLastSpaceFailure = $"{frontProbeStage}: {reason}" + (string.IsNullOrEmpty(obstacle) ? "" : $" [{obstacle}]")
                + $" @ ({position.x:F2}, {position.z:F2})";
        }
    }
}
