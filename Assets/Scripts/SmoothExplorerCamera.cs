using UnityEngine;

namespace JurassicWorld
{
    public sealed class SmoothExplorerCamera : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float acceleration = 6f;
        [SerializeField] private float turnSpeed = 60f;
        [SerializeField] private float cameraFollowSpeed = 3.5f;
        [SerializeField] private float lookSensitivity = 65f;
        [SerializeField] private float initialPitch = 4f;
        [SerializeField] private float eyeHeight = 1.7f;   // 人眼视高（米）：由 JurassicWorldSceneBuilder.BuildCamera 通过反射写入
        // 玩家行为级碰撞半径（米）：可贴近树干观察，但不能穿过树心。
        private const float PlayerCollisionRadius = 0.45f;
        // 漫游可达区：唯一来源 = WorldBounds.PlayerExplorationBounds（x∈[-78,78]、z∈[-78,88]）。
        // 西/东/南三侧 = 恐龙活动包络外扩 8m（玩家可走下平坦区、踩上外围过渡带缓坡），北侧止于北林线 88。
        // 因为可达区已越出地形平坦区，相机 Y 必须采样地形高度（见下方 SampleGroundHeight）——
        // 这是原代码 NOTE 预留的「扩大地图」迁移，取代原先硬编码 eyeHeight 的前提（平坦区 heightmap 恒 0）。
        private Vector3 velocity;
        private Vector3 previousMousePosition;
        private float travelYaw;
        private float yaw;
        private float pitch;
        private Terrain groundTerrain;          // Start 缓存（Terrain.activeTerrain）；null 时回退固定眼高
        private bool warnedMissingTerrain;      // 「无地形」警告只输出一次
        // Blocker 1 防御：SlideMove 不变量违反（理论不可达）只警告一次
        private bool warnedSlideInvariant;

        private void Start()
        {
            yaw = transform.eulerAngles.y;
            travelYaw = yaw;
            pitch = initialPitch;
            // 相机移动同样是纯 Transform 位移（无 Rigidbody），树干阻挡走行为级几何查询：
            // 惰性构建地形树障碍注册表（恐龙侧已构建时为一次 InstanceID 比较，开销可忽略）。
            TerrainTreeObstacleRegistry.EnsureInitialized();
            groundTerrain = Terrain.activeTerrain;
        }

        private void Update()
        {
            float turn = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            float forward = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);

            if (!Mathf.Approximately(turn, 0f))
            {
                travelYaw += turn * turnSpeed * Time.deltaTime;
                forward = 1f;
            }

            Vector3 travelDirection = Quaternion.Euler(0f, travelYaw, 0f) * Vector3.forward;
            Vector3 desired = travelDirection * forward * moveSpeed;
            velocity = Vector3.Lerp(velocity, desired, 1f - Mathf.Exp(-acceleration * Time.deltaTime));

            // ─── 扫掠滑移移动（Blocker 1 修正：边界纳入扫掠前置） ───
            // 1. 计算期望位移
            Vector3 desiredPosition = transform.position + velocity * Time.deltaTime;
            Vector2 currentXZ = new Vector2(transform.position.x, transform.position.z);
            Vector2 desiredXZ = new Vector2(desiredPosition.x, desiredPosition.z);

            // 2. 先将期望点钳制到玩家可探索矩形（WorldBounds.PlayerExplorationXZ；
            //    凸集：起点在界内 + 期望点在界内 → 线段全程在界内）
            desiredXZ = WorldBounds.ClampToPlayerExploration(desiredXZ);

            // 3. 扫掠滑移：SlideMove 返回值必在界内且不在树内（不变量）
            Vector2 slidXZ = TerrainTreeObstacleRegistry.SlideMove(
                currentXZ, desiredXZ, PlayerCollisionRadius, Time.deltaTime);

            // 4. 防御断言：理论不可达，但若触发只警告一次、原样接受（不做瞬移）
            if (!warnedSlideInvariant)
            {
                bool blocked = TerrainTreeObstacleRegistry.IsBlocked(slidXZ, PlayerCollisionRadius);
                bool outOfBounds = !WorldBounds.ContainsPlayer(slidXZ);
                if (blocked || outOfBounds)
                {
                    warnedSlideInvariant = true;
                    Debug.LogWarning("[SmoothExplorerCamera] SlideMove 不变量违反："
                        + $"blocked={blocked}, outOfBounds={outOfBounds}, result={slidXZ}");
                }
            }

            // 5. 赋值最终位置：y = 地形高度 + eyeHeight。
            // 可达区已越出地形平坦区（西/东/南外扩 8m），外围过渡带地面不再恒为 0 —— 必须采样地形高度，
            // 否则相机在起伏处会悬空或钻地。Terrain.SampleHeight 返回世界 Y（本场景地形 GO 的 y = 0，
            // 故与 transform.position.y + GetInterpolatedHeight 等价）。
            transform.position = new Vector3(slidXZ.x, SampleGroundHeight(slidXZ) + eyeHeight, slidXZ.y);

            // ─── 鼠标视角 ───
            if (Input.GetMouseButtonDown(1))
                previousMousePosition = Input.mousePosition;

            if (Input.GetMouseButton(1))
            {
                Vector3 mouseDelta = Input.mousePosition - previousMousePosition;
                previousMousePosition = Input.mousePosition;
                yaw += mouseDelta.x * lookSensitivity * 0.01f;
                travelYaw = yaw;
                pitch -= mouseDelta.y * lookSensitivity * 0.01f;
                pitch = Mathf.Clamp(pitch, -18f, 35f);
            }
            else
            {
                yaw = Mathf.LerpAngle(yaw, travelYaw, 1f - Mathf.Exp(-cameraFollowSpeed * Time.deltaTime));
            }

            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(pitch, yaw, 0f), 1f - Mathf.Exp(-10f * Time.deltaTime));
        }

        /// <summary>
        /// 采样 XZ 处的地形高度（米，世界 Y）。无地形时回退 0（= 原硬编码行为）并只告警一次。
        /// 地形引用在 Start 缓存（与 TerrainTreeObstacleRegistry 同一惰性范式），运行期零查找开销。
        /// </summary>
        private float SampleGroundHeight(Vector2 xz)
        {
            if (groundTerrain == null)
            {
                if (!warnedMissingTerrain)
                {
                    warnedMissingTerrain = true;
                    Debug.LogWarning("[SmoothExplorerCamera] 场景无 Terrain，相机视高回退为固定 eyeHeight：" +
                                     "外围起伏处可能悬空 / 钻地。");
                }
                return 0f;
            }
            return groundTerrain.SampleHeight(new Vector3(xz.x, 0f, xz.y));
        }
    }
}
