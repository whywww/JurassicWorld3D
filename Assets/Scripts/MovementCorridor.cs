using System.Collections.Generic;
using UnityEngine;

namespace JurassicWorld
{
    /// <summary>
    /// Dense Jungle Phase 1 的两条轻 S 型移动通道。此处是路线中心线与左右半宽的唯一数据源；
    /// 出生分派、逐帧沿线移动、编辑态净空验收和可开关标记均使用相同采样。
    /// 两条通道不共享路段，以免现阶段恐龙在岔口会车。Vector2 统一表示世界 XZ。
    /// </summary>
    public static class MovementCorridor
    {
        // ================================================================ 路线索引（跨文件契约）

        /// <summary>西侧通道。</summary>
        public const int MainRoute = 0;

        /// <summary>东侧通道。保留旧常量名以兼容既有调用。</summary>
        public const int ForegroundRoute = 1;

        /// <summary>
        /// 中景西支路【Phase 1.1d 起停用】：数据保留但未注册进网络，索引 2 已越界，
        /// 禁止传入本类 API（恢复时把数组重新注册进 RouteControlPoints 等三张表）。
        /// </summary>
        public const int MidgroundRoute = 2;

        /// <summary>
        /// 东北林缘支路【Phase 1.1d 起停用】：数据保留但未注册进网络，索引 3 已越界，禁止传入本类 API。
        /// </summary>
        public const int ForestEdgeRoute = 3;

        /// <summary>路线总数。</summary>
        public static int RouteCount => RouteNames.Length;

        // ================================================================ 形状不变量（ValidateProject 断言用）

        /// <summary>
        /// 单条路线全宽下限（米）。Phase 1.1b「缩窄轨道」后维持 11：
        /// 仍是「约开放区宽度 1/3 的狭窄通道」，且是分道对会的硬下限
        /// （DinosaurAgent.LaneBiasScale ×（11 − 2×LaneEdgeMargin）= 0.85×9 = 7.65 ≥ 霸王龙+伶盗龙互斥半径 6.5，
        /// 恒定 1.15m 余量 ⇒ 同路线两只恐龙固定车道下任何时刻都不互判阻挡），
        /// 再窄会让让行逻辑长期顶死（撞上僵住的老缺陷）。
        /// </summary>
        public const float MinFullWidth = 11f;

        /// <summary>
        /// 路线全宽上限（米）。Phase 1.1「缩窄轨道」后由 17 收到 14（实际 11–13m）：
        /// 再宽就退回「大草坪」，后续植被做不出遮挡节奏，恐龙也会显得在空地上乱走。
        /// </summary>
        public const float MaxFullWidth = 14f;

        /// <summary>中心线最小横向摆幅（米）：低于此值视为「退化成直线」。对每条路线断言。</summary>
        public const float MinSwingMeters = 10f;

        /// <summary>左右半宽不对称判定阈值（米）：存在样本 |左-右| 大于它即认为该路线非镜像对称。</summary>
        public const float MinAsymmetryMeters = 0.5f;

        /// <summary>
        /// Phase 1 必须恰有两条通道。
        /// </summary>
        public const int MinRouteCount = 2;

        /// <summary>支路与主干中心线的最大偏离下限（米）：低于它说明支路只是主干的平行复制品。</summary>
        public const float MinBranchSeparationMeters = 8f;

        /// <summary>支路端点到主干中心线的最大距离（米）：超过它说明支路没有真正并回主干（未重新汇合）。</summary>
        public const float MergeToleranceMeters = 2f;

        /// <summary>岔口判定容差（米）：不同路线的控制点间距 ≤ 此值即视为同一路口（共享汇合点，恐龙可在此换轨）。</summary>
        public const float JunctionTolerance = 0.75f;

        /// <summary>网络最少岔口数：少于它恐龙在途中无处换轨，「分叉网络」名存实亡（ValidateProject ⑥c 断言）。</summary>
        public const int MinJunctionCount = 2;

        /// <summary>
        /// 轨迹净空带余量（米）：树中心距路线中心线、在【所在侧半宽 + 本值】以内即视为侵入轨迹
        /// （ValidateProject ⑦ 硬门 + SeedTrees 播种后自动外推）。推导：最外车道 = LaneBiasScale×(half−1)
        /// ≈ 4.7m（half=6.5 时），树干避让半径 2.5 + 树干 ≈0.9 ⇒ 树须在 half + 1.8m 之外才不与车道发生
        /// 滑移/阻挡。轨迹内（含贴边）零障碍 = 用户要求「轨迹上不要有障碍物」。
        /// </summary>
        public const float CorridorTreeClearVerge = 1.8f;

        // ================================================================ 几何数据（Phase 1.1 全网络）

        private static readonly string[] RouteNames =
        {
            "West S（西侧）",
            "East S（东侧）",
        };

        /// <summary>
        /// 西侧轻 S：从南侧草地进入，经两次缓弯到北侧草地，首尾均收在森林核心区内。
        /// </summary>
        private static readonly Vector2[] MainControlPoints =
        {
            new Vector2(-13f, -27f),
            new Vector2(-19f, -10f),
            new Vector2(-8f, 8f),
            new Vector2(-16f, 25f),
            new Vector2(-9f, 44f),
        };

        /// <summary>主干逐控制点左半宽（米）。Phase 1.1b 再收窄：全宽 11.5 / 11.0 / 13.0 / 11.0 / 13.0 / 11.5 / 12.5（原 Phase 1 = 12.5–16.5）。</summary>
        private static readonly float[] MainHalfWidthLeft =
        {
            6.5f, 5.25f, 7f, 5.75f, 6.75f,
        };

        /// <summary>主干逐控制点右半宽（米）。与左半宽错开，保证不对称（最大左右差 2.0m ≥ MinAsymmetryMeters）。</summary>
        private static readonly float[] MainHalfWidthRight =
        {
            5f, 5.75f, 6f, 5.25f, 5.75f,
        };

        /// <summary>
        /// 东侧轻 S：与西侧通道分离，保留足够的会车净距。
        /// </summary>
        private static readonly Vector2[] ForegroundControlPoints =
        {
            new Vector2(13f, -27f),
            new Vector2(21f, -10f),
            new Vector2(10f, 8f),
            new Vector2(17f, 25f),
            new Vector2(12f, 44f),
        };

        private static readonly float[] ForegroundHalfWidthLeft =
        {
            6f, 5.5f, 6.5f, 5.5f, 6.5f,
        };

        private static readonly float[] ForegroundHalfWidthRight =
        {
            5.5f, 6f, 5f, 6f, 5.5f,
        };

        /// <summary>
        /// 【Phase 1.1d 停用】中景西支路控制点：与主干共享起点两枚控制点（(-14,-30) 与路口点 (-18,-12)），
        /// 向西甩到 x≈-34（R0 开阔地西界 x=-20 之外，低矮植被梯度门在此已允许非零密度 ⇒
        /// 未来 fern/bush 能布到这里做部分遮挡），沿中景 z∈[0,40] 北上，最后并回主干控制点 (6,46)。
        /// 【Phase 1.1b 避树微调】P1 (-16,-12)→(-18,-12)（跟随主干）：中心带散树 (-11.3,-22.2)
        /// 甩到横向 ≥4m 外。
        /// 恢复方法：把本数组重新注册进 RouteNames / RouteControlPoints / RouteHalfWidthLeft / Right，
        /// 并把 <see cref="MinRouteCount"/> 改回 4、恢复 ValidateProject ⑧ 的中景角色断言。
        /// </summary>
        private static readonly Vector2[] MidgroundControlPoints =
        {
            new Vector2(-14f, -30f),
            new Vector2(-18f, -12f),   // ← 与主干共享（路口）
            new Vector2(-26f, -2f),
            new Vector2(-34f, 12f),
            new Vector2(-31f, 28f),
            new Vector2(-20f, 40f),
            new Vector2(6f, 46f),      // ← 与主干共享（并回点）
        };

        private static readonly float[] MidgroundHalfWidthLeft =
        {
            6.5f, 5.25f, 6.5f, 5.5f, 6.5f, 5.5f, 5.5f,
        };

        private static readonly float[] MidgroundHalfWidthRight =
        {
            5f, 5.75f, 5.5f, 6.5f, 5.5f, 6.5f, 6f,
        };

        /// <summary>
        /// 【Phase 1.1d 停用】东北林缘支路控制点：与主干共享 (8,18)、(14,32) 后向东绕出（核心矩形东界 60，
        /// 林缘带判定阈值 18m ⇒ x≥42 即在林缘带内），沿东北弧线走一圈，回到主干 (6,46) 汇合。
        /// 与前景支路共享 (8,18)/(14,32)：网络在 (14,32) 分叉、在 (6,46) 三线汇合。
        /// 【Phase 1.1b 避树微调】原 (26,38)(38,34)(46,42)(44,54)(30,60)(14,52)
        /// → (26,34)(40,30)(48,38)(44,50)(30,57)(14,50)：让东弧从散树
        /// (42.3,38.5)(43.9,40.1) 的**南侧**绕过、北段南收避开 (23.7,61.3)，
        /// 同时把 (26,38) 附近的 (25.4,39.7) 甩到横向 ≥4m 外。
        /// 恢复方法同 <see cref="MidgroundControlPoints"/>。
        /// </summary>
        private static readonly Vector2[] ForestEdgeControlPoints =
        {
            new Vector2(8f, 18f),      // ← 与主干共享（分叉点）
            new Vector2(14f, 32f),     // ← 与主干共享
            new Vector2(26f, 34f),
            new Vector2(40f, 30f),
            new Vector2(48f, 38f),
            new Vector2(44f, 50f),
            new Vector2(30f, 57f),
            new Vector2(14f, 50f),
            new Vector2(6f, 46f),      // ← 与主干共享（并回点）
        };

        private static readonly float[] ForestEdgeHalfWidthLeft =
        {
            5.75f, 7.5f, 6f, 5.5f, 6.5f, 5.5f, 6.5f, 5.5f, 5.5f,
        };

        private static readonly float[] ForestEdgeHalfWidthRight =
        {
            5.25f, 5.5f, 5.5f, 6.5f, 5.5f, 6.5f, 5.5f, 6.5f, 6f,
        };

        // ── 注册表（Phase 1.1d：暂留 主干 + 前景；中景/林缘数据在上方保留、未注册）──
        private static readonly Vector2[][] RouteControlPoints =
        {
            MainControlPoints, ForegroundControlPoints,
        };

        private static readonly float[][] RouteHalfWidthLeft =
        {
            MainHalfWidthLeft, ForegroundHalfWidthLeft,
        };

        private static readonly float[][] RouteHalfWidthRight =
        {
            MainHalfWidthRight, ForegroundHalfWidthRight,
        };

        /// <summary>每段控制点间的采样数（Catmull-Rom 细分）。24 段 × 6 = ≈0.6m 间距的 LUT。</summary>
        private const int SamplesPerSegment = 24;

        // ================================================================ 岔口（多路线共享的汇合点）

        /// <summary>岔口世界位置（由跨路线重复的控制点聚类得到，见 <see cref="BuildJunctions"/>）。
        /// 注：非 readonly——C# 不允许 static readonly 在静态构造函数调用的辅助方法里赋值。</summary>
        private static Vector2[] junctionPoints;

        /// <summary>每个岔口经过的路线集合（长度 ≥2，否则不成其为岔口）。</summary>
        private static List<int>[] junctionRoutes;

        /// <summary>岔口数量。DinosaurAgent 用它做「过路口分流判定」（Phase 1.1d 起换轨开关暂关，仅结构断言用）。</summary>
        public static int JunctionCount => junctionRoutes.Length;

        /// <summary>
        /// 聚类出全部岔口：把在册路线的控制点按 <see cref="JunctionTolerance"/> 归堆，
        /// 只保留「被 ≥2 条路线经过」的堆。纯几何推导、构建期执行一次。
        /// </summary>
        private static void BuildJunctions()
        {
            var unique = new List<Vector2>();
            var owners = new List<List<int>>();

            for (int r = 0; r < RouteControlPoints.Length; r++)
            {
                Vector2[] points = RouteControlPoints[r];
                for (int i = 0; i < points.Length; i++)
                {
                    int slot = -1;
                    for (int k = 0; k < unique.Count; k++)
                    {
                        if (Vector2.Distance(unique[k], points[i]) <= JunctionTolerance)
                        {
                            slot = k;
                            break;
                        }
                    }
                    if (slot < 0)
                    {
                        unique.Add(points[i]);
                        owners.Add(new List<int>());
                        slot = unique.Count - 1;
                    }
                    if (!owners[slot].Contains(r))
                        owners[slot].Add(r);
                }
            }

            var keptPoints = new List<Vector2>();
            var keptRoutes = new List<List<int>>();
            for (int k = 0; k < unique.Count; k++)
            {
                if (owners[k].Count < 2)
                    continue;   // 只被一条路线经过 = 普通控制点，不是岔口
                keptPoints.Add(unique[k]);
                keptRoutes.Add(owners[k]);
            }

            junctionPoints = keptPoints.ToArray();
            junctionRoutes = keptRoutes.ToArray();
        }

        /// <summary>第 index 个岔口的世界位置（日志/调试用）。</summary>
        public static Vector2 GetJunctionPosition(int index) => junctionPoints[index];

        /// <summary>第 index 个岔口经过的路线条数（恒 ≥2）。</summary>
        public static int JunctionRouteCount(int index) => junctionRoutes[index].Count;

        /// <summary>第 index 个岔口经过的第 k 条路线索引。</summary>
        public static int GetJunctionRoute(int index, int k) => junctionRoutes[index][k];

        /// <summary>找 world XZ 附近的岔口（世界距离 ≤ radius，取最近者）；没有则返回 -1。</summary>
        public static int FindJunction(Vector2 xz, float radius)
        {
            int best = -1;
            float bestSqr = radius * radius;
            for (int k = 0; k < junctionRoutes.Length; k++)
            {
                float sqr = (junctionPoints[k] - xz).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = k;
                }
            }
            return best;
        }

        // ================================================================ LUT（静态构造一次，之后只读）

        /// <summary>通道上的一个采样点：位置 + 切线 + 弧长 + 该处左右半宽。</summary>
        public struct CorridorSample
        {
            /// <summary>世界 XZ 位置。</summary>
            public Vector2 Position;

            /// <summary>单位切线（沿通道前进方向）。</summary>
            public Vector2 Tangent;

            /// <summary>从该路线起点算起的弧长（米）。</summary>
            public float Distance;

            /// <summary>该处左半宽（米，沿左法线）。</summary>
            public float HalfLeft;

            /// <summary>该处右半宽（米，沿右法线）。</summary>
            public float HalfRight;

            /// <summary>左法线 = 切线逆时针旋转 90°：(-t.z, t.x)。lateral &gt; 0 即偏向左侧。</summary>
            public Vector2 LeftNormal => new Vector2(-Tangent.y, Tangent.x);

            /// <summary>该处通道全宽（米）。</summary>
            public float FullWidth => HalfLeft + HalfRight;
        }

        /// <summary>一条路线的完整 LUT（构建后只读）。</summary>
        private sealed class RouteData
        {
            public string Name;
            public CorridorSample[] Samples;
            public float Length;
        }

        private static readonly RouteData[] routes;

        static MovementCorridor()
        {
            routes = new RouteData[RouteControlPoints.Length];
            for (int r = 0; r < routes.Length; r++)
                routes[r] = BuildRoute(r);

            BuildJunctions();   // 路线几何就绪后聚类岔口（依赖控制点数组，不依赖 LUT）
        }

        /// <summary>
        /// 该点是否为主干控制点（岔口判据的简化版：共享点都与主干同位置，容差 <see cref="JunctionTolerance"/>）。
        /// 支路自己的独有控制点距任何主干点均远大于该容差，不会误判。
        /// </summary>
        private static bool IsMainControlPoint(Vector2 point) => MainIndexOf(point) >= 0;

        private static int MainIndexOf(Vector2 point)
        {
            for (int m = 0; m < MainControlPoints.Length; m++)
                if (Vector2.Distance(MainControlPoints[m], point) <= JunctionTolerance)
                    return m;
            return -1;
        }

        /// <summary>
        /// 主干上该控制点的规范虚拟邻点：direction &lt; 0 = 前驱、&gt; 0 = 后继；
        /// 首/尾点按 Catmull-Rom 端点规则自我复制。用于岔口切线统一（见 BuildRoute 内注释）。
        /// </summary>
        private static Vector2 MainNeighborOf(Vector2 point, int direction)
        {
            int m = MainIndexOf(point);
            int last = MainControlPoints.Length - 1;
            if (m < 0)
                return point;   // 不可达（调用前已用 IsMainControlPoint 门控）；防御性返回自身
            if (direction < 0)
                return MainControlPoints[m > 0 ? m - 1 : 0];
            return MainControlPoints[m < last ? m + 1 : last];
        }

        private static RouteData BuildRoute(int route)
        {
            Vector2[] controlPoints = RouteControlPoints[route];
            float[] halfLeft = RouteHalfWidthLeft[route];
            float[] halfRight = RouteHalfWidthRight[route];

            var points = new List<Vector2>(controlPoints.Length * SamplesPerSegment);
            var lefts = new List<float>(controlPoints.Length * SamplesPerSegment);
            var rights = new List<float>(controlPoints.Length * SamplesPerSegment);

            int last = controlPoints.Length - 1;
            for (int i = 0; i < last; i++)
            {
                // Catmull-Rom 端点处理：首尾控制点各复制一次作为虚拟邻点，曲线严格过首尾点。
                Vector2 p0 = controlPoints[i > 0 ? i - 1 : 0];
                Vector2 p1 = controlPoints[i];
                Vector2 p2 = controlPoints[i + 1];
                Vector2 p3 = controlPoints[i + 2 <= last ? i + 2 : last];

                // ── 岔口切线统一（Phase 1.1c）：与主干同位置的控制点（= 岔口），其入/出虚拟邻点
                // 一律改用【主干的邻点】。效果：
                //  ① 段两端都是岔口点的【共享段】在所有路线上采样完全一致（同一条线）——
                //     旧实现各路线用自己的邻点算切线，共享段中段可偏差数米，分属两条路线的恐龙
                //     车道会被挤到互斥半径以内（多恐龙互判阻挡的来源之一）；
                //  ② 分叉/汇入段在岔口处切线相同 ⇒ 路线从路口以同一朝向离开/汇入，路口不打结。
                // 主干自身恒等映射（邻点就是自己），故主干几何不受影响。
                if (IsMainControlPoint(p1))
                    p0 = MainNeighborOf(p1, -1);
                if (IsMainControlPoint(p2))
                    p3 = MainNeighborOf(p2, +1);

                for (int s = 0; s < SamplesPerSegment; s++)
                {
                    float t = (float)s / SamplesPerSegment;
                    points.Add(CatmullRom(p0, p1, p2, p3, t));
                    // 半宽沿段线性插值（比样条更可控：不会在段间过冲出新的最窄/最宽点）
                    lefts.Add(Mathf.Lerp(halfLeft[i], halfLeft[i + 1], t));
                    rights.Add(Mathf.Lerp(halfRight[i], halfRight[i + 1], t));
                }
            }
            points.Add(controlPoints[last]);
            lefts.Add(halfLeft[last]);
            rights.Add(halfRight[last]);

            var samples = new CorridorSample[points.Count];
            float cumulative = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 tangent = i == 0
                    ? points[1] - points[0]
                    : i == points.Count - 1
                        ? points[i] - points[i - 1]
                        : points[i + 1] - points[i - 1];
                if (tangent.sqrMagnitude < 1e-12f)
                    tangent = Vector2.right;
                tangent.Normalize();

                if (i > 0)
                    cumulative += Vector2.Distance(points[i], points[i - 1]);

                samples[i] = new CorridorSample
                {
                    Position = points[i],
                    Tangent = tangent,
                    Distance = cumulative,
                    HalfLeft = lefts[i],
                    HalfRight = rights[i],
                };
            }

            return new RouteData { Name = RouteNames[route], Samples = samples, Length = cumulative };
        }

        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * p1)
                + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        // ================================================================ 查询 API（全部按路线）

        /// <summary>取某条路线的定义（越界即抛，路线索引是编译期常量级契约，不该越界）。</summary>
        private static RouteData Route(int route)
        {
            if (route < 0 || route >= routes.Length)
                throw new System.ArgumentOutOfRangeException(nameof(route),
                    $"路线索引 {route} 越界（RouteCount={routes.Length}）。");
            return routes[route];
        }

        /// <summary>某条路线的显示名（日志/校验信息用）。</summary>
        public static string GetRouteName(int route) => Route(route).Name;

        /// <summary>某条路线的总弧长（米）。</summary>
        public static float Length(int route) => Route(route).Length;

        /// <summary>某条路线的 LUT 样本数。</summary>
        public static int SampleCount(int route) => Route(route).Samples.Length;

        /// <summary>取该路线第 index 个 LUT 样本（供 debug 标注画折线）。</summary>
        public static CorridorSample GetSample(int route, int index) => Route(route).Samples[index];

        /// <summary>
        /// 按弧长插值取样：返回该处位置、切线与左右半宽。distance 自动钳到 [0, Length(route)]。
        /// </summary>
        public static void GetSampleAt(int route, float distance, out Vector2 position, out Vector2 tangent,
            out float halfLeft, out float halfRight)
        {
            RouteData data = Route(route);
            CorridorSample[] samples = data.Samples;

            if (distance <= 0f)
            {
                position = samples[0].Position;
                tangent = samples[0].Tangent;
                halfLeft = samples[0].HalfLeft;
                halfRight = samples[0].HalfRight;
                return;
            }
            if (distance >= data.Length)
            {
                CorridorSample lastSample = samples[samples.Length - 1];
                position = lastSample.Position;
                tangent = lastSample.Tangent;
                halfLeft = lastSample.HalfLeft;
                halfRight = lastSample.HalfRight;
                return;
            }

            int hi = IndexOfDistance(samples, distance);
            int lo = hi > 0 ? hi - 1 : 0;
            CorridorSample a = samples[lo];
            CorridorSample b = samples[hi];
            float span = b.Distance - a.Distance;
            float u = span > 1e-6f ? Mathf.Clamp01((distance - a.Distance) / span) : 0f;

            position = Vector2.Lerp(a.Position, b.Position, u);
            tangent = (Vector2.Lerp(a.Tangent, b.Tangent, u)).normalized;
            if (tangent.sqrMagnitude < 1e-8f)
                tangent = a.Tangent;
            halfLeft = Mathf.Lerp(a.HalfLeft, b.HalfLeft, u);
            halfRight = Mathf.Lerp(a.HalfRight, b.HalfRight, u);
        }

        /// <summary>按弧长取 LUT 样本下标（二分；samples[i].Distance 升序）。</summary>
        private static int IndexOfDistance(CorridorSample[] samples, float distance)
        {
            int lo = 0;
            int hi = samples.Length - 1;
            if (distance <= samples[0].Distance)
                return 0;
            if (distance >= samples[hi].Distance)
                return hi;

            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (samples[mid].Distance < distance)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return lo == 0 ? 0 : lo; // lo 是第一个 >= distance 的样本
        }

        /// <summary>
        /// 该路线上弧长 distance、横向偏移 lateral 处的世界 XZ。
        /// lateral &gt; 0 = 左法线方向，&lt; 0 = 右法线方向。distance 钳到 [0, Length(route)]。
        /// </summary>
        public static Vector2 PointAt(int route, float distance, float lateral)
        {
            GetSampleAt(route, distance, out Vector2 position, out Vector2 tangent, out _, out _);
            var normal = new Vector2(-tangent.y, tangent.x);
            return position + normal * lateral;
        }

        /// <summary>
        /// 把世界 XZ 投影到该路线：返回中心线弧长（不钳，越出端点为负 / 大于 Length）与横向偏移
        /// （相对最近样本左法线，&gt;0 在左）。算法 = 最近 LUT 样本 + 沿该样本切线的一次线性投影。
        /// </summary>
        public static void ProjectUnclamped(int route, Vector2 xz, out float distance, out float lateral)
        {
            CorridorSample[] samples = Route(route).Samples;
            int best = 0;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < samples.Length; i++)
            {
                Vector2 delta = xz - samples[i].Position;
                float sqr = delta.sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = i;
                }
            }

            CorridorSample sample = samples[best];
            Vector2 rel = xz - sample.Position;
            float along = Vector2.Dot(rel, sample.Tangent);
            lateral = Vector2.Dot(rel, sample.LeftNormal);
            distance = sample.Distance + along;
        }

        /// <summary>
        /// 投影到该路线并把弧长钳进 [0, Length(route)]（恐龙行为侧常用形态：只要「路线内的落点」）。
        /// </summary>
        public static void ProjectToCorridor(int route, Vector2 xz, out float distance, out float lateral)
        {
            ProjectUnclamped(route, xz, out distance, out lateral);
            distance = Mathf.Clamp(distance, 0f, Route(route).Length);
        }

        /// <summary>
        /// 把横向偏移钳进该弧长处的路线净宽（margin 为每侧需要留出的余量）：
        /// 返回值 ∈ [margin - halfRight, halfLeft - margin]。用于保证漫步目标永远落在路线内部。
        /// </summary>
        public static Vector2 ClampLateral(int route, float distance, float lateral, float margin)
        {
            GetSampleAt(route, distance, out _, out _, out float halfLeft, out float halfRight);
            float min = Mathf.Min(margin - halfRight, halfLeft - margin);
            float max = Mathf.Max(margin - halfRight, halfLeft - margin);
            return PointAt(route, distance, Mathf.Clamp(lateral, min, max));
        }

        /// <summary>
        /// 到该路线的**带符号**距离（米）：负值 = 在路线内（绝对值 = 到最近边界的净距），正值 = 在路线外。
        /// 端帽之外的点按「到端点的距离」判为外部，避免沿中心线延长线上的点被误判为路线内。
        /// </summary>
        public static float SignedDistanceToCorridor(int route, float worldX, float worldZ)
        {
            CorridorSample[] samples = Route(route).Samples;
            var p = new Vector2(worldX, worldZ);
            ProjectUnclamped(route, p, out float distance, out float lateral);

            if (distance < 0f)
                return Vector2.Distance(p, samples[0].Position);
            if (distance > Route(route).Length)
                return Vector2.Distance(p, samples[samples.Length - 1].Position);

            GetSampleAt(route, distance, out _, out _, out float halfLeft, out float halfRight);
            float half = lateral >= 0f ? halfLeft : halfRight;
            return Mathf.Abs(lateral) - half;
        }

        /// <summary>点是否在该路线内（要求距最近边界至少 margin 米；margin=0 即含边界）。</summary>
        public static bool IsInsideCorridor(int route, float worldX, float worldZ, float margin) =>
            SignedDistanceToCorridor(route, worldX, worldZ) <= -Mathf.Abs(margin);

        /// <summary>任意点到某条路线中心线的最小距离（米，扫该路线样本求最小值）。</summary>
        public static float MinDistanceToRoute(Vector2 xz, int otherRoute)
        {
            CorridorSample[] samples = Route(otherRoute).Samples;
            float best = float.MaxValue;
            for (int i = 0; i < samples.Length; i++)
                best = Mathf.Min(best, Vector2.Distance(xz, samples[i].Position));
            return best;
        }

        /// <summary>该路线端点到另一条路线中心线的最小距离（米）：判断支路是否真的并回了主干。</summary>
        public static float EndpointDistanceToRoute(int route, int otherRoute, bool useStart)
        {
            CorridorSample[] samples = Route(route).Samples;
            Vector2 endpoint = useStart ? samples[0].Position : samples[samples.Length - 1].Position;
            return MinDistanceToRoute(endpoint, otherRoute);
        }

        /// <summary>路线整体度量（ValidateProject 的形状不变量据此断言，避免校验侧重算一遍几何）。</summary>
        public struct CorridorMetrics
        {
            /// <summary>中心线 XZ 包围盒（含控制点曲率后的实际范围）。</summary>
            public Rect CenterlineBounds;

            /// <summary>中心线横向摆幅（米）= 包围盒宽。</summary>
            public float SwingX;

            /// <summary>中心线纵深跨度（米）= 包围盒高。</summary>
            public float SpanZ;

            /// <summary>最小/最大路线全宽（米）。</summary>
            public float MinFullWidthSeen;

            /// <summary>最大路线全宽（米）。</summary>
            public float MaxFullWidthSeen;

            /// <summary>最大左右半宽差（米）= 不对称程度。</summary>
            public float MaxAsymmetry;

            /// <summary>含左右边界的整体包围盒（断言 ⊆ 核心矩形 / 活动包络用）。</summary>
            public Rect OuterBounds;
        }

        /// <summary>扫描一遍该路线 LUT 得到整体度量（校验/日志按需调用，不缓存以保持纯函数）。</summary>
        public static CorridorMetrics Measure(int route)
        {
            CorridorSample[] samples = Route(route).Samples;
            var metrics = new CorridorMetrics
            {
                MinFullWidthSeen = float.MaxValue,
                MaxFullWidthSeen = 0f,
                MaxAsymmetry = 0f,
            };

            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            float outerMinX = float.MaxValue, outerMaxX = float.MinValue;
            float outerMinZ = float.MaxValue, outerMaxZ = float.MinValue;

            for (int i = 0; i < samples.Length; i++)
            {
                CorridorSample s = samples[i];
                Vector2 normal = s.LeftNormal;
                Vector2 left = s.Position + normal * s.HalfLeft;
                Vector2 right = s.Position - normal * s.HalfRight;

                minX = Mathf.Min(minX, s.Position.x);
                maxX = Mathf.Max(maxX, s.Position.x);
                minZ = Mathf.Min(minZ, s.Position.y);
                maxZ = Mathf.Max(maxZ, s.Position.y);

                outerMinX = Mathf.Min(outerMinX, left.x, right.x);
                outerMaxX = Mathf.Max(outerMaxX, left.x, right.x);
                outerMinZ = Mathf.Min(outerMinZ, left.y, right.y);
                outerMaxZ = Mathf.Max(outerMaxZ, left.y, right.y);

                metrics.MinFullWidthSeen = Mathf.Min(metrics.MinFullWidthSeen, s.FullWidth);
                metrics.MaxFullWidthSeen = Mathf.Max(metrics.MaxFullWidthSeen, s.FullWidth);
                metrics.MaxAsymmetry = Mathf.Max(metrics.MaxAsymmetry, Mathf.Abs(s.HalfLeft - s.HalfRight));
            }

            metrics.CenterlineBounds = Rect.MinMaxRect(minX, minZ, maxX, maxZ);
            metrics.OuterBounds = Rect.MinMaxRect(outerMinX, outerMinZ, outerMaxX, outerMaxZ);
            metrics.SwingX = maxX - minX;
            metrics.SpanZ = maxZ - minZ;
            return metrics;
        }

        /// <summary>单行摘要（每条路线一段）：供 Bake 日志与验收报告共用措辞（同 WorldBounds.Describe 先例）。</summary>
        public static string Describe()
        {
            var builder = new System.Text.StringBuilder();
            builder.Append($"恐龙移动通道网络：{RouteCount} 条路线");
            for (int r = 0; r < RouteCount; r++)
            {
                CorridorMetrics m = Measure(r);
                Rect b = m.CenterlineBounds;
                builder.Append(r == 0 ? "；" : " | ");
                builder.Append($"{GetRouteName(r)} 长 {Length(r):F1}m、全宽 {m.MinFullWidthSeen:F1}–{m.MaxFullWidthSeen:F1}m");
                builder.Append($"、范围 x[{b.xMin:F0},{b.xMax:F0}] z[{b.yMin:F0},{b.yMax:F0}]");
            }
            return builder.ToString();
        }
    }
}
