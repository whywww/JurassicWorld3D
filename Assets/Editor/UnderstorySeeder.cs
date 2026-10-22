using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace JurassicWorld.Editor
{
    /// <summary>
    /// Understory / Forest Detail Pass 编辑态播种器（与 JurassicWorldSceneBuilder / ForestSeeder /
    /// TerrainEnvironmentSeeder 同程序集 internal 协作）。
    ///
    /// 职责与所有权：
    ///  - 单一根节点 "Understory Pass"（子节点 Rocks / Vegetation / Deadwood）：每次 Bake 先整根
    ///    DestroyImmediate 再重建，重复 Bake 绝不叠加、关闭 Pass 整根移除；
    ///  - 现有 7 个 detail 层（bush01-06 mesh 层 + grass01 billboard 层）的【密度值】：cluster 化分区重绘，
    ///    逐层幂等写回（与现值比对，一致则跳过）；
    ///  - 资产门控的 fern 原型尾随块（Assets/Environment/Vegetation/Fern02/Prepared/Prefabs 出现 prefab 后自动追加）。
    ///
    /// 绝不触碰：树实例、heightmap / splat、其他 Pass 的根与原型对象、任何运行时脚本
    /// （含 DinosaurSpawnRandomizer —— 碰撞规避完全靠下述几何不变量，不靠运行时避让）。
    ///
    /// 安全缓冲不变量（几何隔离，**以恐龙活动包络为准，不再以地形平坦区为准**）：
    ///  恐龙漫步包络 ⊆ DinosaurActivityBounds（WorldBounds.DinosaurActivityXZ，由 DinosaurSpawnRandomizer 的
    ///  采样矩形 ⊕ DinosaurAgent.WanderRadius 推导；当前数值 x∈[-70,70]、z∈[-70,90]，与地形平坦区恰好同值，
    ///  但两者是**独立概念**——地形平坦区一改不再牵动本类）；
    ///  所有中大型实体道具（rock / hero bush / log / stump）位于活动包络 + DinoSafetyBuffer(7.5m) 之外，
    ///  故与恐龙零接触；低矮无碰撞的 bush / fern detail 层改由 VisualVegetationZones 梯度控制：
    ///  仅 R0 开阔空地（活动包络内缩 50m）内恒 0，从活动包络外缘起单调加密到林缘（见 WorldBounds.LowVegGate01）。
    ///
    /// 回滚契约（EnableUnderstoryPass=false + 重 Bake = 恢复至 Ground Pass 完成后的 Detail 状态）：
    ///  (a) 整根删除 "Understory Pass"；
    ///  (b) 剥离自有 fern 原型块（snapshot/restore 非自有层）；
    ///  (c) bush 层密度用同源 helper（JurassicWorldSceneBuilder.BuildBushBaselineDensity）重放 SeedDetails
    ///      的同一 RNG 流（逐层升序、每层一次），逐格恢复基线；
    ///  (d) grass01 层由 GroundPass 在同一 Bake 中先行的确定性重绘恢复。
    ///  (c)(d) 均为纯函数恢复，「启用后禁用重 Bake」与「从未启用」逐格一致，不留永久覆盖。
    /// </summary>
    internal static class UnderstorySeeder
    {
        // ================================================================ 可调常量块

        // --- 回滚开关 ---
        private const bool EnableUnderstoryPass = true;

        /// <summary>回滚开关的对外只读视图：供 JurassicProjectSetup.ValidateProject 联动断言（属性读取避免常量折叠告警）。</summary>
        internal static bool PassEnabled => EnableUnderstoryPass;

        // --- 种子（固定种子链：271828 Ground / 271831 Forest / 271832 GroundPass → 本 Pass 271833）---
        private const int UnderstoryRandomSeed = 271833;

        // --- 安全缓冲 ---
        /// <summary>平坦矩形外安全缓冲（米，5-10 可调）：中大型实体一律位于缓冲外。</summary>
        internal const float DinoSafetyBuffer = 7.5f;

        // 实体道具在缓冲之外再留的额外余量（米）：吸收 bounds / 缩放误差。
        private const float PlacementExtraMargin = 1.5f;

        // 单簇成员的最大水平偏移（米）：簇心最小距离 = buffer + extra + 该值，保证含成员的整体不越界。
        private const float MaxMemberOffset = 3.5f;

        // 岩石簇 / hero 灌木簇 簇心到平坦矩形的最小距离（米）。
        private const float ClusterCenterMinDistance = DinoSafetyBuffer + PlacementExtraMargin + MaxMemberOffset;

        // --- 根节点 ---
        internal const string RootNodeName = "Understory Pass";
        internal const string RocksNodeName = "Rocks";
        internal const string VegetationNodeName = "Vegetation";
        internal const string DeadwoodNodeName = "Deadwood";

        // --- 视觉梯度（VisualVegetationZones）---
        // 低矮无碰撞植被（bush/fern detail）的门由 WorldBounds.LowVegGate01 按「到恐龙活动包络的带符号距离」
        // 给出：空地(≤ -ClearingInset) 0 → 活动包络外缘(LowVegEdgeLevel) 0.35 稀疏 → NearForestOuter 19.5m 外 1.0。
        // 取值刻意与旧 ramp（Clamp01((dFlat-7.5)/12)，19.5m 外饱和）在饱和点对齐 ⇒ sd ≥ 19.5 的区域逐格不变。

        // 内侧 fern 独立簇流（salt +4）：专供 R1/R2（活动包络外缘 + 玩家过渡带）。
        // 既有 +2 簇流（BuildFernField）保持原样 = 森林 fern（R3/R4），两流互不干扰、各自独立可验证。
        private const int FernInnerBandSalt = UnderstoryRandomSeed + 4;
        private const int FernInnerClusterCount = 160;
        private const float FernInnerBandOuter = WorldBounds.PlayerTransitionOuter; // 8m：过渡带外沿
        private const float FernInnerClusterRadiusMin = 1.2f, FernInnerClusterRadiusMax = 3.0f;

        // --- 密度（bush 分区 / cluster 化）---
        private const float BushRampWidth = 12f;               // 旧密度爬升带宽（米）：现仅用于「梯度门饱和点自检」——
                                                               // WorldBounds.NearForestOuter 必须 == DinoSafetyBuffer + 本值
        private const int EdgeBushMin = 2, EdgeBushMax = 6;    // 林缘重点带（高频团块）
        private const int ForestBushMin = 2, ForestBushMax = 5; // 林内斑块（Fern 到位前的底层过渡覆盖）
        internal const float ForestEdgeDepth = 18f;             // 到核心矩形距离 < 此值视为林缘带（ValidateProject ⑧ 角色断言引用）
        private const float WallInset = 12f;                   // 地形边缘密林墙判定宽度（米）
        private const float WallBushFloor = 5f;                // 密林墙内 bush 密度下限

        // --- 密度（grass 分区）---
        private const float EdgeGrassBand = 25f;               // 林缘斑驳过渡带宽（米）
        private const int GrassMaxDensity = 5;
        private const int BushMaxDensity = 8;
        private const float SpawnHalfRadius = 20f;             // 出生点邻域减半半径（与 SeedDetails 契约一致）

        // --- 密度修正 ---
        private const float MudVegetationReduction = 0.8f;     // mud 区植被 ×(1 - 0.8·soilness)
        private const float TrailVegetationReduction = 0.5f;   // 视觉通道内植被 ×0.5
        private const float ForestInfluenceRadius = 20f;       // 森林影响场半径（米）

        // --- 岩石（资产门控 + 调试）---
        private const int RockClusterCount = 45;
        private const int RocksPerClusterMin = 2, RocksPerClusterMax = 5; // 1 锚石 + 1..4 小石
        private const int HeroRockCount = 10;                  // hero 锚石数（围绕平坦矩形的 360° 方位分层环带）
        private const float RockClusterMinGap = 9f;            // 簇心间 Poisson min-gap（米）
        private const float RockSinkRatioMin = 0.25f, RockSinkRatioMax = 0.45f;
        private const int RockPlacementAttempts = 1200;
        private const bool AddColliderForLargeRocks = false;
        private const string RockMaterialPath = "Assets/Environment/RocksAndDeadwood/RockMossSet01/Prepared/Materials/RockNatural.mat";

        // --- Hero 灌木（bush01-06 作为中层植被主角，cluster 化）---
        private const int HeroBushClusterCount = 16;
        private const int HeroBushPerClusterMin = 2, HeroBushPerClusterMax = 3;
        private const float HeroBushClusterMinGap = 8f;        // 簇心间最小间隙（米）
        private const float HeroBushScaleMin = 1.0f, HeroBushScaleMax = 2.2f;

        // --- Fern（资产门控：Assets/Environment/Vegetation/Fern02/Prepared/Prefabs 无合格 prefab 时 no-op）---
        private const int FernClusterCount = 320;
        private const int FernPerClusterMin = 4, FernPerClusterMax = 10;
        private const float FernClusterRadiusMin = 1.5f, FernClusterRadiusMax = 4f;

        /// <summary>fern 变体分配的独立固定 salt（与既有 +7/+11/+100/+200/+900 等 salt 互不冲突）。</summary>
        private const int FernVariantSalt = UnderstoryRandomSeed + 900;

        // --- Deadwood（资产门控：Assets/Environment/RocksAndDeadwood/Deadwood/Prepared/Prefabs 无合格 prefab 时 no-op）---
        private const int LogCountMin = 6, LogCountMax = 8;
        private const int StumpCountMin = 3, StumpCountMax = 5;
        private const bool AddColliderForDeadwood = false;

        // --- 调试开关：true 时用程序化 Sphere 仅验证岩石分布（正式 Bake 禁止；默认 false）---
        private const bool DebugProceduralRocks = false;

        /// <summary>调试岩石开关只读视图（属性读取避免常量折叠的不可达代码告警；ValidateProject 也会据此告警）。</summary>
        internal static bool DebugProceduralRocksOn => DebugProceduralRocks;

        // --- 非破坏性 Preview 倍数（editor-only；正式 Bake 恒为 1）---
        /// <summary>
        /// 仅由 JurassicWorld.Editor.PolyHavenPassPreview 设置的【非破坏性 Preview】倍数（默认 1）。
        /// >1 时按【确定性前缀】缩小岩石簇 / fern 簇 / deadwood 数量，用于小规模 Preview；
        /// 正式 Full Bake 恒为 1，行为与未引入本字段时逐字节一致。
        ///
        /// 刻意【不】改动任何分布常量、也【不】改变随机契约：前缀语义下「前 K 项」的随机序列
        /// 与生产序列的前 K 项完全相同，且各子系统各持独立 System.Random 实例，互不干扰。
        /// </summary>
        internal static int PreviewScale = 1;

        /// <summary>按 PreviewScale 折算数量（前缀预算）：PreviewScale &lt;= 1 时原样返回。</summary>
        private static int PreviewBudget(int productionCount)
        {
            return PreviewScale <= 1 ? productionCount : Mathf.Max(1, productionCount / PreviewScale);
        }

        // --- 最近一次 fern 铺设的守恒量（进程内传递，供 PolyHavenPassPreview 独立断言 sum(variants) == total target）---
        internal static long LastFernTotalTarget;
        internal static long LastFernAssigned;
        internal static int LastFernVariantCount;
        internal static readonly List<long> LastFernVariantSums = new List<long>();

        // --- 环境模型 ---
        private const int InfluenceRes = 128;  // 影响场网格分辨率（128² ≈ 1.57m/格）
        private const float TerrainSize = TerrainEnvironmentSeeder.TerrainSize; // 同源常量（=200）

        // ================================================================ 入口

        /// <summary>
        /// 在 TerrainEnvironmentSeeder.PopulateGround 之后调用（TerrainData 与 Terrain GameObject 均已就绪、
        /// heightmap 为最终值）。启用时：重算 detail 层密度（cluster 化分区）+ 重建 Understory 根实体；
        /// 禁用时：完整回滚至 Ground Pass 完成后的 Detail 状态。
        /// </summary>
        internal static void Seed(TerrainData data, Transform terrainTransform)
        {
            if (data == null || terrainTransform == null)
                return;

            if (!PassEnabled)
            {
                Rollback(data);
                return;
            }

            // 统一根节点：整根销毁重建（重复 Bake 绝不叠加）
            GameObject root = GameObject.Find(RootNodeName);
            if (root != null)
                UnityEngine.Object.DestroyImmediate(root);
            root = new GameObject(RootNodeName);
            GameObject rocksRoot = NewChild(root, RocksNodeName);
            GameObject vegetationRoot = NewChild(root, VegetationNodeName);
            GameObject deadwoodRoot = NewChild(root, DeadwoodNodeName);

            // 1) 环境模型（纯函数 + 固定 RNG）
            var model = BuildEnvironmentModel(data, terrainTransform);
            List<Vector2> trunkAnchors = CollectTreeAnchors(data, terrainTransform);

            // 2) 岩石簇分布先生成（内存）：与资产门控解耦，供密度场邻域增强使用
            List<RockCluster> rockClusters = GenerateRockClusters(data);
            StampTrunkBoosts(model, trunkAnchors);
            StampRockBoosts(model, rockClusters);

            // 3) detail 层签名识别（不增删原型）
            List<int> bushLayers = FindBushDetailLayerIndicesInternal(data);
            int grassLayer = FindGrass01LayerIndex(data);

            Debug.Log($"[UnderstorySeeder] 启动（seed={UnderstoryRandomSeed}, buffer={DinoSafetyBuffer}m）：" +
                      $"bush 层 {bushLayers.Count} 个 / grass 层 {(grassLayer >= 0 ? grassLayer.ToString() : "未识别")} / " +
                      $"岩石簇 {rockClusters.Count} 个（hero {Mathf.Min(HeroRockCount, rockClusters.Count)}）/ 树干锚点 {trunkAnchors.Count} 个。");

            // 四边界定义（与 ProductionAcceptance 报告共用 WorldBounds.Describe 的同一份措辞，避免两侧漂移）
            Debug.Log($"[WorldBounds] {WorldBounds.Describe()}");

            // 梯度门自检：门的饱和点必须 == 旧硬钳制线 + 旧爬升带宽，否则 sd ≥ 饱和点的区域不再逐格不变
            if (!Mathf.Approximately(WorldBounds.NearForestOuter, DinoSafetyBuffer + BushRampWidth))
                Debug.LogWarning($"[UnderstorySeeder] 梯度门饱和点 {WorldBounds.NearForestOuter}m ≠ DinoSafetyBuffer({DinoSafetyBuffer}) + " +
                                 $"BushRampWidth({BushRampWidth})：sd ≥ 饱和点的区域将与旧 Bake 不再逐格一致（「0 变化」预测失效，需重评估）。");

            LogShowcaseRing();

            // 4) 统一密度场计算 + 逐层幂等写回
            PaintDetailLayers(data, model, bushLayers, grassLayer);

            // 5) 岩石簇实例化（资产门控：无合格 prefab 且非调试模式 → 安全跳过）
            int rockInstances = InstantiateRocks(data, terrainTransform, rocksRoot, rockClusters);

            // 6) hero 灌木（bush01-06 cluster 化，活动区 + 缓冲外）
            int bushInstances = InstantiateHeroBushes(data, terrainTransform, vegetationRoot, rockClusters, trunkAnchors);

            // 7) 资产门控接口：Fern / Deadwood（无合格资产时 no-op + 日志提示目录）
            int fernLayers = ApplyFernGate(data, model);
            int deadwoodCount = ApplyDeadwoodGate(data, terrainTransform, deadwoodRoot, model, rockClusters);

            Debug.Log($"[UnderstorySeeder] 完成：岩石 {rockInstances} 个 / hero 灌木 {bushInstances} 个 / " +
                      $"fern 层 {fernLayers} / deadwood {deadwoodCount} 个。");
        }

        // ================================================================ 回滚（恢复至 Ground Pass 完成后的 Detail 状态）

        private static void Rollback(TerrainData data)
        {
            // (a) 整根删除
            GameObject root = GameObject.Find(RootNodeName);
            bool rootRemoved = root != null;
            if (rootRemoved)
                UnityEngine.Object.DestroyImmediate(root);

            // (b) 剥离自有 fern 原型块（如存在）
            int fernStripped = StripFernPrototypes(data);

            // (c) bush 层恢复 SeedDetails 基线（同源 helper 重放同一 RNG 流；逐层升序 = 原始调用顺序）
            int bushRestored = RestoreBushBaseline(data);

            // (d) grass01 层由 GroundPass 在同一 Bake 中先行确定性重绘，无需处理。
            Debug.Log($"[UnderstorySeeder] 回滚完成：根节点{(rootRemoved ? "已删除" : "不存在")}；" +
                      $"fern 原型剥离 {fernStripped} 个；bush 层恢复 {bushRestored} 个（其余与基线一致跳过）；" +
                      "grass01 层由 GroundPass 恢复。");
        }

        /// <summary>
        /// bush 层基线恢复：重放 SeedDetails 的随机流（逐层升序、每层一次 BuildBushBaselineDensity），
        /// 幂等比对后写回。前提：detailResolution=512（SeedDetails 播种时的分辨率）且 bush 层集合未变。
        /// </summary>
        private static int RestoreBushBaseline(TerrainData data)
        {
            List<int> bushLayers = FindBushDetailLayerIndicesInternal(data);
            if (bushLayers.Count == 0)
            {
                Debug.LogWarning("[UnderstorySeeder] 回滚：未识别到 bush detail 层，跳过基线恢复。");
                return 0;
            }

            int res = data.detailResolution;
            if (res != 512)
                Debug.LogWarning($"[UnderstorySeeder] 回滚：detailResolution={res} ≠ 512；基线复刻按当前分辨率生成，" +
                                 "与「从未启用」逐格一致的前提是 512（SeedDetails 原分辨率）。");

            var random = JurassicWorldSceneBuilder.CreateBushBaselineRandom();
            int restored = 0;
            foreach (int layerIdx in bushLayers)
            {
                int[,] baseline = JurassicWorldSceneBuilder.BuildBushBaselineDensity(
                    random, res, JurassicWorldSceneBuilder.DinosaurSpawnPoints);
                if (WriteLayerIfChanged(data, layerIdx, baseline) > 0)
                    restored++;
            }
            return restored;
        }

        /// <summary>剥离自有 fern 原型块（snapshot/restore 非自有层），返回剥离数（0 = 无自有块）。</summary>
        private static int StripFernPrototypes(TerrainData data)
        {
            DetailPrototype[] protos = data.detailPrototypes ?? Array.Empty<DetailPrototype>();
            var fernIdx = new List<int>();
            for (int i = 0; i < protos.Length; i++)
                if (IsOwnedFernProto(protos[i]))
                    fernIdx.Add(i);
            if (fernIdx.Count == 0)
                return 0;

            int res = data.detailResolution;

            // 快照非 fern 层（flatten 存储）
            int[][] snapshot = new int[protos.Length][];
            for (int i = 0; i < protos.Length; i++)
            {
                if (fernIdx.Contains(i)) continue;
                snapshot[i] = FlattenLayer(data, res, i);
            }

            // 重建不含 fern 的原型数组
            var kept = new DetailPrototype[protos.Length - fernIdx.Count];
            int k = 0;
            for (int i = 0; i < protos.Length; i++)
            {
                if (fernIdx.Contains(i)) continue;
                kept[k++] = protos[i];
            }
            data.detailPrototypes = kept; // setter 可能清空 detail 数据

            int resAfter = data.detailResolution;
            if (resAfter != res)
            {
                Debug.LogWarning($"[UnderstorySeeder] detailResolution 在 fern 剥离后由 {res} 变为 {resAfter}，按新值 restore。");
                res = resAfter;
            }

            // 恢复非 fern 层（新索引 = 原索引 - 位于其前的 fern 层数）
            for (int i = 0; i < protos.Length; i++)
            {
                if (snapshot[i] == null) continue;
                int removedBefore = 0;
                foreach (int fi in fernIdx)
                    if (fi < i) removedBefore++;
                UnflattenLayer(data, res, i - removedBefore, snapshot[i]);
            }

            Debug.Log($"[UnderstorySeeder] fern 原型块剥离：{fernIdx.Count} 个（index={string.Join(",", fernIdx)}）。");
            return fernIdx.Count;
        }

        /// <summary>fern 自有原型签名：mesh 模式且 prefab 资产路径位于 Fern 目录下。</summary>
        private static bool IsOwnedFernProto(DetailPrototype p)
        {
            if (p == null || !p.usePrototypeMesh || p.prototype == null)
                return false;
            string path = AssetDatabase.GetAssetPath(p.prototype);
            return !string.IsNullOrEmpty(path) &&
                   path.StartsWith(UnderstoryAssetLocator.FernFolder + "/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>自有 fern 原型的对外只读视图（供 JurassicProjectSetup.ValidateProject 断言，避免常量折叠）。</summary>
        internal static bool IsOwnedFernPrototype(DetailPrototype p) => IsOwnedFernProto(p);

        /// <summary>
        /// 自有 fern 原型的目标渲染模式。**刻意用 VertexLit 而非 Grass**：Grass 走 Unity 内置 waving-grass
        /// detail shader，其风位移是【整株水平平移】——2026-09-22 `DetailMotionProbe` 同一帧受控渲染实测：
        /// 位移由 `TerrainData.wavingGrass*` 驱动（置 0 → 蕨类像素 71% 变化），`WindZone` 只贡献 2%；
        /// 且蕨类连通块的"底部接触边界像素"与"顶部像素"受影响比例 81.2% vs 79.4% ⇒ 连根部一起被推走，
        /// 0.3m 高的蕨类被推 0.2m 量级 ⇒ 观感"蕨类根部一直左右移动"。VertexLit 按 Unity 文档"不随风移动"，
        /// 且该模式走原型 prefab 的材质（本项目自有双面 Cutout shader）→ 叶尖轻摆由该 shader 的
        /// `_FernSway`（位移 ∝ 归一化局部高度²，根部 y≤0 位移恒为 0）承担。
        /// 注意：此值与 minWidth/density 等一样只烘进 TerrainData，改动后必须重 Bake 才生效。
        /// </summary>
        internal static DetailRenderMode FernRenderModeTarget => DetailRenderMode.VertexLit;

        /// <summary>构造自有 fern 原型（唯一构造点：追加与规格修正共用，避免两处漂移）。</summary>
        private static DetailPrototype BuildFernPrototype(GameObject fern) => new DetailPrototype
        {
            usePrototypeMesh = true,
            prototype = fern,
            renderMode = FernRenderModeTarget,
            healthyColor = new Color(0.30f, 0.62f, 0.22f),
            dryColor = new Color(0.52f, 0.58f, 0.24f),
            minWidth = 0.8f,
            maxWidth = 1.6f,
            minHeight = 0.8f,
            maxHeight = 1.6f,
            noiseSpread = 0.2f,
            density = 1 // CoverageMode 下的实例数倍率基准（InstanceCountMode 下无副作用；整数字面量兼容 int/float 声明）
        };

        /// <summary>现有自有原型是否已符合目标规格（含 renderMode —— 历史产物可能是 Grass，必须能自愈重写）。</summary>
        private static bool FernPrototypeMatchesSpec(DetailPrototype existing, GameObject fern)
        {
            if (existing == null)
                return false;
            return existing.usePrototypeMesh
                && existing.prototype == fern
                && existing.renderMode == FernRenderModeTarget
                && existing.density > 0f;
        }

        private static int[] FlattenLayer(TerrainData data, int res, int layerIdx)
        {
            int[,] layerData = data.GetDetailLayer(0, 0, res, res, layerIdx);
            int[] flat = new int[res * res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
                flat[z * res + x] = layerData[x, z];
            return flat;
        }

        private static void UnflattenLayer(TerrainData data, int res, int layerIdx, int[] flat)
        {
            int[,] restore = new int[res, res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
                restore[x, z] = flat[z * res + x];
            data.SetDetailLayer(0, 0, layerIdx, restore);
        }

        // ================================================================ 环境模型

        private sealed class EnvironmentModel
        {
            public readonly float[,] TreeNear = new float[InfluenceRes, InfluenceRes];   // 1=密林内部，0=远离森林
            public readonly float[,] Soil = new float[InfluenceRes, InfluenceRes];       // 0..1 泥土度（w1+w2）
            public readonly float[,] GrassBoost = new float[InfluenceRes, InfluenceRes]; // 树干基座/岩石簇邻域草增强
            public readonly float[,] BushBoost = new float[InfluenceRes, InfluenceRes];  // 岩石簇邻域 bush 增强
        }

        /// <summary>
        /// 构建环境模型（全部为纯函数 / 固定数据读取，无 RNG）：
        /// 森林影响场（森林树多源 BFS 距离变换）、泥土度（alphamap w1+w2 双线性源）、boost 网格由后续 Stamp 填充。
        /// </summary>
        private static EnvironmentModel BuildEnvironmentModel(TerrainData data, Transform terrainTransform)
        {
            var model = new EnvironmentModel();

            // (1) 森林影响场：仅 prototypeIndex ≥ 中央段基线的森林树作为源（与 ForestSeeder / 校验侧同源常量）。
            var dist = new int[InfluenceRes * InfluenceRes];
            for (int i = 0; i < dist.Length; i++)
                dist[i] = int.MaxValue;
            var queue = new Queue<int>();

            void Push(int x, int z, int d)
            {
                if (x < 0 || x >= InfluenceRes || z < 0 || z >= InfluenceRes)
                    return;
                int index = z * InfluenceRes + x;
                if (d < dist[index])
                {
                    dist[index] = d;
                    queue.Enqueue(index);
                }
            }

            TreeInstance[] trees = data.treeInstances;
            int forestTreeCount = 0;
            foreach (TreeInstance ti in trees)
            {
                if (ti.prototypeIndex < ForestSeeder.CentralTreePrototypeCount)
                    continue;
                forestTreeCount++;
                Vector3 world = terrainTransform.TransformPoint(new Vector3(
                    ti.position.x * data.size.x, ti.position.y * data.size.y, ti.position.z * data.size.z));
                Push(WorldToInfluenceIndex(world.x), WorldToInfluenceIndex(world.z), 0);
            }

            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                int cx = index % InfluenceRes;
                int cz = index / InfluenceRes;
                int nd = dist[index] + 1;
                Push(cx - 1, cz, nd);
                Push(cx + 1, cz, nd);
                Push(cx, cz - 1, nd);
                Push(cx, cz + 1, nd);
            }

            float cellMeters = TerrainSize / (InfluenceRes - 1);
            for (int z = 0; z < InfluenceRes; z++)
            for (int x = 0; x < InfluenceRes; x++)
            {
                int d = dist[z * InfluenceRes + x];
                float meters = d == int.MaxValue ? 999f : d * cellMeters;
                model.TreeNear[x, z] = Mathf.Clamp01(1f - meters / ForestInfluenceRadius);
            }

            // (2) 泥土度：slot 契约 = 0 Grass / 1 Ground02 / 2 Ground03 / 3 DryGrass（GroundPass 铺设约定），
            // soilness = w(Ground02) + w(Ground03)；层数不足时安全归零（GroundPass 未启用等场景）。
            int amW = data.alphamapWidth;
            int amH = data.alphamapHeight;
            if (amW > 0 && amH > 0)
            {
                float[,,] am = data.GetAlphamaps(0, 0, amW, amH); // [height(y), width(x), layer]
                int layerCount = am.GetLength(2);
                for (int gy = 0; gy < InfluenceRes; gy++)
                for (int gx = 0; gx < InfluenceRes; gx++)
                {
                    int sx = Mathf.Clamp(Mathf.RoundToInt((float)gx / (InfluenceRes - 1) * (amW - 1)), 0, amW - 1);
                    int sy = Mathf.Clamp(Mathf.RoundToInt((float)gy / (InfluenceRes - 1) * (amH - 1)), 0, amH - 1);
                    float w = 0f;
                    if (layerCount > 1) w += am[sy, sx, 1];
                    if (layerCount > 2) w += am[sy, sx, 2];
                    model.Soil[gx, gy] = Mathf.Clamp01(w);
                }
            }

            Debug.Log($"[UnderstorySeeder] 环境模型：森林树 {forestTreeCount} 棵 → 影响场 {InfluenceRes}²；" +
                      $"alphamap {amW}×{amH}（{data.GetAlphamaps(0, 0, 1, 1).GetLength(2)} 层）。");
            return model;
        }

        /// <summary>树干基座簇锚点：中央树全部 + 森林树每 4 棵抽样（按 treeInstances 数组顺序，确定性）。</summary>
        private static List<Vector2> CollectTreeAnchors(TerrainData data, Transform terrainTransform)
        {
            var anchors = new List<Vector2>();
            TreeInstance[] trees = data.treeInstances;
            int forestOrdinal = 0;
            foreach (TreeInstance ti in trees)
            {
                bool isForest = ti.prototypeIndex >= ForestSeeder.CentralTreePrototypeCount;
                if (isForest && (forestOrdinal++ % 4 != 0))
                    continue;
                Vector3 world = terrainTransform.TransformPoint(new Vector3(
                    ti.position.x * data.size.x, 0f, ti.position.z * data.size.z));
                anchors.Add(new Vector2(world.x, world.z));
            }
            return anchors;
        }

        // ================================================================ 展示环（近林带可见性布局）
        //
        // 存在理由：实体道具（rock / log / stump）必须位于恐龙活动包络 + DinoSafetyBuffer 之外，而该环带正好是
        // ForestSeeder 的全密度林区 —— 道具被树冠与树干挡死，玩家在可达边界（WorldBounds.PlayerExplorationXZ，
        // 西/东/南 = 活动包络外扩 8m）看不到任何一块。因此把「展示用」道具集中到紧贴缓冲外侧的近林带
        // （12.5–20m），并让 ForestSeeder 按同一份布局在林缘让出「清空圆 + 指向草地的视线通道」，
        // 玩家站在边界正对缺口即可看见。
        //
        // 契约：BuildShowcaseRing() 是纯函数（只依赖固定常量与数学，不读 TerrainData），ForestSeeder（更早运行）
        // 与 UnderstorySeeder 各自调用得到逐字段相同的结果；InShowcaseClearance 只做几何判定，不消费随机数。

        /// <summary>展示环道具种类。</summary>
        internal enum ShowcaseKind
        {
            Rock = 0,
            Log = 1,
            Stump = 2
        }

        /// <summary>展示环中的一个道具位（字段跨 Bake 逐字段稳定）。</summary>
        internal struct ShowcaseSpot
        {
            public Vector2 Center;      // 世界 XZ
            public ShowcaseKind Kind;
            public float AzimuthDeg;    // 自正北 +z 顺时针（倒木据此横置 = 正面朝向草地）
            public float FlatDistance;  // 到平坦矩形的距离（米，恒 ≥ ClusterCenterMinDistance）
        }

        /// <summary>近林带外沿（距平坦矩形，米）：展示道具一律落在 [ClusterCenterMinDistance, 此值]。</summary>
        private const float ShowcaseBandOuter = 20f;

        /// <summary>近林带外沿的对外只读视图（供 JurassicProjectSetup.ValidateProject 断言，避免常量折叠）。</summary>
        internal static float ShowcaseBandOuterMeters => ShowcaseBandOuter;

        /// <summary>展示道具周围的清空半径（米）：此圆内的森林树被让开，道具不被树冠吞没。</summary>
        internal const float ShowcaseClearRadius = 7f;

        /// <summary>视线通道半宽（米）：从道具指向平坦矩形的通道，保证从可行走区能望见。</summary>
        internal const float ShowcaseLaneHalfWidth = 3.5f;

        /// <summary>视线通道向矩形内侧额外延伸的长度（米）：覆盖 x∈[65,70] 的第一排树，玩家站在边界即可直视。</summary>
        private const float ShowcaseLaneInwardExtension = 8f;

        /// <summary>
        /// 展示位距地形边界的最小距离（米）= `ForestSeeder.WallInset - 2`：边界密林墙（WallInset 内强制 0.92
        /// 密度以遮挡地平线）里放展示道具等于白放；而 ForestSeeder 的清空判定同样只在 WallInset 外侧生效，
        /// 贴着墙沿的位最多损失不足 2m 通道，视线基本通透。北侧（平坦矩形 z 上界 90 → 地形边 100）只有 10m
        /// 可用，本约束下天然没有展示位 —— 该方向几何上放不下（缓冲 12.5m > 10m）。
        /// </summary>
        private const float ShowcaseBorderMargin = ForestSeeder.WallInset - 2f;

        private const int ShowcaseLogCount = 3;
        private const int ShowcaseStumpCount = 2;

        private static List<ShowcaseSpot> showcaseRingCache;

        /// <summary>展示环布局（进程内缓存；纯常量派生，跨 Bake 稳定）。</summary>
        internal static List<ShowcaseSpot> BuildShowcaseRing()
        {
            if (showcaseRingCache != null)
                return showcaseRingCache;

            var random = new System.Random(UnderstoryRandomSeed);
            var spots = new List<ShowcaseSpot>();
            Vector2 flatCenter = FlatRectCenterWorld();

            // (a) 岩石簇心：360° 扇区（扇区中心 ± 15° jitter），距平坦矩形 12.5–20m
            for (int i = 0; i < HeroRockCount; i++)
            {
                float azimuth = i * (360f / HeroRockCount) + ((float)random.NextDouble() * 2f - 1f) * 15f;
                float targetDist = ClusterCenterMinDistance
                                 + (float)random.NextDouble() * (ShowcaseBandOuter - ClusterCenterMinDistance);
                AddShowcaseSpot(spots, flatCenter, azimuth, targetDist, ShowcaseKind.Rock, RockClusterMinGap);
            }

            // (b) 倒木 / 树桩：错开岩石扇区（+18° 起步、按 120°/180° 间隔），与已有位保持 min-gap
            for (int i = 0; i < ShowcaseLogCount; i++)
            {
                float azimuth = 18f + i * (360f / ShowcaseLogCount) + ((float)random.NextDouble() * 2f - 1f) * 10f;
                float targetDist = ClusterCenterMinDistance + 1f + (float)random.NextDouble() * 4f;
                AddShowcaseSpot(spots, flatCenter, azimuth, targetDist, ShowcaseKind.Log, RockClusterMinGap);
            }
            for (int i = 0; i < ShowcaseStumpCount; i++)
            {
                float azimuth = 54f + i * (360f / ShowcaseStumpCount) + ((float)random.NextDouble() * 2f - 1f) * 10f;
                float targetDist = ClusterCenterMinDistance + 2f + (float)random.NextDouble() * 4f;
                AddShowcaseSpot(spots, flatCenter, azimuth, targetDist, ShowcaseKind.Stump, RockClusterMinGap);
            }

            showcaseRingCache = spots;
            return spots;
        }

        /// <summary>沿 azimuth 求「距平坦矩形 = targetDist」的点，通过北侧窄带 / min-gap 检验后入列。</summary>
        private static void AddShowcaseSpot(List<ShowcaseSpot> spots, Vector2 flatCenter, float azimuth,
            float targetDist, ShowcaseKind kind, float minGap)
        {
            Vector2 dir = new Vector2(Mathf.Sin(azimuth * Mathf.Deg2Rad), Mathf.Cos(azimuth * Mathf.Deg2Rad));
            Vector2 center = ClampToTerrain(PointAtFlatDistance(flatCenter, dir, targetDist), 3f);
            float flatDistance = DistanceToDinosaurActivity(center.x, center.y);

            // 北侧窄带（平坦矩形 z 上界 90 → 地形边 100）放不下时跳过该方位
            if (flatDistance < ClusterCenterMinDistance)
                return;

            // 边界密林墙（ForestSeeder 在此强制 0.92 密度以遮挡地平线）内不放展示道具：放进去等于白放
            float borderDistance = Mathf.Min(TerrainSize / 2f - Mathf.Abs(center.x), TerrainSize / 2f - Mathf.Abs(center.y));
            if (borderDistance < ShowcaseBorderMargin)
                return;

            foreach (ShowcaseSpot existing in spots)
                if (Vector2.Distance(existing.Center, center) < minGap)
                    return;

            spots.Add(new ShowcaseSpot
            {
                Center = center,
                Kind = kind,
                AzimuthDeg = azimuth,
                FlatDistance = flatDistance
            });
        }

        /// <summary>
        /// 点是否落在某个展示位的「清空圆 ∩ 视线通道」内（供 ForestSeeder 让开森林树）。
        /// 纯几何判定：不消费随机数、不读 TerrainData。
        /// </summary>
        internal static bool InShowcaseClearance(Vector2 p)
        {
            foreach (ShowcaseSpot spot in BuildShowcaseRing())
            {
                if (Vector2.Distance(p, spot.Center) <= ShowcaseClearRadius)
                    return true;
                if (DistanceToSegment(p, spot.Center, ShowcaseLaneEnd(spot)) < ShowcaseLaneHalfWidth)
                    return true;
            }
            return false;
        }

        /// <summary>视线通道终点：平坦矩形上距该展示位最近的点，再向矩形内侧延伸 ShowcaseLaneInwardExtension。</summary>
        private static Vector2 ShowcaseLaneEnd(ShowcaseSpot spot)
        {
            GetFlatRectWorld(out float minX, out float maxX, out float minZ, out float maxZ);
            var nearest = new Vector2(Mathf.Clamp(spot.Center.x, minX, maxX), Mathf.Clamp(spot.Center.y, minZ, maxZ));
            Vector2 inward = nearest - spot.Center;
            float length = inward.magnitude;
            if (length < 1e-3f)
                return nearest;
            return nearest + inward / length * ShowcaseLaneInwardExtension;
        }

        /// <summary>点到线段的最短距离（米）。</summary>
        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denom = ab.sqrMagnitude;
            if (denom < 1e-6f)
                return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>展示环日志：构成与距平坦矩形区间（可见性取证）。</summary>
        private static void LogShowcaseRing()
        {
            List<ShowcaseSpot> spots = BuildShowcaseRing();
            if (spots.Count == 0)
            {
                Debug.LogWarning("[UnderstorySeeder] 展示环为空（北侧窄带放不下？）——近林带将没有可见道具。");
                return;
            }

            int rocks = 0, logs = 0, stumps = 0;
            float minDist = float.MaxValue, maxDist = 0f;
            foreach (ShowcaseSpot spot in spots)
            {
                if (spot.Kind == ShowcaseKind.Rock) rocks++;
                else if (spot.Kind == ShowcaseKind.Log) logs++;
                else stumps++;
                minDist = Mathf.Min(minDist, spot.FlatDistance);
                maxDist = Mathf.Max(maxDist, spot.FlatDistance);
            }

            Debug.Log($"[UnderstorySeeder] 展示环：{spots.Count} 个近林带道具位（岩石 {rocks} / 倒木 {logs} / 树桩 {stumps}），" +
                      $"距平坦矩形 {minDist:F1}–{maxDist:F1}m（目标 {ClusterCenterMinDistance:F1}–{ShowcaseBandOuter:F1}m）；" +
                      $"清空半径 {ShowcaseClearRadius:F1}m + 视线通道半宽 {ShowcaseLaneHalfWidth:F1}m（ForestSeeder 按同一布局让开树）。");
        }

        // ================================================================ 岩石簇分布（与资产门控解耦：无论是否有 prefab 都确定性执行）

        private struct RockMember
        {
            public Vector2 Offset; // 相对簇心的水平偏移（米）
            public int Tier;       // 0=小石 1=普通锚石 2=hero 锚石
        }

        private sealed class RockCluster
        {
            public Vector2 Center;
            public bool IsHero;
            public readonly List<RockMember> Members = new List<RockMember>();
        }

        /// <summary>
        /// 生成岩石簇分布（纯确定性；仅使用固定 RNG + 几何查询，不依赖任何资产）：
        /// (a) hero 锚石 = 展示环岩石位（近林带，距矩形 12.5–20m；ForestSeeder 按同一布局让开森林树 → 玩家可见）；
        /// (b) 其余簇：加权拒绝采样（Poisson min-gap）：近林带优先、林缘/坡脚高、远林低、通道内显著降低。
        /// 所有簇心距平坦矩形 ≥ ClusterCenterMinDistance（含成员偏移后仍 ≥ buffer + extra）。
        /// </summary>
        private static List<RockCluster> GenerateRockClusters(TerrainData data)
        {
            var random = new System.Random(UnderstoryRandomSeed);
            var clusters = new List<RockCluster>();

            foreach (ShowcaseSpot spot in BuildShowcaseRing())
            {
                if (spot.Kind != ShowcaseKind.Rock)
                    continue;
                clusters.Add(BuildClusterMembers(random, spot.Center, true));
            }

            // (b) 其余簇：加权拒绝采样
            for (int attempt = 0; attempt < RockPlacementAttempts && clusters.Count < RockClusterCount; attempt++)
            {
                float wx = (float)(random.NextDouble() * 194.0 - 97.0);
                float wz = (float)(random.NextDouble() * 194.0 - 97.0);
                float flatDist = DistanceToDinosaurActivity(wx, wz);

                if (flatDist < ClusterCenterMinDistance)
                    continue;

                bool tooClose = false;
                foreach (RockCluster c in clusters)
                    if (Vector2.Distance(c.Center, new Vector2(wx, wz)) < RockClusterMinGap) { tooClose = true; break; }
                if (tooClose)
                    continue;

                float coreDist = ForestSeeder.DistanceToCoreRect(wx, wz);
                float weight = 0.35f;
                weight += 0.45f * Mathf.Clamp01(1f - coreDist / 30f);         // 林缘高
                weight += 0.30f * FootWeight(data, wx, wz);                   // 坡脚高
                weight *= NearBandWeight(flatDist);                           // 近林带优先（可见性）
                if (coreDist > 60f) weight *= 0.45f;                          // 远林低
                if (ChannelMask01(wx, wz) > 0f) weight *= 0.25f;              // 通道内显著降低
                if (random.NextDouble() >= weight)
                    continue;

                clusters.Add(BuildClusterMembers(random, new Vector2(wx, wz), false));
            }

            return clusters;
        }

        /// <summary>簇成员：1 锚石（hero/普通按簇）+ 1..4 小石（近距散布，偏移 ≤ MaxMemberOffset）。</summary>
        private static RockCluster BuildClusterMembers(System.Random random, Vector2 center, bool isHero)
        {
            var cluster = new RockCluster { Center = center, IsHero = isHero };
            int memberCount = random.Next(RocksPerClusterMin, RocksPerClusterMax + 1); // 2..5
            cluster.Members.Add(new RockMember { Offset = Vector2.zero, Tier = isHero ? 2 : 1 });
            for (int i = 1; i < memberCount; i++)
            {
                float a = (float)random.NextDouble() * Mathf.PI * 2f;
                float r = 0.8f + (float)random.NextDouble() * 2.4f; // 0.8..3.2m ≤ MaxMemberOffset
                cluster.Members.Add(new RockMember
                {
                    Offset = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r),
                    Tier = 0
                });
            }
            return cluster;
        }

        /// <summary>沿 dir 从平坦矩形中心（内部）射线，二分求「距矩形 = targetDist」的点。</summary>
        private static Vector2 PointAtFlatDistance(Vector2 origin, Vector2 dir, float targetDist)
        {
            float lo = 0f, hi = 145f; // 中心到地形角的最大距离 ≈ 141m
            for (int i = 0; i < 30; i++)
            {
                float mid = (lo + hi) * 0.5f;
                Vector2 p = origin + dir * mid;
                if (DistanceToDinosaurActivity(p.x, p.y) < targetDist)
                    lo = mid;
                else
                    hi = mid;
            }
            return origin + dir * ((lo + hi) * 0.5f);
        }

        /// <summary>坡脚权重：自身近平地且邻域坡度上升时升高（四点采样，纯函数）。</summary>
        private static float FootWeight(TerrainData data, float wx, float wz)
        {
            float nx = Mathf.Clamp01((wx + TerrainSize / 2f) / TerrainSize);
            float nz = Mathf.Clamp01((wz + TerrainSize / 2f) / TerrainSize);
            float s0 = data.GetSteepness(nx, nz);
            const float delta = 0.0125f; // ≈2.5m 邻域
            float sN = Mathf.Max(
                Mathf.Max(data.GetSteepness(Mathf.Clamp01(nx + delta), nz), data.GetSteepness(Mathf.Clamp01(nx - delta), nz)),
                Mathf.Max(data.GetSteepness(nx, Mathf.Clamp01(nz + delta)), data.GetSteepness(nx, Mathf.Clamp01(nz - delta))));
            if (s0 > 0.15f)
                return 0f;
            return Mathf.Clamp01((sN - s0) / 0.10f);
        }

        /// <summary>
        /// 近林带权重：12.5–26m（可达边界外 12.5m 起、可见）小幅加权，26–45m 线性衰减到 0.30。
        /// 平坦矩形占据地形中心，可接受候选本身只落在 ≤≈30m 的窄环内，故此处只做"靠外缘少放"的层次修正。
        /// </summary>
        private static float NearBandWeight(float flatDistance)
        {
            const float near = 26f, far = 45f;
            if (flatDistance <= near)
                return 1.15f;
            return Mathf.Lerp(1.15f, 0.30f, Mathf.Clamp01((flatDistance - near) / (far - near)));
        }

        // ================================================================ Boost 网格（邻域增强）

        private static void StampTrunkBoosts(EnvironmentModel model, List<Vector2> anchors)
        {
            // 树干基座仅增强草（不加 bush）：满足「buffer 内只允许 grass detail 等低矮非碰撞植被」。
            foreach (Vector2 anchor in anchors)
                StampGrid(model.GrassBoost, anchor, 2.2f, 1f);
        }

        private static void StampRockBoosts(EnvironmentModel model, List<RockCluster> clusters)
        {
            // 岩石簇邻域：草 +1、bush +0.9（局部增强，不破坏分区硬钳制）
            foreach (RockCluster cluster in clusters)
            {
                StampGrid(model.GrassBoost, cluster.Center, 4f, 1f);
                StampGrid(model.BushBoost, cluster.Center, 4f, 0.9f);
            }
        }

        private static void StampGrid(float[,] grid, Vector2 center, float radius, float amplitude)
        {
            int minX = WorldToInfluenceIndex(center.x - radius);
            int maxX = WorldToInfluenceIndex(center.x + radius);
            int minZ = WorldToInfluenceIndex(center.y - radius);
            int maxZ = WorldToInfluenceIndex(center.y + radius);
            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                float wx = InfluenceIndexToWorld(x);
                float wz = InfluenceIndexToWorld(z);
                float d = Vector2.Distance(new Vector2(wx, wz), center);
                if (d > radius)
                    continue;
                float w = amplitude * (1f - d / radius);
                if (w > grid[x, z])
                    grid[x, z] = w; // max 语义：多锚点重叠不爆值
            }
        }

        // ================================================================ detail 层签名识别

        /// <summary>bush 层集合（升序索引）：prototype 引用 ∈ NSk2 名称含 "bush" 的 detail mesh 集合。</summary>
        private static List<int> FindBushDetailLayerIndicesInternal(TerrainData data)
        {
            var bushes = new HashSet<GameObject>(BushPrefabs());
            var result = new List<int>();
            DetailPrototype[] protos = data.detailPrototypes ?? Array.Empty<DetailPrototype>();
            for (int i = 0; i < protos.Length; i++)
            {
                if (protos[i] == null || !protos[i].usePrototypeMesh || protos[i].prototype == null)
                    continue;
                if (bushes.Contains(protos[i].prototype))
                    result.Add(i);
            }
            return result;
        }

        /// <summary>bush 层索引（供 ValidateProject 做「buffer 内 bush 密度恒 0」抽样断言）。</summary>
        internal static int[] FindBushDetailLayerIndices(TerrainData data)
        {
            return FindBushDetailLayerIndicesInternal(data).ToArray();
        }

        /// <summary>fern 层索引（供 ValidateProject 做「R0 空地内 fern 密度恒 0」抽样断言）。</summary>
        internal static int[] FindFernDetailLayerIndices(TerrainData data)
        {
            var result = new List<int>();
            DetailPrototype[] protos = data.detailPrototypes ?? Array.Empty<DetailPrototype>();
            for (int i = 0; i < protos.Length; i++)
                if (IsOwnedFernProto(protos[i]))
                    result.Add(i);
            return result.ToArray();
        }

        /// <summary>grass01 层索引（billboard 层，GroundPass 所有权键 = grass01 纹理引用）；未找到返回 -1。</summary>
        private static int FindGrass01LayerIndex(TerrainData data)
        {
            Texture2D grass01 = NatureStarterKitLocator.FindGroundTexture("grass01");
            if (grass01 == null)
                return -1;
            DetailPrototype[] protos = data.detailPrototypes ?? Array.Empty<DetailPrototype>();
            for (int i = 0; i < protos.Length; i++)
                if (protos[i] != null && protos[i].prototypeTexture == grass01)
                    return i;
            return -1;
        }

        /// <summary>NSk2 detail mesh 中名称含 "bush" 的 prefab（名称 Ordinal 升序 → 稳定索引契约）。</summary>
        private static GameObject[] BushPrefabs()
        {
            return NatureStarterKitLocator.FindDetailMeshes()
                .Where(p => p != null && p.name.IndexOf("bush", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(p => p.name, StringComparer.Ordinal)
                .ToArray();
        }

        // ================================================================ 密度场（单次 512² 循环计算全部层）

        /// <summary>
        /// 统一密度场计算（纯函数 + 固定数据，无 RNG）：
        /// 单次 512² 循环同时求 bush 与 grass 两个连续场（共享环境模型求值），
        /// 再逐层量化（bush 6 层按份额 + 逐层 hash 抖动打散；grass 单层含 spawn 减半）后幂等写回。
        /// bush 终态硬钳制：到平坦矩形距离 < DinoSafetyBuffer 的格密度恒 0（几何安全不变量的 Data 侧保证）。
        /// </summary>
        private static void PaintDetailLayers(TerrainData data, EnvironmentModel model, List<int> bushLayers, int grassLayer)
        {
            int res = data.detailResolution;
            if (res <= 0)
            {
                Debug.LogWarning("[UnderstorySeeder] detailResolution 非法，跳过密度绘制。");
                return;
            }
            if (bushLayers.Count == 0 && grassLayer < 0)
            {
                Debug.LogWarning("[UnderstorySeeder] 未识别到任何 bush / grass01 detail 层，跳过密度绘制。");
                return;
            }

            var bushField = new float[res, res];
            var grassField = new float[res, res];
            var nearSpawn = new bool[res, res];
            int bushActiveCells = 0, grassActiveCells = 0;

            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float wx = (float)x / res * TerrainSize - TerrainSize / 2f;
                float wz = (float)z / res * TerrainSize - TerrainSize / 2f;

                float sdAct = SignedDistanceToDinosaurActivity(wx, wz);
                float dAct = Mathf.Max(sdAct, 0f); // 无符号距离（与旧 dFlat 逐位相同：两矩形数值一致）
                float coreDist = ForestSeeder.DistanceToCoreRect(wx, wz);
                float treeNear = SampleGrid(model.TreeNear, wx, wz);
                float soil = SampleGrid(model.Soil, wx, wz);
                float channel = ChannelMask01(wx, wz);

                // ---- bush：视觉梯度门（空地 0 → 活动包络外缘稀疏 → 林缘饱和）→ 团块化 ----
                // 门取代旧的「buffer 硬禁 + ramp」两步：不再以地形平坦区为安全边界，只以 R0 空地为禁止域。
                float lowVegGate = LowVegGate01(sdAct);
                bool inEdgeZone = coreDist < ForestEdgeDepth;
                float clumpNoise = inEdgeZone
                    ? Mathf.PerlinNoise(wx * 0.13f + 31.7f, wz * 0.13f + 12.9f)  // 林缘：高频（≈8m 特征）
                    : Mathf.PerlinNoise(wx * 0.06f + 7.3f, wz * 0.06f + 53.1f);  // 林内：低频斑块（≈17m 特征）
                float clump = Mathf.Clamp01(clumpNoise * 1.9f - 0.45f);
                float baseBush = inEdgeZone
                    ? Mathf.Lerp(EdgeBushMin, EdgeBushMax, clump)
                    : Mathf.Lerp(ForestBushMin, ForestBushMax, clump);
                float borderDist = Mathf.Min(TerrainSize / 2f - Mathf.Abs(wx), TerrainSize / 2f - Mathf.Abs(wz));
                if (borderDist < WallInset)
                    baseBush = Mathf.Max(baseBush, WallBushFloor); // 地形边缘密林墙
                float b = baseBush * lowVegGate;
                b *= 1f - TrailVegetationReduction * channel;
                b *= 1f - MudVegetationReduction * soil;
                b += SampleGrid(model.BushBoost, wx, wz);
                b += (Hash01(x, z, UnderstoryRandomSeed + 7) - 0.5f) * 1.1f;
                bushField[x, z] = b;
                if (b >= 1f)
                    bushActiveCells++;

                // ---- grass：平坦矩形内连续 2-4；外缘斑驳 1-3（带缝隙）；林内 0-2（树冠下减少）----
                // 注意：此处必须用【无符号】距离 —— 若改用带符号距离，矩形内变负会让 Max(...) 结果偏高而误改草层。
                float patch = Mathf.PerlinNoise(wx * 0.10f + 15.7f, wz * 0.10f + 23.3f);  // 斑块（≈10m）
                float micro = Mathf.PerlinNoise(wx * 0.33f + 41.2f, wz * 0.33f + 8.6f);  // 细碎缝隙（≈3m）
                float openness = dAct <= 0.001f
                    ? 0f
                    : Mathf.Clamp01(Mathf.Max(dAct / EdgeGrassBand, treeNear));
                float g = Mathf.Lerp(2.6f + 1.4f * patch, 0.7f, openness);
                if (dAct > 0.001f)
                {
                    float gap = Smoothstep(Mathf.Clamp01((micro - 0.34f) / 0.22f));
                    g *= Mathf.Lerp(0.22f, 1f, gap); // 林缘/林内带缝隙
                }
                g *= 1f - MudVegetationReduction * soil;
                g *= 1f - TrailVegetationReduction * channel;
                g += SampleGrid(model.GrassBoost, wx, wz);
                g += (Hash01(x, z, UnderstoryRandomSeed + 11) - 0.5f) * 1.1f;
                grassField[x, z] = g;
                if (g >= 0.5f)
                    grassActiveCells++;

                // spawn 邻域减半标记（量化时应用：gi = Max(1, gi/2)，与 SeedDetails 契约一致）
                foreach (Vector2 sp in JurassicWorldSceneBuilder.DinosaurSpawnPoints)
                {
                    float dx = sp.x - wx, dz = sp.y - wz;
                    if (dx * dx + dz * dz < SpawnHalfRadius * SpawnHalfRadius)
                    {
                        nearSpawn[x, z] = true;
                        break;
                    }
                }
            }

            // ---- bush 逐层量化 + 幂等写回 ----
            int bushChanged = 0;
            for (int k = 0; k < bushLayers.Count; k++)
            {
                int layerIdx = bushLayers[k];
                int[,] density = new int[res, res];
                float share = k < BushLayerShares.Length ? BushLayerShares[k] : 0.7f;
                for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float wx = (float)x / res * TerrainSize - TerrainSize / 2f;
                    float wz = (float)z / res * TerrainSize - TerrainSize / 2f;
                    float v = bushField[x, z] * share
                              * (0.75f + 0.5f * Hash01(x, z, UnderstoryRandomSeed + 100 + layerIdx));
                    if (SignedDistanceToDinosaurActivity(wx, wz) <= -WorldBounds.ClearingInset)
                        v = 0f; // 终态硬钳制：R0 开阔空地内 bush 密度恒 0
                                //（门已乘过一次，此处防 boost 网格 / hash 抖动在空地内越界加值）
                    density[x, z] = Mathf.Clamp(Mathf.RoundToInt(v), 0, BushMaxDensity);
                }
                int diff = WriteLayerIfChanged(data, layerIdx, density);
                if (diff > 0)
                {
                    bushChanged++;
                    Debug.Log($"[UnderstorySeeder] bush 层 idx={layerIdx} 密度写回（{diff} 格变化，share={share:F2}）。");
                }
            }

            // ---- grass01 量化 + 幂等写回 ----
            bool grassChanged = false;
            if (grassLayer >= 0)
            {
                int[,] density = new int[res, res];
                for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float g = grassField[x, z];
                    int gi = g <= 0.05f ? 0 : Mathf.Clamp(Mathf.RoundToInt(g), 0, GrassMaxDensity);
                    if (gi > 0 && nearSpawn[x, z])
                        gi = Mathf.Max(1, gi / 2);
                    density[x, z] = gi;
                }
                grassChanged = WriteLayerIfChanged(data, grassLayer, density) > 0;
            }
            else
            {
                Debug.LogWarning("[UnderstorySeeder] 未识别到 grass01 层（GroundPass 未启用？），跳过草层密度绘制。");
            }

            Debug.Log($"[UnderstorySeeder] 密度场（{res}×{res}）：bush 活跃格 {bushActiveCells}（{bushLayers.Count} 层，写回 {bushChanged} / 跳过 {bushLayers.Count - bushChanged}）；" +
                      $"grass 活跃格 {grassActiveCells}（层={(grassLayer >= 0 ? grassLayer.ToString() : "无")}，{(grassChanged ? "写回" : "跳过")}）。");
        }

        /// <summary>bush 6 层物种份额（按 bush 层升序）：层间密度差异化，避免 6 层完全同图案。</summary>
        private static readonly float[] BushLayerShares = { 1.0f, 0.88f, 0.80f, 0.68f, 0.52f, 0.40f };

        /// <summary>
        /// 逐层幂等写回：与 GetDetailLayer 现值逐格比对，一致即跳过（返回 0）；
        /// 有差异才 SetDetailLayer 并返回变化格数；分辨率不匹配返回 -1。
        /// </summary>
        private static int WriteLayerIfChanged(TerrainData data, int layerIdx, int[,] target)
        {
            int res = data.detailResolution;
            if (target.GetLength(0) != res || target.GetLength(1) != res)
            {
                Debug.LogWarning($"[UnderstorySeeder] 层 idx={layerIdx} 目标分辨率 {target.GetLength(0)} 与当前 {res} 不符，跳过写回。");
                return -1;
            }

            int[,] current = data.GetDetailLayer(0, 0, res, res, layerIdx);
            int diff = 0;
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
                if (current[x, z] != target[x, z])
                    diff++;

            if (diff == 0)
                return 0;

            data.SetDetailLayer(0, 0, layerIdx, target);
            return diff;
        }

        // ================================================================ 岩石簇实例化（资产门控）

        /// <summary>
        /// 实例化岩石簇。门控：FindRockPrefabs() 为空且非调试模式 → 安全跳过 + 日志（分布已在
        /// GenerateRockClusters 中确定性算毕，仅供植被邻域增强与未来实例化）；
        /// DebugProceduralRocksOn=true 时用程序化 Sphere + RockNatural.mat 仅验证分布（日志打 [DEBUG ONLY]，
        /// ValidateProject 会对此告警）。外观随机流（UnderstoryRandomSeed + 2）只在实例化时消费：
        /// 资产出现不扰动其他子系统。
        /// </summary>
        private static int InstantiateRocks(TerrainData data, Transform terrainTransform, GameObject parent, List<RockCluster> clusters)
        {
            GameObject[] rockPrefabs = UnderstoryAssetLocator.FindRockPrefabs();
            bool debug = DebugProceduralRocksOn;

            if (rockPrefabs.Length == 0 && !debug)
            {
                Debug.Log($"[UnderstorySeeder] 岩石接口 no-op：{UnderstoryAssetLocator.RockFolder} 无合格 prefab" +
                          $"（目录不存在或为空；cluster 分布已按固定种子算毕 {clusters.Count} 个，仅用于植被邻域增强；" +
                          "放入 rock prefab 后同一分布自动实例化；分布验证可临时置 DebugProceduralRocks=true）。");
                return 0;
            }

            Material debugMat = debug ? AssetDatabase.LoadAssetAtPath<Material>(RockMaterialPath) : null;
            var random = new System.Random(UnderstoryRandomSeed + 2);
            int placed = 0;
            int skippedInsideBuffer = 0;

            // Preview 前缀：只实例化前 PreviewBudget 个簇（hero 簇位于列表最前，故预览仍含 hero 岩石）。
            int clusterBudget = PreviewBudget(clusters.Count);
            for (int ci = 0; ci < clusters.Count && ci < clusterBudget; ci++)
            {
                RockCluster cluster = clusters[ci];
                foreach (RockMember member in cluster.Members)
                {
                    float wx = cluster.Center.x + member.Offset.x;
                    float wz = cluster.Center.y + member.Offset.y;

                    // 防御性成员级检查：簇心余量已保证不触发；保留以避免未来常量下调破坏安全不变量
                    if (DistanceToDinosaurActivity(wx, wz) < DinoSafetyBuffer + PlacementExtraMargin)
                    {
                        skippedInsideBuffer++;
                        continue;
                    }

                    float nx = (wx + TerrainSize / 2f) / TerrainSize;
                    float nz = (wz + TerrainSize / 2f) / TerrainSize;

                    float yaw = (float)random.NextDouble() * 360f;
                    float tiltX = (float)random.NextDouble() * 12f - 6f;
                    float tiltZ = (float)random.NextDouble() * 10f - 5f;
                    float scale = member.Tier == 2 ? 1.6f + (float)random.NextDouble() * 0.6f
                                : member.Tier == 1 ? 1.0f + (float)random.NextDouble() * 0.4f
                                : 0.55f + (float)random.NextDouble() * 0.40f; // 小石下限 0.35→0.55：15m 外原来几乎不可见
                    float sink = Mathf.Lerp(RockSinkRatioMin, RockSinkRatioMax, (float)random.NextDouble()) * scale * 0.5f;

                    GameObject rock;
                    string tierTag;
                    if (debug)
                    {
                        rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        rock.transform.SetParent(parent.transform);
                        rock.transform.localScale = new Vector3(2.4f, 1.0f, 1.9f) * scale;
                        if (debugMat != null)
                            rock.GetComponent<Renderer>().sharedMaterial = debugMat;
                        tierTag = "Debug";
                    }
                    else
                    {
                        GameObject prefab = rockPrefabs[(int)(random.NextDouble() * rockPrefabs.Length)];
                        rock = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
                        rock.transform.localScale = Vector3.one * scale;
                        tierTag = member.Tier == 2 ? "Hero" : member.Tier == 1 ? "Anchor" : "Small";
                    }
                    rock.name = $"Rock_{ci:D2}_{tierTag}";

                    float groundY = terrainTransform.position.y + data.GetInterpolatedHeight(nx, nz);
                    rock.transform.position = new Vector3(wx, groundY - sink, wz);
                    rock.transform.rotation = Quaternion.Euler(tiltX, yaw, tiltZ);

                    if (!(AddColliderForLargeRocks && member.Tier == 2))
                    {
                        // 默认零 Collider：石头是纯视觉道具（碰撞语义不依赖 PhysX）
                        foreach (Collider col in rock.GetComponentsInChildren<Collider>(true))
                            UnityEngine.Object.DestroyImmediate(col);
                    }

                    GameObjectUtility.SetStaticEditorFlags(rock, StaticEditorFlags.BatchingStatic);
                    placed++;
                }
            }

            if (debug)
                Debug.LogWarning($"[UnderstorySeeder] [DEBUG ONLY] 程序化 Sphere 岩石 {placed} 个已生成（仅限分布验证；" +
                                 "确认后请恢复 DebugProceduralRocks=false 并重 Bake；ValidateProject 会对此告警）。");
            else
                Debug.Log($"[UnderstorySeeder] 岩石簇实例化：{(clusterBudget < clusters.Count ? $"{clusterBudget}/{clusters.Count}" : clusters.Count.ToString())} 簇 / {placed} 个（{rockPrefabs.Length} 种 prefab）" +
                          (skippedInsideBuffer > 0 ? $"，{skippedInsideBuffer} 个成员因缓冲越界跳过" : "") + "。");
            return placed;
        }

        // ================================================================ Hero 灌木（bush01-06，cluster 化）

        /// <summary>
        /// 林缘灌木主角：bush01-06 加权抽取，16 簇 × 2-3 个（≈36 个）；
        /// 簇心加权拒绝采样（偏好岩石簇 / 树干基座 / 林缘，Poisson min-gap 8m），
        /// 全部位于平坦矩形 + ClusterCenterMinDistance 之外（几何隔离，不修改 DinosaurSpawnRandomizer）；
        /// 1.0-2.2 缩放、随机朝向、静态合批、零 Collider。
        /// </summary>
        private static int InstantiateHeroBushes(TerrainData data, Transform terrainTransform, GameObject parent,
            List<RockCluster> rockClusters, List<Vector2> trunkAnchors)
        {
            GameObject[] bushPrefabs = BushPrefabs();
            if (bushPrefabs.Length == 0)
            {
                Debug.LogWarning("[UnderstorySeeder] 未找到 bush01-06 prefab，跳过 hero 灌木。");
                return 0;
            }

            var random = new System.Random(UnderstoryRandomSeed + 1);
            var centers = new List<Vector2>();
            int attemptsCap = HeroBushClusterCount * 150;
            for (int attempt = 0; attempt < attemptsCap && centers.Count < HeroBushClusterCount; attempt++)
            {
                float wx = (float)(random.NextDouble() * 194.0 - 97.0);
                float wz = (float)(random.NextDouble() * 194.0 - 97.0);
                Vector2 candidate = new Vector2(wx, wz);

                if (DistanceToDinosaurActivity(wx, wz) < ClusterCenterMinDistance)
                    continue;

                bool tooClose = false;
                foreach (Vector2 c in centers)
                    if (Vector2.Distance(c, candidate) < HeroBushClusterMinGap) { tooClose = true; break; }
                if (!tooClose)
                {
                    foreach (RockCluster rc in rockClusters)
                        if (Vector2.Distance(rc.Center, candidate) < 4f) { tooClose = true; break; }
                }
                if (tooClose)
                    continue;

                float coreDist = ForestSeeder.DistanceToCoreRect(wx, wz);
                float weight = 0.20f;
                if (coreDist < 26f)
                    weight += 0.45f * (1f - coreDist / 26f);      // 林缘重点带
                float rockNear = NearestCenterDistance(candidate, rockClusters);
                if (rockNear < 14f)
                    weight += 0.35f * (1f - rockNear / 14f);      // 偏好岩石簇
                float trunkNear = NearestAnchorDistance(candidate, trunkAnchors);
                if (trunkNear < 7f)
                    weight += 0.25f * (1f - trunkNear / 7f);      // 偏好树干基座
                if (random.NextDouble() >= Mathf.Clamp01(weight))
                    continue;

                centers.Add(candidate);
            }

            int placed = 0;
            for (int ci = 0; ci < centers.Count; ci++)
            {
                int perCluster = random.Next(HeroBushPerClusterMin, HeroBushPerClusterMax + 1);
                for (int j = 0; j < perCluster; j++)
                {
                    float a = (float)random.NextDouble() * Mathf.PI * 2f;
                    float r = (float)random.NextDouble() * 3.5f; // 簇内近距散布（≤ MaxMemberOffset）
                    float wx = centers[ci].x + Mathf.Cos(a) * r;
                    float wz = centers[ci].y + Mathf.Sin(a) * r;
                    GameObject prefab = PickBushPrefab(random, bushPrefabs);
                    float scale = HeroBushScaleMin + (float)random.NextDouble() * (HeroBushScaleMax - HeroBushScaleMin);
                    float yaw = (float)random.NextDouble() * 360f;

                    // 簇内成员可能越入缓冲内侧：实体不变量优先，越界点放弃（RNG 已消费，保持确定性）
                    if (DistanceToDinosaurActivity(wx, wz) < DinoSafetyBuffer + PlacementExtraMargin)
                        continue;

                    float nx = (wx + TerrainSize / 2f) / TerrainSize;
                    float nz = (wz + TerrainSize / 2f) / TerrainSize;
                    float groundY = terrainTransform.position.y + data.GetInterpolatedHeight(nx, nz);

                    GameObject bush = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
                    bush.name = $"UnderstoryBush_C{ci:D2}_{j}";
                    bush.transform.position = new Vector3(wx, groundY, wz);
                    bush.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    bush.transform.localScale = Vector3.one * scale;

                    foreach (Collider col in bush.GetComponentsInChildren<Collider>(true))
                        UnityEngine.Object.DestroyImmediate(col);
                    GameObjectUtility.SetStaticEditorFlags(bush, StaticEditorFlags.BatchingStatic);
                    placed++;
                }
            }

            Debug.Log($"[UnderstorySeeder] hero 灌木：{centers.Count} 簇 / {placed} 个实例" +
                      $"（bush01-06 加权；全部位于平坦矩形+{DinoSafetyBuffer}m 外）。");
            return placed;
        }

        /// <summary>按名称升序的 bush prefab 加权抽取（bush01 体积最大 → 权重最高；权重表是随机流契约）。</summary>
        private static readonly int[] BushHeroWeights = { 30, 26, 20, 12, 7, 5 };

        private static GameObject PickBushPrefab(System.Random random, GameObject[] prefabs)
        {
            int sum = 0;
            for (int i = 0; i < prefabs.Length; i++)
                sum += i < BushHeroWeights.Length ? BushHeroWeights[i] : 7;
            int roll = random.Next(sum);
            for (int i = 0; i < prefabs.Length; i++)
            {
                roll -= i < BushHeroWeights.Length ? BushHeroWeights[i] : 7;
                if (roll < 0)
                    return prefabs[i];
            }
            return prefabs[prefabs.Length - 1];
        }

        private static float NearestCenterDistance(Vector2 p, List<RockCluster> clusters)
        {
            float best = float.MaxValue;
            foreach (RockCluster c in clusters)
            {
                float d = Vector2.Distance(p, c.Center);
                if (d < best)
                    best = d;
            }
            return best;
        }

        private static float NearestAnchorDistance(Vector2 p, List<Vector2> anchors)
        {
            float best = float.MaxValue;
            foreach (Vector2 a in anchors)
            {
                float d = Vector2.Distance(p, a);
                if (d < best)
                    best = d;
            }
            return best;
        }

        // ================================================================ Fern（资产门控接口）

        /// <summary>
        /// Fern 门控：Assets/Environment/Vegetation/Fern02/Prepared/Prefabs 无合格 prefab → no-op（仅日志提示目录）。
        /// 有资产 → 规范化尾随块（签名 = 资产路径在 Fern 目录下）：重建原型数组（非自有原样 + fern 尾随块），
        /// snapshot/restore 非自有层，然后按簇绘制密度（林内高密度簇 / 林缘混合 / mud 抑制 / 活动区+缓冲外）。
        /// 返回 fern 层数（0 = no-op）。
        /// </summary>
        private static int ApplyFernGate(TerrainData data, EnvironmentModel model)
        {
            GameObject[] fernPrefabs = UnderstoryAssetLocator.FindFernPrefabs();
            if (fernPrefabs.Length == 0)
            {
                Debug.Log($"[UnderstorySeeder] Fern 接口 no-op：{UnderstoryAssetLocator.FernFolder} 无合格 prefab" +
                          "（目录不存在或为空；放入 fern/plant prefab 后重 Bake 自动追加 mesh DetailPrototype 并按簇铺绘）。");
                return 0;
            }

            // 启用实例化（与 SeedDetails 对 detail mesh 的处理一致）
            foreach (GameObject prefab in fernPrefabs)
            {
                foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.sharedMaterial != null && !r.sharedMaterial.enableInstancing)
                    {
                        r.sharedMaterial.enableInstancing = true;
                        EditorUtility.SetDirty(r.sharedMaterial);
                    }
                }
            }

            int res = data.detailResolution;
            DetailPrototype[] protos = data.detailPrototypes ?? Array.Empty<DetailPrototype>();
            var fernIdx = new List<int>();
            for (int i = 0; i < protos.Length; i++)
                if (IsOwnedFernProto(protos[i]))
                    fernIdx.Add(i);

            // 规格比对："有没有"之外还要"规格对不对" —— renderMode/density 等字段只烘进 TerrainData，
            // 只改代码不重写原型时历史产物会静默沿用旧值（已踩过：Grass→VertexLit 修复在旧 TerrainData 上不生效）。
            bool specsMatch = fernIdx.Count == fernPrefabs.Length;
            if (specsMatch)
            {
                for (int i = 0; i < fernPrefabs.Length; i++)
                {
                    if (!FernPrototypeMatchesSpec(protos[fernIdx[i]], fernPrefabs[i]))
                    {
                        specsMatch = false;
                        break;
                    }
                }
            }

            int fernStart;
            if (specsMatch)
            {
                fernStart = fernIdx[0];
            }
            else
            {
                // snapshot 非自有层 → 重建数组（非自有原样 + fern 尾随块）→ restore 非自有层
                int[][] snapshot = new int[protos.Length][];
                for (int i = 0; i < protos.Length; i++)
                {
                    if (IsOwnedFernProto(protos[i])) continue;
                    snapshot[i] = FlattenLayer(data, res, i);
                }

                var kept = new List<DetailPrototype>();
                for (int i = 0; i < protos.Length; i++)
                {
                    if (IsOwnedFernProto(protos[i])) continue;
                    kept.Add(protos[i]);
                }
                int keptCount = kept.Count;
                foreach (GameObject fern in fernPrefabs)
                    kept.Add(BuildFernPrototype(fern));
                data.detailPrototypes = kept.ToArray(); // setter 可能清空 detail 数据

                int resAfter = data.detailResolution;
                if (resAfter != res)
                {
                    Debug.LogWarning($"[UnderstorySeeder] detailResolution 在 fern 追加后由 {res} 变为 {resAfter}，按新值 restore。");
                    res = resAfter;
                }

                // 恢复非自有层（新索引 = 原索引 - 位于其前的自有层数）
                for (int i = 0; i < protos.Length; i++)
                {
                    if (snapshot[i] == null) continue;
                    int removedBefore = 0;
                    foreach (int fi in fernIdx)
                        if (fi < i) removedBefore++;
                    UnflattenLayer(data, res, i - removedBefore, snapshot[i]);
                }

                fernStart = keptCount;
                Debug.Log($"[UnderstorySeeder] fern 原型块{(fernIdx.Count > 0 ? "重建（规格不符，已按目标 renderMode/密度重写）" : "追加")}于 " +
                          $"index={fernStart}（{fernPrefabs.Length} 个 prefab，renderMode={FernRenderModeTarget}）。");
            }

            // fernField[x,z] = 该 cell 的 Fern【总密度】（不是每层密度）。把这个整数总量确定性拆分给各 variant 层：
            // 逐 unit 用 Hash01 + 独立固定 salt 选 variant，故每个 cell 严格满足
            // densityA + densityB + ... == target，Σ(variant Σdensity) == Σ target 恒成立。
            // 关键性质：Fern 总数量只由 fernField 决定，【新增 fern prefab 不会线性抬高总植被密度】。
            // 不额外消费 System.Random，Fern cluster 的随机流不受影响。
            // 两个独立来源取 max：
            //  (1) 既有森林 fern（R3 林缘 / R4 密林）：+2 流，采样判据与分布**逐格不变**；
            //  (2) 新增内侧 fern（R1 活动包络外缘 / R2 玩家过渡带）：+4 流，见 BuildFernInnerBand。
            // fern 至此与「实体安全边界」彻底解耦：唯一禁止域是 R0 开阔空地（低矮无碰撞植被允许进入恐龙外缘）。
            float[,] fernField = BuildFernField(model, res);
            float[,] fernInnerField = BuildFernInnerBand(model, res);
            int variantCount = fernPrefabs.Length;
            var variantDensity = new int[variantCount][,];
            for (int i = 0; i < variantCount; i++)
                variantDensity[i] = new int[res, res];

            long totalTarget = 0;
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float wx = (float)x / res * TerrainSize - TerrainSize / 2f;
                float wz = (float)z / res * TerrainSize - TerrainSize / 2f;
                float v = Mathf.Max(fernField[x, z], fernInnerField[x, z]);
                if (SignedDistanceToDinosaurActivity(wx, wz) <= -WorldBounds.ClearingInset)
                    v = 0f; // 低矮 fern 的唯一禁止域 = R0 开阔空地（不再以实体 buffer 为界）
                int target = Mathf.Clamp(Mathf.RoundToInt(v), 0, FernPerClusterMax);
                if (target <= 0)
                    continue;

                totalTarget += target;
                for (int k = 0; k < target; k++)
                {
                    int variant = Mathf.Clamp(
                        Mathf.FloorToInt(Hash01(x, z, FernVariantSalt + k * 7919) * variantCount),
                        0, variantCount - 1);
                    variantDensity[variant][x, z]++;
                }
            }

            int changedLayers = 0;
            long layerSum = 0;
            LastFernVariantSums.Clear();
            for (int i = 0; i < variantCount; i++)
            {
                long variantSum = 0;
                for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    variantSum += variantDensity[i][x, z];
                layerSum += variantSum;
                LastFernVariantSums.Add(variantSum);
                if (WriteLayerIfChanged(data, fernStart + i, variantDensity[i]) > 0)
                    changedLayers++;
            }

            LastFernTotalTarget = totalTarget;
            LastFernAssigned = layerSum;
            LastFernVariantCount = variantCount;

            Debug.Log($"[UnderstorySeeder] fern 铺设：{variantCount} 层（层 {fernStart}..{fernStart + variantCount - 1}），" +
                      $"总目标密度 Σ={totalTarget}，各 variant Σ密度和={layerSum}" +
                      $"（守恒 {(layerSum == totalTarget ? "OK" : "FAILED")}），" +
                      $"逐层 Σdensity=[{string.Join(",", LastFernVariantSums)}]，" +
                      $"{(changedLayers > 0 ? $"写回 {changedLayers} 层" : "与现有一致跳过")}。");
            return fernPrefabs.Length;
        }

        /// <summary>按簇构建 fern 密度场：林内高密度簇 / 林缘混合，mud 抑制、避通道、缓冲外（独立流 +2）。</summary>
        private static float[,] BuildFernField(EnvironmentModel model, int res)
        {
            var field = new float[res, res];
            var random = new System.Random(UnderstoryRandomSeed + 2);
            int attemptsCap = FernClusterCount * 40;
            // Preview 前缀：只铺前 PreviewBudget 个簇——前 K 个簇的随机序列与生产序列前 K 项一致。
            int clusterBudget = PreviewBudget(FernClusterCount);
            int clustersPlaced = 0;
            for (int attempt = 0; attempt < attemptsCap && clustersPlaced < clusterBudget; attempt++)
            {
                float wx = (float)(random.NextDouble() * 196.0 - 98.0);
                float wz = (float)(random.NextDouble() * 196.0 - 98.0);

                if (DistanceToDinosaurActivity(wx, wz) < DinoSafetyBuffer)
                    continue;
                if (ChannelMask01(wx, wz) > 0f)
                    continue; // 林下蕨类同样避开视觉通道

                float coreDist = ForestSeeder.DistanceToCoreRect(wx, wz);
                float treeNear = SampleGrid(model.TreeNear, wx, wz);
                float soil = SampleGrid(model.Soil, wx, wz);
                float weight = 0.15f
                             + 0.60f * Mathf.Clamp01(treeNear * 1.2f)          // 林内高密度簇
                             + 0.25f * Mathf.Clamp01(1f - coreDist / 30f);     // 林缘混合
                weight *= 1f - MudVegetationReduction * soil;                  // mud 抑制
                if (random.NextDouble() >= weight)
                    continue;

                float perCluster = random.Next(FernPerClusterMin, FernPerClusterMax + 1);
                float radius = FernClusterRadiusMin + (float)random.NextDouble() * (FernClusterRadiusMax - FernClusterRadiusMin);
                StampFernCluster(field, res, wx, wz, radius, perCluster);
                clustersPlaced++;
            }

            Debug.Log($"[UnderstorySeeder] fern 簇分布：{clustersPlaced}/{(clusterBudget < FernClusterCount ? $"{clusterBudget}（Preview {PreviewScale}×）" : FernClusterCount.ToString())} 个簇（林内高密度 / 林缘混合 / mud 抑制）。");
            return field;
        }

        /// <summary>
        /// 内侧 fern 密度场（R1 活动包络外缘 + R2 玩家过渡带）—— 低矮无碰撞植被进入恐龙活动区外缘的载体。
        /// 独立固定流（FernInnerBandSalt = +4，与 +2 森林 fern 流、+1 hero 灌木、+3 deadwood 完全隔离）：
        /// 既有森林 fern 分布逐格不变。采样域 = 到活动包络的带符号距离 ∈ (-ClearingInset, FernInnerBandOuter]，
        /// 权重与单簇密度都 ∝ LowVegGate01（越靠外越密 = 连续梯度），避视觉通道、受 mud 抑制。
        /// </summary>
        private static float[,] BuildFernInnerBand(EnvironmentModel model, int res)
        {
            var field = new float[res, res];
            var random = new System.Random(FernInnerBandSalt);
            int attemptsCap = FernInnerClusterCount * 40;
            // Preview 前缀：只铺前 PreviewBudget 个簇——前 K 个簇的随机序列与生产序列前 K 项一致。
            int clusterBudget = PreviewBudget(FernInnerClusterCount);
            int clustersPlaced = 0;
            for (int attempt = 0; attempt < attemptsCap && clustersPlaced < clusterBudget; attempt++)
            {
                float wx = (float)(random.NextDouble() * 196.0 - 98.0);
                float wz = (float)(random.NextDouble() * 196.0 - 98.0);

                float sd = SignedDistanceToDinosaurActivity(wx, wz);
                if (sd <= -WorldBounds.ClearingInset || sd > FernInnerBandOuter)
                    continue; // 只允许 R1/R2：空地之外、玩家过渡带之内
                if (ChannelMask01(wx, wz) > 0f)
                    continue; // 与森林 fern 同规：避开视觉通道

                float gate = LowVegGate01(sd);
                float soil = SampleGrid(model.Soil, wx, wz);
                float weight = 0.10f + 0.55f * gate;                  // 越靠外越密（连续梯度）
                weight *= 1f - MudVegetationReduction * soil;          // mud 抑制
                if (random.NextDouble() >= weight)
                    continue;

                float perCluster = random.Next(FernPerClusterMin, FernPerClusterMax + 1);
                float radius = FernInnerClusterRadiusMin
                             + (float)random.NextDouble() * (FernInnerClusterRadiusMax - FernInnerClusterRadiusMin);
                // 单簇密度同样按 gate 衰减（活动包络外缘最稀疏），避免过渡带出现密集斑块
                StampFernCluster(field, res, wx, wz, radius, perCluster * Mathf.Max(gate, 0.25f));
                clustersPlaced++;
            }

            Debug.Log($"[UnderstorySeeder] fern 内侧带（R1/R2）：{clustersPlaced}/{(clusterBudget < FernInnerClusterCount ? $"{clusterBudget}（Preview {PreviewScale}×）" : FernInnerClusterCount.ToString())} 个簇" +
                      $"（活动包络外缘 → 玩家过渡带递增；独立流 salt={FernInnerBandSalt}）。");
            return field;
        }

        private static void StampFernCluster(float[,] field, int res, float wx, float wz, float radius, float perCluster)
        {
            int minX = WorldToDetailIndex(wx - radius, res);
            int maxX = WorldToDetailIndex(wx + radius, res);
            int minZ = WorldToDetailIndex(wz - radius, res);
            int maxZ = WorldToDetailIndex(wz + radius, res);
            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                float cx = (float)x / res * TerrainSize - TerrainSize / 2f;
                float cz = (float)z / res * TerrainSize - TerrainSize / 2f;
                float d = Vector2.Distance(new Vector2(cx, cz), new Vector2(wx, wz));
                if (d > radius)
                    continue;
                float v = perCluster * (1f - 0.55f * (d / radius));
                if (v > field[x, z])
                    field[x, z] = v; // max 语义：簇重叠取更密
            }
        }

        // ================================================================ Deadwood（资产门控接口）

        /// <summary>
        /// 倒木/树桩门控：Assets/Environment/RocksAndDeadwood/Deadwood/Prepared/Prefabs 无合格 prefab → no-op（仅日志提示目录）。
        /// 有资产 → 先落展示环近林带固定位（3 倒木 + 2 树桩，森林已按同一布局让开空隙 → 玩家可见），
        /// 再按「近树冠/林缘加权、避通道、平坦矩形+缓冲外、min-gap 12m」补足到 6-8 倒木 + 3-5 树桩；
        /// 贴地 + 轻微沉入（0.12-0.3m）、随机朝向（展示位横置朝向草地）、静态合批、默认零 Collider（独立流 +3）。
        /// 返回放置总数（0 = no-op）。
        /// </summary>
        private static int ApplyDeadwoodGate(TerrainData data, Transform terrainTransform, GameObject parent,
            EnvironmentModel model, List<RockCluster> rockClusters)
        {
            GameObject[] deadwoodPrefabs = UnderstoryAssetLocator.FindDeadwoodPrefabs();
            if (deadwoodPrefabs.Length == 0)
            {
                Debug.Log($"[UnderstorySeeder] 倒木/树桩接口 no-op：{UnderstoryAssetLocator.DeadwoodFolder} 无合格 prefab" +
                          "（目录不存在或为空；放入 log / stump 模型后重 Bake 自动按规则放置）。");
                return 0;
            }

            GameObject[] logs = deadwoodPrefabs
                .Where(p => p.name.IndexOf("stump", StringComparison.OrdinalIgnoreCase) < 0)
                .ToArray();
            GameObject[] stumps = deadwoodPrefabs
                .Where(p => p.name.IndexOf("stump", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();

            var random = new System.Random(UnderstoryRandomSeed + 3);
            var placedCenters = new List<Vector2>();
            int placed = 0;

            // 展示环位优先落地（近林带、森林已按同一布局让开空隙）→ 玩家从可达边界可见
            List<ShowcaseSpot> ring = BuildShowcaseRing();
            List<ShowcaseSpot> logSpots = SelectShowcaseSpots(ring, ShowcaseKind.Log);
            List<ShowcaseSpot> stumpSpots = SelectShowcaseSpots(ring, ShowcaseKind.Stump);

            placed += PlaceDeadwoodBatch(data, terrainTransform, parent, model, random, logs,
                PreviewBudget(random.Next(LogCountMin, LogCountMax + 1)), false, placedCenters, rockClusters, logSpots);
            placed += PlaceDeadwoodBatch(data, terrainTransform, parent, model, random, stumps,
                PreviewBudget(random.Next(StumpCountMin, StumpCountMax + 1)), true, placedCenters, rockClusters, stumpSpots);

            Debug.Log($"[UnderstorySeeder] deadwood 放置：{placed} 个（倒木/树桩；其中近林带展示位 {logSpots.Count} 倒木 + {stumpSpots.Count} 树桩；" +
                      $"其余分布于森林内部与林缘、避通道、平坦矩形+{DinoSafetyBuffer}m 外）{(PreviewScale > 1 ? $"【Preview {PreviewScale}× 缩减】" : "")}。");
            return placed;
        }

        /// <summary>展示环中指定种类的道具位（顺序即稳定契约）。</summary>
        private static List<ShowcaseSpot> SelectShowcaseSpots(List<ShowcaseSpot> ring, ShowcaseKind kind)
        {
            var list = new List<ShowcaseSpot>();
            foreach (ShowcaseSpot spot in ring)
                if (spot.Kind == kind)
                    list.Add(spot);
            return list;
        }

        /// <summary>
        /// 放置一批倒木 / 树桩：先落展示环固定位（近林带），再按加权拒绝采样补足剩余数量。
        /// </summary>
        private static int PlaceDeadwoodBatch(TerrainData data, Transform terrainTransform, GameObject parent,
            EnvironmentModel model, System.Random random, GameObject[] prefabs, int targetCount, bool isStump,
            List<Vector2> placedCenters, List<RockCluster> rockClusters, List<ShowcaseSpot> showcaseSpots)
        {
            if (prefabs.Length == 0 || targetCount <= 0)
                return 0;

            int placed = 0;

            // 1) 展示位优先
            for (int i = 0; i < showcaseSpots.Count && placed < targetCount; i++)
            {
                Vector2 center = showcaseSpots[i].Center;
                // 防御性检查：BuildShowcaseRing 已保证在缓冲外；保留以防未来常量下调破坏安全不变量
                if (DistanceToDinosaurActivity(center.x, center.y) < DinoSafetyBuffer + PlacementExtraMargin)
                    continue;

                PlaceDeadwood(data, terrainTransform, parent, random, prefabs, isStump, center,
                    showcaseSpots[i].AzimuthDeg, placed);
                placedCenters.Add(center);
                placed++;
            }

            // 2) 其余：加权拒绝采样
            const int attemptsCap = 800;
            for (int attempt = 0; attempt < attemptsCap && placed < targetCount; attempt++)
            {
                float wx = (float)(random.NextDouble() * 194.0 - 97.0);
                float wz = (float)(random.NextDouble() * 194.0 - 97.0);

                if (DistanceToDinosaurActivity(wx, wz) < DinoSafetyBuffer + PlacementExtraMargin)
                    continue;
                if (ChannelMask01(wx, wz) > 0f)
                    continue;

                Vector2 candidate = new Vector2(wx, wz);
                bool tooClose = false;
                foreach (Vector2 c in placedCenters)
                    if (Vector2.Distance(c, candidate) < 12f) { tooClose = true; break; }
                if (!tooClose)
                {
                    foreach (RockCluster rc in rockClusters)
                        if (Vector2.Distance(rc.Center, candidate) < 8f) { tooClose = true; break; }
                }
                if (tooClose)
                    continue;

                float coreDist = ForestSeeder.DistanceToCoreRect(wx, wz);
                float treeNear = SampleGrid(model.TreeNear, wx, wz);
                float weight = 0.30f + 0.50f * treeNear;
                if (coreDist < ForestEdgeDepth + 10f)
                    weight += 0.30f * Mathf.Clamp01(1f - coreDist / 28f);
                if (random.NextDouble() >= Mathf.Clamp01(weight))
                    continue;

                PlaceDeadwood(data, terrainTransform, parent, random, prefabs, isStump, candidate, float.NaN, placed);
                placedCenters.Add(candidate);
                placed++;
            }
            return placed;
        }

        /// <summary>
        /// 实例化单个倒木 / 树桩：贴地 + 轻微沉入（0.12–0.30m）、随机缩放、静态合批、默认零 Collider。
        /// broadsideYawDeg 有效时按该方位角横置（±12° 抖动，倒木长轴垂直于视线 = 正面朝向草地），否则自由朝向。
        /// </summary>
        private static void PlaceDeadwood(TerrainData data, Transform terrainTransform, GameObject parent,
            System.Random random, GameObject[] prefabs, bool isStump, Vector2 center, float broadsideYawDeg, int ordinal)
        {
            GameObject prefab = prefabs[(int)(random.NextDouble() * prefabs.Length)];
            float sink = Mathf.Lerp(0.12f, 0.30f, (float)random.NextDouble());
            float scale = isStump
                ? 0.8f + (float)random.NextDouble() * 0.6f
                : 0.9f + (float)random.NextDouble() * 0.7f;
            float yawRoll = (float)random.NextDouble();
            float yaw = float.IsNaN(broadsideYawDeg)
                ? yawRoll * 360f
                : broadsideYawDeg + (yawRoll * 2f - 1f) * 12f;
            float tilt = isStump ? 0f : (float)random.NextDouble() * 6f - 3f;

            float nx = (center.x + TerrainSize / 2f) / TerrainSize;
            float nz = (center.y + TerrainSize / 2f) / TerrainSize;
            float groundY = terrainTransform.position.y + data.GetInterpolatedHeight(nx, nz);

            GameObject item = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
            item.name = (isStump ? "Stump_" : "FallenLog_") + ordinal.ToString("D2");
            item.transform.position = new Vector3(center.x, groundY - sink, center.y);
            item.transform.rotation = Quaternion.Euler(tilt, yaw, tilt * 0.5f);
            item.transform.localScale = Vector3.one * scale;

            if (!AddColliderForDeadwood)
            {
                foreach (Collider col in item.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(col);
            }

            GameObjectUtility.SetStaticEditorFlags(item, StaticEditorFlags.BatchingStatic);
        }

        // ================================================================ 几何 / 数学辅助

        /// <summary>到恐龙活动包络（WorldBounds.DinosaurActivityBounds）的最短距离（米，矩形内为 0）。
        /// **所有实体落位 / 安全阈值判定一律用本函数**（不再用地形平坦区）。</summary>
        internal static float DistanceToDinosaurActivity(float worldX, float worldZ) =>
            WorldBounds.DistanceToDinosaurActivity(worldX, worldZ);

        /// <summary>到恐龙活动包络的带符号距离（米，内为负）：低矮植被梯度门的唯一轴心。</summary>
        internal static float SignedDistanceToDinosaurActivity(float worldX, float worldZ) =>
            WorldBounds.SignedDistanceToDinosaurActivity(worldX, worldZ);

        /// <summary>低矮无碰撞植被（bush/fern）的分布门（0..1，见 WorldBounds.LowVegGate01）。</summary>
        internal static float LowVegGate01(float signedDistanceToDinosaurActivity) =>
            WorldBounds.LowVegGate01(signedDistanceToDinosaurActivity);

        /// <summary>到地形平坦矩形（x∈[-70,70]、z∈[-70,90]）的最短距离（米，世界坐标；矩形内为 0）。
        /// 仅供**几何锚点**使用（展示环的射线/二分定位）：平坦矩形与恐龙活动包络当前数值相同，
        /// 故锚点等价；任何**安全/落位判定**都必须用 DistanceToDinosaurActivity。</summary>
        internal static float DistanceToFlatRectWorld(float worldX, float worldZ)
        {
            GetFlatRectWorld(out float minX, out float maxX, out float minZ, out float maxZ);
            float dx = Mathf.Max(Mathf.Max(minX - worldX, worldX - maxX), 0f);
            float dz = Mathf.Max(Mathf.Max(minZ - worldZ, worldZ - maxZ), 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static void GetFlatRectWorld(out float minX, out float maxX, out float minZ, out float maxZ)
        {
            float half = TerrainSize / 2f;
            minX = TerrainEnvironmentSeeder.FlatMinX * TerrainSize - half;
            maxX = TerrainEnvironmentSeeder.FlatMaxX * TerrainSize - half;
            minZ = TerrainEnvironmentSeeder.FlatMinZ * TerrainSize - half;
            maxZ = TerrainEnvironmentSeeder.FlatMaxZ * TerrainSize - half;
        }

        private static Vector2 FlatRectCenterWorld()
        {
            GetFlatRectWorld(out float minX, out float maxX, out float minZ, out float maxZ);
            return new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
        }

        private static Vector2 ClampToTerrain(Vector2 p, float margin)
        {
            float limit = TerrainSize / 2f - margin;
            return new Vector2(Mathf.Clamp(p.x, -limit, limit), Mathf.Clamp(p.y, -limit, limit));
        }

        /// <summary>视觉通道方向（方位角自正北 +z 顺时针）：复用 ForestSeeder 的通道常量（同源，不各自硬编码）。</summary>
        private static readonly Vector2[] ChannelDirs = BuildChannelDirs();

        private static Vector2[] BuildChannelDirs()
        {
            Vector2[] dirs = new Vector2[ForestSeeder.ChannelAzimuths.Length];
            for (int i = 0; i < dirs.Length; i++)
            {
                float rad = ForestSeeder.ChannelAzimuths[i] * Mathf.Deg2Rad;
                dirs[i] = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)).normalized;
            }
            return dirs;
        }

        /// <summary>视觉通道掩码（0/1）：与 ForestSeeder 的通道线段语义一致（t∈[0, SegmentLength] 且横向距离 &lt; HalfWidth）。</summary>
        private static float ChannelMask01(float wx, float wz)
        {
            Vector2 p = new Vector2(wx, wz);
            foreach (Vector2 dir in ChannelDirs)
            {
                Vector2 rel = p - ForestSeeder.ChannelOrigin;
                float t = Vector2.Dot(rel, dir);
                if (t < 0f || t > ForestSeeder.ChannelSegmentLength)
                    continue;
                if (Vector2.Distance(p, ForestSeeder.ChannelOrigin + dir * t) < ForestSeeder.ChannelHalfWidth)
                    return 1f;
            }
            return 0f;
        }

        /// <summary>双线性采样 128² 影响场网格（世界坐标 → 网格）。</summary>
        private static float SampleGrid(float[,] grid, float wx, float wz)
        {
            float gx = Mathf.Clamp((wx + TerrainSize / 2f) / TerrainSize * (InfluenceRes - 1), 0f, InfluenceRes - 1);
            float gz = Mathf.Clamp((wz + TerrainSize / 2f) / TerrainSize * (InfluenceRes - 1), 0f, InfluenceRes - 1);
            int x0 = (int)gx, z0 = (int)gz;
            int x1 = Mathf.Min(x0 + 1, InfluenceRes - 1);
            int z1 = Mathf.Min(z0 + 1, InfluenceRes - 1);
            float tx = gx - x0, tz = gz - z0;
            float a = Mathf.Lerp(grid[x0, z0], grid[x1, z0], tx);
            float b = Mathf.Lerp(grid[x0, z1], grid[x1, z1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        private static int WorldToInfluenceIndex(float world)
        {
            return Mathf.Clamp(Mathf.RoundToInt((world + TerrainSize / 2f) / TerrainSize * (InfluenceRes - 1)), 0, InfluenceRes - 1);
        }

        private static float InfluenceIndexToWorld(int index)
        {
            return (float)index / (InfluenceRes - 1) * TerrainSize - TerrainSize / 2f;
        }

        private static int WorldToDetailIndex(float world, int res)
        {
            return Mathf.Clamp(Mathf.FloorToInt((world + TerrainSize / 2f) / TerrainSize * res), 0, res - 1);
        }

        /// <summary>逐格 hash dither（[0,1)，纯函数）：打散整数化边界，避免规则化图案。</summary>
        private static float Hash01(int x, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)seed;
                h *= 2654435761u;
                h ^= h >> 13;
                h *= 2246822519u;
                h ^= h >> 15;
                return (h & 0xFFFFFFu) / 16777216f;
            }
        }

        private static float Smoothstep(float t)
        {
            return t * t * (3f - 2f * t);
        }

        private static GameObject NewChild(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform);
            return child;
        }
    }
}
