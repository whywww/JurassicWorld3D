using System.Collections.Generic;
using UnityEngine;

namespace JurassicWorld
{
    [DefaultExecutionOrder(1000)]
    public sealed class DinosaurFootLock : MonoBehaviour
    {
        private readonly List<Transform> feet = new List<Transform>();
        private readonly List<float> groundHeights = new List<float>();
        private readonly List<float> previousHeights = new List<float>();
        private DinosaurAgent agent;
        private int plantedFootIndex = -1;
        private Vector3 plantedPosition;
        private float releaseHeight;
        private int jointDepth = 3;
        private bool releaseOnFootRise;
        private float riseThreshold;
        private float maximumLockDistance = float.PositiveInfinity;

        public void Bind(Transform visualRoot, DinosaurAgent dinosaurAgent, string[] exactFootNames = null,
            int maximumJointDepth = 3, bool releaseWhenFootRises = false, float contactHeight = -1f,
            float footRiseThreshold = -1f, float lockDistanceLimit = -1f)
        {
            agent = dinosaurAgent;
            jointDepth = Mathf.Max(1, maximumJointDepth);
            releaseOnFootRise = releaseWhenFootRises;
            maximumLockDistance = lockDistanceLimit > 0f ? lockDistanceLimit : float.PositiveInfinity;
            feet.Clear();
            groundHeights.Clear();
            previousHeights.Clear();
            plantedFootIndex = -1;

            Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            var bones = new List<Transform>();
            foreach (SkinnedMeshRenderer renderer in visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (Transform bone in renderer.bones)
                {
                    if (bone != null && !bones.Contains(bone))
                        bones.Add(bone);
                }
            }

            if (exactFootNames != null && exactFootNames.Length > 0)
                AddExactFeet(bones, exactFootNames);
            else
            {
                AddNamedFeet(bones, "foot");
                if (feet.Count < 2)
                    AddNamedFeet(bones, "ankle");
                if (feet.Count < 2)
                    AddNamedFeet(bones, "toe");
                if (feet.Count < 2)
                    AddLowestEndBones(bones, bounds);
            }

            releaseHeight = contactHeight > 0f ? contactHeight : Mathf.Max(0.04f, bounds.size.y * 0.04f);
            riseThreshold = footRiseThreshold > 0f
                ? footRiseThreshold
                : Mathf.Max(0.002f, bounds.size.y * 0.001f);
            foreach (Transform foot in feet)
            {
                groundHeights.Add(foot.position.y);
                previousHeights.Add(foot.position.y);
            }

            if (feet.Count < 2)
                Debug.LogWarning(agent.DisplayName + "未找到足够的脚部骨骼，已停用落脚锁定。", this);
        }

        private void LateUpdate()
        {
            if (agent == null || feet.Count < 2)
                return;

            if (agent.CurrentState == DinosaurAgent.DinosaurState.Idle)
            {
                plantedFootIndex = -1;
                for (int i = 0; i < feet.Count; i++)
                {
                    if (feet[i] != null)
                    {
                        groundHeights[i] = Mathf.Min(groundHeights[i], feet[i].position.y);
                        previousHeights[i] = feet[i].position.y;
                    }
                }
                return;
            }

            int lowestFootIndex = -1;
            float lowestClearance = float.MaxValue;
            bool plantedFootIsRising = false;
            for (int i = 0; i < feet.Count; i++)
            {
                Transform foot = feet[i];
                if (foot == null)
                    continue;

                bool footIsRising = releaseOnFootRise && foot.position.y > previousHeights[i] + riseThreshold;
                if (i == plantedFootIndex)
                    plantedFootIsRising = footIsRising;
                if (!footIsRising && foot.position.y - groundHeights[i] < lowestClearance)
                {
                    lowestFootIndex = i;
                    lowestClearance = foot.position.y - groundHeights[i];
                }
            }

            for (int i = 0; i < feet.Count; i++)
            {
                if (feet[i] != null)
                    previousHeights[i] = feet[i].position.y;
            }

            if (plantedFootIndex >= 0 &&
                (feet[plantedFootIndex] == null || plantedFootIsRising ||
                 feet[plantedFootIndex].position.y - groundHeights[plantedFootIndex] > releaseHeight))
                plantedFootIndex = -1;

            if (lowestFootIndex < 0)
                return;

            if (plantedFootIndex < 0)
            {
                if (lowestClearance > releaseHeight)
                    return;

                plantedFootIndex = lowestFootIndex;
                plantedPosition = feet[plantedFootIndex].position;
                return;
            }

            if (Vector3.Distance(feet[plantedFootIndex].position, plantedPosition) > maximumLockDistance)
            {
                plantedFootIndex = -1;
                return;
            }

            SolveLeg(feet[plantedFootIndex], plantedPosition);
        }

        private void SolveLeg(Transform foot, Vector3 target)
        {
            for (int iteration = 0; iteration < 3; iteration++)
            {
                Transform joint = foot.parent;
                for (int depth = 0; depth < jointDepth && joint != null; depth++)
                {
                    Vector3 currentDirection = foot.position - joint.position;
                    Vector3 targetDirection = target - joint.position;
                    if (currentDirection.sqrMagnitude > 0.000001f && targetDirection.sqrMagnitude > 0.000001f)
                        joint.rotation = Quaternion.FromToRotation(currentDirection, targetDirection) * joint.rotation;
                    joint = joint.parent;
                }
            }
        }

        private void AddNamedFeet(List<Transform> bones, string namePart)
        {
            foreach (Transform bone in bones)
            {
                string lowerName = bone.name.ToLowerInvariant();
                if (lowerName.Contains(namePart) && !lowerName.Contains("ik") && !feet.Contains(bone))
                    feet.Add(bone);
            }
        }

        private void AddExactFeet(List<Transform> bones, string[] exactNames)
        {
            foreach (string exactName in exactNames)
            {
                foreach (Transform bone in bones)
                {
                    if (bone.name == exactName && !feet.Contains(bone))
                    {
                        feet.Add(bone);
                        break;
                    }
                }
            }
        }

        private void AddLowestEndBones(List<Transform> bones, Bounds bounds)
        {
            var candidates = new List<Transform>();
            float bodyRadius = Mathf.Max(bounds.size.x, bounds.size.z) * 0.35f;
            float maximumHeight = bounds.min.y + bounds.size.y * 0.45f;

            foreach (Transform bone in bones)
            {
                if (HasChildBone(bone, bones) || bone.position.y > maximumHeight)
                    continue;

                Vector2 centerOffset = new Vector2(bone.position.x - bounds.center.x, bone.position.z - bounds.center.z);
                if (centerOffset.magnitude <= bodyRadius)
                    candidates.Add(bone);
            }

            candidates.Sort((left, right) => left.position.y.CompareTo(right.position.y));
            int count = Mathf.Min(8, candidates.Count);
            for (int i = 0; i < count; i++)
                feet.Add(candidates[i]);
        }

        private static bool HasChildBone(Transform bone, List<Transform> bones)
        {
            foreach (Transform candidate in bones)
            {
                if (candidate.parent == bone)
                    return true;
            }
            return false;
        }
    }
}
