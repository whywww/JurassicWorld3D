using System.Collections.Generic;
using UnityEngine;

namespace JurassicWorld
{
    [DefaultExecutionOrder(1000)]
    public sealed partial class DinosaurFootLock : MonoBehaviour
    {
        private readonly List<Transform> feet = new List<Transform>();
        private readonly List<float> groundHeights = new List<float>();
        private readonly List<float> previousHeights = new List<float>();
        private DinosaurAgent agent;
        private int plantedFootIndex = -1;
        private Vector3 plantedPosition;
        private int pendingFootIndex = -1;      // 候选捕获态：脚已进入释放高度带、正在下降，等待「最低点拐点」
        private float pendingLowestY;           // 候选脚跌到过的历史最低 Y
        private Vector3 pendingPosition;        // 候选脚跌到过的最低位置（= 咬合锚点）
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
            pendingFootIndex = -1;

            // 脚骨发现抽为静态方法：烘焙期 JurassicWorldSceneBuilder.GroundFeetByAnimation 复用同一套规则，
            // 保证「接地对齐基准」与「运行期落脚锁定骨骼」始终是同一批骨骼。
            Bounds bounds;
            if (!DiscoverFeet(visualRoot, exactFootNames, feet, out bounds))
                return;

            releaseHeight = contactHeight > 0f ? contactHeight : Mathf.Max(0.04f, bounds.size.y * 0.04f);
            riseThreshold = footRiseThreshold > 0f
                ? footRiseThreshold
                : Mathf.Max(0.002f, bounds.size.y * 0.001f);

            // 地面基准 = 真实地面高度（恐龙根节点 Y，地面单位由 Agent.MovementHeight 钉死为 0）。
            // 【修复滑步根因】旧实现把 groundHeights 初始化为 Start 时刻脚的世界 Y，会把烘焙接地残差
            // 当成「地面」：clearance 恒为 0 → 首帧即锁死在悬空高度，且 releaseHeight 早退/ratchet 全部失效，
            // 站立与行走都不产生有效的落脚约束 → 滑步。地面基准必须是常量地面，不能是脚的当前高度。
            float groundY = agent != null ? agent.MovementHeight : transform.position.y;
            foreach (Transform foot in feet)
            {
                groundHeights.Add(groundY);
                previousHeights.Add(foot != null ? foot.position.y : groundY);
            }

            if (feet.Count < 2)
                Debug.LogWarning((agent != null ? agent.DisplayName : name) + "未找到足够的脚部骨骼，已停用落脚锁定。", this);
            BindTRexFeet(visualRoot);
            BindGaitFeet();
        }

        /// <summary>
        /// 脚骨发现（运行期落脚锁定 + 烘焙期接地对齐共用）：显式 exactFootNames 精确匹配优先 →
        /// 命名启发式（foot / ankle / toe，排除 IK 骨）→ 几何兜底（包围盒下半身、靠近体轴的末端骨，最多 8 根）。
        /// 显式名全部落空时（模型改名/换资源）退回启发式，避免静默失去锁定能力。
        /// 返回 false 表示 visualRoot 下没有任何 Renderer（无从度量），result 为空。
        /// </summary>
        public static bool DiscoverFeet(Transform visualRoot, string[] exactFootNames, List<Transform> result, out Bounds modelBounds)
        {
            modelBounds = new Bounds();
            result.Clear();
            if (visualRoot == null)
                return false;

            Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return false;

            modelBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                modelBounds.Encapsulate(renderers[i].bounds);

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
                AddExactFeet(bones, exactFootNames, result);
            if (result.Count == 0)
            {
                AddNamedFeet(bones, "foot", result);
                if (result.Count < 2)
                    AddNamedFeet(bones, "ankle", result);
                if (result.Count < 2)
                    AddNamedFeet(bones, "toe", result);
                if (result.Count < 2)
                    AddLowestEndBones(bones, modelBounds, result);
            }
            return true;
        }

        private void LateUpdate()
        {
            if (gaitFeet != null) { UpdateGaitFeet(Time.deltaTime); return; }
            if (agent != null && agent.HasFootPhaseData && leftSupport != null && rightSupport != null)
            { UpdateTRexFeet(); return; }
            if (agent == null || feet.Count < 2)
                return;

            if (agent.CurrentState == DinosaurAgent.DinosaurState.Idle
                || (agent.InteractionPhase != DinosaurAgent.PlayerInteractionPhase.Inactive && !agent.IsInteractionMoving))
            {
                // Idle 不做 IK 求解。groundHeights 现在恒为真实地面常量，不再做 ratchet 下探：
                // 旧写法 Mathf.Min(groundHeights[i], 脚当前 Y) 会把地面基准拉到脚的实际高度（含悬空残差），
                // 使 clearance 恒近 0、releaseHeight 早退失效，等于没有落脚约束。
                plantedFootIndex = -1;
                pendingFootIndex = -1;
                for (int i = 0; i < feet.Count; i++)
                {
                    if (feet[i] != null)
                        previousHeights[i] = feet[i].position.y;
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

            if (plantedFootIndex >= 0)
            {
                // 已有锁定脚：候选态无意义，清掉后走距离检查 + IK。
                pendingFootIndex = -1;
                if (Vector3.Distance(feet[plantedFootIndex].position, plantedPosition) > maximumLockDistance)
                {
                    plantedFootIndex = -1;
                    return;
                }

                SolveLeg(feet[plantedFootIndex], plantedPosition);
                return;
            }

            // 候选捕获（自然最低点咬合）：脚骨下降途中首次进入释放高度带时不做立即 plant，
            // 而是持续跟踪「跌到过的最低位置」，直到该脚 Y 不再创新低（自然最低点拐点）才以最低位置为锚点咬合。
            // 旧行为在下降途中首次低于阈值即 plant：锚点定格在半空（离自然最低点差数厘米、落点未到位），
            // 支撑相内错位逐步累积，起脚释放瞬间跳回动画位 →「脚先踩在空中，然后突然弹起到另一个位置」。
            if (lowestClearance > releaseHeight)
            {
                pendingFootIndex = -1;   // 离开释放高度带：候选作废（本次步态周期未构成落脚）
                return;
            }

            Transform lowestFoot = feet[lowestFootIndex];
            if (pendingFootIndex != lowestFootIndex)
            {
                // 新候选脚（或候选脚已被更高脚替换）：从当前帧开始记录历史最低点。
                pendingFootIndex = lowestFootIndex;
                pendingLowestY = lowestFoot.position.y;
                pendingPosition = lowestFoot.position;
                return;
            }

            if (lowestFoot.position.y < pendingLowestY)
            {
                // 仍在下降（刷新历史最低）：继续等待拐点，不咬合。
                pendingLowestY = lowestFoot.position.y;
                pendingPosition = lowestFoot.position;
                return;
            }

            // Y 不再创新低（拐点已现）→ 以「最低位置」为锚点咬合；plant 帧仍不做 IK（沿用旧语义）。
            plantedFootIndex = pendingFootIndex;
            plantedPosition = pendingPosition;
            pendingFootIndex = -1;
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

        private static void AddNamedFeet(List<Transform> bones, string namePart, List<Transform> result)
        {
            foreach (Transform bone in bones)
            {
                string lowerName = bone.name.ToLowerInvariant();
                if (lowerName.Contains(namePart) && !lowerName.Contains("ik") && !result.Contains(bone))
                    result.Add(bone);
            }
        }

        private static void AddExactFeet(List<Transform> bones, string[] exactNames, List<Transform> result)
        {
            foreach (string exactName in exactNames)
            {
                if (string.IsNullOrEmpty(exactName))
                    continue;
                foreach (Transform bone in bones)
                {
                    if (bone.name == exactName && !result.Contains(bone))
                    {
                        result.Add(bone);
                        break;
                    }
                }
            }
        }

        private static void AddLowestEndBones(List<Transform> bones, Bounds bounds, List<Transform> result)
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
                result.Add(candidates[i]);
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
