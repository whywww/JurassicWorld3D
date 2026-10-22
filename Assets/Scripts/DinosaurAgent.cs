using System.Collections.Generic;
using UnityEngine;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent : MonoBehaviour
    {
        public enum DinosaurState
        {
            Idle,
            RandomWalk,
            Approaching
        }

        public enum PlayerInteractionPhase { Inactive, Turning, Returning, FrontSequence }

        [Header("T-Rex player interaction")]
        [SerializeField] private bool playerInteractionEnabled;
        public PlayerInteractionPhase InteractionPhase { get; private set; }
        public bool IsInteractionMoving => InteractionPhase != PlayerInteractionPhase.Inactive && interactionMoved;
        private bool interactionReady, interactionMoved;

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
        [SerializeField] private DinosaurGaitProfile gaitProfile;
        public DinosaurGaitProfile GaitProfile => gaitProfile;
        public bool GaitMotionActive => gaitMoving;
        public bool GaitHoldsStoppedPose => freezePoseWhenStopped;
        private bool gaitMoving;
        // 循环窗口（烘焙期由 JurassicWorldSceneBuilder 从脚轨迹搜索写入；<0 = 禁用，整段循环）：
        // move clip 总长与内部步态周期不整除时，clip 接缝处有相位跳变（脚位瞬移，表现为每循环一次的
        // 小弹跳）；运行期把播放时间卷在窗口内（跳过节拍外的起手/收尾段）即可把缝压到最小。
        [SerializeField] private float moveClipLoopStart = -1f;
        [SerializeField] private float moveClipLoopEnd = -1f;
        [SerializeField] private string description = "";
        // 飞行单位烘焙为 false，跳过树阻挡但保留同类互斥检查（跨脚本契约：字段名 groundObstacleAware 由 SceneBuilder SetField 写入）。
        [SerializeField] private bool groundObstacleAware = true;

        public Camera ViewCamera => viewCamera;
        [SerializeField] private Sprite minimapPortrait;
        public Sprite MinimapPortrait => minimapPortrait;
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
        /// <summary>出生点（Phase 1 起由 DinosaurSpawnRandomizer 在所选路线内采样写入）。</summary>
        public Vector3 SpawnPoint => spawnPoint;

        /// <summary>
        /// 本恐龙所选路线索引（Phase 1.1：出生时由 DinosaurSpawnRandomizer 随机选取，见 MovementCorridor 路线索引）。
        /// 漫游目标、车道、让路落点、绕行落点全部按这条路线的几何计算；未随机化时保持主干 MainRoute。
        /// </summary>
        public int RouteIndex => routeIndex;
        public float RouteArc => routeArc;

        /// <summary>漫步半径（米）。
        /// 【跨文件契约】internal const：WorldBounds 以「出生矩形 ⊕ 本值」推导 DinosaurActivityBounds
        /// （恐龙活动包络的唯一来源），改这里即自动改包络，禁止另处复制字面量。
        /// 【Dense Jungle Phase 1 起语义变更】漫步目标不再被本半径圆盘钳制（改由 MovementCorridor 约束
        /// —— 圆盘边界点不在通道上，二次钳制会把目标拉出通道）；本常量仅作为包络推导输入保留。
        /// 恐龙实际可达域 = 通道 ⊕ 召唤瞬态，二者均 ⊆ 该包络（ValidateProject 断言）。</summary>
        internal const float WanderRadius = 45f;
        // 【Dense Jungle Phase 1】漫步目标改由 MovementCorridor（恐龙移动通道）决定：不再是
        // 「当前朝向 ±WanderTurnAngle 转向」的自由游走，而是沿通道的**自有车道** ±步长推进。
        // 原 WanderTurnAngle(70°) 常量随之移除；步长上限与车道常量见下。
        private const float WanderStepDistance = 36f;
        private const float WanderStepDistanceMin = 16f;

        // ─── 车道（lane）：「每只恐龙各走各的」的来源 ───────────────────────────────
        // Phase 1 首版让所有恐龙把目标投影回**同一条中心线**再叠 ±2.5m 抖动，
        // 且全部 Agent 共用同一随机流（见 random 字段注释）⇒ 两只恐龙前几步逐位相同、
        // 反复在中线迎面相撞。现在每只恐龙 Awake 时按稳定序号分到一个固定 laneBias ∈ [-1,1]，
        // 映射到通道左右半宽的对应位置，形成两条（多只时 N 条）彼此分开的行车线。
        /// <summary>
        /// 车道偏置占「可用半宽」的最大比例：留出 LaneEdgeMargin 后再各留 15% 中线余量。
        /// 【跨文件契约】public const：JurassicProjectSetup.ValidateProject 用它换算「两条最外侧车道的
        /// 最小间距」= 本值 ×（通道全宽 − 2×LaneEdgeMargin），并断言其 ≥ 最宽一对恐龙的互斥半径之和。
        /// Phase 1.1c 由 0.75 提到 0.85：车道是【固定轨迹】（横向零抖动），最小间距 = 0.85×9 = 7.65m
        /// 恒 > 6.5m 互斥半径 ⇒ 同路线两只恐龙【任何时刻都不会判定互相阻挡】（0.75 时仅 6.75m、
        /// 余量 0.25m，加上抖动后最小 3.75m，两只持续互判阻挡 ⇒ 卡死，用户报告的「都走不动」）。
        /// 同路线同向/对向均恒不阻塞；分属不同路线的窄点（如并回路口附近）仍由先行权/让行调度兜底。
        /// 注：N ≥ 3 只同时挤在同一路段时车道本身排不下（11m 宽放不下 3 条 ≥6.5m 间距的车道），
        /// 必须依赖该调度的「让路者停步等先行者通过」，这是设计上限而非缺陷。
        /// </summary>
        public const float LaneBiasScale = 0.85f;

        /// <summary>
        /// 车道离通道边界的最小净距（米）。【跨文件契约】public const：
        /// JurassicProjectSetup.ValidateProject 用它断言「通道最窄处容得下分道对会」：
        /// LaneBiasScale ×（minFullWidth − 2×本值）≥ 场内最宽一对恐龙的 collisionRadius 之和，
        /// 防止日后收窄通道时两条车道被压进互斥半径之内、让让行逻辑长期顶死。
        /// </summary>
        public const float LaneEdgeMargin = 1f;

        // 【Phase 1.1c 移除 LaneJitter（±1.5m 车道内横向抖动）】固定轨迹 = 横向零抖动：
        // 目标恒落在本车道中心线上。抖动曾让两车道实际间距在 3.75–9.75m 间波动，频繁跌破
        // 6.5m 互斥半径 ⇒ 两只恐龙每帧互判阻挡、陷入让行/停步死循环（用户报告「都走不动」）。
        // 「每只恐龙路径不一样」改由 路线(Phase 1.1d 起出生**固定分派**、每只各一条，岔口换轨暂关)
        // × 车道 × 行进方向 × 走停时机 四层保证。

        /// <summary>
        /// 贴线前瞻距离（米）：RandomWalk 每帧航向指向「沿路线朝目的地方向、前方该距离处的线上点」，
        /// 替代「朝 8~18m 外远目标直线走」的切弯行为（见 ComputeRouteAimPoint）。
        /// </summary>
        private const float RouteLookaheadDistance = 5f;

        /// <summary>
        /// 召唤功能总开关（用户要求：Phase 1.1c 起【暂时停用】）。恢复时改回 true 即可，
        /// Summon / UpdateApproach / TrySummonVisibleDinosaur 全链路代码原样保留、未删除。
        /// 用 static readonly 而非 const：const false 会把后续代码判成不可达、产生 CS0162 告警刷屏。
        /// </summary>
        private static readonly bool SummonEnabled = false;

        /// <summary>
        /// 岔口换轨总开关（Phase 1.1d 起【暂时停用】）：用户要求「先保留两条路线，让每只恐龙各沿一条走」
        /// —— 每只固定一条线、出生随机选线与途中换轨一并下线，走停节奏/不掉头/贴线行为不变。
        /// 代码原样保留，恢复分流时改回 true 即可。同 <see cref="SummonEnabled"/>：static readonly 避免 CS0162。
        /// </summary>
        private static readonly bool EnableJunctionSwitch = false;

        /// <summary>
        /// 到达岔口时换到「另一条也过该路口的路线」的概率（0..1）。
        /// 0.4 = 稳定性与随机性的折中：多数过路继续原路（走停节奏稳定、不乱窜），
        /// 偶尔分流换轨（分叉网络真的在起作用）。
        /// </summary>
        private const float JunctionSwitchChance = 0.4f;

        /// <summary>判定「已到岔口附近」的世界距离（米）：路面宽 11–13m、恐龙可横向偏出中心线半个半宽，7m 正好罩住路口。</summary>
        private const float JunctionSwitchRadius = 7f;

        // ─── 同类会车：先行权 + 让行（修复「两只恐龙相撞后互相顶死」）──────────────────
        // 旧实现：被同伴挡住时**每帧**重掷走廊目标（仍指向对方身上）+ blockedTime 累计 1.5s→Idle 3~7s，
        // 两只恐龙于是进入「顶住→双双 Idle→再顶住」的循环，表现为撞上后一动不动。
        // 现在按确定性全序分派：同一时刻只有一只「先行」、另一只「让路」，杜绝「都不让」与「都让」。
        /// <summary>先行者被挡多久仍无进展才改走绕行落点（给让路者腾挪的宽限，秒）。</summary>
        private const float RightOfWayGraceSeconds = 1.2f;
        /// <summary>同类受阻升级阈值（秒）：RandomWalk 超过则 Idle 退让（与 HandleBlockedEscalation 共用）。</summary>
        private const float BlockedEscalationSeconds = 1.5f;
        /// <summary>召唤态同类受阻放弃阈值（秒）。</summary>
        private const float ApproachBlockedGiveUpSeconds = 3f;
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
        public static float TreeAvoidRadiusCap => DinoTreeAvoidRadiusCap;

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


        // 【缺陷修复】原实现所有 Agent 共用 `new Random(314159)`：各恐龙第一轮 Idle 计时、第一段步长、
        // 首次转向、首次横向抖动逐位相同，叠加「目标都投影回同一条中心线」⇒ 两只恐龙走完全一样的轨迹
        // （用户报告：路径一模一样、像在同一条轨道上）。现改为 Awake 按稳定车道序号派生独立种子：
        // 基准值 314159（历史值）+ 序号 × 大质数打散，跨运行可复现（序号来自对象名排序，见 AssignLane）。
        private const int WanderSeedBase = 314159;
        private const int WanderSeedStride = 104729;
        private System.Random random;
        private Vector3 spawnPoint;
        /// <summary>所选路线（出生时随机写入，未随机化时为主干）。全部 MovementCorridor 查询都带它。</summary>
        private int routeIndex = MovementCorridor.MainRoute;
        /// <summary>
        /// 沿所选路线的行进方向（+1 = 弧长增大方向，-1 = 减小）。**只在路线端点翻转，途中不掉头**
        /// ——旧实现每次重掷目标 50% 反向，叠加 4~8s 走 / 3~7s 停，恐龙就在出生点附近兜圈
        /// （用户报告「霸王龙一直转圈走」）。方向出生随机、跨运行由车道种子决定。
        /// </summary>
        private int travelSign = 1;
        // 漫步的权威进度。逐帧沿弧长推进，避免「先直走、再投影」在 S 弯上反复跳动。
        private float routeArc;
        private float targetArc;
        private float walkRemaining;
        private bool waitingAtCrossing;
        /// <summary>岔口换轨的候选路线缓冲（单个岔口最多 4 条路，避免每次判定分配数组）。</summary>
        private readonly int[] junctionRouteBuffer = new int[8];
        private Vector3 target;
        private Vector3 approachDestination;
        private float stateTimer;
        private Animation legacyAnimation;
        // move state 缓存（循环窗口卷绕用；Animation 索引器是字典查找，避免每帧调用）。
        private AnimationState moveState;
        private string activeClip;
        [SerializeField] private bool freezePoseWhenStopped;
        private string idleClip;
        private string moveClip;
        private static int lastSummonInputFrame = -1;
        // 同场景其它恐龙（Awake 缓存，避免每帧 FindObjectsOfType）：用于恐龙互斥的行为级碰撞检查。
        private DinosaurAgent[] peers = System.Array.Empty<DinosaurAgent>();
        // Minor 4：统一时间制受阻累加器（秒）。RandomWalk 阈值 1.5s → BeginIdle；Approaching 阈值 3s → BlockedGiveUp。
        // 进度达标时清零；所有状态切换入口（Summon/BeginIdle/BeginRandomWalk/CompleteSummon）同步复位。
        private float blockedTime;

        // ─── 会车让行（同类防卡死）状态 ─────────────────────────────────────────────
        /// <summary>本 Agent 的车道序号：稳定排序结果，同时充当随机种子偏移与「先行权」末级裁决。</summary>
        private int laneIndex;
        public int LaneIndex => laneIndex;
        /// <summary>车道偏置 ∈ [-1,1]：负=靠通道右半宽，正=靠左半宽（左法线为正），由 AssignLane 写入。</summary>
        private float laneBias;

        /// <summary>
        /// 本 Agent 的固定车道偏置（[−LaneBiasScale, LaneBiasScale]，Awake AssignLane 写入后不变）。
        /// 【跨文件使用】DinosaurSpawnRandomizer 用它把出生点**直接采在本车道上**（Phase 1.1d 固定轨迹：
        /// 出生即贴线，消除开局横切上车道的「偏离」段；采样即校验最终落点，相机/树/同伴检查因此精确）。
        /// </summary>
        public float LaneBias => laneBias;

        /// <summary>
        /// 运行时推导的树干避让半径（非序列化，不需要重新 Bake）：
        /// = Min(collisionRadius, DinoTreeAvoidRadiusCap)。
        /// 与恐龙互斥半径（collisionRadius）解耦：互斥保持各恐龙真实体型，树干避让统一钳制到
        /// 可通过狭窄树缝的合理范围，解决大体型恐龙因 reach 过大而被整片林区拒绝通行的问题。
        /// </summary>
        private float treeAvoidRadius;
        private DinosaurCoreBody coreBody;
        private Vector2 CoreForward { get { Vector3 f = transform.rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward; return new Vector2(f.x, f.z).normalized; } }

        private void Awake()
        {
            if (species == DinosaurSpecies.Other) species = GroundSpeciesForName(name);
            startupRoamingMode = roamingMode;
            spawnPoint = transform.position;
            coreBody = GetComponent<DinosaurCoreBody>();
            // 运行时为纯 Transform 位移、无 Rigidbody/物理组件，树干与同类阻挡只能走几何查询：
            // 此处惰性构建地形树障碍注册表（一次性，毫秒级），之后每帧只做网格查询。
            TerrainTreeObstacleRegistry.EnsureInitialized();
            // 推导树干避让半径：将恐龙体型互斥半径与树干碰撞半径解耦（详见 treeAvoidRadius 注释）。
            treeAvoidRadius = Mathf.Min(collisionRadius, DinoTreeAvoidRadiusCap);
            CachePeers();
            // 分配车道 + 派生独立随机流：必须先于 BeginIdle（BeginIdle 要用 random 取 Idle 计时）。
            AssignLane();
            random = new System.Random(WanderSeedBase + laneIndex * WanderSeedStride);
            if (!IsFreeRoaming)
                MovementCorridor.ProjectToCorridor(routeIndex,
                    new Vector2(transform.position.x, transform.position.z), out routeArc, out _);
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

        /// <summary>
        /// 分配「车道」：把同场景所有恐龙按 (displayName, name, instanceID) 稳定排序后均分到
        /// 归一化车道位置（首只 -LaneBiasScale、末只 +LaneBiasScale、中间等距）。
        /// 每只 Awake 都能从同一份数组重算出同一结果（FindObjectsOfType 不依赖 Awake 顺序），
        /// 因此序号跨运行稳定 ⇒ 车道与随机种子均可复现。
        /// 只有一只时车道Bias=0（居中）。
        /// </summary>
        private void AssignLane()
        {
            DinosaurAgent[] all = FindObjectsOfType<DinosaurAgent>();
            System.Array.Sort(all, CompareByStableKey);
            int index = System.Array.IndexOf(all, this);
            laneIndex = index >= 0 ? index : 0;

            if (all.Length <= 1)
            {
                laneBias = 0f;
                return;
            }

            float t = (float)laneIndex / (all.Length - 1);   // 0..1
            laneBias = Mathf.Lerp(-1f, 1f, t) * LaneBiasScale;
        }

        /// <summary>稳定全序比较器：保证「车道序号 / 随机种子 / 先行权」三处裁决结果一致且不随帧序漂移。</summary>
        private static int CompareByStableKey(DinosaurAgent a, DinosaurAgent b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            int byDisplay = string.CompareOrdinal(a.displayName, b.displayName);
            if (byDisplay != 0) return byDisplay;

            int byName = string.CompareOrdinal(a.name, b.name);
            if (byName != 0) return byName;

            // 同名同显示名（极端情况）才退化到 instanceID——仅用于打破平局，不影响正常场景。
            return a.GetInstanceID().CompareTo(b.GetInstanceID());
        }

        /// <summary>
        /// 车道横向位置（静态纯函数）：把归一化偏置 laneBias ∈ [-1,1] 映射到**该路线**弧长 distance 处
        ///「可用半宽」上的实际横向坐标（左法线为正）。laneBias&lt;0 靠右、&gt;0 靠左，
        /// 恒以 LaneEdgeMargin 为边界净距。ValidateProject 的最坏情况断言
        /// （LaneBiasScale ×（全宽 − 2×LaneEdgeMargin））正是本式的两端取值（对每条路线分别断言）。
        /// </summary>
        public static float LaneLateral(int route, float distance, float laneBias)
        {
            MovementCorridor.GetSampleAt(route, distance, out _, out _, out float halfLeft, out float halfRight);
            float usable = Mathf.Max(0f, (laneBias >= 0f ? halfLeft : halfRight) - LaneEdgeMargin);
            return laneBias * usable;   // laneBias 自带符号：负=右半宽、正=左半宽
        }

        /// <summary>本 Agent 在**所选路线**弧长 distance 处的车道横向位置（含 LaneEdgeMargin 边界净距）。</summary>
        private float LaneLateralAt(float distance)
        {
            return LaneLateral(routeIndex, distance, laneBias);
        }

        /// <summary>
        /// 卡死看门狗（Phase 1.1c）：RandomWalk 中位置 5 秒移动不足 0.5m ⇒ 打一条含现场数据的 WARNING
        /// （每次静止期只打一次，动起来即复位）。正常漫步每帧都在走位、走停节奏的站定属 Idle 态
        /// 不归它管；让路等待 / 预检拒步 / 任何残余死锁都会在这里现形 —— Play 中若仍见「不动」，
        /// 直接看控制台这条日志即可定位，不必再靠静态推演。
        /// </summary>
        private float stuckWatchdogTime;
        private Vector3 stuckWatchdogPos;
        private bool stuckWatchdogLogged;

        private void Update()
        {
            ObserveContinuousPeerMotion(Time.deltaTime);
            if (gaitProfile == null) { UpdateBehaviour(); return; }
            Vector3 before = transform.position;
            Quaternion beforeRotation = transform.rotation;
            gaitMoving = false;
            UpdateBehaviour();
            if (gaitMoving && moveState != null && activeClip == moveClip)
            {
                Vector3 delta = transform.position - before; delta.y = 0f;
                float direction = trafficMoving && trafficReverse ? -1f : 1f;
                float measuredSpeed = delta.magnitude / Mathf.Max(.00001f, Time.deltaTime);
                // In-place turning needs alternating steps even when translation is zero.
                if (Quaternion.Angle(beforeRotation, transform.rotation) > .0001f)
                    measuredSpeed = Mathf.Max(measuredSpeed, MoveAnimationGroundSpeed);
                moveState.speed = direction * measuredSpeed / Mathf.Max(.01f, MoveAnimationGroundSpeed);
            }
            ApplyMoveLoopWindow();
        }

        private void UpdateBehaviour()
        {
            RestoreSideHeadPose();
            // This must precede traffic/interaction early returns: their own retry
            // clocks cannot provide a bound on the total time without movement.
            if (UpdateEmergencyEscape(Time.deltaTime)) return;
            if (frontTestWaiting)
            {
                if (!UpdatePlayerInteraction()) PlayClip(idleClip);
                return;
            }
            if (SummonEnabled && Input.GetKeyDown(KeyCode.Space))
                TrySummonVisibleDinosaur();

            // 循环窗口卷绕（见 moveClipLoopStart 字段注释）：把 move 播放时间钳在窗口内。
            // 放在 Update：动画采样在本帧 Update 之后发生，此帧修正即时生效。
            if (gaitProfile == null) ApplyMoveLoopWindow();

            if (UpdateGroundTraffic()) return;

            if (UpdateRouteStepping()) return;
            if (IsSteppingTurn && stepTurnUse == TurnUse.Wander) { TickSteppingTurn(); return; }
            if (freeRecoveryActive && InteractionPhase == PlayerInteractionPhase.Inactive && UpdateFreeRoaming()) return;
            if (UpdatePlayerInteraction()) return;
            if (UpdateFreeRoaming()) return;

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
                    UpdateStuckWatchdog();
                    if (!MoveTowards(target, WalkSpeed))
                    {
                        if (waitingAtCrossing) break;
                        stateTimer -= Time.deltaTime;
                        Vector2 currentXZ = new Vector2(transform.position.x, transform.position.z);
                        bool crossing = MovementCorridorCrossings.ShouldKeepMoving(this, currentXZ);
                        if (crossing && walkRemaining <= 0.05f)
                            walkRemaining = 1f;
                        if (!crossing && (stateTimer <= 0f || walkRemaining <= 0.05f))
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
            if (gaitProfile != null)
            {
                float advance = Time.deltaTime * moveState.speed;
                moveState.time = moveClipLoopStart + Mathf.Repeat(moveState.time + advance - moveClipLoopStart, window) - advance;
                return;
            }
            if (trafficMoving && trafficReverse && moveState.speed < 0f)
            {
                float advance = Time.deltaTime * moveState.speed;
                if (moveState.time + advance < moveClipLoopStart || moveState.time >= moveClipLoopEnd)
                {
                    float wrapped = moveClipLoopStart + Mathf.Repeat(moveState.time + advance - moveClipLoopStart, window);
                    if (HasFootPhaseData)
                    {
                        var motion = interactionProfile.Find(InteractionMotionRole.Walk);
                        var start = SampleFootFrame(motion, moveClipLoopStart);
                        var end = SampleFootFrame(motion, moveClipLoopEnd);
                        GetComponentInChildren<DinosaurFootLock>()?.BlendWalkSeam(
                            start.leftSole - end.leftSole, start.rightSole - end.rightSole);
                    }
                    moveState.time = wrapped - advance;
                }
                return;
            }
            if (moveState.time < moveClipLoopStart)
                moveState.time = moveClipLoopStart;   // 跳过窗口前的起手段（含 CrossFade 入场：time 从 0 起步）
            else if (HasFootPhaseData && moveState.time + Time.deltaTime * moveState.speed >= moveClipLoopEnd)
            {
                float advance = Time.deltaTime * moveState.speed;
                float wrapped = moveClipLoopStart + Mathf.Repeat(moveState.time + advance - moveClipLoopStart, window);
                var motion = interactionProfile.Find(InteractionMotionRole.Walk);
                var end = SampleFootFrame(motion, moveClipLoopEnd);
                var start = SampleFootFrame(motion, moveClipLoopStart);
                var footLock = GetComponentInChildren<DinosaurFootLock>();
                if (footLock != null)
                    footLock.BlendWalkSeam(end.leftSole - start.leftSole, end.rightSole - start.rightSole);
                // Legacy Animation advances after Update. Wrap its upcoming sample, not the previous one.
                moveState.time = wrapped - advance;
            }
            else if (moveState.time >= moveClipLoopEnd)
                moveState.time = moveClipLoopStart + (moveState.time - moveClipLoopStart) % window;
        }

        /// <summary>
        /// 卡死看门狗本体（见字段注释）：只在 RandomWalk 态推进计时；位置一动即复位。
        /// 日志带上路线/弧长/车道横向/持久方向/目标/让行计时/与各同伴的间距 —— 足以一眼区分
        /// 「同伴互斥锁死」「让路等待」「目标不可达」三类症状。
        /// </summary>
        private void UpdateStuckWatchdog()
        {
            if (waitingAtCrossing)
            {
                stuckWatchdogPos = transform.position;
                stuckWatchdogTime = 0f;
                stuckWatchdogLogged = false;
                return;
            }
            if (FlatDistance(transform.position, stuckWatchdogPos) > 0.5f)
            {
                stuckWatchdogPos = transform.position;
                stuckWatchdogTime = 0f;
                stuckWatchdogLogged = false;
                return;
            }

            stuckWatchdogTime += Time.deltaTime;
            if (stuckWatchdogTime < 5f || stuckWatchdogLogged)
                return;

            stuckWatchdogLogged = true;
            MovementCorridor.ProjectToCorridor(routeIndex,
                new Vector2(transform.position.x, transform.position.z), out float arc, out float lateral);
            string peerInfo = " 无";
            for (int i = 0; i < peers.Length; i++)
            {
                DinosaurAgent peer = peers[i];
                if (peer == null || peer == this)
                    continue;
                peerInfo = peerInfo == " 无" ? "" : peerInfo;
                peerInfo += $" {peer.displayName} d={FlatDistance(transform.position, peer.transform.position):F1}m r={peer.CollisionRadius}";
            }
            Debug.LogWarning(
                $"[DinosaurAgent] 卡死看门狗「{displayName}」{stuckWatchdogTime:F0}s 未移动：state={CurrentState} " +
                $"route={routeIndex} arc={arc:F1} lateral={lateral:F1} travelSign={travelSign} " +
                $"target=({target.x:F1},{target.z:F1}) " +
                $"blockedTime={blockedTime:F1} timer={stateTimer:F1} " +
                $"同伴:{peerInfo}", this);
        }

        /// <summary>
        /// 运行期开局随机布置入口（仅 DinosaurSpawnRandomizer 调用；采样约束见该类注释）：
        /// 瞬时把恐龙放到新的出生点并更新出生点记录（spawnPoint），Y 恒等于 MovementHeight——
        /// 地面恐龙恒为 0、飞行单位为其烘焙高度，不采样地形高程。
        /// Phase 1 起该出生点由 MovementCorridor 路线内采样给出；spawnPoint 不再充当漫步圆心
        /// （漫游已由所选路线约束，见 ComputeWanderTarget），仅作记录 / 调试读取。
        /// Phase 1.1 起同时写入**所选路线**（routeIndex）：本恐龙此后的漫步/车道/让路几何全部按它计算。
        /// 【Phase 1.1d 固定轨迹】入口签名去掉随机 yaw：出生点横向钳到**本车道**、朝向直接取
        /// 路线切线 × travelSign —— 开局即沿车道直走，消除「出生点在半宽内随机横向 + 随机朝向」
        /// 造成的开局拐进车道偏离段（用户报告的「还是会偏离」来源之一）。
        /// 只做根节点 Position/Rotation 写入：不触碰模型缩放/相对偏移等 Fit 产物（非幂等，严禁运行期重算）。
        /// </summary>
        public void ApplySpawnPlacement(Vector2 xz, int route)
        {
            routeIndex = route >= 0 && route < MovementCorridor.RouteCount ? route : MovementCorridor.MainRoute;
            travelSign = 1;

            // 出生点与朝向都直接取中心线，绝无横向车道偏移。
            MovementCorridor.ProjectToCorridor(routeIndex, xz, out float arc, out _);
            routeArc = arc;
            targetArc = arc;
            Vector2 laneXZ = MovementCorridor.PointAt(routeIndex, arc);
            spawnPoint = new Vector3(laneXZ.x, MovementHeight, laneXZ.y);

            // ② 朝向沿行进方向：root = LookRotation(切线×travelSign) × Euler(-VisualYaw)（与 MoveTowards
            //    的 desiredRotation 同式）⇒ GetVisualForward() 恰好等于车道前进方向，第一步就贴线走。
            MovementCorridor.GetSampleAt(routeIndex, arc, out _, out Vector2 tangent);
            Vector2 heading = tangent;
            Quaternion rootRotation = Quaternion.LookRotation(
                new Vector3(heading.x, 0f, heading.y), Vector3.up) * Quaternion.Euler(0f, -VisualYaw, 0f);
            transform.SetPositionAndRotation(spawnPoint, rootRotation);
        }

        public void BindAnimation(Animation animation, string idleHint = "idle", string moveHint = "run")
        {
            legacyAnimation = animation;
            idleClip = FindClip(idleHint, "idle", "stand", "wait");
            moveClip = FindClip(moveHint, "run", "walk", "fly", "animation");
            // 缓存 move state（循环窗口卷绕用；Animation 索引器是字典查找，避免每帧调用）。
            moveState = legacyAnimation != null && !string.IsNullOrEmpty(moveClip) ? legacyAnimation[moveClip] : null;
            BindCuriosityAnimations();
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

            // 边界契约：召唤瞬态同样受 DinosaurActivityBounds 约束（与漫步同域）。此处必须钳制——玩家探索
            // 范围已扩到平坦区外（WorldBounds.PlayerExplorationXZ 比活动包络宽 8m），若不钳制则「相机位 +
            // 前进 9m + 侧向 6m」可落到外围起伏地形上，而恐龙位置 Y 恒为 MovementHeight → 明显陷地
            // （最坏地形高 ≈1.7m）。钳制后玩家站在包络外时，恐龙走到包络边缘即停。
            // 注：钳制后若目标距恐龙 ≤0.75m，UpdateApproach 会立即判定 Arrived（恐龙「已到最近处」），
            // 既有 3s 受阻放弃 / 30s 兜底超时路径保持不变。
            Vector2 clampedDestination = WorldBounds.ClampToDinosaurActivity(
                new Vector2(approachDestination.x, approachDestination.z));
            approachDestination = new Vector3(clampedDestination.x, approachDestination.y, clampedDestination.y);

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
            if (!SummonEnabled)   // 双保险：输入口已门控，这里再拦一道（防止其它入口误触发）
                return;

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
            waitingAtCrossing = false;
            CurrentState = DinosaurState.Idle;
            stateTimer = IsContinuousWalker ? 0f : species == DinosaurSpecies.Pachycephalosaurus ? Range(1f, 2f) : Range(3f, 7f);
            blockedTime = 0f;
            PlayClip(idleClip);
        }

        private void BeginRandomWalk()
        {
            waitingAtCrossing = false;
            CurrentState = DinosaurState.RandomWalk;
            stateTimer = Range(8f, 16f);
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

        /// <summary>
        /// 计算新的漫步目标点，供 BeginRandomWalk 和 RerollWanderTarget 复用。
        ///
        /// 【Dense Jungle Phase 1：目标恒落在 MovementCorridor（恐龙移动通道）内】
        /// 旧实现 = 当前朝向 ±70° 转向 + 8~18m 步长 + WanderRadius 圆盘钳制，恐龙可在整个活动包络
        /// 自由游走，「它到底会从哪儿穿过」不可预测，后续的遮挡布置没有确定对象。
        /// 现实现 = 先把当前位置投影到**所选路线**（routeIndex，出生时固定分派、每只各一条），
        /// 【岔口掷骰换轨】（Phase 1.1d 起 EnableJunctionSwitch=false 直接旁路），
        /// 沿该路线中心线按**持久方向**（travelSign，只在端点翻转）推进一个步长，
        /// 横向落在**本 Agent 自己的固定车道**（LaneLateralAt，Phase 1.1c 起零抖动）上，最后钳进该弧长处的真实净宽。
        ///
        /// 【方向持久化 = 「不掉头」的关键】旧实现在中段 50% 随机取反向，叠加走停节奏，
        /// 恐龙变成沿弧长的对称随机游走 → 永远在出生点附近兜圈。现在 travelSign 只在
        /// 距端点不足一个步长时强制翻转（尽头掉头是必须的），途中一律朝同一方向推进。
        /// 换轨（TrySwitchRouteAtJunction，当前旁路）启用时会用换前的世界行进方向重定 travelSign，同样不掉头。
        ///
        /// 【为什么不再叠加 WanderRadius 圆盘钳制】圆盘边界点一般【不在】路线上，二次钳制会把目标
        /// 拉出路线（与本改动的目的恰好相反）。安全性改由「每条路线 ⊆ WorldBounds.DinosaurActivityXZ」
        /// 这条 ValidateProject 断言承担；WanderRadius 常量保留不动（WorldBounds 仍由它推导包络）。
        ///
        /// 端点处理：当前位置距路线端不足一个步长时强制朝内侧推进，避免一直被端点钳住反复重掷。
        /// 召唤瞬态（Approaching）不走本方法，仍可临时离开路线（玩家召唤是刻意玩法）。
        /// </summary>
        private Vector3 ComputeWanderTarget()
        {
            float step = Range(WanderStepDistanceMin, WanderStepDistance);
            walkRemaining = step;
            targetArc = MovementCorridor.TravelDistance(routeIndex, routeArc + travelSign * step);
            Vector2 targetXZ = MovementCorridor.PointAt(routeIndex, targetArc);
            return new Vector3(targetXZ.x, MovementHeight, targetXZ.y);
        }

        /// <summary>
        /// 岔口换轨：当前位置落在某条**岔口**（多条路线共享的汇合点）附近时，按
        /// <see cref="JunctionSwitchChance"/> 掷骰换到另一条也过该岔口的路线；未命中或无其他路线则原样返回。
        /// 换轨前记下**世界行进方向**（切线 × travelSign），换轨后用它与新路线切线的点积重定 travelSign
        /// ⇒ 视觉上只是在路口自然分流/汇流，绝不因换轨掉头。
        /// 【Phase 1.1d：EnableJunctionSwitch=false 时本方法第一行直接返回】——每只恐龙各沿一条
        /// 固定路线（出生选线也已改固定分派），途中不再变线；逻辑整体保留，恢复随机性时打开开关即可。
        /// </summary>
        private void TrySwitchRouteAtJunction(Vector2 currentXZ, ref float centerDistance)
        {
            if (!EnableJunctionSwitch)   // Phase 1.1d：每只恐龙各沿一条固定路线，路口不换轨（见字段注释）
                return;

            int junction = MovementCorridor.FindJunction(currentXZ, JunctionSwitchRadius);
            if (junction < 0)
                return;

            int count = MovementCorridor.JunctionRouteCount(junction);
            if (count < 2)
                return;

            // 候选 = 该岔口经过的路线里除自己以外的那些（岔口最多 4 条路，缓冲区 8 足够）。
            int candidates = 0;
            for (int i = 0; i < count && candidates < junctionRouteBuffer.Length; i++)
            {
                int route = MovementCorridor.GetJunctionRoute(junction, i);
                if (route != routeIndex)
                    junctionRouteBuffer[candidates++] = route;
            }
            if (candidates == 0)
                return;
            if (random.NextDouble() >= JunctionSwitchChance)
                return;

            int chosen = junctionRouteBuffer[random.Next(candidates)];

            MovementCorridor.GetSampleAt(routeIndex, centerDistance, out _, out Vector2 oldTangent, out _, out _);
            Vector2 worldDirection = oldTangent * travelSign;   // 换轨前的世界行进方向

            routeIndex = chosen;
            MovementCorridor.ProjectToCorridor(routeIndex, currentXZ, out centerDistance, out _);
            MovementCorridor.GetSampleAt(routeIndex, centerDistance, out _, out Vector2 newTangent, out _, out _);
            travelSign = Vector2.Dot(worldDirection, newTangent) >= 0f ? 1 : -1;
        }

        /// <summary>
        /// 贴线前瞻点：把远目标换算成「沿路线朝目的地方向、前方 <see cref="RouteLookaheadDistance"/> 米处的线上点」。
        ///
        /// 【为什么需要它】目标点恒被 ClampLateral 钳在左右边界内，但实际移动是每帧朝目标的**直线**推进：
        ///  ① 8~18m 远目标与当前位置的连线是弦，S 弯上切弯能切出好几米（越出黄线的主因）；
        ///  ② 转弯率仅 22~55°/s，目标方向突变时恐龙按旧朝向多滑几帧才跟上，横向甩出；
        ///  ③ 树干 SlideMove 的切向滑移也会横向推离。
        /// 每帧朝「前方 5m 的线上点」转向后，逐步推进 = 微小弦，切弯偏差压到厘米级；
        /// 横向取目的地的横向（车道+抖动 / 让路外缘，早已钳在界内），被推出车道时逐步拉回而非一步横切。
        /// 让路后退时目的地弧长在后方 ⇒ dirSign 自然取反，倒退让行同样贴线。
        /// 到达判定仍由调用方对**原始 destination** 做（FlatDistance &lt; 0.5f），语义不变。
        /// 召唤态（Approaching）不走本方法，保持「可临时离开路线」的既有玩法。
        /// </summary>
        private Vector3 ComputeRouteAimPoint(Vector3 destination)
        {
            Vector2 currentXZ = new Vector2(transform.position.x, transform.position.z);
            Vector2 destXZ = new Vector2(destination.x, destination.z);
            MovementCorridor.ProjectToCorridor(routeIndex, currentXZ, out float arc, out _);
            MovementCorridor.ProjectToCorridor(routeIndex, destXZ, out float destArc, out float destLateral);

            // 前瞻方向 = 朝目的地；弧长已几乎重合（到达附近 / 车道回归）时维持本段行进方向。
            float dirSign = Mathf.Abs(destArc - arc) < 1f ? travelSign : Mathf.Sign(destArc - arc);

            float aimArc = Mathf.Clamp(
                arc + dirSign * RouteLookaheadDistance, 0f, MovementCorridor.Length(routeIndex));
            Vector2 aimXZ = MovementCorridor.ClampLateral(routeIndex, aimArc, destLateral, LaneEdgeMargin);
            return new Vector3(aimXZ.x, MovementHeight, aimXZ.y);
        }

        /// <summary>
        /// 向目的地移动一帧。返回 true 表示本帧内发生了状态切换（HandleBlockedEscalation 触发了
        /// BeginIdle / CompleteSummon→BeginRandomWalk），调用方应跳过后续 timer 递减与到达判定，
        /// 避免同帧二次状态切换（Minor 5）。
        /// </summary>
        private bool MoveTowards(Vector3 destination, float speed)
        {
            if (CurrentState == DinosaurState.RandomWalk)
                return MoveAlongCorridor(speed);

            // RandomWalk：不直接朝远目标走（S 弯切弯 + 转弯滞后会越出边界），改朝贴线前瞻点转向；
            // 召唤态保持原语义（刻意可离开路线）。到达判定始终对原始 destination。
            Vector3 aim = CurrentState == DinosaurState.RandomWalk
                ? ComputeRouteAimPoint(destination)
                : destination;

            Vector3 direction = aim - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f)
                return false;

            Quaternion desiredRotation = Quaternion.LookRotation(direction.normalized, Vector3.up)
                * Quaternion.Euler(0f, -VisualYaw, 0f);
            float turnRate = Mathf.Lerp(MinimumTurnRate, MaximumTurnRate,
                Mathf.InverseLerp(0.7f, 5.5f, speed));
            Quaternion previousRotation = transform.rotation;
            Vector2 previousForward = CoreForward;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, desiredRotation, turnRate * Time.deltaTime);
            if (groundObstacleAware && coreBody != null)
            {
                Vector2 at = new Vector2(transform.position.x, transform.position.z);
                if (TerrainTreeObstacleRegistry.IsCoreStepBlocked(at, at, previousForward, CoreForward, coreBody))
                    transform.rotation = previousRotation;
            }
            Vector3 visualForward = GetVisualForward();
            Vector3 step = visualForward * speed * Time.deltaTime;

            Vector2 currentXZ = new Vector2(transform.position.x, transform.position.z);
            Vector2 desiredXZ = new Vector2(currentXZ.x + step.x, currentXZ.y + step.z);

            // ─── 恐龙互斥预检查（快速路径，维持原停步语义）─────────────────────────
            // 保留滑移前的 desiredXZ 互斥预检查：若期望落点已侵入同伴互斥圆，直接交给同类会车
            // 调度（HandlePeerBlocked：先行权 + 让路落点），省去一次 SlideMove 调用。
            // 与滑移后的 actualXZ 复检互补（Major 2）。互斥仍不使用滑移，防止恐龙互相推挤滑行。
            // Phase 1.1c 起用**方向性**判定 FindBlockingStep(from, next)：只拦「落点在互斥半径内
            // 且本步在靠近对方」的步，后撤/横移放行 —— 旧的无方向判定曾把「两恐龙进入 6.5m 半径」
            // 变成双方任何步都被拒的永久互锁（用户两轮报告的「都卡死」根因）。
            DinosaurAgent preBlocker = FindBlockingStep(currentXZ, desiredXZ);
            if (preBlocker != null)
                return HandlePeerBlocked(currentXZ, preBlocker);

            // ─── 树干避让：扫掠滑移接入点 ──────────────────────────────────────────
            // groundObstacleAware=true 的地面恐龙使用 SlideMove 沿树干切向滑行绕树，
            // 替代原来的「预检查通过才移动」策略，解决贴树后每帧都被拒绝导致冻结的问题。
            // groundObstacleAware=false 的飞行单位完全跳过树干碰撞，行为与现状一致。
            // 边界契约（Phase 1.1b 起）：漫步/让路目标恒在界内，且 RandomWalk 逐帧把**实际落点**
            // 钳回左右边界净距内（下方「通道硬边界」块）——目标在界内不等于轨迹在界内：朝远目标
            // 直线走会切弯、转弯率滞后会甩出、树干滑移会横推。通道 ⊆ WorldBounds.DinosaurActivityXZ
            // 由 ValidateProject 断言；召唤瞬态目标本身已被 ClampToDinosaurActivity 钳进包络，不参与硬钳。
            Vector2 actualXZ;
            float progress;
            if (groundObstacleAware)
            {
                actualXZ = coreBody != null ? TerrainTreeObstacleRegistry.MoveCore(currentXZ, desiredXZ, CoreForward, coreBody) : TerrainTreeObstacleRegistry.SlideMove(
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

            // ─── 通道硬边界（仅 RandomWalk）：把实际落点收回左右边界内 ─────────────────
            // 目标/让路点早已 ClampLateral 在界内，越界来自移动过程本身（切弯、转弯滞后、树滑横推）。
            // 逐帧投影回路线并钳进 LaneEdgeMargin 净距 ⇒ 视觉上恐龙永不走出左右黄线；
            // 放在互斥复检之前，让同伴检查看到的就是钳后的最终落点。
            if (CurrentState == DinosaurState.RandomWalk)
            {
                MovementCorridor.ProjectToCorridor(routeIndex, actualXZ, out float passArc, out float passLat);
                actualXZ = MovementCorridor.ClampLateral(routeIndex, passArc, passLat, LaneEdgeMargin);
            }

            // ─── Major 2：滑移后 actualXZ 互斥复检 ──────────────────────────────────
            // SlideMove 切向滑移后 actualXZ 方向可与 desiredXZ 近乎垂直，可能把恐龙横向滑进同伴互斥圆。
            // 若被拒则回退 currentXZ（本帧停步），同样交给同类会车调度（HandlePeerBlocked）。
            // 与滑移前的 desiredXZ 预检查互补：预检查省 SlideMove 调用，复检兜住滑移后的横向侵入。
            // 同为**方向性**判定（参照帧起点 currentXZ）：滑移/硬钳若只是横移或拉开距离则放行。
            DinosaurAgent postBlocker = FindBlockingStep(currentXZ, actualXZ);
            if (postBlocker != null)
                return HandlePeerBlocked(currentXZ, postBlocker);

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

            // 正常前进：清零受阻累计时间（含先行权宽限计时，下一次被挡重新起算）。
            blockedTime = 0f;
            return false;
        }

        // 持续沿闭合中心线推进；没有端点掉头或原地转向。
        private bool MoveAlongCorridor(float speed)
        {
            if (TryRouteTurn()) return true;
            Vector2 currentXZ = new Vector2(transform.position.x, transform.position.z);
            waitingAtCrossing = false;
            float step = Mathf.Min(walkRemaining, speed * Time.deltaTime);
            if (step <= 1e-5f) return false;
            float nextArc = Mathf.Repeat(routeArc + step, MovementCorridor.Length(routeIndex));
            Vector2 nextXZ = MovementCorridor.PointAt(routeIndex, nextArc);

            if (!MovementCorridorCrossings.CanAdvance(this, peers, nextXZ))
            {
                waitingAtCrossing = true;
                blockedTime = 0f;
                PlayClip(idleClip);
                return false;
            }

            DinosaurAgent blocker = FindBlockingPeer(nextXZ);
            if (blocker != null)
            {
                blockedTime += Time.deltaTime;
                PlayClip(idleClip);
                if (blockedTime > BlockedEscalationSeconds)
                {
                    Debug.LogWarning($"[DinosaurAgent] {displayName} 在中心线 {routeIndex} 被同伴阻挡。", this);
                    BeginIdle();
                    return true;
                }
                return false;
            }

            MovementCorridor.GetSampleAt(routeIndex, nextArc, out _, out Vector2 tangent);
            if (groundObstacleAware && (coreBody != null ? TerrainTreeObstacleRegistry.IsCoreStepBlocked(currentXZ, nextXZ, CoreForward, tangent, coreBody) : TerrainTreeObstacleRegistry.IsBlocked(nextXZ, treeAvoidRadius)))
            {
                blockedTime += Time.deltaTime;
                PlayClip(idleClip);
                if (blockedTime > BlockedEscalationSeconds)
                {
                    Debug.LogWarning($"[DinosaurAgent] {displayName} 的路线 {routeIndex} 在弧长 {nextArc:F1}m 被树阻挡。", this);
                    BeginIdle();
                    return true;
                }
                return false;
            }

            Quaternion rotation = Quaternion.LookRotation(
                new Vector3(tangent.x, 0f, tangent.y), Vector3.up)
                * Quaternion.Euler(0f, -VisualYaw, 0f);
            transform.SetPositionAndRotation(new Vector3(nextXZ.x, MovementHeight, nextXZ.y), rotation);
            routeArc = nextArc;
            walkRemaining -= step;
            blockedTime = 0f;
            PlayMovementClip(speed);
            return false;
        }

        /// <summary>
        /// 受阻升级处理（Minor 4 统一时间制 + Minor 5 返回状态切换标志）：
        /// 返回 true 表示本帧发生了状态切换（BeginIdle / CompleteSummon→BeginRandomWalk），
        /// 调用方据此跳过后续 timer 递减与到达判定。
        /// 仅供**树干**受阻使用（会重掷走廊目标换方向）；同类受阻走 HandlePeerBlocked → EscalateAfterLongBlock，
        /// 那条路径刻意【不重掷】——旧实现被同伴挡住时每帧重掷，目标仍指向对方身上，正是「顶死」成因。
        /// - RandomWalk：累计受阻时间 ≤ 阈值 → RerollWanderTarget 尝试新方向。
        /// - Approaching：累计受阻时间 > 3s → 提前放弃穿越（BlockedGiveUp），不再空转等 30s 超时。
        /// </summary>
        private bool HandleBlockedEscalation()
        {
            if (CurrentState == DinosaurState.RandomWalk && blockedTime <= BlockedEscalationSeconds)
            {
                RerollWanderTarget();
                return false;
            }
            return EscalateAfterLongBlock();
        }

        /// <summary>
        /// 长时间受阻的终局升级（不重掷目标）：
        /// - RandomWalk：受阻超过 BlockedEscalationSeconds → BeginIdle 退让重来。
        /// - Approaching：受阻超过 ApproachBlockedGiveUpSeconds → 放弃穿越。
        /// 返回 true 表示本帧发生了状态切换。
        /// </summary>
        private bool EscalateAfterLongBlock()
        {
            if (CurrentState == DinosaurState.RandomWalk)
            {
                if (blockedTime > BlockedEscalationSeconds)
                {
                    BeginIdle();
                    return true;
                }
                return false;
            }
            if (CurrentState == DinosaurState.Approaching && blockedTime > ApproachBlockedGiveUpSeconds)
            {
                CompleteSummon(SummonExitReason.BlockedGiveUp);
                return true;
            }
            return false;
        }

        /// <summary>Route movement hands peer conflicts to the common ground scheduler.</summary>
        private bool HandlePeerBlocked(Vector2 currentXZ, DinosaurAgent blocker)
        {
            if (IsGroundTrafficPeer(blocker))
            {
                RegisterGroundConflict(blocker);
                PlayClip(idleClip);
                return false;
            }
            PlayClip(idleClip);
            return false;
        }

        /// <summary>Frozen interaction tier, explicit species rank, then stable lane order.</summary>
        private bool HasRightOfWay(DinosaurAgent peer) => GroundRightOfWay(peer);

        /// <summary>
        /// 恐龙互斥检查（**落点型 / 无方向**）：落点与同场景其它恐龙的平面距离 &lt; 双方
        /// collisionRadius 之和则视为占用。只查 XZ 平面。互斥使用各自完整的 collisionRadius（不做 Cap 钳制），
        /// 保持体型语义。与树干避让分离：树干走 SlideMove 扫掠滑移，互斥走会车调度（HandlePeerBlocked，不推挤滑行）。
        /// 【用途】校验「让路点/绕行点本身是否可走」（IsPeerBlocked）——这类远端落点没有步进方向概念，
        /// 用绝对距离；**运动步**的每帧预检/复检请用方向性的 <see cref="FindBlockingStep"/>（否则会死锁）。
        /// Minor 6：加高度差门槛——|MovementHeight - peer.MovementHeight| &gt; PeerHeightTolerance 时跳过该 peer，
        /// 避免飞行单位（风神翼龙 movementHeight=11）与地面恐龙 XZ 投影重叠时互阻。
        /// </summary>
        private bool IsPeerBlocked(Vector2 nextXZ)
        {
            return FindBlockingPeer(nextXZ) != null;
        }

        /// <summary>返回侵入 nextXZ 互斥圆的那个同伴（无则 null）：让 HandlePeerBlocked 能对阻塞者做先行权裁决。</summary>
        private bool IsCollisionPeer(DinosaurAgent peer) => peer != null && peer != this && peer.gameObject.activeInHierarchy
            && Mathf.Abs(movementHeight - peer.MovementHeight) <= PeerHeightTolerance
            && groundObstacleAware == peer.groundObstacleAware;

        private DinosaurAgent FindBlockingPeer(Vector2 nextXZ)
        {
            DinosaurAgent blocker = null;
            foreach (var peer in peers)
            {
                if (!IsCollisionPeer(peer)) continue;
                Vector2 center = new Vector2(peer.transform.position.x, peer.transform.position.z);
                float radius = collisionRadius + peer.CollisionRadius;
                if ((nextXZ - center).sqrMagnitude < radius * radius
                    && (blocker == null || peer.HasRightOfWay(blocker))) blocker = peer;
            }
            return blocker;
        }

        /// <summary>Check the whole segment. Existing overlaps may only shrink, never deepen.</summary>
        private DinosaurAgent FindBlockingStep(Vector2 fromXZ, Vector2 nextXZ)
        {
            DinosaurAgent blocker = null;
            Vector2 step = nextXZ - fromXZ;
            foreach (var peer in peers)
            {
                if (!IsCollisionPeer(peer)) continue;
                Vector2 center = new Vector2(peer.transform.position.x, peer.transform.position.z);
                float radius = collisionRadius + peer.CollisionRadius;
                float start = (fromXZ - center).sqrMagnitude;
                float t = step.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector2.Dot(center - fromXZ, step) / step.sqrMagnitude) : 0f;
                float minimum = (fromXZ + t * step - center).sqrMagnitude;
                // No per-frame approach allowance: thousands of tiny steps must not creep through a peer.
                float limit = Mathf.Min(start, radius * radius);
                if (minimum + .000001f < limit && (blocker == null || peer.HasRightOfWay(blocker))) blocker = peer;
            }
            return blocker;
        }

        private Vector3 GetVisualForward()
        {
            return transform.rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
        }

        private void PlayMovementClip(float groundSpeed)
        {
            float referenceSpeed = Mathf.Max(0.01f, MoveAnimationGroundSpeed);
            PlayClip(moveClip, groundSpeed / referenceSpeed, true);
        }

        private void PlayClip(string clipName, float playbackSpeed = 1f, bool moving = false)
        {
            if (legacyAnimation == null || string.IsNullOrEmpty(clipName))
                return;
            if (frontWaitHoldingPose) { frontWaitHoldingPose = false; activeClip = null; }

            AnimationState state = legacyAnimation[clipName];
            if (state == null)
            {
                Debug.LogWarning("动画片段不存在：" + clipName, this);
                return;
            }

            gaitMoving = moving;
            state.wrapMode = WrapMode.Loop;
            state.speed = freezePoseWhenStopped && !moving && clipName == moveClip
                ? 0f : trafficMoving && trafficReverse && moving ? -Mathf.Max(.01f, Mathf.Abs(playbackSpeed)) : Mathf.Max(0.01f, playbackSpeed);
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
