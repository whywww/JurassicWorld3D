using System.Collections.Generic;
using UnityEngine;

namespace JurassicWorld
{
    /// <summary>
    /// 运行期开局随机布置器：每次进入 Play（RuntimeInitializeOnLoadMethod.AfterSceneLoad——
    /// 场景对象 Awake/OnEnable 完成之后、各 Start 与首帧渲染之前，画面不会闪现原位置）
    /// 把场上所有 DinosaurAgent 重新放到随机出生点 + 随机朝向，并同步重置漫步圆心（spawnPoint）。
    ///
    /// 采样约束（与硬编码运行期契约对齐）：
    ///  - 采样矩形 = 地形平坦核心区 x∈[-70,70]、z∈[-70,90]（TerrainEnvironmentSeeder.FlatMin/Max）
    ///    扣除 WanderRadius=45m 漫游包络余量 → 保证恐龙漫步永不进入外围起伏地形（避免脚悬空/陷地）；
    ///  - 拒绝采样避让：树木（TerrainTreeObstacleRegistry）、岩石/灌木（散落物包围圆）、
    ///    同伴互斥圆、相机近区；全部尝试失败时回退到烘焙原位置（仅朝向仍随机）。
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
        private const float SpawnMinX = -25f, SpawnMaxX = 25f;
        private const float SpawnMinZ = -25f, SpawnMaxZ = 45f;

        /// <summary>与相机初始位置的最小水平距离（米）：防止开局恐龙贴脸相机。</summary>
        private const float CameraMinDistance = 25f;

        /// <summary>恐龙互斥附加余量（米）：在双方 collisionRadius 之和之上再加此值
        /// （烘焙出生点间最小净距 ≈9m、collisionRadius 和 ≈6.5m，取 6m 与之相当）。</summary>
        private const float PeerMargin = 6f;

        /// <summary>岩石/灌木包围圆之外的附加余量（米）。</summary>
        private const float PropMargin = 2f;

        /// <summary>单只恐龙拒绝采样上限：超出则回退烘焙原位置（仅朝向随机），保证不放到非法区域。</summary>
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

            var placedPositions = new List<Vector2>(agents.Length);
            var placedRadii = new List<float>(agents.Length);
            int acceptedCount = 0;
            int fallbackCount = 0;

            for (int i = 0; i < agents.Length; i++)
            {
                DinosaurAgent agent = agents[i];
                if (agent == null)
                    continue;

                Vector3 previous = agent.transform.position;
                float yaw = (float)random.NextDouble() * 360f;

                Vector2 position;
                if (TrySamplePosition(agent, hasCamera, cameraXZ, propPositions, propRadii,
                        placedPositions, placedRadii, random, out position))
                {
                    acceptedCount++;
                }
                else
                {
                    position = new Vector2(previous.x, previous.z);
                    fallbackCount++;
                    Debug.LogWarning($"[SpawnRandomizer] {agent.DisplayName}: {MaxAttemptsPerDinosaur} 次采样均失败，回退烘焙原位置 ({position.x:F1}, {position.y:F1})。");
                }

                agent.ApplySpawnPlacement(position, yaw);
                placedPositions.Add(position);
                placedRadii.Add(agent.CollisionRadius);

                Debug.Log($"[SpawnRandomizer] {agent.DisplayName}: ({previous.x:F1}, {previous.z:F1}) -> ({position.x:F1}, {position.y:F1}) yaw={yaw:F0}°");
            }

            Debug.Log($"[SpawnRandomizer] 开局随机布置完成：{acceptedCount}/{agents.Length} 只随机成功，{fallbackCount} 只回退原位置。");
        }

        // ---------------------------------------------------------------- 采样

        /// <summary>
        /// 拒绝采样一只恐龙的合法落点。全部条件按「便宜→贵」短路求值：
        /// 采样矩形 → 相机距离 → 树木（注册表网格查询）→ 散落物包围圆 → 已放置恐龙互斥圆。
        /// 互斥统一按 XZ 平面判定、不做高度分层：当前场景均为地面单位，保守整列更简单。
        /// </summary>
        private static bool TrySamplePosition(DinosaurAgent agent, bool hasCamera, Vector2 cameraXZ,
            List<Vector2> propPositions, List<float> propRadii,
            List<Vector2> placedPositions, List<float> placedRadii,
            System.Random random, out Vector2 result)
        {
            float radius = agent.CollisionRadius;
            float treeAvoidRadius = agent.TreeAvoidRadius;

            for (int attempt = 0; attempt < MaxAttemptsPerDinosaur; attempt++)
            {
                var candidate = new Vector2(
                    Mathf.Lerp(SpawnMinX, SpawnMaxX, (float)random.NextDouble()),
                    Mathf.Lerp(SpawnMinZ, SpawnMaxZ, (float)random.NextDouble()));

                if (hasCamera && Vector2.Distance(candidate, cameraXZ) < CameraMinDistance)
                    continue;

                if (TerrainTreeObstacleRegistry.IsBlocked(candidate, treeAvoidRadius))
                    continue;

                if (OverlapsCircles(candidate, radius + PropMargin, propPositions, propRadii))
                    continue;

                if (OverlapsCircles(candidate, radius + PeerMargin, placedPositions, placedRadii))
                    continue;

                result = candidate;
                return true;
            }

            result = Vector2.zero;
            return false;
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
