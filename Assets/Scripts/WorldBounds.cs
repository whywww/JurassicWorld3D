using UnityEngine;

namespace JurassicWorld
{
    /// <summary>
    /// 四类空间边界的**唯一权威定义**（runtime 程序集；供运行期相机 / 编辑器播种器 / 验收工具同源引用）。
    ///
    /// 职责划分（禁止互相代用 —— 历史教训：一个「平坦矩形」同时兼任地形几何与全部植被安全边界，
    /// 导致中央活动区必然空洞，且地形一改则恐龙包络与植被一起静默漂移）：
    ///  ① <see cref="TerrainFlatXZ"/>         TerrainFlatBounds      —— 只描述哪些地形保持平坦；
    ///  ② <see cref="DinosaurActivityXZ"/>    DinosaurActivityBounds —— 恐龙可能移动 / 被召唤经过的 gameplay envelope；
    ///  ③ <see cref="PlayerExplorationXZ"/>   PlayerExplorationBounds—— 玩家允许探索的范围；
    ///  ④ VisualVegetationZones（本类常量 + <see cref="LowVegGate01"/>）—— Grass/Fern/Bush/Rock/Deadwood 的视觉分布分带。
    ///
    /// 【为什么①不能动】<c>DinosaurAgent</c> 的位置 Y 恒为 <c>MovementHeight</c>、<c>DinosaurFootLock</c> 的接地
    /// 基准在 Start 采样一次，二者都建立在「活动区地面恒平」之上。故 **② ⊆ ① 是这套硬编码合法的唯一前提**，
    /// 由 <c>JurassicProjectSetup.ValidateProject</c> 机器断言。
    ///
    /// 【本类只放边界与几何，不放密度策略】Grass/Fern/Bush 到底多密仍由 UnderstorySeeder 决定，
    /// 本类只提供「带在哪里」。
    ///
    /// XZ 平面矩形约定：<c>Rect.x / Rect.width</c> 表示 world X，<c>Rect.y / Rect.height</c> 表示 world Z
    /// （与全项目「Vector2 = (x, z)」约定一致）。
    /// </summary>
    public static class WorldBounds
    {
        // ================================================================ ① TerrainFlatBounds

        /// <summary>地形尺寸（米）。与 JurassicWorldSceneBuilder.TerrainSize 同值；
        /// 地形 GameObject 位于 (-TerrainSize/2, 0, -TerrainSize/2)，故 world 范围 = [-100, 100]²。</summary>
        public const float TerrainSize = 200f;

        // 平坦矩形归一化边界（0..1）：PrepareTerrain 的 heightmap 语言。TerrainEnvironmentSeeder 的
        // FlatMinX/MaxX/MinZ/MaxZ 转发此处，保证「地形平坦区」只有一个数值来源。
        public const float TerrainFlatMinXNorm = 0.15f;
        public const float TerrainFlatMaxXNorm = 0.85f;
        public const float TerrainFlatMinZNorm = 0.15f;
        public const float TerrainFlatMaxZNorm = 0.95f;

        /// <summary>地形恒平区（world XZ）：x∈[-70,70]、z∈[-70,90] 内 heightmap 恒 0，
        /// 向外由 TransitionWidth(0.15 归一化 ≈30m) 平滑爬升，最高 HillAmplitude(0.2)×TerrainHeight(10m) = 2m。</summary>
        public static readonly Rect TerrainFlatXZ = Rect.MinMaxRect(
            TerrainFlatMinXNorm * TerrainSize - TerrainSize * 0.5f,
            TerrainFlatMinZNorm * TerrainSize - TerrainSize * 0.5f,
            TerrainFlatMaxXNorm * TerrainSize - TerrainSize * 0.5f,
            TerrainFlatMaxZNorm * TerrainSize - TerrainSize * 0.5f);

        /// <summary>到地形平坦矩形的最短距离（米，矩形内为 0）。</summary>
        public static float DistanceToTerrainFlat(float worldX, float worldZ) =>
            DistanceToRect(TerrainFlatXZ, worldX, worldZ);

        // ================================================================ ② DinosaurActivityBounds

        /// <summary>
        /// 恐龙活动包络（world XZ）：出生采样矩形 ⊕ WanderRadius 的**外接矩形（AABB）**。
        ///
        /// ⚠️ 真实漫步包络是 Minkowski 和的**圆角矩形**（例：出生矩形角点 (-25,-25) 到 AABB 角点 (-70,-70)
        /// 距离 63.6 &gt; WanderRadius 45，四角实际不可达），故本 AABB 是**保守超集**——
        /// 用作植被安全边界只会偏保守，永不偏松。**禁止**对它做「等于真实包络」的断言。
        ///
        /// 与恐龙自己的常量推导（不复制字面量）：见 DinosaurSpawnRandomizer.SpawnMinX/MaxX/MinZ/MaxZ
        /// 与 DinosaurAgent.WanderRadius。恐龙的**实际**漫游已由 MovementCorridor 通道约束（Phase 1 起），
        /// 通道整体 ⊆ 本包络由 JurassicProjectSetup.ValidateProject 断言；召唤瞬态额外受约束：
        /// DinosaurAgent.Summon 把 approachDestination 钳进本矩形（玩家出包络时恐龙停在边缘）。
        /// </summary>
        public static readonly Rect DinosaurActivityXZ = Rect.MinMaxRect(
            DinosaurSpawnRandomizer.SpawnMinX - DinosaurAgent.WanderRadius,
            DinosaurSpawnRandomizer.SpawnMinZ - DinosaurAgent.WanderRadius,
            DinosaurSpawnRandomizer.SpawnMaxX + DinosaurAgent.WanderRadius,
            DinosaurSpawnRandomizer.SpawnMaxZ + DinosaurAgent.WanderRadius);

        /// <summary>到恐龙活动包络（AABB）的最短距离（米，矩形内为 0）。</summary>
        public static float DistanceToDinosaurActivity(float worldX, float worldZ) =>
            DistanceToRect(DinosaurActivityXZ, worldX, worldZ);

        /// <summary>到恐龙活动包络的**带符号**距离（米）：矩形内为负（= 到最近边的距离），矩形外为正。
        /// 植被梯度以它为唯一轴心（见 VisualVegetationZones）。</summary>
        public static float SignedDistanceToDinosaurActivity(float worldX, float worldZ)
        {
            Rect r = DinosaurActivityXZ;
            float dx = Mathf.Max(r.xMin - worldX, worldX - r.xMax); // >0 在外，≤0 在内
            float dz = Mathf.Max(r.yMin - worldZ, worldZ - r.yMax);
            if (dx > 0f || dz > 0f)
                return Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) + Mathf.Max(dz, 0f) * Mathf.Max(dz, 0f));
            return Mathf.Max(dx, dz); // 两侧都 ≤0：取更近的边（负值 = 内缩深度）
        }

        /// <summary>把 XZ 钳制进恐龙活动包络（供 DinosaurAgent.Summon 约束召唤终点的瞬态越界）。</summary>
        public static Vector2 ClampToDinosaurActivity(Vector2 xz) => new Vector2(
            Mathf.Clamp(xz.x, DinosaurActivityXZ.xMin, DinosaurActivityXZ.xMax),
            Mathf.Clamp(xz.y, DinosaurActivityXZ.yMin, DinosaurActivityXZ.yMax));

        // ================================================================ ③ PlayerExplorationBounds

        /// <summary>玩家可探索范围相对恐龙活动包络的**外扩余量**（米）：西/东/南三侧 = 平坦区 +8m，
        /// 使玩家真正踩上外围过渡带缓坡（地形高 ≈0.35m）——这正是相机必须采样地形高度的原因。</summary>
        public const float PlayerOutwardMargin = 8f;

        /// <summary>玩家范围在北侧（+z）的止步线（米）。北侧地形 z=100 即结束、北林线在 z=87
        /// （ForestSeeder.CoreMaxZ 82 + 硬隔离带 5），故取 88：玩家停在林线内 1m，再放就站在地形边缘/密林墙里。
        /// 注意 88 &lt; 90，即北侧玩家止步于活动包络**内** 2m —— 北侧没有「玩家过渡带」。</summary>
        public const float PlayerNorthStopZ = 88f;

        /// <summary>玩家可探索矩形（world XZ）：x∈[-78,78]、z∈[-78,88]。凸集 → 保留
        /// SmoothExplorerCamera 的「起点/期望点都在界内 ⇒ 线段全程在界内」滑移论证。
        /// 实际可达范围仍由树干阻挡（TerrainTreeObstacleRegistry.SlideMove）进一步收缩。</summary>
        public static readonly Rect PlayerExplorationXZ = Rect.MinMaxRect(
            DinosaurActivityXZ.xMin - PlayerOutwardMargin,
            DinosaurActivityXZ.yMin - PlayerOutwardMargin,
            DinosaurActivityXZ.xMax + PlayerOutwardMargin,
            PlayerNorthStopZ);

        /// <summary>点是否在玩家可探索范围内。</summary>
        public static bool ContainsPlayer(Vector2 xz) =>
            xz.x >= PlayerExplorationXZ.xMin && xz.x <= PlayerExplorationXZ.xMax &&
            xz.y >= PlayerExplorationXZ.yMin && xz.y <= PlayerExplorationXZ.yMax;

        /// <summary>把 XZ 钳制进玩家可探索矩形。</summary>
        public static Vector2 ClampToPlayerExploration(Vector2 xz) => new Vector2(
            Mathf.Clamp(xz.x, PlayerExplorationXZ.xMin, PlayerExplorationXZ.xMax),
            Mathf.Clamp(xz.y, PlayerExplorationXZ.yMin, PlayerExplorationXZ.yMax));

        /// <summary>到玩家可探索矩形的最短距离（米，矩形内为 0）——验收工具用它衡量「道具离玩家有多远」。</summary>
        public static float DistanceToPlayerExploration(float worldX, float worldZ) =>
            DistanceToRect(PlayerExplorationXZ, worldX, worldZ);

        // ================================================================ ④ VisualVegetationZones

        /// <summary>开阔空地（R0）内缩距离（米）：到活动包络的距离 ≤ -此值 时，bush/fern 密度恒 0（纯草坪）。
        /// 50m → 空地 ≈ x[-20,20] × z[-20,40]（覆盖相机起点 (0,-16) 与 5 个恐龙出生点）。</summary>
        public const float ClearingInset = 50f;

        /// <summary>活动包络外缘（sd → 0）处的低矮植被门值：0 → 空地的线性爬升终点，
        /// 即「活动包络外缘的稀疏低矮植被」到底有多密（1 = 与林缘同等密）。</summary>
        public const float LowVegEdgeLevel = 0.35f;

        /// <summary>低矮植被门的**饱和点**（米）：sd ≥ 此值时门值恒为 1.0。
        /// 取值 19.5 = 旧硬钳制线 7.5 + 旧爬升带宽 12 —— 于是本次改动**逐格不触碰** sd ≥ 19.5 的区域。</summary>
        public const float NearForestOuter = 19.5f;

        /// <summary>玩家过渡带（R2）外沿（米）：到活动包络的正向外扩量（西/东/南三侧）。
        /// 北侧玩家止步于包络内（PlayerNorthStopZ 88 &lt; 90），故该侧无过渡带。</summary>
        public const float PlayerTransitionOuter = PlayerOutwardMargin;

        /// <summary>
        /// 低矮无碰撞植被（bush / fern detail 层）的分布门（0..1），以到恐龙活动包络的带符号距离为轴心，
        /// 从中央向外**连续单调**爬升：空地 0 → 包络外缘 0.35（稀疏）→ 19.5m 外 1.0（与旧行为逐格一致）。
        ///
        ///     gate(sd) = 0                            sd ≤ -50（空地）
        ///              = 0.35 * (sd + 50) / 50        -50 &lt; sd ≤ 0（活动包络外缘，稀疏低矮植被）
        ///              = 0.35 + 0.65 * sd / 19.5      0 &lt; sd &lt; 19.5（玩家过渡带 → 林缘）
        ///              = 1                            sd ≥ 19.5（与旧 ramp 逐格相同）
        ///
        /// 中大型实体道具（rock / hero bush / log / stump）**不使用本门**：它们仍以
        /// DinoSafetyBuffer 为边界（见 UnderstorySeeder），与恐龙零接触。
        /// </summary>
        public static float LowVegGate01(float signedDistanceToDinosaurActivity)
        {
            float sd = signedDistanceToDinosaurActivity;
            if (sd <= -ClearingInset)
                return 0f;
            if (sd <= 0f)
                return LowVegEdgeLevel * (sd + ClearingInset) / ClearingInset;
            if (sd < NearForestOuter)
                return LowVegEdgeLevel + (1f - LowVegEdgeLevel) * sd / NearForestOuter;
            return 1f;
        }

        // ================================================================ 摘要（Bake 日志 / 验收报告共用措辞）

        /// <summary>四边界单行摘要。Bake 日志与 ProductionAcceptance 报告共用同一份措辞，避免两侧漂移。</summary>
        public static string Describe()
        {
            Rect flat = TerrainFlatXZ;
            Rect act = DinosaurActivityXZ;
            Rect player = PlayerExplorationXZ;
            return $"① 地形平坦区 x[{flat.xMin:F0},{flat.xMax:F0}] z[{flat.yMin:F0},{flat.yMax:F0}]（heightmap 恒 0）"
                 + $" | ② 恐龙活动包络 x[{act.xMin:F0},{act.xMax:F0}] z[{act.yMin:F0},{act.yMax:F0}]（出生矩形⊕WanderRadius 的 AABB，⊆①）"
                 + $" | ③ 玩家探索 x[{player.xMin:F0},{player.xMax:F0}] z[{player.yMin:F0},{player.yMax:F0}]"
                 + $" | ④ 植被梯度 R0 空地(内缩 {ClearingInset:F0}m) → R1 稀疏低矮植被 → R2 玩家过渡带(外扩 {PlayerTransitionOuter:F0}m) → R3 林缘(≤{NearForestOuter:F1}m) → R4 密林";
        }

        // ================================================================ 几何辅助

        /// <summary>点到 XZ 矩形的最短距离（米，矩形内为 0）。与 UnderstorySeeder 既有算式逐位一致。</summary>
        private static float DistanceToRect(Rect rect, float worldX, float worldZ)
        {
            float dx = Mathf.Max(Mathf.Max(rect.xMin - worldX, worldX - rect.xMax), 0f);
            float dz = Mathf.Max(Mathf.Max(rect.yMin - worldZ, worldZ - rect.yMax), 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
