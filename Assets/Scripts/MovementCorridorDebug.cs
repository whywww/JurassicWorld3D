using UnityEngine;
using UnityEngine.Rendering;

namespace JurassicWorld
{
    /// <summary>
    /// 两条恐龙通道的临时标注（Dense Jungle Phase 1，随时可关）。
    ///
    /// 【存在理由】Phase 1/1.1 的交付物只是「先决定恐龙从哪里穿过去」，需要一种最简单的方式
    /// 让你在 Play 模式里一眼看出路线是否正确（恐龙是否真的沿着它走、支路是否真的分叉又汇合）。
    /// 本组件在 Awake 时按 <see cref="MovementCorridor"/> 的权威几何程序化生成线段
    /// （不持有任何手工资产，几何一改、重新进 Play 即同步），并提供两级开关：
    ///  - <c>showMarkers</c> 序列化字段（Inspector 勾选，烘焙进场景，阶段结束后置 false 即整体消失）；
    ///  - 运行期快捷键（默认 F3）即时切换，无需重烘焙。
    ///
    /// 每条路线一组「中心线 + 左右边界 + 宽度刻度」：西侧青、东侧绿，边界黄色。
    ///
    /// 【关闭后的语义】所有 LineRenderer 仅 disabled（不销毁、不残留渲染）；Scene 视图 Gizmo
    /// 同样受该开关控制。彻底移除标注 = 把 JurassicWorldSceneBuilder 的创建步骤关掉重 Bake。
    ///
    /// 【为什么不画在 TerrainData 上】地面彩绘会改 alphamap、牵动全部验收基线与地面视觉；
    /// debug 视图只需可开关的场景线段，纯线段是改动面最小的形态。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MovementCorridorDebug : MonoBehaviour
    {
        // ------------------------------------------------------------- 跨程序集契约（Editor 侧引用）

        /// <summary>场景根节点名：Bake 创建、ValidateProject 断言存在，同 UnderstorySeeder.RootNodeName 范式。</summary>
        public const string RootNodeName = "Movement Corridor (Debug)";

        /// <summary>烘焙期写入的默认开关值（Phase = 开；阶段结束改为 false 重 Bake 即关闭）。</summary>
        public const bool ShowMarkersDefault = true;

        /// <summary>每条路线的中心线颜色（顺序与 MovementCorridor 路线索引一致；数组留有余量，超出部分取末色）。</summary>
        private static readonly Color[] RouteCenterColors =
        {
            new Color(0.15f, 1f, 1f),     // Main 主干：青
            new Color(0.35f, 1f, 0.45f),  // Foreground 前景：绿
            new Color(1f, 0.35f, 0.75f),  // Midground 中景：品红
            new Color(1f, 0.65f, 0.2f),   // ForestEdge 林缘：橙
        };

        // ------------------------------------------------------------- 可调常量

        [Header("开关")]
        [SerializeField] private bool showMarkers = ShowMarkersDefault;
        [SerializeField] private KeyCode toggleKey = KeyCode.F3;

        [Header("外观")]
        [Tooltip("线段抬离地面的高度（米）：地形恒平区 y=0，抬一点避免与草/地面 z-fighting。")]
        [SerializeField] private float groundLift = 0.06f;

        [Tooltip("线宽（米）。")]
        [SerializeField] private float lineWidth = 0.18f;

        [Tooltip("宽度刻度线的弧长间隔（米）：沿**主干**每隔多远画一条横跨左右边界的短线。")]
        [SerializeField] private float widthTickStep = 8f;

        private Material[] centerMaterials = new Material[0];
        private Material boundaryMaterial;
        private LineRenderer[] centerLines = new LineRenderer[0];
        private LineRenderer[] boundaryLines = new LineRenderer[0];
        private LineRenderer[] ticks = new LineRenderer[0];
        private bool built;

        /// <summary>标注当前是否显示（读取供校验/日志）。</summary>
        public bool ShowMarkers => showMarkers;

        private void Awake()
        {
            BuildMarkers();
            ApplyVisibility();
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                showMarkers = !showMarkers;
                ApplyVisibility();
                Debug.Log($"[MovementCorridorDebug] 通道标注 {(showMarkers ? "显示" : "隐藏")}（快捷键 {toggleKey}）。", this);
            }
        }

        private void OnDestroy()
        {
            // 材质是运行期 new 出来的实例，退出 Play 时显式释放（销毁 LineRenderer 不会自动回收 material 实例）。
            for (int i = 0; i < centerMaterials.Length; i++)
                DestroyMaterial(ref centerMaterials[i]);
            DestroyMaterial(ref boundaryMaterial);
        }

        private static void DestroyMaterial(ref Material material)
        {
            if (material == null)
                return;
            if (Application.isPlaying)
                Destroy(material);
            else
                DestroyImmediate(material);
            material = null;
        }

        // ------------------------------------------------------------- 标注生成（全部程序化，无手工资产）

        private void BuildMarkers()
        {
            if (built)
                return;
            built = true;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Debug.LogWarning("[MovementCorridorDebug] 找不到可用线段 shader，通道标注不可用。", this);
                return;
            }

            int routeCount = MovementCorridor.RouteCount;
            boundaryMaterial = new Material(shader) { color = new Color(1f, 0.9f, 0.2f, 0.95f) };
            centerMaterials = new Material[routeCount];
            centerLines = new LineRenderer[routeCount];
            boundaryLines = new LineRenderer[routeCount * 2];

            for (int route = 0; route < routeCount; route++)
            {
                Color color = RouteCenterColors[Mathf.Min(route, RouteCenterColors.Length - 1)];
                color.a = 0.95f;
                centerMaterials[route] = new Material(shader) { color = color };

                centerLines[route] = NewLine($"Route {route} Centerline", centerMaterials[route], lineWidth);
                boundaryLines[route * 2] = NewLine($"Route {route} Left Boundary", boundaryMaterial, lineWidth * 0.8f);
                boundaryLines[route * 2 + 1] = NewLine($"Route {route} Right Boundary", boundaryMaterial, lineWidth * 0.8f);

                SetCenterline(centerLines[route], route);
                SetBoundary(boundaryLines[route * 2], route, leftSide: true);
                SetBoundary(boundaryLines[route * 2 + 1], route, leftSide: false);
            }

            BuildWidthTicks();
        }

        /// <summary>每条通道沿弧长绘制宽度刻度，便于检查局部净宽。</summary>
        private void BuildWidthTicks()
        {
            float step = Mathf.Max(1f, widthTickStep);
            var tickLines = new System.Collections.Generic.List<LineRenderer>();
            for (int route = 0; route < MovementCorridor.RouteCount; route++)
            {
                int count = Mathf.Max(0, Mathf.FloorToInt(MovementCorridor.Length(route) / step));
                for (int i = 0; i < count; i++)
                {
                    float distance = (i + 1) * step;
                    MovementCorridor.GetSampleAt(route, distance,
                        out Vector2 position, out Vector2 tangent,
                        out float halfLeft, out float halfRight);
                    var normal = new Vector2(-tangent.y, tangent.x);
                    LineRenderer tick = NewLine($"Route_{route}_WidthTick_{i:00}", boundaryMaterial, lineWidth * 0.6f);
                    tick.positionCount = 2;
                    tick.SetPosition(0, Lift(position + normal * halfLeft));
                    tick.SetPosition(1, Lift(position - normal * halfRight));
                    tickLines.Add(tick);
                }
            }
            ticks = tickLines.ToArray();
        }

        private void SetCenterline(LineRenderer line, int route)
        {
            int count = MovementCorridor.SampleCount(route);
            var points = new Vector3[count];
            for (int i = 0; i < count; i++)
                points[i] = Lift(MovementCorridor.GetSample(route, i).Position);
            line.positionCount = count;
            line.SetPositions(points);
        }

        private void SetBoundary(LineRenderer line, int route, bool leftSide)
        {
            int count = MovementCorridor.SampleCount(route);
            var points = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                MovementCorridor.CorridorSample s = MovementCorridor.GetSample(route, i);
                Vector2 edge = leftSide
                    ? s.Position + s.LeftNormal * s.HalfLeft
                    : s.Position - s.LeftNormal * s.HalfRight;
                points[i] = Lift(edge);
            }
            line.positionCount = count;
            line.SetPositions(points);
        }

        private LineRenderer NewLine(string childName, Material material, float width)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;   // 顶点写世界坐标（几何是世界系），父子层级只作组织
            line.loop = false;
            line.sharedMaterial = material;
            line.startWidth = width;
            line.endWidth = width;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            return line;
        }

        private Vector3 Lift(Vector2 xz) => new Vector3(xz.x, groundLift, xz.y);

        private void ApplyVisibility()
        {
            if (!built)
                return;
            for (int route = 0; route < centerLines.Length; route++)
                SetEnabled(centerLines[route], showMarkers);
            for (int i = 0; i < boundaryLines.Length; i++)
                SetEnabled(boundaryLines[i], showMarkers);
            for (int i = 0; i < ticks.Length; i++)
                SetEnabled(ticks[i], showMarkers);
        }

        private static void SetEnabled(LineRenderer line, bool value)
        {
            if (line != null)
                line.enabled = value;
        }

        // ------------------------------------------------------------- Scene 视图（编辑态调形状用）

        private void OnDrawGizmos()
        {
            if (!showMarkers)
                return;

            // 编辑态 Awake 未跑、没有 LineRenderer，用 Gizmo 画同一份几何（不进 Game 视图、不进 Build）。
            for (int route = 0; route < MovementCorridor.RouteCount; route++)
            {
                Color color = RouteCenterColors[Mathf.Min(route, RouteCenterColors.Length - 1)];
                Gizmos.color = new Color(color.r, color.g, color.b, 0.9f);
                DrawPolyline(route, isCenter: true, leftSide: true);
                Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.9f);
                DrawPolyline(route, isCenter: false, leftSide: true);
                DrawPolyline(route, isCenter: false, leftSide: false);
            }
        }

        private void DrawPolyline(int route, bool isCenter, bool leftSide)
        {
            int count = MovementCorridor.SampleCount(route);
            for (int i = 1; i < count; i++)
            {
                Vector3 a = Lift(EdgeOf(MovementCorridor.GetSample(route, i - 1), isCenter, leftSide));
                Vector3 b = Lift(EdgeOf(MovementCorridor.GetSample(route, i), isCenter, leftSide));
                Gizmos.DrawLine(a, b);
            }
        }

        private static Vector2 EdgeOf(MovementCorridor.CorridorSample s, bool isCenter, bool leftSide)
        {
            if (isCenter)
                return s.Position;
            return leftSide
                ? s.Position + s.LeftNormal * s.HalfLeft
                : s.Position - s.LeftNormal * s.HalfRight;
        }
    }
}
