using UnityEngine;

namespace JurassicWorld
{
    public static class DinosaurModelFitter
    {
        public static bool Fit(GameObject model, Transform anchor, float targetLength, bool alignBottom)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0 || targetLength <= 0f)
                return false;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            float horizontalLength = Mathf.Max(bounds.size.x, bounds.size.z);
            if (horizontalLength <= 0.0001f)
                return false;

            model.transform.localScale *= targetLength / horizontalLength;

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            Vector3 targetCenter = anchor.position;
            targetCenter.y = alignBottom ? anchor.position.y + bounds.extents.y : anchor.position.y;
            model.transform.position += targetCenter - bounds.center;
            return true;
        }
    }
}
