using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace JurassicWorld
{
    /// <summary>Authored rollback bindings only; no per-frame work or runtime material mutation.</summary>
    public sealed class CreekWetSurfaceState : MonoBehaviour
    {
        [Serializable] public sealed class Binding
        {
            public Renderer renderer;
            public Material[] originals;
            public ReflectionProbeUsage originalReflection;
        }
        public CreekWetnessSettings settings;
        public Terrain terrain;
        public TerrainLayer originalBank, wetBank;
        public string originalWeightsBackup;
        public Binding[] bindings = new Binding[0];
    }
}
