using UnityEngine;

namespace JurassicWorld
{
    /// <summary>World-space trunk circle for authored scene trees. Foliage is not an obstacle.</summary>
    public sealed class SceneTreeObstacle : MonoBehaviour
    {
        [SerializeField, Min(0.05f)] private float radius = 0.7f;
        public float Radius => radius;
        // World-height/offset sections; optionally include hard branches for local rainforest trees.
        public TreeTrunkSection[] trunkSections = System.Array.Empty<TreeTrunkSection>();
        public bool includeHardBranches;

#if UNITY_EDITOR
        public void SetRadius(float value) => radius = Mathf.Max(0.05f, value);
#endif
    }
}
