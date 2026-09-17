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
        // 漫游可达区钳制（米）：依赖 Terrain 核心平坦活动区，地形扩大时需参数化。
        private const float TravelLimit = 47f;
        private Vector3 velocity;
        private Vector3 previousMousePosition;
        private float travelYaw;
        private float yaw;
        private float pitch;
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

            // 2. 先将期望点钳制到 ±TravelLimit（凸集：起点在界内 + 期望点在界内 → 线段全程在界内）
            desiredXZ.x = Mathf.Clamp(desiredXZ.x, -TravelLimit, TravelLimit);
            desiredXZ.y = Mathf.Clamp(desiredXZ.y, -TravelLimit, TravelLimit);

            // 3. 扫掠滑移：SlideMove 返回值必在界内且不在树内（不变量）
            Vector2 slidXZ = TerrainTreeObstacleRegistry.SlideMove(
                currentXZ, desiredXZ, PlayerCollisionRadius, Time.deltaTime);

            // 4. 防御断言：理论不可达，但若触发只警告一次、原样接受（不做瞬移）
            if (!warnedSlideInvariant)
            {
                bool blocked = TerrainTreeObstacleRegistry.IsBlocked(slidXZ, PlayerCollisionRadius);
                bool outOfBounds = Mathf.Abs(slidXZ.x) > TravelLimit || Mathf.Abs(slidXZ.y) > TravelLimit;
                if (blocked || outOfBounds)
                {
                    warnedSlideInvariant = true;
                    Debug.LogWarning("[SmoothExplorerCamera] SlideMove 不变量违反："
                        + $"blocked={blocked}, outOfBounds={outOfBounds}, result={slidXZ}");
                }
            }

            // 5. 赋值最终位置：y = eyeHeight（人眼视高 1.7m，依赖 Terrain 核心活动区 heightmap=0）
            // NOTE: 引入地形起伏或扩大地图时需参数化 eyeHeight 并采样 Terrain 高度。
            transform.position = new Vector3(slidXZ.x, eyeHeight, slidXZ.y);

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
    }
}
