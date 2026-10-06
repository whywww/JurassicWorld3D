using System;
using UnityEngine;
namespace JurassicWorld
{
    // Prefab profiles are authored in prefab metres; instance scaling remains Terrain-owned.
    public sealed class ForestTrunkProfiles : MonoBehaviour
    {
        [Serializable] public sealed class Prototype { public GameObject prefab; public TreeTrunkSection[] sections; }
        public Prototype[] prototypes = Array.Empty<Prototype>();
    }
}
