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
    ///  - 资产门控的 fern 原型尾随块（Assets/Vegetation/Ferns 出现 prefab 后自动追加）。
    ///
    /// 绝不触碰：树实例、heightmap / splat、其他 Pass 的根与原型对象、任何运行时脚本
    /// （含 DinosaurSpawnRandomizer —— 碰撞规避完全靠下述几何不变量，不靠运行时避让）。
    ///
    /// 安全缓冲不变量（几何隔离）：
    ///  恐龙漫步包络 ⊆ 平坦矩形 x∈[-70,70]、z∈[-70,90]（DinosaurSpawnRandomizer 的采样契约）；
    ///  所有中大型实体道具（rock / hero bush / log / stump）位于平坦矩形 + DinoSafetyBuffer(7.5m) 之外，
    ///  故与恐龙零接触；buffer 内 bush detail 密度恒 0（终态硬钳制），只允许 grass detail 等低矮非碰撞植被。
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
        private const string RocksNodeName = "Rocks";
        private const string VegetationNodeName = "Vegetation";
        private const string DeadwoodNodeName = "Deadwood";

        // --- 密度（bush 分区 / cluster 化）---
        private const float BushRampWidth = 12f;               // 缓冲外密度爬升带宽度（米）：0 → 全值
        private const int EdgeBushMin = 2, EdgeBushMax = 6;    // 林缘重点带（高频团块）
        private const int ForestBushMin = 2, ForestBushMax = 5; // 林内斑块（Fern 到位前的底层过渡覆盖）
        private const float ForestEdgeDepth = 18f;             // 到核心矩形距离 < 此值视为林缘带
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
        private const string RockMaterialPath = "Assets/Materials/RockNatural.mat";

        // --- Hero 灌木（bush01-06 作为中层植被主角，cluster 化）---
        private const int HeroBushClusterCount = 16;
        private const int HeroBushPerClusterMin = 2, HeroBushPerClusterMax = 3;
        private const float HeroBushClusterMinGap = 8f;        // 簇心间最小间隙（米）
        private const float HeroBushScaleMin = 1.0f, HeroBushScaleMax = 2.2f;

        // --- Fern（资产门控：Assets/Vegetation/Ferns 无合格 prefab 时 no-op）---
        private const int FernClusterCount = 320;
        private const int FernPerClusterMin = 4, FernPerClusterMax = 10;
        private const float FernClusterRadiusMin = 1.5f, FernClusterRadiusMax = 4f;

        // --- Deadwood（资产门控：Assets/Vegetation/Deadwood 无合格 prefab 时 no-op）---
        private const int LogCountMin = 6, LogCountMax = 8;
        private const int StumpCountMin = 3, StumpCountMax = 5;
        private const bool AddColliderForDeadwood = false;

        // --- 调试开关：true 时用程序化 Sphere 仅验证岩石分布（正式 Bake 禁止；默认 false）---
        private const bool DebugProceduralRocks = false;

        /// <summary>调试岩石开关只读视图（属性读取避免常量折叠的不可达代码告警；ValidateProject 也会据此告警）。</summary>
        internal static bool DebugProceduralRocksOn => DebugProceduralRocks;

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
        /// (a) hero 锚石环带：围绕平坦矩形的 360° 方位分层（10 扇区 × jitter），距矩形 = 簇心最小距离 + 0..26m；
        /// (b) 其余簇：加权拒绝采样（Poisson min-gap）：林缘/坡脚高、远林低、通道内显著降低、泥边不额外处理。
        /// 所有簇心距平坦矩形 ≥ ClusterCenterMinDistance（含成员偏移后仍 ≥ buffer + extra）。
        /// </summary>
        private static List<RockCluster> GenerateRockClusters(TerrainData data)
        {
            var random = new System.Random(UnderstoryRandomSeed);
            var clusters = new List<RockCluster>();
            Vector2 flatCenter = FlatRectCenterWorld();

            int heroCount = Mathf.Min(HeroRockCount, RockClusterCount);
            for (int i = 0; i < heroCount; i++)
            {
                // 方位分层：扇区中心 + ±15° jitter（角度约定：0°=+z 北，顺时针向 +x，与 ForestSeeder.ChannelAzimuths 一致）
                float sectorCenter = i * (360f / heroCount);
                float jitter = ((float)random.NextDouble() * 2f - 1f) * 15f;
                float angle = (sectorCenter + jitter) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                float targetDist = ClusterCenterMinDistance + (float)random.NextDouble() * 26f;

                Vector2 center = ClampToTerrain(PointAtFlatDistance(flatCenter, dir, targetDist), 3f);
                if (DistanceToFlatRectWorld(center.x, center.y) < ClusterCenterMinDistance)
                    continue; // 北侧窄带（平坦矩形 z 上界 90 → 地形边 100）无空间时跳过该扇区

                bool tooClose = false;
                foreach (RockCluster c in clusters)
                    if (Vector2.Distance(c.Center, center) < RockClusterMinGap) { tooClose = true; break; }
                if (tooClose)
                    continue;

                clusters.Add(BuildClusterMembers(random, center, true));
            }

            // (b) 其余簇：加权拒绝采样
            for (int attempt = 0; attempt < RockPlacementAttempts && clusters.Count < RockClusterCount; attempt++)
            {
                float wx = (float)(random.NextDouble() * 194.0 - 97.0);
                float wz = (float)(random.NextDouble() * 194.0 - 97.0);

                if (DistanceToFlatRectWorld(wx, wz) < ClusterCenterMinDistance)
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
                if (DistanceToFlatRectWorld(p.x, p.y) < targetDist)
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

                float dFlat = DistanceToFlatRectWorld(wx, wz);
                float coreDist = ForestSeeder.DistanceToCoreRect(wx, wz);
                float treeNear = SampleGrid(model.TreeNear, wx, wz);
                float soil = SampleGrid(model.Soil, wx, wz);
                float channel = ChannelMask01(wx, wz);

                // ---- bush：缓冲硬禁 → ramp 起步 → 林缘高频团块 / 林内低频斑块（全按 cluster 噪声聚簇化）----
                float ramp = Mathf.Clamp01((dFlat - DinoSafetyBuffer) / BushRampWidth);
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
                float b = baseBush * ramp;
                b *= 1f - TrailVegetationReduction * channel;
                b *= 1f - MudVegetationReduction * soil;
                b += SampleGrid(model.BushBoost, wx, wz);
                b += (Hash01(x, z, UnderstoryRandomSeed + 7) - 0.5f) * 1.1f;
                bushField[x, z] = b;
                if (b >= 1f)
                    bushActiveCells++;

                // ---- grass：平坦矩形内连续 2-4；外缘斑驳 1-3（带缝隙）；林内 0-2（树冠下减少）----
                float patch = Mathf.PerlinNoise(wx * 0.10f + 15.7f, wz * 0.10f + 23.3f);  // 斑块（≈10m）
                float micro = Mathf.PerlinNoise(wx * 0.33f + 41.2f, wz * 0.33f + 8.6f);  // 细碎缝隙（≈3m）
                float openness = dFlat <= 0.001f
                    ? 0f
                    : Mathf.Clamp01(Mathf.Max(dFlat / EdgeGrassBand, treeNear));
                float g = Mathf.Lerp(2.6f + 1.4f * patch, 0.7f, openness);
                if (dFlat > 0.001f)
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
                    if (DistanceToFlatRectWorld(wx, wz) < DinoSafetyBuffer)
                        v = 0f; // 终态硬钳制：buffer 内 bush 密度恒 0
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

            for (int ci = 0; ci < clusters.Count; ci++)
            {
                RockCluster cluster = clusters[ci];
                foreach (RockMember member in cluster.Members)
                {
                    float wx = cluster.Center.x + member.Offset.x;
                    float wz = cluster.Center.y + member.Offset.y;

                    // 防御性成员级检查：簇心余量已保证不触发；保留以避免未来常量下调破坏安全不变量
                    if (DistanceToFlatRectWorld(wx, wz) < DinoSafetyBuffer + PlacementExtraMargin)
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
                                : 0.35f + (float)random.NextDouble() * 0.35f;
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
                Debug.Log($"[UnderstorySeeder] 岩石簇实例化：{clusters.Count} 簇 / {placed} 个（{rockPrefabs.Length} 种 prefab）" +
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

                if (DistanceToFlatRectWorld(wx, wz) < ClusterCenterMinDistance)
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
                    if (DistanceToFlatRectWorld(wx, wz) < DinoSafetyBuffer + PlacementExtraMargin)
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
        /// Fern 门控：Assets/Vegetation/Ferns 无合格 prefab → no-op（仅日志提示目录）。
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

            bool alreadyNormalized = fernIdx.Count == fernPrefabs.Length;
            if (alreadyNormalized)
            {
                for (int i = 0; i < fernPrefabs.Length; i++)
                {
                    if (protos[fernIdx[i]].prototype != fernPrefabs[i])
                    {
                        alreadyNormalized = false;
                        break;
                    }
                }
            }

            int fernStart;
            if (alreadyNormalized)
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
                {
                    kept.Add(new DetailPrototype
                    {
                        usePrototypeMesh = true,
                        prototype = fern,
                        renderMode = DetailRenderMode.Grass,
                        healthyColor = new Color(0.30f, 0.62f, 0.22f),
                        dryColor = new Color(0.52f, 0.58f, 0.24f),
                        minWidth = 0.8f,
                        maxWidth = 1.6f,
                        minHeight = 0.8f,
                        maxHeight = 1.6f,
                        noiseSpread = 0.2f
                    });
                }
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
                Debug.Log($"[UnderstorySeeder] fern 原型块追加于 index={fernStart}（{fernPrefabs.Length} 个 prefab）。");
            }

            // 按簇绘制密度（确定性流 +2；同密度场写所有 fern 层，逐层 hash 抖动）
            float[,] fernField = BuildFernField(model, res);
            int changedLayers = 0;
            for (int i = 0; i < fernPrefabs.Length; i++)
            {
                int layerIdx = fernStart + i;
                int[,] density = new int[res, res];
                for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float wx = (float)x / res * TerrainSize - TerrainSize / 2f;
                    float wz = (float)z / res * TerrainSize - TerrainSize / 2f;
                    float v = fernField[x, z]
                              * (0.7f + 0.6f * Hash01(x, z, UnderstoryRandomSeed + 200 + i));
                    if (DistanceToFlatRectWorld(wx, wz) < DinoSafetyBuffer)
                        v = 0f; // 与实体同规：活动区 + 缓冲外才允许 fern
                    density[x, z] = Mathf.Clamp(Mathf.RoundToInt(v), 0, FernPerClusterMax);
                }
                if (WriteLayerIfChanged(data, layerIdx, density) > 0)
                    changedLayers++;
            }

            Debug.Log($"[UnderstorySeeder] fern 铺设：{fernPrefabs.Length} 层（层 {fernStart}..{fernStart + fernPrefabs.Length - 1}），" +
                      $"{(changedLayers > 0 ? $"写回 {changedLayers} 层" : "与现有一致跳过")}。");
            return fernPrefabs.Length;
        }

        /// <summary>按簇构建 fern 密度场：林内高密度簇 / 林缘混合，mud 抑制、避通道、缓冲外（独立流 +2）。</summary>
        private static float[,] BuildFernField(EnvironmentModel model, int res)
        {
            var field = new float[res, res];
            var random = new System.Random(UnderstoryRandomSeed + 2);
            int attemptsCap = FernClusterCount * 40;
            int clustersPlaced = 0;
            for (int attempt = 0; attempt < attemptsCap && clustersPlaced < FernClusterCount; attempt++)
            {
                float wx = (float)(random.NextDouble() * 196.0 - 98.0);
                float wz = (float)(random.NextDouble() * 196.0 - 98.0);

                if (DistanceToFlatRectWorld(wx, wz) < DinoSafetyBuffer)
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

            Debug.Log($"[UnderstorySeeder] fern 簇分布：{clustersPlaced}/{FernClusterCount} 个簇（林内高密度 / 林缘混合 / mud 抑制）。");
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
        /// 倒木/树桩门控：Assets/Vegetation/Deadwood 无合格 prefab → no-op（仅日志提示目录）。
        /// 有资产 → 森林内部/林缘放置 6-8 倒木 + 3-5 树桩：近树冠/林缘加权、避通道、平坦矩形+缓冲外、
        /// min-gap 12m、贴地+轻微沉入（0.12-0.3m）、随机朝向、静态合批、默认零 Collider（独立流 +3）。
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

            placed += PlaceDeadwoodBatch(data, terrainTransform, parent, model, random, logs,
                random.Next(LogCountMin, LogCountMax + 1), false, placedCenters, rockClusters);
            placed += PlaceDeadwoodBatch(data, terrainTransform, parent, model, random, stumps,
                random.Next(StumpCountMin, StumpCountMax + 1), true, placedCenters, rockClusters);

            Debug.Log($"[UnderstorySeeder] deadwood 放置：{placed} 个（倒木/树桩；森林内部与林缘、避通道、" +
                      $"平坦矩形+{DinoSafetyBuffer}m 外）。");
            return placed;
        }

        private static int PlaceDeadwoodBatch(TerrainData data, Transform terrainTransform, GameObject parent,
            EnvironmentModel model, System.Random random, GameObject[] prefabs, int targetCount, bool isStump,
            List<Vector2> placedCenters, List<RockCluster> rockClusters)
        {
            if (prefabs.Length == 0 || targetCount <= 0)
                return 0;

            int placed = 0;
            const int attemptsCap = 800;
            for (int attempt = 0; attempt < attemptsCap && placed < targetCount; attempt++)
            {
                float wx = (float)(random.NextDouble() * 194.0 - 97.0);
                float wz = (float)(random.NextDouble() * 194.0 - 97.0);

                if (DistanceToFlatRectWorld(wx, wz) < DinoSafetyBuffer + PlacementExtraMargin)
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

                GameObject prefab = prefabs[(int)(random.NextDouble() * prefabs.Length)];
                float sink = Mathf.Lerp(0.12f, 0.30f, (float)random.NextDouble());
                float scale = isStump
                    ? 0.8f + (float)random.NextDouble() * 0.6f
                    : 0.9f + (float)random.NextDouble() * 0.7f;
                float yaw = (float)random.NextDouble() * 360f;
                float tilt = isStump ? 0f : (float)random.NextDouble() * 6f - 3f;

                float nx = (wx + TerrainSize / 2f) / TerrainSize;
                float nz = (wz + TerrainSize / 2f) / TerrainSize;
                float groundY = terrainTransform.position.y + data.GetInterpolatedHeight(nx, nz);

                GameObject item = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
                item.name = (isStump ? "Stump_" : "FallenLog_") + placed.ToString("D2");
                item.transform.position = new Vector3(wx, groundY - sink, wz);
                item.transform.rotation = Quaternion.Euler(tilt, yaw, tilt * 0.5f);
                item.transform.localScale = Vector3.one * scale;

                if (!AddColliderForDeadwood)
                {
                    foreach (Collider col in item.GetComponentsInChildren<Collider>(true))
                        UnityEngine.Object.DestroyImmediate(col);
                }

                GameObjectUtility.SetStaticEditorFlags(item, StaticEditorFlags.BatchingStatic);
                placedCenters.Add(candidate);
                placed++;
            }
            return placed;
        }

        // ================================================================ 几何 / 数学辅助

        /// <summary>到平坦矩形（x∈[-70,70]、z∈[-70,90]）的最短距离（米，世界坐标；矩形内为 0）。
        /// 平坦矩形世界坐标由 TerrainEnvironmentSeeder.Flat* 常量（归一化）换算，播种侧/校验侧同源。</summary>
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
