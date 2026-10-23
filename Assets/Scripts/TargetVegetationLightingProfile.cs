using System;
using UnityEngine;

namespace JurassicWorld
{
    // Authoring data only; materials contain the runtime values.
    public sealed class TargetVegetationLightingProfile : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string category;
            public Material material;
            public Shader shader;
            public Texture packedNormal;
            [Range(0f, 1f)] public float occlusionStrength;
            [Range(1f, 4f)] public float albedoGain = 1f;
            [Range(0f, 1f)] public float instanceTintStrength = 1f;
        }
        public Entry[] entries = new Entry[0];
    }
}
