using System.Collections.Generic;
using UnityEngine;

namespace JurassicWorld
{
    /// <summary>
    /// 地形树「行为级」障碍注册表。
    ///
    /// 背景：本项目运行时移动全部是纯 Transform 位移（DinosaurAgent.MoveTowards 直接写 position、
    /// SmoothExplorerCamera.Update 直接写 position），场景中没有任何 Rigidbody，
    /// 因此 Terrain 树上的静态 Collider 对移动实体毫无阻挡效果（PhysX 不参与）。
    /// 本类用「几何查询」代替物理碰撞：一次性把 TerrainData.treeInstances 烘焙成 XZ 平面上的
    /// 圆形障碍，并按 4m 空间网格分桶，供恐龙与相机在移动前/移动后做 O(1) 级查询。
    ///
    /// 设计约束：
    /// 1. 纯静态、零 Update 开销、零物理组件、零 GC 压力（构建一次，之后只读）。
    /// 2. 数据源为 Terrain.activeTerrain，缓存其 InstanceID；地形变化（重 Bake / 换场景）自动重建。
    /// 3. 无地形（Terrain.activeTerrain == null）时注册表为空集，所有查询「放行」，
    ///    保证没有 Terrain 的场景依然能正常运行，且只警告一次不刷屏。
    /// </summary>
    public static class TerrainTreeObstacleRegistry
    {
        /// <summary>单棵树的碰撞圆：仅 XZ 平面位置（米，世界坐标）+ 半径（米）。</summary>
        private struct TreeObstacle
        {
            public Vector2 position;
            public float radius;
            public TreeTrunkSection[] sections;
        }

        /// <summary>空间网格边长（米）。4m 兼顾「一格内树数量少」与「邻域遍历次数少」。</summary>
        private const float CellSize = 4f;

        /// <summary>统一树干半径近似（米）× TreeInstance.widthScale。
        /// 树冠不参与碰撞——玩家/恐龙可以走到枝叶下方，但不能穿过树干。</summary>
        private const float TrunkRadiusBase = 0.7f;

        /// <summary>SlideMove 最多迭代次数：每次迭代求一次扫掠最早命中并切向滑移，5 次足够在多树夹缝收敛。</summary>
        private const int MaxSlideIterations = 5;

        /// <summary>SlideMove 命中点沿法线外推量（米）：避免浮点误差导致下一次迭代仍判定「在树内」。</summary>
        private const float SlideEpsilon = 0.001f;

        /// <summary>
        /// SlideMove 剩余位移收敛阈值（米）：仅用于「第 2 轮起」的迭代收敛判断，必须 > SlideEpsilon。
        /// 重要：首轮不受此阈值限制——无论剩余位移多小都会照常扫掠推进（若首轮即按阈值丢弃，
        /// 慢速单位在高帧率下（如 1.5 m/s @300+fps 每帧步长 &lt;5mm）会被静默冻结为「原地走」）。
        /// 【跨文件契约】public const：DinosaurAgent.MoveTowards 的进度比除零护栏与此阈值对齐——
        /// 当 desiredLength ≤ 此值时调用方按 progress=1f（正常小步）处理，避免慢速单位/高帧率/低 timeScale
        /// 下除零或噪声误判受阻。禁止在 DinosaurAgent 侧硬编码独立常量，必须引用此字段以保证两侧一致。
        /// </summary>
        public const float SlideRemainingThreshold = 5e-3f;

        /// <summary>去穿透最大速度（米/秒）：起点陷入树内时按速度制限幅，多帧收敛避免瞬移观感。
        /// 取 3f（低于玩家 moveSpeed 6f），观感为快速走出而非弹出。</summary>
        private const float MaxDepenetrationSpeed = 3f;

        /// <summary>同心退化判定阈值：点与树心几乎重合时无法定义推出方向。</summary>
        private const float DegenerateSqrDistance = 1e-8f;

        /// <summary>收敛检测阈值：连续两轮 advance 小于此值视为夹缝停滞，提前 break。</summary>
        private const float StallThreshold = 1e-4f;

        private static readonly Dictionary<long, List<TreeObstacle>> cells =
            new Dictionary<long, List<TreeObstacle>>();

        private static int cachedTerrainInstanceId;   // 已构建注册表所对应的 Terrain InstanceID（0 = 无地形）
        private static int cachedTreeInstanceCount;     // 缓存的 terrainData.treeInstanceCount（任一变化即重建）
        private static int obstacleCount;             // 有效障碍圆数量
        private static float maxTreeRadius;           // 最大树半径，用于按查询半径推导邻域跨度
        private static bool initialized;              // 是否已完成首次构建
        private static bool warnedMissingTerrain;     // 「无地形」警告只输出一次
        private static bool warnedMissingTerrainData; // 「无 TerrainData」警告只输出一次

        /// <summary>当前注册表内的障碍圆数量（自动惰性构建，供验证/调试读取）。</summary>
        public static int ObstacleCount
        {
            get
            {
                EnsureInitialized();
                return obstacleCount;
            }
        }

        /// <summary>
        /// 惰性构建注册表：首次调用、或 Terrain.activeTerrain 实例发生变化、或 treeInstanceCount 变化时重建。
        /// 其余调用只是一次 InstanceID + treeInstanceCount 比较，可在 Awake/Start 中安全多次调用。
        /// </summary>
        public static void EnsureInitialized()
        {
            Terrain activeTerrain = Terrain.activeTerrain;
            int activeInstanceId = activeTerrain != null ? activeTerrain.GetInstanceID() : 0;
            // 缓存失效增强：除 Terrain InstanceID 外同时比对 treeInstanceCount，
            // 防 InstanceID 复用极端情形与同 Terrain 树数据被修改的情形。
            int activeTreeCount = 0;
            if (activeTerrain != null && activeTerrain.terrainData != null)
                activeTreeCount = activeTerrain.terrainData.treeInstanceCount;

            if (initialized && cachedTerrainInstanceId == activeInstanceId && cachedTreeInstanceCount == activeTreeCount)
                return;

            Rebuild(activeTerrain, activeInstanceId, activeTreeCount);
        }

        /// <summary>强制下次查询重建注册表（例如运行期动态增删树后调用）。</summary>
        public static void Invalidate()
        {
            initialized = false;
        }

        /// <summary>
        /// 查询：以 xz 为圆心、radius 为半径的圆是否与任意树干圆重叠。
        /// 注册表为空（无地形 / 无树）时恒返回 false（放行）。
        /// 边界语义：dist² &lt; reach² 判「阻塞」，dist² >= reach² 判「自由」。
        /// </summary>
        public static bool IsCoreBlocked(Vector2 xz, Vector2 forward, DinosaurCoreBody body)
        {
            EnsureInitialized();
            float broad = body.BroadRadius;
            int span = NeighborSpan(broad), cx = CellIndex(xz.x), cz = CellIndex(xz.y);
            for (int x = -span; x <= span; x++) for (int z = -span; z <= span; z++)
            {
                if (!cells.TryGetValue(CellKey(cx + x, cz + z), out var bucket)) continue;
                foreach (var obstacle in bucket)
                {
                    if ((obstacle.position - xz).sqrMagnitude >= (broad + obstacle.radius) * (broad + obstacle.radius)) continue;
                    foreach (var section in obstacle.sections)
                        if (body.Intersects(xz, forward, section)) return true;
                }
            }
            return false;
        }
        public static bool IsCoreStepBlocked(Vector2 from, Vector2 to, Vector2 fromForward, Vector2 toForward, DinosaurCoreBody body)
        {
            float angle = Vector2.SignedAngle(fromForward, toForward);
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / .1f), Mathf.CeilToInt(Mathf.Abs(angle) / 2f));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps, a = angle * t * Mathf.Deg2Rad;
                Vector2 forward = new Vector2(fromForward.x * Mathf.Cos(a) - fromForward.y * Mathf.Sin(a), fromForward.x * Mathf.Sin(a) + fromForward.y * Mathf.Cos(a));
                if (IsCoreBlocked(Vector2.Lerp(from, to, t), forward, body)) return true;
            }
            return false;
        }
        public static Vector2 MoveCore(Vector2 current, Vector2 desired, Vector2 forward, DinosaurCoreBody body)
        {
            if (!IsCoreStepBlocked(current, desired, forward, forward, body)) return desired;
            float low = 0f, high = 1f;
            for (int i = 0; i < 10; i++)
            {
                float t = (low + high) * .5f;
                if (IsCoreStepBlocked(current, Vector2.Lerp(current, desired, t), forward, forward, body)) high = t; else low = t;
            }
            return Vector2.Lerp(current, desired, low);
        }
        public static bool IsBlocked(Vector2 xz, float radius)
        {
            EnsureInitialized();
            if (obstacleCount == 0)
                return false;

            float queryRadius = Mathf.Max(0f, radius);
            int span = NeighborSpan(queryRadius);
            int centerCellX = CellIndex(xz.x);
            int centerCellZ = CellIndex(xz.y);

            for (int offsetX = -span; offsetX <= span; offsetX++)
            {
                for (int offsetZ = -span; offsetZ <= span; offsetZ++)
                {
                    List<TreeObstacle> bucket;
                    if (!cells.TryGetValue(CellKey(centerCellX + offsetX, centerCellZ + offsetZ), out bucket))
                        continue;

                    for (int i = 0; i < bucket.Count; i++)
                    {
                        TreeObstacle obstacle = bucket[i];
                        float reach = obstacle.radius + queryRadius;
                        float deltaX = xz.x - obstacle.position.x;
                        float deltaZ = xz.y - obstacle.position.y;
                        // 统一语义：dist² < reach² 为阻塞
                        if (deltaX * deltaX + deltaZ * deltaZ < reach * reach)
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 扫掠滑移（转发重载）：deltaTime 缺省按 1/60 秒，保持旧调用兼容。
        /// </summary>
        public static Vector2 SlideMove(Vector2 current, Vector2 desired, float radius)
        {
            return SlideMove(current, desired, radius, 1f / 60f);
        }

        /// <summary>
        /// 扫掠滑移：把「从 current 移动到 desired」的位移视为一条 XZ 平面上的有向线段，
        /// 对邻域内每棵树做「圆-圆扫掠」（树半径扩张为 treeRadius + radius），求最早命中时间 t；
        /// 命中时沿法线显式放置到障碍表面（result = obstacle.position + hitNormal × (R + ε)），
        /// 重新计算 remaining = desired - result 并投影到切向（消除法线分量），
        /// 继续下一次迭代，最多 <see cref="MaxSlideIterations"/> 次。
        /// 未命中则直接走完剩余位移。
        ///
        /// 边界处理契约：调用方负责先将期望点钳制到可达区域（凸集），保证 current 与 desired 均在界内，
        /// 由凸集性质线段全程在界内，SlideMove 返回值必在界内。
        ///
        /// inside 分支：起点已处于扩张圆内时，法线取径向 radial = (result - treeCenter).normalized，
        /// 保留切向滑行分量，不吞掉全部输入。
        ///
        /// 兜底：起点已陷入树内（出生点/极端情况）时先做速度制限幅去穿透
        /// （maxCorrection = <see cref="MaxDepenetrationSpeed"/> × deltaTime），多帧收敛避免瞬移观感；
        /// 去穿透后再进入常规扫掠滑移。
        ///
        /// 复杂度：每次迭代遍历线段包围盒外扩 (maxTreeRadius + radius) 覆盖的网格桶（4m 格），
        /// 常规相机半径 0.45m、位移 ≤ 0.1m/帧 下仅涉及 1~4 个桶、几棵树，开销可忽略。
        ///
        /// 语义：像角色控制器一样「挡住 + 滑行」，永不相穿、永不弹出。
        /// 注册表为空时直接返回 desired（放行）。
        /// </summary>
        /// <param name="current">当前 XZ 位置（世界坐标，米）</param>
        /// <param name="desired">期望 XZ 位置（调用方已钳制到可达区域内）</param>
        /// <param name="radius">查询体碰撞半径（米）</param>
        /// <param name="deltaTime">帧间隔（秒），用于去穿透速度制限幅</param>
        public static Vector2 SlideMove(Vector2 current, Vector2 desired, float radius, float deltaTime)
        {
            EnsureInitialized();
            if (obstacleCount == 0)
                return desired;

            float queryRadius = Mathf.Max(0f, radius);
            Vector2 result = current;

            // 兜底：起点已陷入树内（出生点/两树重叠/边界夹缝等极端情况），
            // 做速度制限幅去穿透，多帧收敛避免瞬移观感。
            if (IsBlockedInternal(result, queryRadius))
            {
                float maxCorrection = MaxDepenetrationSpeed * Mathf.Max(0f, deltaTime);
                result = LimitPushOut(result, queryRadius, maxCorrection);
            }

            Vector2 remaining = desired - result;

            // 预计算扫掠邻域外扩量：最大树半径 + 查询半径，覆盖整条线段包围盒。
            float pad = maxTreeRadius + queryRadius;
            int stallCount = 0; // 收敛检测：连续两轮 advance < StallThreshold 则 break

            for (int iteration = 0; iteration < MaxSlideIterations; iteration++)
            {
                float remainingLength = remaining.magnitude;
                // 【修复「原地走」】收敛阈值仅从第 2 轮起生效：首轮即使剩余位移低于阈值也必须照常扫掠推进。
                // 慢速单位在高帧率下每帧步长可低于阈值（如 1.5 m/s @300+fps ≈ 3~5mm），此前首轮直接
                // break 返回 current 会造成零位移——动画正常播放但原地走、永不前进。精确零位移
                //（timeScale=0 / desired==current）仍立即返回，避免 dir = remaining/0 产生 NaN。
                if (remainingLength < 1e-6f || (iteration > 0 && remainingLength < SlideRemainingThreshold))
                    break;

                Vector2 dir = remaining / remainingLength;

                // 圆-圆扫掠：遍历线段包围盒外扩 pad 覆盖的所有网格桶，对每棵树解二次方程
                // |result + dir·t - treeCenter| = treeRadius + queryRadius，取 t ∈ [0, remainingLength] 的最早命中。
                float earliestT = float.MaxValue;
                Vector2 hitNormal = Vector2.zero;
                Vector2 hitObstaclePos = Vector2.zero;
                float hitR = 0f;
                bool hitFound = false;

                float segEndX = result.x + remaining.x;
                float segEndZ = result.y + remaining.y;
                int cellMinX = CellIndex(Mathf.Min(result.x, segEndX) - pad);
                int cellMaxX = CellIndex(Mathf.Max(result.x, segEndX) + pad);
                int cellMinZ = CellIndex(Mathf.Min(result.y, segEndZ) - pad);
                int cellMaxZ = CellIndex(Mathf.Max(result.y, segEndZ) + pad);

                for (int cx = cellMinX; cx <= cellMaxX; cx++)
                {
                    for (int cz = cellMinZ; cz <= cellMaxZ; cz++)
                    {
                        List<TreeObstacle> bucket;
                        if (!cells.TryGetValue(CellKey(cx, cz), out bucket))
                            continue;

                        for (int i = 0; i < bucket.Count; i++)
                        {
                            TreeObstacle obstacle = bucket[i];
                            float R = obstacle.radius + queryRadius;
                            float Rsqr = R * R;
                            Vector2 toTree = obstacle.position - result;
                            float along = Vector2.Dot(toTree, dir);
                            float perpSqr = toTree.sqrMagnitude - along * along;
                            if (perpSqr > Rsqr)
                                continue; // 扫掠线不进入扩张圆

                            // 起点已在扩张圆内（统一语义：< 判重叠）：
                            // 法线取径向 radial = (result - treeCenter)，保留切向滑行分量。
                            if (toTree.sqrMagnitude < Rsqr)
                            {
                                if (0f < earliestT)
                                {
                                    earliestT = 0f;
                                    Vector2 radial = result - obstacle.position;
                                    hitNormal = radial.sqrMagnitude > DegenerateSqrDistance
                                        ? radial.normalized
                                        : -dir;
                                    hitObstaclePos = obstacle.position;
                                    hitR = R;
                                    hitFound = true;
                                }
                                continue;
                            }

                            // 二次方程 |P(t) - T|² = R² 展开：t² - 2·along·t + (|toTree|² - R²) = 0
                            // 判别式 Δ = along² - perpSqr，最早命中 t = along - √Δ。
                            float halfChord = Mathf.Sqrt(Mathf.Max(0f, Rsqr - perpSqr));
                            float t = along - halfChord;
                            if (t < 0f || t > remainingLength)
                                continue; // 命中点在线段之外，忽略

                            if (t < earliestT)
                            {
                                earliestT = t;
                                Vector2 hitPoint = result + dir * t;
                                Vector2 n = hitPoint - obstacle.position;
                                // 数值保险：hitPoint 与树心重合时退化为 -dir。
                                hitNormal = n.sqrMagnitude > DegenerateSqrDistance ? n.normalized : -dir;
                                hitObstaclePos = obstacle.position;
                                hitR = R;
                                hitFound = true;
                            }
                        }
                    }
                }

                if (!hitFound)
                {
                    // 无命中：直接走完剩余位移，结束迭代。
                    result += remaining;
                    break;
                }

                // 命中：沿法线显式放置到障碍表面，消除擦边 ULP 退化与贴树单调外漂。
                Vector2 prevResult = result;
                if (earliestT > 0f)
                {
                    // 常规命中：result = obstacle.position + hitNormal × (R + ε)
                    result = hitObstaclePos + hitNormal * (hitR + SlideEpsilon);
                }
                // inside 分支 earliestT==0：result 不动，仅靠法线做切向投影。

                // 重新计算剩余位移并投影到切向：消除法线分量，实现「贴树滑行」而非「被弹开」。
                remaining = desired - result;
                float nComp = Vector2.Dot(remaining, hitNormal);
                if (nComp < 0f)
                    remaining -= nComp * hitNormal;

                // 收敛检测：连续两轮 advance 过小视为夹缝停滞，提前 break 避免白耗迭代。
                float advance = (result - prevResult).magnitude;
                if (advance < StallThreshold)
                {
                    stallCount++;
                    if (stallCount >= 2)
                        break;
                }
                else
                {
                    stallCount = 0;
                }
            }

            return result;
        }

        /// <summary>
        /// 限幅去穿透（速度制）：起点陷入树内时的兜底。
        /// 收集所有重叠树，按穿透深度加权合力方向推出；推出量 = min(maxPenetration + ε, maxCorrection)。
        /// 合力近零时退化方向用最近树心的反向（而非硬编码 Vector2.right）。
        /// 推出后复检 IsBlockedInternal：仍阻塞则尝试最近树径向方向；再不行原样返回不推（等下一帧）。
        /// </summary>
        private static Vector2 LimitPushOut(Vector2 current, float queryRadius, float maxCorrection)
        {
            int span = NeighborSpan(queryRadius);
            int centerCellX = CellIndex(current.x);
            int centerCellZ = CellIndex(current.y);

            Vector2 netPush = Vector2.zero;
            float maxPenetration = 0f;
            float nearestSqrDist = float.MaxValue;
            Vector2 nearestDelta = Vector2.zero;
            bool foundAny = false;

            for (int offsetX = -span; offsetX <= span; offsetX++)
            {
                for (int offsetZ = -span; offsetZ <= span; offsetZ++)
                {
                    List<TreeObstacle> bucket;
                    if (!cells.TryGetValue(CellKey(centerCellX + offsetX, centerCellZ + offsetZ), out bucket))
                        continue;

                    for (int i = 0; i < bucket.Count; i++)
                    {
                        TreeObstacle obstacle = bucket[i];
                        float reach = obstacle.radius + queryRadius;
                        Vector2 delta = current - obstacle.position;
                        float sqrDist = delta.sqrMagnitude;
                        // 统一语义：dist² < reach² 为重叠
                        if (sqrDist >= reach * reach)
                            continue;

                        foundAny = true;
                        float dist = Mathf.Sqrt(sqrDist);
                        float penetration = reach - dist;

                        if (penetration > maxPenetration)
                            maxPenetration = penetration;

                        if (sqrDist < nearestSqrDist)
                        {
                            nearestSqrDist = sqrDist;
                            nearestDelta = delta;
                        }

                        // 深度加权：(delta/|delta|) × penetration
                        netPush += sqrDist > DegenerateSqrDistance
                            ? (delta / dist) * penetration
                            : Vector2.right * penetration;
                    }
                }
            }

            if (!foundAny)
                return current;

            // 退化方向：合力近零时用最近树心的反向（而非 Vector2.right）
            Vector2 pushDirection;
            if (netPush.sqrMagnitude > 1e-6f)
            {
                pushDirection = netPush.normalized;
            }
            else
            {
                pushDirection = nearestDelta.sqrMagnitude > DegenerateSqrDistance
                    ? nearestDelta.normalized
                    : Vector2.right; // 极端退化：点与树心完全重合
            }

            // pushDistance = min(maxPenetration + ε, maxCorrection)
            float pushDistance = Mathf.Min(maxPenetration + SlideEpsilon, Mathf.Max(0f, maxCorrection));
            Vector2 newPos = current + pushDirection * pushDistance;

            // 推出后复检：仍阻塞则依次尝试最近树径向方向，再不行原样返回不推
            if (IsBlockedInternal(newPos, queryRadius))
            {
                if (nearestDelta.sqrMagnitude > DegenerateSqrDistance)
                {
                    Vector2 radialDir = nearestDelta.normalized;
                    Vector2 altPos = current + radialDir * pushDistance;
                    if (!IsBlockedInternal(altPos, queryRadius))
                        return altPos;
                }
                // 宁可留在原地等下一帧，也不推进第三棵树
                return current;
            }

            return newPos;
        }

        /// <summary>内部 IsBlocked（不触发 EnsureInitialized，假定已初始化）。
        /// 统一语义：dist² &lt; reach² 为阻塞。</summary>
        private static bool IsBlockedInternal(Vector2 xz, float queryRadius)
        {
            int span = NeighborSpan(queryRadius);
            int centerCellX = CellIndex(xz.x);
            int centerCellZ = CellIndex(xz.y);

            for (int offsetX = -span; offsetX <= span; offsetX++)
            {
                for (int offsetZ = -span; offsetZ <= span; offsetZ++)
                {
                    List<TreeObstacle> bucket;
                    if (!cells.TryGetValue(CellKey(centerCellX + offsetX, centerCellZ + offsetZ), out bucket))
                        continue;

                    for (int i = 0; i < bucket.Count; i++)
                    {
                        TreeObstacle obstacle = bucket[i];
                        float reach = obstacle.radius + queryRadius;
                        float deltaX = xz.x - obstacle.position.x;
                        float deltaZ = xz.y - obstacle.position.y;
                        if (deltaX * deltaX + deltaZ * deltaZ < reach * reach)
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 从 TerrainData.treeInstances 全量重建网格（一次性，数千实例为毫秒级）。
        /// </summary>
        private static void Rebuild(Terrain terrain, int terrainInstanceId, int treeInstanceCount)
        {
            initialized = true;
            cachedTerrainInstanceId = terrainInstanceId;
            cachedTreeInstanceCount = treeInstanceCount;
            cells.Clear();
            obstacleCount = 0;
            maxTreeRadius = 0f;

            if (terrain == null)
            {
                // 无地形：注册表保持空集，所有查询放行（不抛异常，只警告一次）。
                if (!warnedMissingTerrain)
                {
                    warnedMissingTerrain = true;
                    Debug.LogWarning("[TerrainTreeObstacleRegistry] Terrain.activeTerrain 为空，"
                        + "树障碍注册表为空集：所有碰撞查询放行（无地形场景仍可正常运行）。");
                }
                return;
            }

            warnedMissingTerrain = false;

            TerrainData terrainData = terrain.terrainData;
            if (terrainData == null)
            {
                if (!warnedMissingTerrainData)
                {
                    warnedMissingTerrainData = true;
                    Debug.LogWarning("[TerrainTreeObstacleRegistry] Terrain 缺少 TerrainData，"
                        + "树障碍注册表为空集：所有碰撞查询放行。", terrain);
                }
                return;
            }

            warnedMissingTerrainData = false;

            TreeInstance[] instances = terrainData.treeInstances;
            var baked = Object.FindObjectOfType<ForestTrunkProfiles>();

            Vector3 origin = terrain.transform.position;
            Vector3 size = terrainData.size;
            for (int i = 0; instances != null && i < instances.Length; i++)
            {
                TreeInstance instance = instances[i];
                // Unity 用负 widthScale 标记「已被擦除」的树实例，这类实例不渲染，也不应参与碰撞。
                if (instance.widthScale <= 0f)
                    continue;

                float radius = TrunkRadiusBase * instance.widthScale;
                if (radius <= 0f)
                    continue;

                // TreeInstance.position 是 0-1 归一化地形坐标；只取 XZ 平面，
                // Y 由各脚本自行决定（恐龙恒为 MovementHeight、相机 = 地形高 + eyeHeight），不参与碰撞。
                float worldX = origin.x + instance.position.x * size.x;
                float worldZ = origin.z + instance.position.z * size.z;
                TreeTrunkSection[] sections = null;
                if (baked != null && instance.prototypeIndex < baked.prototypes.Length)
                {
                    var source = baked.prototypes[instance.prototypeIndex].sections; sections = new TreeTrunkSection[source.Length];
                    float cos = Mathf.Cos(instance.rotation), sin = Mathf.Sin(instance.rotation);
                    for (int j = 0; j < source.Length; j++)
                    {
                        var band = source[j]; Vector2 p = band.center * instance.widthScale;
                        band.center = new Vector2(worldX + p.x * cos + p.y * sin, worldZ - p.x * sin + p.y * cos);
                        float y = origin.y + instance.position.y * size.y; band.bottom = y + band.bottom * instance.heightScale; band.top = y + band.top * instance.heightScale;
                        band.radius *= instance.widthScale; sections[j] = band;
                    }
                }
                Insert(worldX, worldZ, radius, sections);
            }

            // Authored scene trees use the same swept-circle movement queries as Terrain trees.
            // The marker stores a world-space radius, so prefab scale is already accounted for.
            foreach (SceneTreeObstacle marker in Object.FindObjectsOfType<SceneTreeObstacle>())
            {
                if (!marker.isActiveAndEnabled || marker.Radius <= 0f) continue;
                Vector3 position = marker.transform.position;
                var sections = new TreeTrunkSection[marker.trunkSections.Length];
                for (int j = 0; j < sections.Length; j++) { sections[j] = marker.trunkSections[j]; sections[j].center += new Vector2(position.x, position.z); }
                Insert(position.x, position.z, marker.Radius, sections);
            }
        }

        /// <summary>把一个障碍圆写入其所属网格桶。</summary>
        private static void Insert(float worldX, float worldZ, float radius, TreeTrunkSection[] sections = null)
        {
            long key = CellKey(CellIndex(worldX), CellIndex(worldZ));
            List<TreeObstacle> bucket;
            if (!cells.TryGetValue(key, out bucket))
            {
                bucket = new List<TreeObstacle>(4);
                cells.Add(key, bucket);
            }

            TreeObstacle obstacle;
            obstacle.position = new Vector2(worldX, worldZ);
            obstacle.radius = radius;
            obstacle.sections = sections != null && sections.Length > 0 ? sections : new[] { new TreeTrunkSection { center = new Vector2(worldX, worldZ), radius = radius, bottom = -100f, top = 100f } };
            bucket.Add(obstacle);

            obstacleCount++;
            if (radius > maxTreeRadius)
                maxTreeRadius = radius;
        }

        /// <summary>
        /// 邻域跨度（格）：需要覆盖「查询半径 + 最大树半径」，否则大半径查询（如阿根廷龙 8m）
        /// 会漏检远处格子里的树。常规查询（相机 0.45m、伶盗龙 1.5m）下 span == 1，即 3×3 邻域。
        /// </summary>
        private static int NeighborSpan(float queryRadius)
        {
            return Mathf.Max(1, Mathf.CeilToInt((queryRadius + maxTreeRadius) / CellSize));
        }

        /// <summary>世界坐标 → 网格索引（向下取整，负坐标同样正确）。</summary>
        private static int CellIndex(float worldValue)
        {
            return Mathf.FloorToInt(worldValue / CellSize);
        }

        /// <summary>把 (cellX, cellZ) 整数对无冲突打包成 long 作为字典键。</summary>
        private static long CellKey(int cellX, int cellZ)
        {
            return ((long)cellX << 32) | (uint)cellZ;
        }
    }
}
