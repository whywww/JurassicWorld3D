using System;
using System.Collections.Generic;
using Crest;
using UnityEngine;

namespace JurassicWorld
{
    // Session-only overrides. Authored assets and collision geometry remain unchanged.
    public sealed class LowQualityRendering : MonoBehaviour
    {
        private readonly Dictionary<OceanPlanarReflection, bool> reflections = new Dictionary<OceanPlanarReflection, bool>();
        private readonly List<OceanPlanarReflection> removedReflections = new List<OceanPlanarReflection>();
        private OceanRenderer ocean;
        private readonly Dictionary<Behaviour, bool> waterBehaviours = new Dictionary<Behaviour, bool>();
        private GameObject simpleWater;
        private bool oceanWasEnabled, waterReplaced;
        private readonly LowQualityVegetation vegetation = new LowQualityVegetation();
        internal int EligibleSoftPlants => vegetation.EligiblePlants;
        internal int HiddenSoftPlants => vegetation.HiddenPlants;
        internal int SkippedSoftPlants => vegetation.SkippedPlants;
        internal static int Reduction
        {
            get
            {
                if (!RuntimePerformanceBenchmark.Requested) return QualitySettings.GetQualityLevel() == 0 ? 50 : QualitySettings.GetQualityLevel() == 1 ? 30 : 10;
                int reduction = int.Parse(RuntimePerformanceBenchmark.Argument("-jwLowVegetationReduction", "-1"));
                if (reduction == -1) return QualitySettings.GetQualityLevel() == 0 ? 50 : QualitySettings.GetQualityLevel() == 1 ? 30 : 10;
                if (reduction != 0 && reduction != 10 && reduction != 20 && reduction != 30 && reduction != 50)
                    throw new ArgumentException("Vegetation reduction must be auto (-1), 0, 10, 20, 30 or 50.");
                return reduction;
            }
        }
        private Camera sortCamera;
        private UnityEngine.Rendering.OpaqueSortMode originalSort;

        internal void ApplyQuality(int tier)
        {
            vegetation.Apply(Reduction);
            var camera = Camera.main;
            if (camera != sortCamera)
            {
                if (sortCamera != null) sortCamera.opaqueSortMode = originalSort;
                sortCamera = camera;
                if (camera != null) originalSort = camera.opaqueSortMode;
            }
            if (sortCamera != null) sortCamera.opaqueSortMode = UnityEngine.Rendering.OpaqueSortMode.NoDistanceSort;
            removedReflections.Clear();
            foreach (var entry in reflections) if (entry.Key == null) removedReflections.Add(entry.Key);
            foreach (var reflection in removedReflections) reflections.Remove(reflection);
            foreach (var reflection in FindObjectsByType<OceanPlanarReflection>(FindObjectsSortMode.None))
                if (!reflections.ContainsKey(reflection)) reflections.Add(reflection, reflection.enabled);
            foreach (var entry in reflections)
                if (entry.Key != null) entry.Key.enabled = false;

            var current = FindObjectOfType<OceanRenderer>();
            if (current != ocean)
            {
                RestoreWater();
                ocean = current;
            }
            if (ocean == null || ocean.OceanMaterial == null) return;
            ApplySimpleWater(true);
        }

        private void ApplySimpleWater(bool replace)
        {
            if (replace == waterReplaced) return;
            if (replace)
            {
                Mesh mesh = Resources.Load<Mesh>("LowQuality/CreekSurface");
                Material material = Resources.Load<Material>("LowQuality/CreekWater");
                if (mesh == null || material == null) throw new InvalidOperationException("Prepare creek assets before applying the rendering profile.");
                simpleWater = new GameObject("Low Quality Creek Surface");
                simpleWater.transform.SetParent(ocean.transform.parent, false);
                simpleWater.layer = ocean.Layer;
                simpleWater.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = simpleWater.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                oceanWasEnabled = ocean.enabled;
                waterBehaviours.Clear();
                foreach (var behaviour in ocean.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == ocean) continue;
                    waterBehaviours.Add(behaviour, behaviour.enabled);
                    behaviour.enabled = false;
                }
                ocean.enabled = false;
                waterReplaced = true;
            }
            else
            {
                ocean.enabled = oceanWasEnabled;
                foreach (var entry in waterBehaviours) if (entry.Key != null) entry.Key.enabled = entry.Value;
                waterBehaviours.Clear();
                if (simpleWater != null) { simpleWater.SetActive(false); Release(simpleWater); }
                simpleWater = null;
                waterReplaced = false;
            }
        }

        private static void Release(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        private void RestoreWater()
        {
            if (ocean != null) ApplySimpleWater(false);
            else
            {
                if (simpleWater != null) Release(simpleWater);
                simpleWater = null; waterReplaced = false; waterBehaviours.Clear();
            }
        }

        private void OnDestroy()
        {
            if (sortCamera != null) sortCamera.opaqueSortMode = originalSort;
            vegetation.Restore();
            RestoreWater();
            foreach (var entry in reflections) if (entry.Key != null) entry.Key.enabled = entry.Value;
        }
    }
}
