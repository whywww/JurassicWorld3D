using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    internal static class TRexTurnCalibration
    {
        internal static Transform Bone(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
        internal static Vector3 Sole(Transform root, string side) => Bone(root, "jt_ClawMiddle_" + side).position;

        internal static void Sample(DinosaurAgent agent, Animation animation, DinosaurInteractionProfile.Motion motion)
        {
            int count = Mathf.CeilToInt(motion.clip.length * 60f) + 1;
            var frames = new DinosaurInteractionProfile.FootFrame[count];
            var leftHeels = new float[count]; var rightHeels = new float[count];
            Transform left = Bone(animation.transform, "jt_ClawMiddle_L"), right = Bone(animation.transform, "jt_ClawMiddle_R");
            Transform cog = Bone(animation.transform, "jt_Cog_C");
            float previousYaw = 0f, authoredYaw = 0f;
            var skin = animation.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(s => s.sharedMesh.boneWeights.Length > 0);
            Transform leftFoot = Bone(animation.transform, "jt_Foot_L"), rightFoot = Bone(animation.transform, "jt_Foot_R");
            Vector3 leftForward = leftFoot.InverseTransformDirection(left.position - leftFoot.position).normalized;
            Vector3 rightForward = rightFoot.InverseTransformDirection(right.position - rightFoot.position).normalized;
            Transform leftKnee = leftFoot.parent, rightKnee = rightFoot.parent;
            while (!leftKnee.name.Contains("Knee")) leftKnee = leftKnee.parent;
            while (!rightKnee.name.Contains("Knee")) rightKnee = rightKnee.parent;
            var leftVertices = FootVertices(skin, leftFoot); var rightVertices = FootVertices(skin, rightFoot);
            var mesh = new Mesh(); var vertices = new List<Vector3>();
            var poseWeights = skin.sharedMesh.boneWeights;
            Quaternion inverse = Quaternion.Inverse(agent.transform.rotation * Quaternion.Euler(0f, agent.VisualYaw, 0f));
            bool owns = !AnimationMode.InAnimationMode();
            if (owns) AnimationMode.StartAnimationMode();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    float time = motion.clip.length * i / (count - 1);
                    AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(animation.gameObject, motion.clip, time); AnimationMode.EndSampling();
                    frames[i] = new DinosaurInteractionProfile.FootFrame { time = time,
                        leftSole = inverse * (left.position - agent.transform.position), rightSole = inverse * (right.position - agent.transform.position) };
                    leftHeels[i] = leftFoot.position.y - agent.MovementHeight; rightHeels[i] = rightFoot.position.y - agent.MovementHeight;
                    frames[i].leftHeading = inverse * leftFoot.TransformDirection(leftForward); frames[i].leftHeading.y = 0f; frames[i].leftHeading.Normalize();
                    frames[i].rightHeading = inverse * rightFoot.TransformDirection(rightForward); frames[i].rightHeading.y = 0f; frames[i].rightHeading.Normalize();
                    frames[i].leftKnee = inverse * (leftKnee.position - agent.transform.position); frames[i].rightKnee = inverse * (rightKnee.position - agent.transform.position);
                    Vector3 facing = inverse * cog.forward; facing.y = 0f;
                    float sampleYaw = Vector3.SignedAngle(Vector3.forward, facing, Vector3.up);
                    if (i > 0) authoredYaw += Mathf.DeltaAngle(previousYaw, sampleYaw);
                    frames[i].authoredYaw = authoredYaw; previousYaw = sampleYaw;
                    skin.BakeMesh(mesh); mesh.GetVertices(vertices);
                    var poseBands = new Dictionary<int, Bounds>();
                    for (int v = 0; v < vertices.Count; v++)
                    {
                        Vector3 point = inverse * (skin.transform.TransformPoint(vertices[v]) - agent.transform.position);
                        int layer = Mathf.FloorToInt(point.y / .5f);
                        var weight = poseWeights[v]; int bone = weight.boneIndex0; float largest = weight.weight0;
                        if (weight.weight1 > largest) { bone = weight.boneIndex1; largest = weight.weight1; }
                        if (weight.weight2 > largest) { bone = weight.boneIndex2; largest = weight.weight2; }
                        if (weight.weight3 > largest) bone = weight.boneIndex3;
                        int key = bone * 1000 + layer + 100;
                        if (poseBands.TryGetValue(key, out var bounds)) { bounds.Encapsulate(point); poseBands[key] = bounds; }
                        else poseBands[key] = new Bounds(point, Vector3.zero);
                    }
                    frames[i].playerBodyBands = poseBands.Select(b => new DinosaurCoreBody.Band { layer = b.Key, bounds = b.Value }).ToArray();
                    frames[i].leftGroundHeight = leftVertices.Min(v => skin.transform.TransformPoint(vertices[v]).y - agent.MovementHeight);
                    frames[i].rightGroundHeight = rightVertices.Min(v => skin.transform.TransformPoint(vertices[v]).y - agent.MovementHeight);
                }
            }
            finally { if (owns) AnimationMode.StopAnimationMode(); UnityEngine.Object.DestroyImmediate(mesh); }
            float lowLeft = frames.Min(f => f.leftGroundHeight), lowRight = frames.Min(f => f.rightGroundHeight);
            for (int i = 0; i < count; i++)
            {
                frames[i].leftContact = frames[i].leftGroundHeight <= lowLeft + .08f;
                frames[i].rightContact = frames[i].rightGroundHeight <= lowRight + .08f;
            }
            if (motion.role == InteractionMotionRole.Turn180Look)
            {
                // A curled claw can stay low while the heel lifts and rolls. That is not a flat
                // planted foot: retaining its old yaw then corrupts the subsequent walk entry.
                float lh = leftHeels.Min(), rh = rightHeels.Min();
                float lt = frames.Min(f => f.leftSole.y), rt = frames.Min(f => f.rightSole.y);
                for (int i = 0; i < count; i++)
                {
                    frames[i].leftContact &= leftHeels[i] <= lh + .07f && frames[i].leftSole.y <= lt + .06f;
                    frames[i].rightContact &= rightHeels[i] <= rh + .07f && frames[i].rightSole.y <= rt + .06f;
                }
            }
            if (motion.role == InteractionMotionRole.Walk || motion.role == InteractionMotionRole.FollowWalk || motion.role == InteractionMotionRole.Run
                || motion.role == InteractionMotionRole.ChaseRoar)
            {
                // A curled toe/heel mesh extremum can be low while its foot is swinging.
                // Use the sole marker's stable height and backwards stance travel for walking.
                var leftContacts = WalkingContacts(frames, true);
                var rightContacts = WalkingContacts(frames, false);
                for (int i = 0; i < count; i++)
                {
                    frames[i].leftContact = leftContacts[i];
                    frames[i].rightContact = rightContacts[i];
                }
            }
            motion.footFrames = frames;
            var playerPoses = new List<DinosaurInteractionProfile.PlayerPose>();
            for (int i = 0; i < frames.Length; i += 3)
            {
                // Each stored pose conservatively covers all three 60Hz samples, avoiding duplicate serialized arrays.
                var groups = new Dictionary<int, Bounds>();
                for (int j = i; j <= Mathf.Min(frames.Length - 1, i + 3); j++)
                    foreach (var band in frames[j].playerBodyBands)
                    {
                        if (groups.TryGetValue(band.layer, out var bounds)) { bounds.Encapsulate(band.bounds); groups[band.layer] = bounds; }
                        else groups[band.layer] = band.bounds;
                    }
                playerPoses.Add(new DinosaurInteractionProfile.PlayerPose { time = frames[i].time,
                    bands = groups.Select(b => { var bounds = b.Value; bounds.Expand(.04f); return new DinosaurCoreBody.Band { layer = b.Key, bounds = bounds }; }).ToArray() });
            }
            motion.playerPoses = playerPoses.ToArray();
            if (motion.role == InteractionMotionRole.TurnLeft || motion.role == InteractionMotionRole.TurnRight || motion.role == InteractionMotionRole.Turn180Look)
                ReconstructTurn(motion);
            else if (motion.role == InteractionMotionRole.Investigate || motion.role == InteractionMotionRole.InvestigateOut
                || motion.role == InteractionMotionRole.BigRoarStep || motion.role == InteractionMotionRole.Roar
                || motion.role == InteractionMotionRole.CaveBite || motion.role == InteractionMotionRole.CaveBiteOut
                || motion.role == InteractionMotionRole.BiteCloseLeft || motion.role == InteractionMotionRole.BiteCloseRight || motion.role == InteractionMotionRole.IdleAttack
                || motion.role == InteractionMotionRole.LookForwardTwo || motion.role == InteractionMotionRole.SideRoarLeft || motion.role == InteractionMotionRole.SideRoarRight)
                ReconstructTurn(motion);
        }

        private static bool[] WalkingContacts(DinosaurInteractionProfile.FootFrame[] frames, bool left)
        {
            var heights = frames.Select(f => left ? f.leftSole.y : f.rightSole.y).OrderBy(y => y).ToArray();
            float ground = heights[heights.Length / 10];
            var contacts = new bool[frames.Length];
            for (int i = 0; i < frames.Length; i++)
            {
                int a = Mathf.Max(0, i - 2), b = Mathf.Min(frames.Length - 1, i + 2);
                Vector3 p = left ? frames[i].leftSole : frames[i].rightSole;
                Vector3 before = left ? frames[a].leftSole : frames[a].rightSole;
                Vector3 after = left ? frames[b].leftSole : frames[b].rightSole;
                float speed = (after.z - before.z) / Mathf.Max(.001f, frames[b].time - frames[a].time);
                contacts[i] = p.y <= ground + .055f && speed < -.2f;
            }
            // Close isolated one-frame holes, then discard contacts shorter than 50 ms.
            var stable = (bool[])contacts.Clone();
            for (int i = 1; i < contacts.Length - 1; i++)
                if (contacts[i - 1] && contacts[i + 1]) stable[i] = true;
            for (int i = 0; i < stable.Length;)
            {
                if (!stable[i]) { i++; continue; }
                int start = i;
                while (i < stable.Length && stable[i]) i++;
                if (frames[i - 1].time - frames[start].time < .05f)
                    for (int j = start; j < i; j++) stable[j] = false;
            }
            return stable;
        }

        public static void RefreshWalkContacts()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var agent = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex");
            var animation = agent.GetComponentInChildren<Animation>(true);
            var profile = AssetDatabase.LoadAssetAtPath<DinosaurInteractionProfile>("Assets/Navigation/TRexCuriosity.asset");
            var motion = profile.Find(InteractionMotionRole.Walk);
            var copy = motion.clip;
            try
            {
                motion.clip = animation[motion.sourceName].clip;
                Sample(agent, animation, motion);
            }
            finally { motion.clip = copy; }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log("TREX_WALK_CONTACTS_UPDATED: sampled actual route walk; scene and navigation unchanged.");
        }

        private static int[] FootVertices(SkinnedMeshRenderer skin, Transform foot)
        {
            bool[] bones = skin.bones.Select(b => b == foot || b.IsChildOf(foot)).ToArray();
            return skin.sharedMesh.boneWeights.Select((w, i) => new { index = i, influence =
                (bones[w.boneIndex0] ? w.weight0 : 0f) + (bones[w.boneIndex1] ? w.weight1 : 0f)
                + (bones[w.boneIndex2] ? w.weight2 : 0f) + (bones[w.boneIndex3] ? w.weight3 : 0f) })
                .Where(v => v.influence > .6f).Select(v => v.index).ToArray();
        }

        private static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
        private static Vector2 Rotate(Vector2 p, float yaw)
        {
            float a = yaw * Mathf.Deg2Rad;
            return new Vector2(p.x * Mathf.Cos(a) + p.y * Mathf.Sin(a), -p.x * Mathf.Sin(a) + p.y * Mathf.Cos(a));
        }
        internal static void ReconstructTurn(DinosaurInteractionProfile.Motion motion)
        {
            var frames = motion.footFrames;
            var weights = new float[frames.Length];
            float total = 0f;
            for (int i = 1; i < frames.Length; i++)
            {
                // Derive turn progress from stepping phases, then soften contact transitions.
                if (frames[i].leftContact == frames[i].rightContact) continue;
                Vector3 swing = frames[i].leftContact ? frames[i].rightSole - frames[i - 1].rightSole : frames[i].leftSole - frames[i - 1].leftSole;
                weights[i] = new Vector2(swing.x, swing.z).magnitude;
                total += weights[i];
            }
            var smooth = new float[weights.Length]; total = 0f;
            for (int i = 1; i < weights.Length; i++)
            {
                // Spread a support transition over 0.2s instead of concentrating the
                // turn in one isolated contact sample (up to 28 degrees at 60Hz).
                for (int j = Mathf.Max(1, i - 6); j <= Mathf.Min(weights.Length - 1, i + 6); j++) smooth[i] += weights[j] / 13f;
                total += smooth[i];
            }
            weights = smooth;
            if (total < .01f && Mathf.Abs(motion.turnDegrees) > 1f) throw new InvalidOperationException("Turn has no identifiable stepping support phase: " + motion.sourceName);
            total = Mathf.Max(total, .01f);
            Vector2 root = Vector2.zero;
            float yaw = 0f, peakRate = 0f, maximumOffset = 0f;
            for (int i = 1; i < frames.Length; i++)
            {
                float previousDesired = frames[i - 1].yaw + (Mathf.Abs(motion.turnDegrees) > 1f ? frames[i - 1].authoredYaw : 0f);
                float nextYaw = previousDesired + motion.turnDegrees * weights[i] / total
                    - (Mathf.Abs(motion.turnDegrees) > 1f ? frames[i].authoredYaw : 0f);
                root += SupportTravel(frames[i - 1], frames[i], yaw, nextYaw);
                frames[i].rootOffset = root; frames[i].yaw = nextYaw;
                peakRate = Mathf.Max(peakRate, Mathf.Abs(nextYaw - yaw) / (frames[i].time - frames[i - 1].time));
                maximumOffset = Mathf.Max(maximumOffset, root.magnitude);
                yaw = nextYaw;
            }
            motion.turnPlaybackSpeed = motion.role == InteractionMotionRole.TurnLeft || motion.role == InteractionMotionRole.TurnRight
                || motion.role == InteractionMotionRole.Turn180Look || Mathf.Abs(motion.turnDegrees) < 1f
                ? 1f : Mathf.Clamp(90f / Mathf.Max(90f, peakRate), .2f, 1f);
            motion.blendDuration = .12f;
            Debug.Log($"TURN_CALIBRATION {motion.sourceName}: frames={frames.Length}, rootYaw={yaw:F2}, authoredYaw={frames[frames.Length - 1].authoredYaw:F2}, maxRootOffset={maximumOffset:F3}, playback={motion.turnPlaybackSpeed:F3}.");
        }

        // Integrate adjacent sole positions. Reusing a stale support anchor when a foot
        // lands again creates a discontinuity unrelated to the authored animation.
        private static Vector2 SupportTravel(DinosaurInteractionProfile.FootFrame previous,
            DinosaurInteractionProfile.FootFrame next, float yaw, float nextYaw)
        {
            Vector2 delta = Vector2.zero; int count = 0;
            if (next.leftContact) { delta += Rotate(XZ(previous.leftSole), yaw) - Rotate(XZ(next.leftSole), nextYaw); count++; }
            if (next.rightContact) { delta += Rotate(XZ(previous.rightSole), yaw) - Rotate(XZ(next.rightSole), nextYaw); count++; }
            return count == 0 ? Vector2.zero : delta / count;
        }

        public static void Repair90DegreeTurns()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before repairing turn samples.");
            foreach (string path in new[] { "Assets/Navigation/TRexCuriosity.asset", "Assets/Scenes/TRexFrontTestAssets/SideInteractionProfile.asset" })
            {
                var profile = AssetDatabase.LoadAssetAtPath<DinosaurInteractionProfile>(path);
                if (profile == null) throw new InvalidOperationException("Missing turn profile: " + path);
                foreach (var role in new[] { InteractionMotionRole.TurnLeft, InteractionMotionRole.TurnRight })
                {
                    var motion = profile.Find(role);
                    if (motion == null) throw new InvalidOperationException("Missing turn: " + role + " in " + path);
                    ReconstructTurn(motion);
                }
                EditorUtility.SetDirty(profile);
            }
            AssetDatabase.SaveAssets();
        }

        public static void RepairLegacyRootContinuity()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before repairing root samples.");
            var profile = AssetDatabase.LoadAssetAtPath<DinosaurInteractionProfile>("Assets/Navigation/TRexCuriosity.asset");
            var report = new StringBuilder("# Legacy root continuity\n\nRebuild rootOffset and smooth the legacy 90-degree root heading across contact transitions. Clip assets, net heading and contacts are preserved; 90-degree turns use original playback speed.\n\n");
            foreach (var motion in profile.motions)
            {
                if (motion.role != InteractionMotionRole.TurnLeft && motion.role != InteractionMotionRole.TurnRight
                    && motion.role != InteractionMotionRole.CaveBite && motion.role != InteractionMotionRole.CaveBiteOut) continue;
                var frames = motion.footFrames; Vector2 root = frames[0].rootOffset;
                float before = 0f, after = 0f;
                for (int i = 1; i < frames.Length; i++) before = Mathf.Max(before, Vector2.Distance(frames[i-1].rootOffset, frames[i].rootOffset));
                if (motion.role == InteractionMotionRole.TurnLeft || motion.role == InteractionMotionRole.TurnRight)
                {
                    float blend = motion.blendDuration;
                    ReconstructTurn(motion);
                    motion.blendDuration = blend;
                }
                for (int i = 1; i < frames.Length; i++)
                {
                    Vector2 delta = SupportTravel(frames[i-1], frames[i], frames[i-1].yaw, frames[i].yaw);
                    root += delta; frames[i].rootOffset = root; after = Mathf.Max(after, delta.magnitude);
                }
                report.AppendLine($"- {motion.sourceName}: max adjacent root step {before:F4} → {after:F4}m; playback {motion.turnPlaybackSpeed:F4}.");
            }
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();
            File.WriteAllText(RainforestSample.Output + "/RootContinuity.md", report.ToString());
            Debug.Log("ROOT_CONTINUITY_REPAIRED");
        }

        public static void Audit()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var agent = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex");
            var animation = agent.GetComponentInChildren<Animation>(true);
            Directory.CreateDirectory("Logs/turns");
            foreach (var name in new[] { "turn 90 left", "turn 90 right", "walk slow loop" })
            {
                AnimationClip clip = null;
                foreach (AnimationState state in animation) if (state.name == name) clip = state.clip;
                var role = name == "turn 90 left" ? InteractionMotionRole.TurnLeft : name == "turn 90 right" ? InteractionMotionRole.TurnRight : InteractionMotionRole.Walk;
                var motion = new DinosaurInteractionProfile.Motion { clip = clip, role = role, sourceName = name, turnDegrees = role == InteractionMotionRole.TurnLeft ? -90f : role == InteractionMotionRole.TurnRight ? 90f : 0f };
                Sample(agent, animation, motion);
                var output = new StringBuilder("time,leftX,leftY,leftZ,rightX,rightY,rightZ,leftContact,rightContact,rootX,rootZ,yaw\n");
                foreach (var f in motion.footFrames) output.AppendLine(FormattableString.Invariant($"{f.time},{f.leftSole.x},{f.leftSole.y},{f.leftSole.z},{f.rightSole.x},{f.rightSole.y},{f.rightSole.z},{f.leftContact},{f.rightContact},{f.rootOffset.x},{f.rootOffset.y},{f.yaw}"));
                File.WriteAllText("Logs/turns/" + role + ".csv", output.ToString());
            }
        }
    }
}
