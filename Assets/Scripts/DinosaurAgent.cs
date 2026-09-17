using System.Collections.Generic;
using UnityEngine;

namespace JurassicWorld
{
    public sealed class DinosaurAgent : MonoBehaviour
    {
        public enum DinosaurState
        {
            Idle,
            RandomWalk,
            Approaching
        }

        /// <summary>召唤退出原因：区分「到达」「受阻放弃」「30s 兜底超时」三种场景。</summary>
        private enum SummonExitReason
        {
            Arrived,
            BlockedGiveUp,
            TimeoutFallback
        }

        [SerializeField] private Camera viewCamera;
        [SerializeField] private string displayName = "恐龙";
        [SerializeField] private float movementHeight;
        // 行为级碰撞半径（米）：由 JurassicWorldSceneBuilder.BuildDinosaur 通过反射 SetField 按恐龙模板烘焙写入
        //（字段名 collisionRadius 为跨脚本契约，禁止改名）。运行期只读，严禁在此修改——遵循「烘焙态构建、运行期仅绑定」架构。
        [SerializeField] private float collisionRadius = 1.5f;
        [SerializeField] private float viewFocusHeight = 2.5f;
        [SerializeField] private float visualYaw;
        [SerializeField] private float walkSpeed = 1.15f;
        [SerializeField] private float approachSpeed = 2.8f;
        [SerializeField] private float moveAnimationGroundSpeed = 1.15f;
        // 循环窗口（烘焙期由 JurassicWorldSceneBuilder 从脚轨迹搜索写入；<0 = 禁用，整段循环）：
        // move clip 总长与内部步态周期不整除时，clip 接缝处有相位跳变（脚位瞬移，表现为每循环一次的
        // 小弹跳）；运行期把播放时间卷在窗口内（跳过节拍外的起手/收尾段）即可把缝压到最小。
        [SerializeField] private float moveClipLoopStart = -1f;
        [SerializeField] private float moveClipLoopEnd = -1f;
        [SerializeField] private string description = "";
        // 飞行单位烘焙为 false，跳过树阻挡但保留同类互斥检查（跨脚本契约：字段名 groundObstacleAware 由 SceneBuilder SetField 写入）。
        [SerializeField] private bool groundObstacleAware = true;

        public Camera ViewCamera => viewCamera;
        public string DisplayName => displayName;
        public float MovementHeight => movementHeight;
        public float CollisionRadius => collisionRadius;
        /// <summary>运行期树干避让半径（= Min(collisionRadius, DinoTreeAvoidRadiusCap)，Awake 推导）。
        /// 供 DinosaurSpawnRandomizer 开局采样复用与运行期移动完全一致的避树半径。</summary>
        public float TreeAvoidRadius => treeAvoidRadius;
        public float ViewFocusHeight => viewFocusHeight;
        public float VisualYaw => visualYaw;
        public float WalkSpeed => walkSpeed;
        public float ApproachSpeed => approachSpeed;
        public float MoveAnimationGroundSpeed => moveAnimationGroundSpeed;
        public string Description => description;
        public DinosaurState CurrentState { get; private set; } = DinosaurState.Idle;
        public bool HasBeenSummoned { get; private set; }

        private const float WanderRadius = 45f;
        private const float WanderTurnAngle = 70f;
        private const float WanderStepDistance = 18f;
        private const float MinimumTurnRate = 22f;
        private const float MaximumTurnRate = 55f;
        private const float PassDistance = 9f;
        private const float PassSideOffset = 6f;

        /// <summary>
        /// 恐龙对树干的避让半径上限（米）。大体型恐龙（如阿根廷龙 collisionRadius=8）允许躯干视觉
        /// 探入树冠区域，但树干附近必须能正常通行，因此将 SlideMove 使用的查询半径钳制在此上限，
        /// 避免因体型过大导致树周围大片区域判阻塞而无法通过。可调：增大→绕行更远；减小→可贴近树干。
        /// </summary>
        private const float DinoTreeAvoidRadiusCap = 2.5f;

        /// <summary>
        /// 扫掠滑移后的「实质受阻」进度阈值：实际位移长度 / 期望位移长度 < 此值视为受阻。
        /// SlideMove 的切向滑行通常能保持 30%~100% 进度；仅当恐龙正面夹在多树之间、几乎无法
        /// 前进时才低于 10%，此时触发连续受阻计数递增以启用兜底退让。
        /// </summary>
        private const float SlideBlockedThreshold = 0.1f;

        /// <summary>
        /// 恐龙互斥的高度差容忍值（米）：|MovementHeight - peer.MovementHeight| > 此值时跳过该 peer。
        /// 飞行单位（风神翼龙 movementHeight=11）与地面恐龙 XZ 投影重叠不应互阻。
        /// </summary>
        private const float PeerHeightTolerance = 2f;

        private readonly System.Random random = new System.Random(314159);
        private Vector3 spawnPoint;
        private Vector3 target;
        private Vector3 approachDestination;
        private float stateTimer;
        private Animation legacyAnimation;
        // move state 缓存（循环窗口卷绕用；Animation 索引器是字典查找，避免每帧调用）。
        private AnimationState moveState;
        private string activeClip;
        private string idleClip;
        private string moveClip;
        private static int lastSummonInputFrame = -1;
        // 同场景其它恐龙（Awake 缓存，避免每帧 FindObjectsOfType）：用于恐龙互斥的行为级碰撞检查。
        private DinosaurAgent[] peers = System.Array.Empty<DinosaurAgent>();
        // Minor 4：统一时间制受阻累加器（秒）。RandomWalk 阈值 1.5s → BeginIdle；Approaching 阈值 3s → BlockedGiveUp。
        // 进度达标时清零；所有状态切换入口（Summon/BeginIdle/BeginRandomWalk/CompleteSummon）同步复位。
        private float blockedTime;

        /// <summary>
        /// 运行时推导的树干避让半径（非序列化，不需要重新 Bake）：
        /// = Min(collisionRadius, DinoTreeAvoidRadiusCap)。
        /// 与恐龙互斥半径（collisionRadius）解耦：互斥保持各恐龙真实体型，树干避让统一钳制到
        /// 可通过狭窄树缝的合理范围，解决大体型恐龙因 reach 过大而被整片林区拒绝通行的问题。
        /// </summary>
        private float treeAvoidRadius;

        private void Awake()
        {
            spawnPoint = transform.position;
            // 运行时为纯 Transform 位移、无 Rigidbody/物理组件，树干与同类阻挡只能走几何查询：
            // 此处惰性构建地形树障碍注册表（一次性，毫秒级），之后每帧只做网格查询。
            TerrainTreeObstacleRegistry.EnsureInitialized();
            // 推导树干避让半径：将恐龙体型互斥半径与树干碰撞半径解耦（详见 treeAvoidRadius 注释）。
            treeAvoidRadius = Mathf.Min(collisionRadius, DinoTreeAvoidRadiusCap);
            CachePeers();
            BeginIdle();
        }

        private void CachePeers()
        {
            DinosaurAgent[] all = FindObjectsOfType<DinosaurAgent>();
            List<DinosaurAgent> others = new List<DinosaurAgent>(Mathf.Max(0, all.Length - 1));
            for (int i = 0; i < all.Length; i++)
            {
                DinosaurAgent candidate = all[i];
                if (candidate != null && candidate != this)
                    others.Add(candidate);
            }
            peers = others.ToArray();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
                TrySummonVisibleDinosaur();

            // 循环窗口卷绕（见 moveClipLoopStart 字段注释）：把 move 播放时间钳在窗口内。
            // 放在 Update：动画采样在本帧 Update 之后发生，此帧修正即时生效。
            ApplyMoveLoopWindow();

            switch (CurrentState)
            {
                case DinosaurState.Idle:
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0f)
                        BeginRandomWalk();
                    break;
                case DinosaurState.RandomWalk:
                    // Minor 5：MoveTowards 返回 true 表示本帧内已发生状态切换（BeginIdle / BeginRandomWalk），
                    // 跳过后续 timer 递减与到达判定，避免同帧二次状态切换。
                    if (!MoveTowards(target, WalkSpeed))
                    {
                        stateTimer -= Time.deltaTime;
                        if (stateTimer <= 0f || FlatDistance(transform.position, target) < 0.5f)
                            BeginIdle();
                    }
                    break;
                case DinosaurState.Approaching:
                    UpdateApproach();
                    break;
            }
        }

        /// <summary>
        /// 循环窗口卷绕：move clip 若带有烘焙期搜索出的「最优循环窗口」（见 SceneBuilder.FindBestLoopWindow），
        /// 把播放时间钳在窗口内——跳过节拍外的起手/收尾段，消除多周期 clip 接缝的相位跳（每循环一次的「脚位瞬移」）。
        /// 在 Update 中调用：Unity 动画采样发生在 Update 之后，此帧修正即时生效。
        /// </summary>
        private void ApplyMoveLoopWindow()
        {
            if (moveClipLoopStart < 0f || moveClipLoopEnd <= moveClipLoopStart || moveState == null || !moveState.enabled)
                return;

            float window = moveClipLoopEnd - moveClipLoopStart;
            if (moveState.time < moveClipLoopStart)
                moveState.time = moveClipLoopStart;   // 跳过窗口前的起手段（含 CrossFade 入场：time 从 0 起步）
            else if (moveState.time >= moveClipLoopEnd)
                moveState.time = moveClipLoopStart + (moveState.time - moveClipLoopStart) % window;
        }

        /// <summary>
        /// 运行期开局随机布置入口（仅 DinosaurSpawnRandomizer 调用；采样约束见该类注释）：
        /// 瞬时把恐龙放到新的出生点并重置漫步圆心（spawnPoint），Y 恒等于 MovementHeight——
        /// 地面恐龙恒为 0、飞行单位为其烘焙高度，不采样地形高程。
        /// 只做根节点 Position/Rotation 写入：不触碰模型缩放/相对偏移等 Fit 产物（非幂等，严禁运行期重算）。
        /// </summary>
        public void ApplySpawnPlacement(Vector2 xz, float yawDegrees)
        {
            spawnPoint = new Vector3(xz.x, MovementHeight, xz.y);
            transform.SetPositionAndRotation(spawnPoint, Quaternion.Euler(0f, yawDegrees, 0f));
        }

        public void BindAnimation(Animation animation, string idleHint = "idle", string moveHint = "run")
        {
            legacyAnimation = animation;
            idleClip = FindClip(idleHint, "idle", "stand", "wait");
            moveClip = FindClip(moveHint, "run", "walk", "fly", "animation");
            // 缓存 move state（循环窗口卷绕用；Animation 索引器是字典查找，避免每帧调用）。
            moveState = legacyAnimation != null && !string.IsNullOrEmpty(moveClip) ? legacyAnimation[moveClip] : null;
            if (string.IsNullOrEmpty(idleClip))
                idleClip = moveClip;
            if (string.IsNullOrEmpty(moveClip))
                moveClip = idleClip;
            if (CurrentState == DinosaurState.Idle)
                PlayClip(idleClip);
            else
                PlayMovementClip(CurrentState == DinosaurState.Approaching ? ApproachSpeed : WalkSpeed);
        }

        public bool Summon()
        {
            if (CurrentState == DinosaurState.Approaching || ViewCamera == null)
                return false;

            Vector3 cameraPosition = ViewCamera.transform.position;
            cameraPosition.y = transform.position.y;
            Vector3 runDirection = cameraPosition - transform.position;
            runDirection.y = 0f;
            if (runDirection.sqrMagnitude < 0.01f)
                return false;

            Vector3 cameraRight = Vector3.ProjectOnPlane(ViewCamera.transform.right, Vector3.up).normalized;
            float side = Vector3.Dot(transform.position - cameraPosition, cameraRight) < 0f ? -1f : 1f;
            approachDestination = cameraPosition + runDirection.normalized * PassDistance
                + cameraRight * side * PassSideOffset;
            HasBeenSummoned = true;
            CurrentState = DinosaurState.Approaching;
            stateTimer = 30f;
            blockedTime = 0f;
            PlayMovementClip(ApproachSpeed);
            HudController.ShowStatus(DisplayName + "正在穿越视角…");
            return true;
        }

        private static void TrySummonVisibleDinosaur()
        {
            if (lastSummonInputFrame == Time.frameCount)
                return;

            lastSummonInputFrame = Time.frameCount;
            DinosaurAgent selected = null;
            float bestScreenDistance = float.MaxValue;
            foreach (DinosaurAgent candidate in FindObjectsOfType<DinosaurAgent>())
            {
                if (candidate.ViewCamera == null || candidate.CurrentState == DinosaurState.Approaching)
                    continue;

                Vector3 viewport = candidate.ViewCamera.WorldToViewportPoint(
                    candidate.transform.position + Vector3.up * candidate.ViewFocusHeight);
                bool visible = viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f
                    && viewport.y >= 0f && viewport.y <= 1f;
                if (!visible)
                    continue;

                float screenDistance = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f).sqrMagnitude;
                if (screenDistance < bestScreenDistance)
                {
                    selected = candidate;
                    bestScreenDistance = screenDistance;
                }
            }

            if (selected != null)
                selected.Summon();
        }

        private void UpdateApproach()
        {
            if (ViewCamera == null)
            {
                BeginIdle();
                return;
            }

            stateTimer -= Time.deltaTime;
            if (FlatDistance(transform.position, approachDestination) <= 0.75f)
            {
                CompleteSummon(SummonExitReason.Arrived);
                return;
            }

            if (stateTimer <= 0f)
            {
                // 30s 兜底超时：按中性文案处理（未到目的地本身即受阻或路程远）
                CompleteSummon(SummonExitReason.TimeoutFallback);
                return;
            }

            MoveTowards(approachDestination, ApproachSpeed);
        }

        private void CompleteSummon(SummonExitReason reason)
        {
            HasBeenSummoned = false;
            blockedTime = 0f;
            switch (reason)
            {
                case SummonExitReason.Arrived:
                    HudController.ShowStatus(DisplayName + "已穿过视角，恢复自由活动");
                    break;
                case SummonExitReason.BlockedGiveUp:
                    HudController.ShowStatus(DisplayName + "路径受阻，已放弃穿越");
                    break;
                case SummonExitReason.TimeoutFallback:
                    HudController.ShowStatus(DisplayName + "已中止穿越，恢复自由活动");
                    break;
            }
            BeginRandomWalk();
        }

        private void BeginIdle()
        {
            CurrentState = DinosaurState.Idle;
            stateTimer = Range(3f, 7f);
            blockedTime = 0f;
            PlayClip(idleClip);
        }

        private void BeginRandomWalk()
        {
            CurrentState = DinosaurState.RandomWalk;
            stateTimer = Range(4f, 8f);
            blockedTime = 0f;
            target = ComputeWanderTarget();
            PlayMovementClip(WalkSpeed);
        }

        /// <summary>
        /// 只重掷漫步目标（不重置 stateTimer、不重播动画）：用于 RandomWalk 被阻挡时尝试新方向，
        /// 避免逐帧硬顶造成原地跑步。
        /// </summary>
        private void RerollWanderTarget()
        {
            target = ComputeWanderTarget();
        }

        /// <summary>计算新的漫步目标点（角度偏转 + 距离 + 包络钳制），供 BeginRandomWalk 和 RerollWanderTarget 复用。</summary>
        private Vector3 ComputeWanderTarget()
        {
            float angle = Range(-WanderTurnAngle, WanderTurnAngle);
            float distance = Range(8f, WanderStepDistance);
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * GetVisualForward();
            Vector3 result = transform.position + direction * distance;
            Vector3 spawnOffset = result - spawnPoint;
            spawnOffset.y = 0f;
            if (spawnOffset.magnitude > WanderRadius)
                result = spawnPoint + spawnOffset.normalized * WanderRadius;
            result.y = MovementHeight;
            return result;
        }

        /// <summary>
        /// 向目的地移动一帧。返回 true 表示本帧内发生了状态切换（HandleBlockedEscalation 触发了
        /// BeginIdle / CompleteSummon→BeginRandomWalk），调用方应跳过后续 timer 递减与到达判定，
        /// 避免同帧二次状态切换（Minor 5）。
        /// </summary>
        private bool MoveTowards(Vector3 destination, float speed)
        {
            Vector3 direction = destination - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f)
                return false;

            Quaternion desiredRotation = Quaternion.LookRotation(direction.normalized, Vector3.up)
                * Quaternion.Euler(0f, -VisualYaw, 0f);
            float turnRate = Mathf.Lerp(MinimumTurnRate, MaximumTurnRate,
                Mathf.InverseLerp(0.7f, 5.5f, speed));
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, desiredRotation, turnRate * Time.deltaTime);
            Vector3 visualForward = GetVisualForward();
            Vector3 step = visualForward * speed * Time.deltaTime;

            Vector2 currentXZ = new Vector2(transform.position.x, transform.position.z);
            Vector2 desiredXZ = new Vector2(currentXZ.x + step.x, currentXZ.y + step.z);

            // ─── 恐龙互斥预检查（快速路径，维持原停步语义）─────────────────────────
            // 保留滑移前的 desiredXZ 互斥预检查：若期望落点已侵入同伴互斥圆，直接走停步语义，
            // 省去一次 SlideMove 调用。与滑移后的 actualXZ 复检互补（Major 2）。
            // 互斥不使用滑移，防止恐龙之间产生不自然的推挤滑行。
            if (IsPeerBlocked(desiredXZ))
            {
                // Minor 7：停步帧也执行仅去穿透滑移，防止恐龙陷在树干内时因 peer 早退而永不触发 LimitPushOut。
                // desired==current 时 SlideMove 只做入口去穿透，不产生常规位移。
                Vector2 depenXZ = TerrainTreeObstacleRegistry.SlideMove(
                    currentXZ, currentXZ, treeAvoidRadius, Time.deltaTime);
                // Minor 5：先写入位移，再调用 HandleBlockedEscalation，保证状态切换读到本帧已结算位置。
                // MovementHeight 依赖 Terrain 核心活动区 heightmap=0（world Y=0），引入地形起伏时需改为高度采样 + eyeOffset。
                transform.position = new Vector3(depenXZ.x, MovementHeight, depenXZ.y);
                blockedTime += Time.deltaTime;
                return HandleBlockedEscalation();
            }

            // ─── 树干避让：扫掠滑移接入点 ──────────────────────────────────────────
            // groundObstacleAware=true 的地面恐龙使用 SlideMove 沿树干切向滑行绕树，
            // 替代原来的「预检查通过才移动」策略，解决贴树后每帧都被拒绝导致冻结的问题。
            // groundObstacleAware=false 的飞行单位完全跳过树干碰撞，行为与现状一致。
            // 边界契约：恐龙无逐帧边界钳制——漫步目标已按 WanderRadius=45 钳制在出生点凸圆盘内，
            // SlideMove 只会缩短位移，desired 恒在可达域内。
            Vector2 actualXZ;
            float progress;
            if (groundObstacleAware)
            {
                actualXZ = TerrainTreeObstacleRegistry.SlideMove(
                    currentXZ, desiredXZ, treeAvoidRadius, Time.deltaTime);
                // Major 1 + Minor 3：进度比改用方向投影 + Clamp01，护栏阈值与 SlideMove 收敛阈值对齐。
                // desiredLength ≤ SlideRemainingThreshold 时按 progress=1f（正常小步）处理，避免慢速
                // 单位/高帧率下 desired 极小导致除零或噪声误判受阻。SlideMove 首轮不再丢弃微小位移，
                // 小步会照常推进（历史「原地走」缺陷已修复），此护栏保留作安全网。
                // 方向投影比：Dot(actualDelta, desiredDelta) / |desiredDelta|²，Clamp01 防止去穿透推出
                // （方向可与目标相反）算出 progress>1 被当正常前进并清零计数。
                Vector2 desiredDelta = desiredXZ - currentXZ;
                float desiredSqr = desiredDelta.sqrMagnitude;
                float threshold = TerrainTreeObstacleRegistry.SlideRemainingThreshold;
                if (desiredSqr > threshold * threshold)
                {
                    Vector2 actualDelta = actualXZ - currentXZ;
                    progress = Mathf.Clamp01(Vector2.Dot(actualDelta, desiredDelta) / desiredSqr);
                }
                else
                {
                    progress = 1f;
                }
            }
            else
            {
                actualXZ = desiredXZ;
                progress = 1f;
            }

            // ─── Major 2：滑移后 actualXZ 互斥复检 ──────────────────────────────────
            // SlideMove 切向滑移后 actualXZ 方向可与 desiredXZ 近乎垂直，可能把恐龙横向滑进同伴互斥圆。
            // 若被拒则回退 currentXZ（本帧停步），走原停步语义（blockedTime++/HandleBlockedEscalation）。
            // 与滑移前的 desiredXZ 预检查互补：预检查省 SlideMove 调用，复检兜住滑移后的横向侵入。
            if (IsPeerBlocked(actualXZ))
            {
                // Minor 7：回退帧也执行仅去穿透滑移。
                Vector2 depenXZ = TerrainTreeObstacleRegistry.SlideMove(
                    currentXZ, currentXZ, treeAvoidRadius, Time.deltaTime);
                // Minor 5：先写入位移，再调用 HandleBlockedEscalation。
                // MovementHeight 依赖 Terrain 核心活动区 heightmap=0（world Y=0），引入地形起伏时需改为高度采样 + eyeOffset。
                transform.position = new Vector3(depenXZ.x, MovementHeight, depenXZ.y);
                blockedTime += Time.deltaTime;
                return HandleBlockedEscalation();
            }

            // ─── 受阻判据（进度比）─────────────────────────────────────────────────
            // SlideMove 的切向滑行通常保持 30%~100% 进度；仅正面夹在多树之间几乎无法前进时
            // progress < 10%，此时递增受阻时间以启用兜底退让（BeginIdle / BlockedGiveUp）。
            // Minor 5：先写入位移，再调用 HandleBlockedEscalation，保证状态切换读到本帧已结算位置。
            // MovementHeight 依赖 Terrain 核心活动区 heightmap=0（world Y=0），引入地形起伏时需改为高度采样 + eyeOffset。
            transform.position = new Vector3(actualXZ.x, MovementHeight, actualXZ.y);

            if (progress < SlideBlockedThreshold)
            {
                blockedTime += Time.deltaTime;
                return HandleBlockedEscalation();
            }

            // 正常前进：清零受阻累计时间。
            blockedTime = 0f;
            return false;
        }

        /// <summary>
        /// 受阻升级处理（Minor 4 统一时间制 + Minor 5 返回状态切换标志）：
        /// 返回 true 表示本帧发生了状态切换（BeginIdle / CompleteSummon→BeginRandomWalk），
        /// 调用方据此跳过后续 timer 递减与到达判定。
        /// - RandomWalk：累计受阻时间 > 1.5s → BeginIdle 退让；否则 RerollWanderTarget 尝试新方向。
        /// - Approaching：累计受阻时间 > 3s → 提前放弃穿越（BlockedGiveUp），不再空转等 30s 超时。
        /// </summary>
        private bool HandleBlockedEscalation()
        {
            if (CurrentState == DinosaurState.RandomWalk)
            {
                if (blockedTime > 1.5f)
                {
                    BeginIdle();
                    return true;
                }
                RerollWanderTarget();
                return false;
            }
            if (CurrentState == DinosaurState.Approaching)
            {
                if (blockedTime > 3f)
                {
                    CompleteSummon(SummonExitReason.BlockedGiveUp);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 恐龙互斥检查：落点与同场景其它恐龙的平面距离 &lt; 双方 collisionRadius 之和则视为阻塞。
        /// 只查 XZ 平面。互斥使用各自完整的 collisionRadius（不做 Cap 钳制），保持体型语义。
        /// 与树干避让分离：树干走 SlideMove 扫掠滑移，互斥走原停步语义（防止恐龙互相推挤滑行）。
        /// Minor 6：加高度差门槛——|MovementHeight - peer.MovementHeight| > PeerHeightTolerance 时跳过该 peer，
        /// 避免飞行单位（风神翼龙 movementHeight=11）与地面恐龙 XZ 投影重叠时互阻。
        /// </summary>
        private bool IsPeerBlocked(Vector2 nextXZ)
        {
            for (int i = 0; i < peers.Length; i++)
            {
                DinosaurAgent peer = peers[i];
                if (peer == null || peer == this)
                    continue;

                // Minor 6：高度差超过容忍值时跳过（飞行 vs 地面不互阻）。
                if (Mathf.Abs(movementHeight - peer.MovementHeight) > PeerHeightTolerance)
                    continue;

                Vector3 peerPosition = peer.transform.position;
                float minimumDistance = collisionRadius + peer.CollisionRadius;
                float deltaX = nextXZ.x - peerPosition.x;
                float deltaZ = nextXZ.y - peerPosition.z;
                if (deltaX * deltaX + deltaZ * deltaZ < minimumDistance * minimumDistance)
                    return true;
            }

            return false;
        }

        private Vector3 GetVisualForward()
        {
            return transform.rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
        }

        private void PlayMovementClip(float groundSpeed)
        {
            float referenceSpeed = Mathf.Max(0.01f, MoveAnimationGroundSpeed);
            PlayClip(moveClip, groundSpeed / referenceSpeed);
        }

        private void PlayClip(string clipName, float playbackSpeed = 1f)
        {
            if (legacyAnimation == null || string.IsNullOrEmpty(clipName))
                return;

            AnimationState state = legacyAnimation[clipName];
            if (state == null)
            {
                Debug.LogWarning("动画片段不存在：" + clipName, this);
                return;
            }

            state.wrapMode = WrapMode.Loop;
            state.speed = Mathf.Max(0.01f, playbackSpeed);
            if (activeClip == clipName)
                return;

            legacyAnimation.CrossFade(clipName, 0.35f);
            activeClip = clipName;
        }

        private string FindClip(params string[] hints)
        {
            if (legacyAnimation == null)
                return null;

            string first = null;
            string rootMotionMatch = null;
            foreach (AnimationState state in legacyAnimation)
            {
                if (first == null)
                    first = state.name;
                foreach (string hint in hints)
                {
                    if (!string.IsNullOrEmpty(hint) && state.name.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        bool isRootMotion = state.name.IndexOf("_RM", System.StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!isRootMotion)
                            return state.name;
                        if (rootMotionMatch == null)
                            rootMotionMatch = state.name;
                    }
                }
            }
            return rootMotionMatch ?? first;
        }

        private float Range(float min, float max)
        {
            return Mathf.Lerp(min, max, (float)random.NextDouble());
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
