using System;
using UnityEngine;

namespace JurassicWorld
{
    // Baked animation envelope in route-forward coordinates, in world metres.
    public sealed class DinosaurCoreBody : MonoBehaviour
    {
        [Serializable] public struct Band { public int layer; public Bounds bounds; public int part; }
        public Band[] bands = Array.Empty<Band>();
        public string[] coreBones = Array.Empty<string>();
        private Band[] radiusBands;
        private float cachedRadius;
        // Arrays replaced by pose sampling invalidate automatically; call this after in-place band edits.
        public void InvalidateBoundsCache() => radiusBands = null;
        private void OnValidate() => InvalidateBoundsCache();
        public float BroadRadius
        {
            get
            {
                if (Application.isPlaying && ReferenceEquals(radiusBands, bands)) return cachedRadius;
                float radius = 0f;
                foreach (var band in bands) radius = Mathf.Max(radius,
                    new Vector2(band.bounds.center.x, band.bounds.center.z).magnitude + new Vector2(band.bounds.extents.x, band.bounds.extents.z).magnitude);
                radiusBands = bands; cachedRadius = radius;
                return radius;
            }
        }
        public bool Intersects(Vector2 position, Vector2 forward, TreeTrunkSection section)
        {
            if (section.radius <= 0f) return false;
            Vector2 delta = section.center - position;
            Vector2 right = new Vector2(forward.y, -forward.x);
            float localX = Vector2.Dot(delta, right), localZ = Vector2.Dot(delta, forward);
            float radiusSquared = section.radius * section.radius;
            foreach (var band in bands)
            {
                if (section.top <= band.layer * .5f || section.bottom >= (band.layer + 1) * .5f) continue;
                Bounds b = band.bounds;
                float x = Mathf.Max(Mathf.Abs(localX - b.center.x) - b.extents.x, 0f);
                float z = Mathf.Max(Mathf.Abs(localZ - b.center.z) - b.extents.z, 0f);
                if (x * x + z * z < radiusSquared) return true;
            }
            return false;
        }
        public float Gap(Vector2 position, Vector2 forward, TreeTrunkSection section)
        {
            Vector2 right = new Vector2(forward.y, -forward.x);
            Vector2 delta = section.center - position;
            Vector2 local = new Vector2(Vector2.Dot(delta, right), Vector2.Dot(delta, forward));
            float gap = float.PositiveInfinity;
            foreach (var band in bands)
            {
                if (section.top <= band.layer * .5f || section.bottom >= (band.layer + 1) * .5f) continue;
                Bounds b = band.bounds;
                float x = Mathf.Max(Mathf.Abs(local.x - b.center.x) - b.extents.x, 0f);
                float z = Mathf.Max(Mathf.Abs(local.y - b.center.z) - b.extents.z, 0f);
                gap = Mathf.Min(gap, Mathf.Sqrt(x * x + z * z) - section.radius);
            }
            return gap;
        }
    }
    [Serializable] public struct TreeTrunkSection
    {
        public Vector2 center;
        public float bottom, top, radius;
    }
}
