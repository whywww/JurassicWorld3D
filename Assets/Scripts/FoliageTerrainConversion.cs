using System;
using UnityEngine;

namespace JurassicWorld
{
    // Edit-mode ownership ledger. Runtime never changes the TerrainData or tree population.
    public sealed class FoliageTerrainConversion : MonoBehaviour
    {
        [Serializable] public struct RemovedTree
        {
            public int originalIndex;
            public TreeInstance instance;
            public GameObject prototype;
        }
        public Terrain terrain;
        public RemovedTree[] removed = Array.Empty<RemovedTree>();
        public int originalTreeCount;
        public string retainedSignature;
    }
}
