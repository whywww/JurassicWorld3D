using UnityEngine;

namespace JurassicWorld
{
    public sealed class SmoothExplorerCamera : MonoBehaviour
    {
        /// <summary>玩家碰撞半径（米）：与 TerrainTreeObstacleRegistry 文档口径一致，验收夹具共用。</summary>
        internal const float PlayerRadius = 0.45f;

        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float acceleration = 6f;
        [SerializeField] private float turnSpeed = 60f;
        [SerializeField] private float cameraFollowSpeed = 3.5f;
        [SerializeField] private float lookSensitivity = 65f;
        [SerializeField] private float initialPitch = 4f;
        [SerializeField] private float eyeHeight = 1.7f;   // 人眼视高（米）：由 JurassicWorldSceneBuilder.BuildCamera 通过反射写入
        // 漫游可达区：唯一来源 = WorldBounds.PlayerExplorationBounds（x∈[-78,78]、z∈[-78,88]）。
        // 西/东/南三侧 = 恐龙活动包络外扩 8m（玩家可走下平坦区、踩上外围过渡带缓坡），北侧止于北林线 88。
        // 因为可达区已越出地形平坦区，相机 Y 必须采样地形高度（见下方 SampleGroundHeight）——
        // 这是原代码 NOTE 预留的「扩大地图」迁移，取代原先硬编码 eyeHeight 的前提（平坦区 heightmap 恒 0）。
        [SerializeField] private EmotivPlayerInput emotivInput;
        private Vector3 velocity;
        private Vector3 previousMousePosition;
        public float TravelHeading => Mathf.Repeat(travelYaw, 360f);

        private float travelYaw;
        private float yaw;
        private float pitch;
        private Terrain groundTerrain;          // Start 缓存（Terrain.activeTerrain）；null 时回退固定眼高
        private bool warnedMissingTerrain;      // 「无地形」警告只输出一次
        private Rect[] solidBlocks;             // 齐腰以上挡人石头的 XZ 矩形（世界 X/Z）
        private bool solidBlocksResolved;       // 只解析一次：无 settings 的场景不做逐帧查找

        private void Start()
        {
            if (emotivInput == null) emotivInput = GetComponent<EmotivPlayerInput>();
            yaw = transform.eulerAngles.y;
            travelYaw = yaw;
            pitch = initialPitch;
            groundTerrain = Terrain.activeTerrain;
            TerrainTreeObstacleRegistry.EnsureInitialized();
            EnsureSolidBlocks();
        }

        private void Update()
        {
            float turn = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            float forward = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);

            // Detect held keys, even when opposing keys cancel, so the keyboard always wins.
            bool keyboardActive = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.S)
                || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D)
                || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow)
                || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow);
            Vector2 movement = ResolveMovementInput(keyboardActive, turn, forward,
                keyboardActive ? Vector2.zero : ReadBrainMovement());
            turn = movement.x;
            forward = movement.y;
            Vector2 speeds = ResolveMovementSpeeds(keyboardActive);
            travelYaw += turn * speeds.y * Time.deltaTime;

            Vector3 travelDirection = Quaternion.Euler(0f, travelYaw, 0f) * Vector3.forward;
            Vector3 desired = travelDirection * forward * speeds.x;
            velocity = Vector3.Lerp(velocity, desired, 1f - Mathf.Exp(-acceleration * Time.deltaTime));

            // 树干与齐腰以上大石会挡住玩家并沿表面滑行；矮石/倒木可跨过，探索边界仍是硬钳制。
            Vector3 desiredPosition = transform.position + velocity * Time.deltaTime;
            MoveTo(new Vector2(desiredPosition.x, desiredPosition.z));

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

        private Vector2 ReadBrainMovement()
        {
            if (JurassicWorld.PlayerInputs.PlayerMovementInput.HasProvider)
                return JurassicWorld.PlayerInputs.PlayerMovementInput.ReadMovement();
            return emotivInput != null ? emotivInput.ReadMovement() : Vector2.zero;
        }

        private Vector2 ResolveMovementSpeeds(bool keyboardActive)
        {
            if (!keyboardActive && !JurassicWorld.PlayerInputs.PlayerMovementInput.HasProvider
                && emotivInput != null && emotivInput.isActiveAndEnabled)
            {
                var settings = emotivInput.ControlSettings;
                return new Vector2(settings.moveSpeed, settings.turnSpeed);
            }
            return new Vector2(moveSpeed, turnSpeed);
        }

        /// <summary>
        /// 移动流水线：先钳进凸矩形 → 整片树林扫掠滑行 → 大石头 AABB 推出 → 树干去穿透 → 再钳边界。
        /// 石头数据在 Start 一次性缓存；编辑态夹具会临时改写 dinosaur solidObstacles，本相机不跟随。
        /// </summary>
        private void MoveTo(Vector2 desiredXZ)
        {
            Vector2 current = new Vector2(transform.position.x, transform.position.z);
            Vector2 target = WorldBounds.ClampToPlayerExploration(desiredXZ);

            // 一次查询覆盖 Terrain 树与全部场景树标记，整片树林共享同一份 4m 网格。
            Vector2 resolved = TerrainTreeObstacleRegistry.SlideMove(
                current, target, PlayerRadius, Time.deltaTime);

            if (EnsureSolidBlocks())
            {
                resolved = PlayerSolidObstacleSolver.PushOut(resolved, PlayerRadius, solidBlocks);
                // desired == current：只走去穿透分支，处理被石头推回树内的边缘情况。
                resolved = TerrainTreeObstacleRegistry.SlideMove(
                    resolved, resolved, PlayerRadius, Time.deltaTime);
            }

            // 贴面外推与盒子推出都可能外溢几毫米，凸集收尾。
            resolved = WorldBounds.ClampToPlayerExploration(resolved);
            transform.position = new Vector3(resolved.x, SampleGroundHeight(resolved) + eyeHeight, resolved.y);
        }

        private bool EnsureSolidBlocks()
        {
            if (!solidBlocksResolved)
            {
                solidBlocksResolved = true;
                DinosaurInteractionSettings settings = FindObjectOfType<DinosaurInteractionSettings>();
                solidBlocks = settings != null
                    ? PlayerSolidObstacleSolver.BuildBlockingXZ(settings.solidObstacles)
                    : System.Array.Empty<Rect>();
            }
            return solidBlocks.Length > 0;
        }

        internal static Vector2 ResolveMovementInput(bool keyboardActive, float turn, float forward, Vector2 mental)
        {
            return keyboardActive
                ? new Vector2(turn, !Mathf.Approximately(turn, 0f) ? 1f : forward)
                : mental;
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
            return groundTerrain.SampleHeight(new Vector3(xz.x, 0f, xz.y)) + groundTerrain.transform.position.y;
        }
    }
}
