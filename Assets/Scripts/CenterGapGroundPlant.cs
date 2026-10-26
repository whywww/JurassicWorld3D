using UnityEngine;

namespace JurassicWorld
{
    // Explicit opt-in for flexible understory; never registered as a hard tree.
    public sealed class CenterGapGroundPlant : MonoBehaviour
    {
        private LODGroup runtimeLod;
        private Renderer[] lodRenderers;

        internal void ApplyQuality(int tier)
        {
            if (runtimeLod == null)
            {
                if (GetComponentInParent<LODGroup>() != null || GetComponentInChildren<LODGroup>() != null) return;
                lodRenderers = GetComponentsInChildren<Renderer>();
                if (lodRenderers.Length == 0) return;
                Bounds bounds = lodRenderers[0].bounds;
                for (int i = 1; i < lodRenderers.Length; i++) bounds.Encapsulate(lodRenderers[i].bounds);
                // This explicit marker is never a hard tree; only budget the small ground plants.
                if (bounds.size.y > 1.5f || Mathf.Max(bounds.size.x, bounds.size.z) > 4f) return;
                runtimeLod = gameObject.AddComponent<LODGroup>();
                runtimeLod.localReferencePoint = transform.InverseTransformPoint(bounds.center);
                Vector3 scale = transform.lossyScale;
                float maximumScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                runtimeLod.size = Mathf.Max(2.5f, bounds.size.y) / Mathf.Max(.0001f, maximumScale);
            }
            // Medium: about 54m at 60-degree FOV. High keeps the original distant coverage.
            // QualitySettings.lodBias also scales this range, including the Low tier.
            runtimeLod.SetLODs(new[] { new LOD(tier >= 2 ? .01f : .04f, lodRenderers) });
        }
    }
}
