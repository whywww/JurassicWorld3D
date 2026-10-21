using System;
using UnityEngine;

namespace JurassicWorld
{
    // Owned by the local planting pass; removing this root restores the previous vegetation.
    public sealed class RainforestSampleLayout : MonoBehaviour
    {
        public Vector3[] clearingPositions = Array.Empty<Vector3>();
        public Quaternion[] clearingRotations = Array.Empty<Quaternion>();
        public Vector3[] corridor = Array.Empty<Vector3>();
        public float referenceDensity, plantableArea, corridorArea, clearingArea;
        public int originalTrees, plantedTrees;
    }
}
