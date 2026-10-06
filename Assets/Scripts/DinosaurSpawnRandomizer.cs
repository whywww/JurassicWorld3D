using System.Collections.Generic;
using UnityEngine;

namespace JurassicWorld
{
    /// <summary>
    /// 运行期开局布置器：每次进入 Play（RuntimeInitializeOnLoadMethod.AfterSceneLoad——
    /// 场景对象 Awake/OnEnable 完成之后、各 Start 与首帧渲染之前，画面不会闪现原位置）
    /// 把场上所有 DinosaurAgent 固定分派到各自路线上、放到该线**车道**内的随机出生点
    /// （朝向沿路线切线），并同步重置漫步圆心（spawnPoint）。
    ///
    /// 采样约束（与硬编码运行期契约对齐）：
    ///  - Dense Jungle Phase 1 → 1.1 → 1.1d：候选点由 **MovementCorridor（恐龙移动通道网络）** 生成 ——
    ///    开局按占位顺序（体型降序）**固定分派**路线：i % RouteCount ⇒ 每只恐龙各一条、跨运行确定
    ///    （Phase 1.1d 起停用「随机选线」与途中岔口换轨，用户要求「先保留两条路线，各沿一条走」）；
    ///    出生点采在该路线的**本车道**上（与 DinosaurAgent.LaneLateral 漫步目标同源）、朝向沿路线切线
    ///    ⇒ 出生帧即贴线直走，不再有「随机朝向 + 横切上车道」的开局偏离段。
    ///    ⚠️ SpawnMin/Max 与 WanderRadius 两个常量【一律不动】：它们仍推导 DinosaurActivityBounds，
    ///    动了会牵动全项目植被安全边界（属后续阶段的非目标）。通道整体 ⊆ 该包络，由 ValidateProject 断言。
    ///  - 采样矩形 ⊕ WanderRadius = 恐龙活动包络 DinosaurActivityBounds，由 WorldBounds 统一推导
    ///    （当前 x∈[-70,70]、z∈[-70,90]），并被 ValidateProject 断言 ⊆ TerrainFlatBounds
    ///    → 保证恐龙漫步永不进入外围起伏地形（避免脚悬空/陷地）；
    ///    ⚠️ 该包络现在是全部植被安全边界的唯一基准（不再共用「地形平坦区」那份定义），
    ///    调大 SpawnMin/Max 或 DinosaurAgent.WanderRadius 会同时改变植被安全边界，必须复核①。
    ///  - 拒绝采样避让：树木（TerrainTreeObstacleRegistry）、岩石/灌木（散落物包围圆）、
    ///    同伴互斥圆、相机近区；全部尝试失败时回退到烘焙原位置（仍贴车道、朝向沿路线）。
    ///  - 纯运行期 root 节点 transform 操作：不改场景资产、不需要重新 Bake、不触碰
    ///    Fit/缩放（遵循「烘焙态构建、运行期仅绑定」架构）。
    /// </summary>
    public static class DinosaurSpawnRandomizer
    {
        // ---------------------------------------------------------------- 可调常量块

        /// <summary>总开关：false 时完全跳过（恐龙保持烘焙初始位置/朝向）。</summary>
        private const bool RandomizeOnPlay = true;

        // 采样矩形：平坦核心区 x∈[-70,70], z∈[-70,90] 扣除 WanderRadius=45m 包络余量后的最大安全域。
        // 修改前必须复核包络约束：SpawnMinX ≥ -70+45、SpawnMaxX ≤ 70-45、SpawnMinZ ≥ -70+45、SpawnMaxZ ≤ 90-45。
        // 【跨文件契约】internal const：WorldBounds 以「本矩形 ⊕ DinosaurAgent.WanderRadius」推导
        // DinosaurActivityBounds（恐龙活动包络的唯一来源），改这里即自动改包络，禁止另处复制字面量。
        internal const float SpawnMinX = -25f, SpawnMaxX = 25f;
        internal const float SpawnMinZ = -25f, SpawnMaxZ = 45f;

        /// <summary>与相机初始位置的最小水平距离（米）：防止开局恐龙贴脸相机。</summary>
        private const float CameraMinDistance = 25f;

        /// <summary>恐龙互斥附加余量（米）：在双方 collisionRadius 之和之上再加此值
        /// （烘焙出生点间最小净距 ≈9m、collisionRadius 和 ≈6.5m，取 6m 与之相当）。</summary>
        private const float PeerMargin = 6f;

        /// <summary>岩石/灌木包围圆之外的附加余量（米）。</summary>
        private const float PropMargin = 2f;

        /// <summary>单只恐龙拒绝采样上限：超出则回退烘焙原位置（仍由 ApplySpawnPlacement 贴车道、沿路线定向），保证不放到非法区域。</summary>
        private const int MaxAttemptsPerDinosaur = 200;

        private const string RocksRootName = "Ground Pass Rocks";
        private const string PropsRootName = "Environment Props";

        // ---------------------------------------------------------------- 入口

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnPlayModeEntered()
        {
            // 常量开关并入正向分支：写 `if (!RandomizeOnPlay) return;` 会因常量折叠触发 CS0162（return 不可达），
            // 与 TerrainEnvironmentSeeder 的开关风格保持一致。
            if (RandomizeOnPlay)
                RandomizeAll();
        }

        private static void RandomizeAll()
        {
            DinosaurAgent[] agents = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>();
            if (agents.Length == 0)
                return;

            // 大恐龙优先占位：受限最大的个体先完成采样，小恐龙在剩余空隙里更容易找到落点。
            System.Array.Sort(agents, (a, b) => b.CollisionRadius.CompareTo(a.CollisionRadius));

            // 一次性收集散落物（岩石/灌木）XZ 包围圆：bounds 近似，运行期只读、无物理依赖。
            var propPositions = new List<Vector2>();
            var propRadii = new List<float>();
            CollectPropCircles(RocksRootName, propPositions, propRadii);
            CollectPropCircles(PropsRootName, propPositions, propRadii);

            Camera camera = Camera.main;
            bool hasCamera = camera != null;
            Vector2 cameraXZ = hasCamera
                ? new Vector2(camera.transform.position.x, camera.transform.position.z)
                : Vector2.zero;

            var random = new System.Random(); // 无参构造：时间种子，每次 Play 面貌不同
            var routeOrder = new int[MovementCorridor.RouteCount];
            for (int r = 0; r < routeOrder.Length; r++) routeOrder[r] = r;
            for (int r = routeOrder.Length - 1; r > 0; r--)
            {
                int other = random.Next(r + 1);
                int swap = routeOrder[r];
                routeOrder[r] = routeOrder[other];
                routeOrder[other] = swap;
            }

            var placedPositions = new List<Vector2>(agents.Length);
            var placedRadii = new List<float>(agents.Length);
            int acceptedCount = 0;
            var routeCounts = new int[MovementCorridor.RouteCount];   // 各路线分到的恐龙数（日志用）

            for (int i = 0; i < agents.Length; i++)
            {
                DinosaurAgent agent = agents[i];
                if (agent == null)
                    continue;

                Vector3 previous = agent.transform.position;
                // 每次 Play 随机打乱通道顺序，再轮转分派。当前两只恐龙必分到不同通道，
                // 同时霸王龙/伶盗龙各自进入哪一条线仍是随机的。
                int route = -1;
                Vector2 position = Vector2.zero;
                for (int attemptRoute = 0; attemptRoute < routeOrder.Length; attemptRoute++)
                {
                    int candidateRoute = routeOrder[(i + attemptRoute) % routeOrder.Length];
                    if (routeCounts[candidateRoute] != 0) continue;
                    if (!TrySamplePosition(agent, candidateRoute, hasCamera, cameraXZ, propPositions, propRadii,
                            placedPositions, placedRadii, random, out position)) continue;
                    route = candidateRoute;
                    break;
                }
                if (route < 0)
                    throw new System.InvalidOperationException($"[SpawnRandomizer] {agent.DisplayName} 没有安全的中心线出生点。");
                acceptedCount++;

                agent.ApplySpawnPlacement(position, route);
                placedPositions.Add(position);
                placedRadii.Add(agent.CollisionRadius);
                routeCounts[route]++;

                Debug.Log($"[SpawnRandomizer] {agent.DisplayName}: 随机路线[{route}] {MovementCorridor.GetRouteName(route)}，" +
                          $"({previous.x:F1}, {previous.z:F1}) -> ({position.x:F1}, {position.y:F1})（贴车道、朝向沿路线）");
            }

            var distribution = new System.Text.StringBuilder();
            for (int r = 0; r < routeCounts.Length; r++)
                distribution.Append(r == 0 ? "；" : "、").Append(MovementCorridor.GetRouteName(r)).Append(" × ").Append(routeCounts[r]);
            Debug.Log($"[SpawnRandomizer] 开局随机分派完成：{acceptedCount}/{agents.Length} 只在线上安全出生；路线分布 {distribution}。");
        }

        // ---------------------------------------------------------------- 采样

        /// <summary>
        /// 拒绝采样一只恐龙的合法落点。全部条件按「便宜→贵」短路求值：
        /// 所选路线上随机弧长 + **本车道**横向（Phase 1.1d：采样点即最终落点，三项检查不会因后续贴线失效）→ 相机距离 → 树木（注册表网格查询）
        /// → 散落物包围圆 → 已放置恐龙互斥圆。
        /// 互斥统一按 XZ 平面判定、不做高度分层：当前场景均为地面单位，保守整列更简单。
        /// </summary>
        private static bool TrySamplePosition(DinosaurAgent agent, int route, bool hasCamera, Vector2 cameraXZ,
            List<Vector2> propPositions, List<float> propRadii,
            List<Vector2> placedPositions, List<float> placedRadii,
            System.Random random, out Vector2 result)
        {
            float radius = agent.CollisionRadius;
            float treeAvoidRadius = agent.TreeAvoidRadius;

            for (int attempt = 0; attempt < MaxAttemptsPerDinosaur; attempt++)
            {
                Vector2 candidate = SampleCorridorCandidate(agent, route, random);
                if (IsSpawnSafe(route, candidate, radius, treeAvoidRadius, hasCamera, cameraXZ,
                        propPositions, propRadii, placedPositions, placedRadii))
                {
                    result = candidate;
                    return true;
                }
            }

            // 随机采样运气差时穷举路线 LUT，避免直接把烘焙原位置投到未经检查的障碍物上。
            for (int sample = 0; sample < MovementCorridor.SampleCount(route); sample++)
            {
                float arc = MovementCorridor.GetSample(route, sample).Distance;
                Vector2 candidate = MovementCorridor.PointAt(route, arc);
                if (IsSpawnSafe(route, candidate, radius, treeAvoidRadius, hasCamera, cameraXZ,
                        propPositions, propRadii, placedPositions, placedRadii))
                {
                    result = candidate;
                    return true;
                }
            }

            result = Vector2.zero;
            return false;
        }

        private static bool IsSpawnSafe(int route, Vector2 candidate, float radius, float treeAvoidRadius,
            bool hasCamera, Vector2 cameraXZ, List<Vector2> propPositions, List<float> propRadii,
            List<Vector2> placedPositions, List<float> placedRadii)
        {
            return (!hasCamera || Vector2.Distance(candidate, cameraXZ) >= CameraMinDistance)
                && !TerrainTreeObstacleRegistry.IsBlocked(candidate, treeAvoidRadius)
                && !OverlapsCircles(candidate, radius + PropMargin, propPositions, propRadii)
                && !OverlapsCircles(candidate, radius + PeerMargin, placedPositions, placedRadii);
        }

        /// <summary>
        /// 在**所选路线**上取一个候选落点（Phase 1.1d 固定轨迹）：随机弧长 + 横向 = 该弧长处的
        /// **本 Agent 车道**（DinosaurAgent.LaneLateral 与漫步目标同源，|bias| ≤ LaneBiasScale 保证
        /// 天然落在 LaneEdgeMargin 净距内）。
        ///
        /// 【为什么不再随机横向】旧版在半宽内随机横向，出生后要先横切一段才上车道 —— 用户看到的
        /// 「开局就偏离轨迹」正来源于此；现在采样点 = 车道点 = ApplySpawnPlacement 的钳位结果，
        /// 于是相机距离 / 树干 / 同伴互斥检查全部对**最终落点**精确成立（不因后续贴线而失效）。
        /// 出生点在车道内 ≠ 一定合法：三项检查仍由调用方逐条拒绝采样。
        /// </summary>
        private static Vector2 SampleCorridorCandidate(DinosaurAgent agent, int route, System.Random random)
        {
            float distance = (float)random.NextDouble() * MovementCorridor.Length(route);
            return MovementCorridor.PointAt(route, distance);
        }

        /// <summary>候选点是否侵入任一圆（距离² &lt; (radiusWithMargin + circleRadius)²，与运行期互斥判定同语义）。</summary>
        private static bool OverlapsCircles(Vector2 candidate, float radiusWithMargin,
            List<Vector2> positions, List<float> radii)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                float minimumDistance = radiusWithMargin + radii[i];
                float deltaX = candidate.x - positions[i].x;
                float deltaZ = candidate.y - positions[i].y;
                if (deltaX * deltaX + deltaZ * deltaZ < minimumDistance * minimumDistance)
                    return true;
            }

            return false;
        }

        // ---------------------------------------------------------------- 散落物收集

        /// <summary>
        /// 收集指定根节点下全部 Renderer 的 XZ 包围圆（中心 + 最大水平 extents 半径）。
        /// 根不存在时静默跳过（场景缺该 Pass 时随机化仍可用）。
        /// </summary>
        private static void CollectPropCircles(string rootName, List<Vector2> positions, List<float> radii)
        {
            GameObject root = GameObject.Find(rootName);
            if (root == null)
                return;

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                Bounds bounds = renderer.bounds;
                positions.Add(new Vector2(bounds.center.x, bounds.center.z));
                radii.Add(Mathf.Max(bounds.extents.x, bounds.extents.z));
            }
        }
    }
}
