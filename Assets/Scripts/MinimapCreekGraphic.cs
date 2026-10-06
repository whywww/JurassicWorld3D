using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace JurassicWorld
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MinimapCreekGraphic : MaskableGraphic
    {
        [SerializeField, Min(0.1f)] private float lineWidth = 3f;
        [SerializeField] private Vector2[] points = new Vector2[0];
        public IReadOnlyList<Vector2> Points => points;

        public void SetPoints(Vector2[] positions)
        {
            points = positions;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (points == null) return;
            for (int i = 1; i < points.Length; i++)
            {
                Vector2 delta = points[i] - points[i - 1];
                if (delta.sqrMagnitude < 0.000001f) continue;
                Vector2 normal = new Vector2(-delta.y, delta.x).normalized * Mathf.Max(0.1f, lineWidth) * 0.5f;
                int start = vh.currentVertCount;
                vh.AddVert(points[i - 1] + normal, color, Vector2.zero);
                vh.AddVert(points[i] + normal, color, Vector2.zero);
                vh.AddVert(points[i] - normal, color, Vector2.zero);
                vh.AddVert(points[i - 1] - normal, color, Vector2.zero);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start, start + 2, start + 3);
            }
        }
    }
}