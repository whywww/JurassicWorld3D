using UnityEngine;
using UnityEngine.Rendering;

namespace JurassicWorld
{
    /// <summary>Phase 1 临时标记：每条闭合通道只显示一条中心线。Inspector 与 F3 均可开关。</summary>
    [DisallowMultipleComponent]
    public sealed class MovementCorridorDebug : MonoBehaviour
    {
        public const string RootNodeName = "Movement Corridor (Debug)";
        public const bool ShowMarkersDefault = true;
        private static readonly Color[] Colors =
        {
            new Color(0.15f, 1f, 1f),
            new Color(0.35f, 1f, 0.45f),
            new Color(1f, 0.7f, 0.2f),
            new Color(1f, 0.35f, 0.55f),
            new Color(0.65f, 0.45f, 1f),
            new Color(1f, 0.95f, 0.3f),
        };

        [SerializeField] private bool showMarkers = ShowMarkersDefault;
        [SerializeField] private KeyCode toggleKey = KeyCode.F3;
        [SerializeField] private float groundLift = 0.06f;
        [SerializeField] private float lineWidth = 0.18f;

        private LineRenderer[] lines = new LineRenderer[0];
        private Material[] materials = new Material[0];
        public bool ShowMarkers => showMarkers;

        private void Awake()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Debug.LogWarning("[MovementCorridorDebug] 找不到中心线 shader。", this);
                return;
            }

            lines = new LineRenderer[MovementCorridor.RouteCount];
            materials = new Material[lines.Length];
            for (int route = 0; route < lines.Length; route++)
            {
                Color color = Colors[Mathf.Min(route, Colors.Length - 1)];
                materials[route] = new Material(shader) { color = color };
                var child = new GameObject($"Route {route} Centerline");
                child.transform.SetParent(transform, false);
                var line = child.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.loop = true;
                line.sharedMaterial = materials[route];
                line.startWidth = lineWidth;
                line.endWidth = lineWidth;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.alignment = LineAlignment.View;
                line.positionCount = MovementCorridor.SampleCount(route);
                var vertices = new Vector3[line.positionCount];
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector2 p = MovementCorridor.GetSample(route, i).Position;
                    vertices[i] = new Vector3(p.x, groundLift, p.y);
                }
                line.SetPositions(vertices);
                lines[route] = line;
            }
            ApplyVisibility();
        }

        private void Update()
        {
            if (!Input.GetKeyDown(toggleKey)) return;
            SetShowMarkers(!showMarkers);
            Debug.Log($"[MovementCorridorDebug] 中心线 {(showMarkers ? "显示" : "隐藏")}（{toggleKey}）。", this);
        }

        public void SetShowMarkers(bool visible)
        {
            showMarkers = visible;
            ApplyVisibility();
        }

        private void OnValidate() => ApplyVisibility();

        private void ApplyVisibility()
        {
            foreach (LineRenderer line in lines)
                if (line != null) line.enabled = showMarkers;
        }

        private void OnDestroy()
        {
            foreach (Material material in materials)
            {
                if (material == null) continue;
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
        }

        private void OnDrawGizmos()
        {
            if (!showMarkers) return;
            for (int route = 0; route < MovementCorridor.RouteCount; route++)
            {
                Gizmos.color = Colors[Mathf.Min(route, Colors.Length - 1)];
                int count = MovementCorridor.SampleCount(route);
                for (int i = 0; i < count; i++)
                {
                    Vector2 a = MovementCorridor.GetSample(route, i).Position;
                    Vector2 b = MovementCorridor.GetSample(route, (i + 1) % count).Position;
                    Gizmos.DrawLine(new Vector3(a.x, groundLift, a.y),
                        new Vector3(b.x, groundLift, b.y));
                }
            }
        }
    }
}
