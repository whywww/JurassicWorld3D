using UnityEngine;
using UnityEngine.UI;

namespace JurassicWorld
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class NavigationArrowGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            vh.AddVert(new Vector3(0f, r.yMax), color, Vector2.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMin), color, Vector2.zero);
            vh.AddVert(new Vector3(0f, r.yMin + r.height * 0.25f), color, Vector2.zero);
            vh.AddVert(new Vector3(r.xMin, r.yMin), color, Vector2.zero);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
        }
    }
}