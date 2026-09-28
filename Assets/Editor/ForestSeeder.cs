using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace JurassicWorld.Editor
{
    /// <summary>
    /// 编辑态外围密集森林播种器（与 JurassicWorldSceneBuilder 同程序集 internal 协作）。
    ///
    /// 安全契约（与计划 v2 Step 1 一致）：
    ///  - 森林专属 prototype 区间 = [baseCount, baseCount + 追加 prefab 数)，由本类独占管理；
    ///  - 中央 161 棵树（前 baseCount 个原型对应的 TreeInstance）从 data.treeInstances 读出后【按值原样写回】，
    ///    全部字段（position / scale / rotation / color / prototypeIndex）零变更、零重新随机；
    ///  - 多次 Bake 幂等（固定 ForestRandomSeed），EnableForest=false + 重 Bake 即精确剥离森林批次；
    ///  - 绝不触碰 SceneBuilder 的 GroundRandomSeed=271828 烘焙随机流（SeedTrees/SeedDetails/BuildScatterPrefabs）。
    ///
    /// 【核心不变量：核心矩形内永无森林树】
    ///  硬隔离判定使用「未经 wobble 扰动的原始距离 d」，并且【先于】wobble 执行；EdgeWobbleAmp 只塑造林缘形状
    ///  （dEff 被 Mathf.Max 钳到 >= ForestHardMargin），因此任何噪声取值都不可能把核心矩形内（d=0）或
    ///  d &lt; ForestHardMargin 环带内的候选点抬过阈值。该不变量由 JurassicProjectSetup.ValidateProject
    ///  做机器验证（核心矩形内 prototypeIndex >= CentralTreePrototypeCount 的实例数必须为 0）。
    /// </summary>
    internal static class ForestSeeder
    {
        // ------------------------------------------------------------------ 可调常量块
        // 回滚开关：false + 重 Bake = 精确剥离森林批次（中央 161 棵不动）
        private const bool EnableForest = true;

        /// <summary>
        /// 回滚开关的对外只读视图：供 JurassicProjectSetup.ValidateProject 把树数量阈值（启用森林 400 / 回滚态 100）
        /// 与本类状态联动，避免校验侧硬编码而与播种侧漂移。
        /// </summary>
        internal static bool ForestEnabled => EnableForest;

        // 独立 System.Random 种子（绝不使用 GroundRandomSeed 271828 烘焙流）
        private const int ForestRandomSeed = 271831;

        // 中央段 TreePrototype 历史基线（NSk2 默认 tree01..tree04，四者 prefab 引用互不相同）。
        // 仅用于告警与 ValidateProject 同源引用；实际 baseCount 一律以「重复 prototype 检测」结果为准。
        internal const int CentralTreePrototypeCount = 4;

        // 开阔核心矩形（覆盖相机 ±47 钳制区与全部恐龙漫游包络，确保中央活动区无森林树）。
        // 声明为 internal const：JurassicProjectSetup.ValidateProject 直接引用同一份定义做零泄漏断言，不各自硬编码。
        // 【余量提示，仅注释不改数值】阿根廷龙模板 collisionRadius=8、出生点 (-13, 35)、漫游半径 45m，
        //   其包络西界 x=-58、北界 z=80，距 CoreMinX=-60 / CoreMaxZ=82 仅约 2m 余量。未来把该恐龙
        //   enabled 翻回 true 之前，需评估：外扩 CoreMinX/CoreMaxZ（并同步森林密度与遮挡），或调小其
        //   collisionRadius —— 否则巨型个体会贴着森林内缘行走、行为级阻挡频繁触发。
        internal const float CoreMinX = -60f;
        internal const float CoreMaxX = 60f;
        internal const float CoreMinZ = -50f;
        internal const float CoreMaxZ = 82f;

        // 核心矩形外的硬隔离带：任何森林树都不允许出现在【原始距离】d < ForestHardMargin 处。
        // 该判定先于林缘 wobble 执行，且 wobble 无法突破（见 Seed 步骤 (b1)/(b2)）。
        private const float ForestHardMargin = 5f;

        // 密度渐变坡宽（产生自然的 forest edge：稀疏 → 中等 → 密集）
        private const float RampWidth = 18f;

        // 边界强制密林墙厚度（borderDist < WallInset 时 p 强制 ≥ 0.92，遮挡地形边）
        internal const float WallInset = 12f;

        // 树群噪声尺度（≈22m 一团，产生大小不一的树群与不规则空隙）
        private const float ClusterScale = 0.045f;

        // 林缘不规则扰动振幅（米），打破核心矩形的人造直线感
        private const float EdgeWobbleAmp = 6f;

        // 视觉通道半宽（米）：自中央 (0,15) 向地形边放射的 3 条裸带，玩家视线穿透感
        // （internal：UnderstorySeeder 通道掩码同源引用）
        internal const float ChannelHalfWidth = 6f;

        // 拒绝采样次数（密度迭代协议起点；预期森林 ~1500-2200 棵）
        private const int ForestAttempts = 6000;

        // 总实例硬上限（中央 + 森林 ≤ 此值；达到即停止接受新候选）
        private const int MaxTotalInstances = 3000;

        // 视觉通道方位角（度，自正北 +z 顺时针）。
        // 实况描述（原注释「避开北侧薄林带」不准确，方位角数值不变）：35° 通道会穿过北侧较薄的林带
        // （z > 82 区域），但边界密林墙（borderDist < WallInset 段）不被通道挖穿 —— 通道剔除仅在
        // borderDist >= WallInset 时才生效，因此远端地平线的遮挡保证不受影响。
        // （internal：UnderstorySeeder 通道掩码同源引用）
        internal static readonly float[] ChannelAzimuths = { 35f, 160f, 285f };

        // 视觉通道起点（世界 XZ 坐标，位于中央活动区）
        // （internal：UnderstorySeeder 通道掩码同源引用）
        internal static readonly Vector2 ChannelOrigin = new Vector2(0f, 15f);

        // 4 种树（按 prefab 名称升序对应 tree01/02/03/04）的加权抽取权重 ≈ 35/25/25/15。
        // tree04 为多干小树，作为亚冠层下层填充，权重最低。
        private static readonly int[] SpeciesWeights4 = { 35, 25, 25, 15 };

        // 采样候选坐标范围（地形 200m×200m，世界 x,z ∈ [-100,100]，留 1m 安全边）
        private const float SampleRangeMin = -99f;
        private const float SampleRangeMax = 99f;

        // 地形尺寸（与 SceneBuilder.TerrainSize 一致；写死避免反向依赖、保持本类自包含）
        private const float TerrainSize = 200f;

        // 视觉通道线段长度（远超地形对角线，使候选采样范围内的通道近似为射线）
        // （internal：UnderstorySeeder 通道掩码同源引用）
        internal const float ChannelSegmentLength = 300f;

        // 径向密度直方图环带边界（米）：按到核心矩形的【原始距离 d】分带统计棵数，供「遮挡达标即止」
        // 密度迭代协议做量化对比。环带 = [ForestHardMargin,10) [10,15) [15,20) [20,25) [25,+)；
        // 第一带恒从 ForestHardMargin 起（d < 5 的候选已被硬隔离拒绝），该带棵数也是硬隔离的自检指标。
        private static readonly float[] RadialBandEdges = { 10f, 15f, 20f, 25f };
        private const int RadialBandCount = 5;

        // ------------------------------------------------------------------ 入口

        /// <summary>
        /// 在 TerrainData 上播种/重建外围森林。每次 Bake 无条件调用：
        /// 自清除自己区间 [baseCount, ...) 的旧实例，按固定种子重建；中央段原样保留。
        /// </summary>
        public static void Seed(TerrainData data)
        {
            if (data == null)
            {
                Debug.LogWarning("[ForestSeeder] TerrainData 为 null，跳过森林播种。");
                return;
            }

            GameObject[] treePrefabs = NatureStarterKitLocator.FindTreePrefabs();
            if (treePrefabs == null || treePrefabs.Length == 0)
            {
                Debug.LogWarning("[ForestSeeder] 未找到 NSk2 树 Prefab，跳过森林播种（优雅降级）。");
                return;
            }

            // 1) 先读副本：treePrototypes / treeInstances 的 getter 返回新数组（TreeInstance 是 struct，按值拷贝）。
            //    赋值 treePrototypes 会清空实例（Unity 文档行为），故必须先读、最后统一 SetTreeInstances 写回。
            TreePrototype[] protos = data.treePrototypes;
            TreeInstance[] existing = data.treeInstances;

            // 2) 基础区间 = 中央树所占的前 baseCount 个 prototype。
            //    【已废弃】Mathf.Min(protos.Length, treePrefabs.Length) 启发式：NSk2 树数量变化时它会把上一批
            //    森林段误判为中央段（树变多 → 森林实例被当成中央树保留并再追加一轮）或丢弃部分中央树（树变少）。
            //    改用「重复 prototype 检测」：中央段的 prefab 引用互不相同，森林段是把同一批 prefab 重复追加到尾部，
            //    所以第一个「与更早 prototype 持有相同 prefab 引用」的下标就是森林段起点 forestStart。
            int forestStart = protos.Length;
            for (int i = 0; i < protos.Length && forestStart == protos.Length; i++)
            {
                if (protos[i] == null || protos[i].prefab == null) continue;
                for (int j = 0; j < i; j++)
                {
                    if (protos[j] != null && protos[j].prefab == protos[i].prefab)
                    {
                        forestStart = i;
                        break;
                    }
                }
            }
            int baseCount = forestStart;
            bool forestSegmentDetected = baseCount < protos.Length;

            //    结构一致性校验：原型数组只允许是「中央段」或「中央段 + 恰好一轮森林段」。
            //    正常路径静默（首次 Bake: protos.Length == baseCount；二次 Bake: == baseCount + treePrefabs.Length），
            //    不刷噪声；其余情形说明原型被外部改动或上次 Bake 使用了不同数量的森林 prefab —— 此时按
            //    baseCount 切分会破坏中央段，故【报错并直接 return，不做任何写回落盘】。
            if (forestSegmentDetected && protos.Length != baseCount + treePrefabs.Length)
            {
                Debug.LogError($"[ForestSeeder] 原型数组结构与预期不符，已中止且不写回：检测到森林段起点 baseCount={baseCount}，" +
                    $"但 treePrototypes.Length={protos.Length} ≠ baseCount + treePrefabs.Length={baseCount + treePrefabs.Length}" +
                    $"（当前 NSk2 树 prefab 数={treePrefabs.Length}）。疑似上一次 Bake 使用了不同数量的森林 prefab，" +
                    $"或 treePrototypes 被手工/外部脚本改动；为防止破坏性写入落盘，本次不修改 TerrainData。");
                return;
            }
            if (baseCount != CentralTreePrototypeCount)
            {
                Debug.LogWarning($"[ForestSeeder] 检测到的中央段原型数 baseCount={baseCount} 与历史基线 " +
                    $"CentralTreePrototypeCount={CentralTreePrototypeCount} 不符 — 以检测值为准（不阻断），请确认 NSk2 中央树种数量是否变更。");
            }

            // 3) 森林 prefab 按名称升序（保证森林段 prototypeIndex 跨 Bake 稳定；
            //    绝不在 NatureStarterKitLocator 中排序，避免重映射中央树物种）。
            GameObject[] sortedForestPrefabs = treePrefabs
                .OrderBy(p => p != null ? p.name : string.Empty, StringComparer.Ordinal)
                .ToArray();

            // 4) 回滚分支：剥离森林批次，prototypes 缩回前 baseCount 个，existing 中仅保留 prototypeIndex < baseCount。
            //    剥离条件 = 「检测到森林段存在」（重复 prefab 引用），不用 protos.Length > baseCount 之类的脆弱比较。
            if (!EnableForest)
            {
                if (forestSegmentDetected)
                {
                    TreeInstance[] kept = existing.Where(i => i.prototypeIndex < baseCount).ToArray();
                    int removed = existing.Length - kept.Length;
                    TreePrototype[] baseOnly = protos.Take(baseCount).ToArray();

                    data.treePrototypes = baseOnly;
                    data.SetTreeInstances(kept, false);

                    Debug.Log($"[ForestSeeder] 回滚：剥离森林批次（移除 {removed} 棵森林树），" +
                        $"prototypes 缩回 {baseCount} 个，中央 {kept.Length} 棵原样保留。");
                }
                else
                {
                    Debug.Log("[ForestSeeder] EnableForest=false 且未检测到森林段（treePrototypes 中无重复 prefab 引用），无需操作。");
                }
                return;
            }

            // 5) 重建 prototypes：前 baseCount 个【原样保留】 + 追加 sortedForestPrefabs 的森林段。
            //    森林专属区间 = [baseCount, baseCount + sortedForestPrefabs.Length)
            TreePrototype[] newProtos = new TreePrototype[baseCount + sortedForestPrefabs.Length];
            for (int i = 0; i < baseCount; i++)
                newProtos[i] = protos[i]; // 中央段原型原样保留（TreePrototype 是 class，Unity 赋值时内部克隆），零字段变更
            for (int i = 0; i < sortedForestPrefabs.Length; i++)
                newProtos[baseCount + i] = new TreePrototype
                {
                    prefab = sortedForestPrefabs[i],
                    bendFactor = 0.1f
                };

            // 中央段实例：纯值拷贝原样写回（LINQ 直接传递 struct，禁止任何重新计算 / 重新随机）
            TreeInstance[] preservedCenter = existing.Where(i => i.prototypeIndex < baseCount).ToArray();

            // 6) 加权抽取表（按 sortedForestPrefabs 索引 0..N-1）。
            int[] weights = BuildSpeciesWeights(sortedForestPrefabs);
            int weightSum = 0;
            for (int i = 0; i < weights.Length; i++) weightSum += weights[i];
            if (weightSum <= 0)
            {
                // 退化保护：权重全为非正数时，若只把 weightSum 改成 Length，WeightedPick 的累加循环永远
                // 满足不了 roll < acc，会一路兜底返回【最后一个物种】，导致整片森林单物种化。
                // 故同时把权重全部重置为 1，使抽取退化为真正的均匀分布。
                for (int i = 0; i < weights.Length; i++) weights[i] = 1;
                weightSum = weights.Length;
            }

            // 7) 视觉通道方向：方位角自正北 +z 顺时针，dir = (sin(az), cos(az)) in (x, z)。
            Vector2[] channelDirs = new Vector2[ChannelAzimuths.Length];
            for (int i = 0; i < ChannelAzimuths.Length; i++)
            {
                float rad = ChannelAzimuths[i] * Mathf.Deg2Rad;
                channelDirs[i] = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)).normalized;
            }

            // 8) 拒绝采样生成森林实例。
            var random = new System.Random(ForestRandomSeed);
            var forest = new List<TreeInstance>();
            int forestCapacity = Mathf.Max(0, MaxTotalInstances - preservedCenter.Length);
            int[] speciesHistogram = new int[sortedForestPrefabs.Length];

            // 量化日志用统计：实际消费的采样次数、是否触顶（forestCapacity）、径向密度直方图。
            int attemptsUsed = 0;
            bool capacityHit = false;
            int[] radialHistogram = new int[RadialBandCount];

            // 林下层展示环布局由 UnderstorySeeder 独占（纯函数、无随机消费）；其 Pass 被禁用时不留缺口（回滚契约）。
            bool showcaseClearance = UnderstorySeeder.PassEnabled;
            int showcaseExcluded = 0;

            for (int attempt = 0; attempt < ForestAttempts; attempt++)
            {
                if (forest.Count >= forestCapacity)
                {
                    capacityHit = true;   // 触顶：总实例硬上限达到，提前停止接受新候选
                    break;
                }
                attemptsUsed = attempt + 1;

                float wx = (float)(random.NextDouble() * (SampleRangeMax - SampleRangeMin) + SampleRangeMin);
                float wz = (float)(random.NextDouble() * (SampleRangeMax - SampleRangeMin) + SampleRangeMin);

                // (a) 距核心矩形距离（参考 SceneBuilder.DistanceToFlatRegion 模式：dx/dz 取 0 与超出量的较大者）
                float d = DistanceToCoreRect(wx, wz);

                // (b1) 绝对硬隔离：用【未扰动的原始距离】先做硬拒绝，必须早于 wobble。
                //      旧实现先算 dEff = d - wobble 再比较阈值，EdgeWobbleAmp=6 的正向扰动会把核心矩形内
                //      （d=0）的点抬到 dEff≈2~6，以 p≈2~6% 的概率每次 Bake 确定性泄漏 2~5 棵进中央活动区。
                if (d < ForestHardMargin) continue;

                // (b2) 林缘不规则扰动（Perlin 噪声 [0,1] → [-1,1] × EdgeWobbleAmp）：只影响林缘形状，
                //      下限钳到 ForestHardMargin，任何情况下都不能突破硬隔离带。
                float wobble = (Mathf.PerlinNoise(wx * 0.02f + 7.7f, wz * 0.02f + 3.3f) * 2f - 1f) * EdgeWobbleAmp;
                float dEff = Mathf.Max(d - wobble, ForestHardMargin);

                // (c) 渐变坡 + 树群噪声
                float ramp = Mathf.Clamp01((dEff - ForestHardMargin) / RampWidth);
                float cluster = 0.35f + 0.75f * Mathf.PerlinNoise(wx * ClusterScale + 11.1f, wz * ClusterScale + 5.5f);
                float p = Mathf.Min(ramp * cluster, 1f);

                // (d) 边界密林墙：贴近地形边时强制高密度，遮挡远端地平线
                float borderDist = Mathf.Min(TerrainSize / 2f - Mathf.Abs(wx), TerrainSize / 2f - Mathf.Abs(wz));
                if (borderDist < WallInset)
                    p = Mathf.Max(p, 0.92f);

                // (e) 视觉通道（仅当不打穿密林墙时生效；密林墙段保留遮挡语义）
                if (borderDist >= WallInset)
                {
                    Vector2 candidate = new Vector2(wx, wz);
                    bool inChannel = false;
                    for (int k = 0; k < channelDirs.Length; k++)
                    {
                        Vector2 rel = candidate - ChannelOrigin;
                        float t = Vector2.Dot(rel, channelDirs[k]);
                        if (t < 0f || t > ChannelSegmentLength) continue; // 起点后方或超出线段
                        Vector2 closest = ChannelOrigin + channelDirs[k] * t;
                        if (Vector2.Distance(candidate, closest) < ChannelHalfWidth)
                        {
                            inChannel = true;
                            break;
                        }
                    }
                    if (inChannel) continue;

                    // (e2) 林下层展示环清空：为近林带的岩石簇 / 倒木 / 树桩让出「清空圆 + 指向平坦矩形的视线
                    //      通道」，否则 1-5m 高的实体道具会被树冠与树干完全挡死（玩家在可达边界看不到）。
                    //      与视觉通道同一条 borderDist 守卫：密林墙段保持遮挡语义、地平线仍被挡住。
                    if (showcaseClearance && UnderstorySeeder.InShowcaseClearance(candidate))
                    {
                        showcaseExcluded++;
                        continue;
                    }
                }

                // (f) 接受 / 拒绝
                if (random.NextDouble() >= p) continue;

                // (g) 加权抽取物种（在森林段内索引）
                int speciesLocal = WeightedPick(random, weights, weightSum);
                int prototypeIndex = baseCount + speciesLocal;
                speciesHistogram[speciesLocal]++;
                radialHistogram[RadialBandOf(d)]++;   // 径向密度环带统计（用原始距离 d，与硬隔离同源）

                // (h) 实例构造（严格镜像 SceneBuilder.SeedTrees L374-387 约定）
                float nx = (wx + TerrainSize / 2f) / TerrainSize;
                float nz = (wz + TerrainSize / 2f) / TerrainSize;
                float hy = data.GetInterpolatedHeight(nx, nz) / data.size.y;

                forest.Add(new TreeInstance
                {
                    position = new Vector3(nx, hy, nz),
                    widthScale = 0.8f + (float)random.NextDouble() * 0.5f,
                    heightScale = 0.8f + (float)random.NextDouble() * 0.5f,
                    rotation = (float)random.NextDouble() * Mathf.PI * 2f,
                    color = Color.white,
                    lightmapColor = Color.white,
                    prototypeIndex = prototypeIndex
                });
            }

            // 9) 统一写回：先赋 prototypes（会清空实例），再 SetTreeInstances 全量。
            //    preservedCenter 段是纯值拷贝原样写回；forest 段为本批次新生成。
            TreeInstance[] finalInstances = preservedCenter.Concat(forest).ToArray();

            //    幂等写回优化：固定 ForestRandomSeed 下二次 Bake 的结果与现有数据逐字段一致，此时跳过
            //    treePrototypes 赋值与 SetTreeInstances，避免每次 Bake 重写 ~3MB 二进制 TerrainData 资产
            //    并触发 Unity 重新烘焙树 billboard 图集。prototypes 与 instances 必须【同时】相同才跳过。
            bool prototypesUnchanged = TreePrototypesEqual(protos, newProtos);
            bool instancesUnchanged = TreeInstancesEqual(existing, finalInstances);
            bool wroteBack = !(prototypesUnchanged && instancesUnchanged);
            if (wroteBack)
            {
                data.treePrototypes = newProtos;
                data.SetTreeInstances(finalInstances, false);
            }
            else
            {
                Debug.Log($"[ForestSeeder] 结果与现有实例一致，跳过写回（prototypes={newProtos.Length} 个、instances={finalInstances.Length} 棵逐字段相同）。");
            }

            // 10) 日志：森林棵数、总棵数、prototype 区间、采样效率、物种直方图、径向密度直方图。
            var histogramParts = new List<string>(sortedForestPrefabs.Length);
            for (int i = 0; i < sortedForestPrefabs.Length; i++)
            {
                string prefabName = sortedForestPrefabs[i] != null ? sortedForestPrefabs[i].name : "<null>";
                histogramParts.Add($"{prefabName}={speciesHistogram[i]}");
            }
            var radialParts = new List<string>(RadialBandCount);
            for (int i = 0; i < RadialBandCount; i++)
                radialParts.Add($"{RadialBandLabel(i)}={radialHistogram[i]}");
            float acceptRate = attemptsUsed > 0 ? (float)forest.Count / attemptsUsed : 0f;
            Debug.Log($"[ForestSeeder] 森林播种完成：森林 {forest.Count} 棵 / 中央 {preservedCenter.Length} 棵 / 总 {finalInstances.Length} 棵；" +
                $"森林 prototype 区间 [{baseCount}, {baseCount + sortedForestPrefabs.Length})；" +
                $"硬上限 MaxTotalInstances={MaxTotalInstances}（capacity={forestCapacity}）；" +
                $"attemptsUsed={attemptsUsed}/{ForestAttempts}，接受率={acceptRate:P2}（forest/attemptsUsed），触顶 forestCapacity={capacityHit}；" +
                $"展示环清空={UnderstorySeeder.BuildShowcaseRing().Count} 处（排除候选 {showcaseExcluded} 个）；" +
                $"写回={(wroteBack ? "已写入" : "跳过（与现有一致）")}；" +
                $"物种直方图: {string.Join(", ", histogramParts.ToArray())}；" +
                $"径向密度直方图（到核心矩形原始距离 d）: {string.Join(", ", radialParts.ToArray())}");
        }

        // ------------------------------------------------------------------ 内部辅助

        /// <summary>
        /// 到核心矩形的最短距离（米），矩形内为 0。声明为 internal 供播种循环与 ValidateProject 共用同一份换算。
        /// </summary>
        internal static float DistanceToCoreRect(float worldX, float worldZ)
        {
            float dx = Mathf.Max(Mathf.Max(CoreMinX - worldX, worldX - CoreMaxX), 0f);
            float dz = Mathf.Max(Mathf.Max(CoreMinZ - worldZ, worldZ - CoreMaxZ), 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// 世界 XZ 是否落在核心矩形内（含边界）。供 ValidateProject 做「中央活动区森林树零泄漏」断言。
        /// </summary>
        internal static bool IsInsideCoreRect(Vector2 worldXZ)
        {
            return worldXZ.x >= CoreMinX && worldXZ.x <= CoreMaxX &&
                   worldXZ.y >= CoreMinZ && worldXZ.y <= CoreMaxZ;
        }

        /// <summary>
        /// 原始距离 d 所属的径向环带下标（[5,10) [10,15) [15,20) [20,25) [25,+)）。
        /// </summary>
        private static int RadialBandOf(float d)
        {
            for (int i = 0; i < RadialBandEdges.Length; i++)
                if (d < RadialBandEdges[i]) return i;
            return RadialBandCount - 1;
        }

        private static string RadialBandLabel(int band)
        {
            if (band <= 0)
                return $"[{ForestHardMargin:0},{RadialBandEdges[0]:0})";
            if (band >= RadialBandCount - 1)
                return $"[{RadialBandEdges[RadialBandEdges.Length - 1]:0}+)";
            return $"[{RadialBandEdges[band - 1]:0},{RadialBandEdges[band]:0})";
        }

        /// <summary>
        /// TreePrototype 数组等值比较（TreePrototype 是 class，getter 每次返回新对象，故比内容不比引用）：
        /// 比长度 + 逐个 prefab 引用 + bendFactor。
        /// </summary>
        private static bool TreePrototypesEqual(TreePrototype[] a, TreePrototype[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                TreePrototype x = a[i];
                TreePrototype y = b[i];
                if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;
                if (x.prefab != y.prefab) return false;   // 同一资产跨 Bake 引用相等
                if (!Mathf.Approximately(x.bendFactor, y.bendFactor)) return false;
            }
            return true;
        }

        /// <summary>
        /// TreeInstance 数组逐元素逐字段比较（TreeInstance 是 struct，未重写 Equals，不依赖默认反射比较）：
        /// position / widthScale / heightScale / rotation / prototypeIndex / color / lightmapColor。
        /// float 字段用 Mathf.Approximately 容忍二进制资产 round-trip 的 ULP 级误差；判定偏保守 ——
        /// 若误判为「不同」，代价只是多做一次幂等写回（安全方向），绝不会漏写。
        /// </summary>
        private static bool TreeInstancesEqual(TreeInstance[] a, TreeInstance[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                TreeInstance x = a[i];
                TreeInstance y = b[i];
                if (x.prototypeIndex != y.prototypeIndex) return false;
                if (x.position != y.position) return false;             // Vector3 == 自带 1e-5 级容差
                if (!Color32Equal(x.color, y.color)) return false;      // color/lightmapColor 是 Color32，无 ==/!= 运算符
                if (!Color32Equal(x.lightmapColor, y.lightmapColor)) return false;
                if (!Mathf.Approximately(x.widthScale, y.widthScale)) return false;
                if (!Mathf.Approximately(x.heightScale, y.heightScale)) return false;
                if (!Mathf.Approximately(x.rotation, y.rotation)) return false;
            }
            return true;
        }

        /// <summary>
        /// Color32 逐分量比较（Color32 是 byte 四元组 struct，Unity 未提供 ==/!= 运算符）。
        /// </summary>
        private static bool Color32Equal(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }

        /// <summary>
        /// 构建物种加权表：当 sortedForestPrefabs 为 NSk2 默认 4 种（tree01..tree04）时使用 35/25/25/15；
        /// 其他情形（资产包变更）退化为均匀权重，保证不崩。
        /// </summary>
        private static int[] BuildSpeciesWeights(GameObject[] sortedForestPrefabs)
        {
            if (sortedForestPrefabs.Length == SpeciesWeights4.Length &&
                NameContains(sortedForestPrefabs[0], "tree01") &&
                NameContains(sortedForestPrefabs[1], "tree02") &&
                NameContains(sortedForestPrefabs[2], "tree03") &&
                NameContains(sortedForestPrefabs[3], "tree04"))
            {
                return (int[])SpeciesWeights4.Clone();
            }

            int n = sortedForestPrefabs.Length;
            int[] uniform = new int[n];
            for (int i = 0; i < n; i++) uniform[i] = 1;
            return uniform;
        }

        private static bool NameContains(GameObject go, string token)
        {
            return go != null && go.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 在权重数组上做一次随机抽取（roll ∈ [0, weightSum)，累加直至命中）。
        /// </summary>
        private static int WeightedPick(System.Random random, int[] weights, int weightSum)
        {
            if (weights.Length == 0) return 0;
            if (weights.Length == 1) return 0;
            int roll = random.Next(weightSum);
            int acc = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                acc += weights[i];
                if (roll < acc) return i;
            }
            return weights.Length - 1; // 数值边界兜底
        }
    }
}
