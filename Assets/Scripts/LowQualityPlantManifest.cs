using System;
using UnityEngine;

namespace JurassicWorld
{
    // Baked ownership list: one entry is one complete soft plant, including all its LOD renderers.
    public sealed class LowQualityPlantManifest : ScriptableObject
    {
        [Serializable] public struct Entry
        {
            public string hierarchy;
            public string name;
            public Vector3 position;
            public int renderers;
            public int priority;
            public float routeDistance;
            public uint stableId;
        }
        public Entry[] entries = Array.Empty<Entry>();
    }
}
