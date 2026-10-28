using System;
using System.Collections.Generic;
using Crest;
using UnityEngine;

namespace JurassicWorld
{
    // Session-only overrides. Authored materials, population and culling distances are untouched.
    public sealed class LowQualityRendering : MonoBehaviour
    {
        internal enum OptimizationStage { Baseline, Contact, Reflection, Water, Batching, Materials }
        internal static OptimizationStage Stage => RuntimePerformanceBenchmark.Requested
            ? ParseStage(RuntimePerformanceBenchmark.Argument("-jwLowStage", "Materials")) : OptimizationStage.Materials;

        private readonly Dictionary<OceanPlanarReflection, bool> reflections = new Dictionary<OceanPlanarReflection, bool>();
        private readonly List<OceanPlanarReflection> removedReflections = new List<OceanPlanarReflection>();
        private OceanRenderer ocean;
        private Material originalWater, reflectionFreeWater;
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
                if (!RuntimePerformanceBenchmark.Requested) return LowQualityVegetation.DefaultReduction;
                int reduction = int.Parse(RuntimePerformanceBenchmark.Argument("-jwLowVegetationReduction", "0"));
                if (reduction != 0 && reduction != 10 && reduction != 20 && reduction != 30)
                    throw new ArgumentException("Low vegetation reduction must be 0, 10, 20 or 30.");
                return reduction;
            }
        }
        private Camera sortCamera;
        private UnityEngine.Rendering.OpaqueSortMode originalSort;

        private static OptimizationStage ParseStage(string value)
        {
            if (!Enum.TryParse(value, true, out OptimizationStage stage) || !Enum.IsDefined(typeof(OptimizationStage), stage))
                throw new ArgumentException("Unknown -jwLowStage: " + value);
            return stage;
        }

        internal void ApplyQuality(int tier)
        {
            vegetation.Apply(tier == 0 && Stage >= OptimizationStage.Batching, Stage >= OptimizationStage.Materials,
                tier == 0 && Stage >= OptimizationStage.Materials ? Reduction : 0);
            var camera = Camera.main;
            if (camera != sortCamera)
            {
                if (sortCamera != null) sortCamera.opaqueSortMode = originalSort;
                sortCamera = camera;
                if (camera != null) originalSort = camera.opaqueSortMode;
            }
            if (sortCamera != null) sortCamera.opaqueSortMode = tier == 0 && Stage >= OptimizationStage.Batching
                ? UnityEngine.Rendering.OpaqueSortMode.NoDistanceSort : originalSort;
            bool low = tier == 0 && Stage >= OptimizationStage.Reflection;
            removedReflections.Clear();
            foreach (var entry in reflections) if (entry.Key == null) removedReflections.Add(entry.Key);
            foreach (var reflection in removedReflections) reflections.Remove(reflection);
            foreach (var reflection in FindObjectsByType<OceanPlanarReflection>(FindObjectsSortMode.None))
                if (!reflections.ContainsKey(reflection)) reflections.Add(reflection, reflection.enabled);
            foreach (var entry in reflections)
                if (entry.Key != null) entry.Key.enabled = low ? false : entry.Value;

            var current = FindObjectOfType<OceanRenderer>();
            if (current != ocean)
            {
                RestoreWater();
                ocean = current;
                originalWater = ocean != null ? ocean.OceanMaterial : null;
            }
            if (ocean == null || originalWater == null) return;
            if (low && reflectionFreeWater == null)
            {
                reflectionFreeWater = new Material(originalWater) { name = originalWater.name + " (Low, no planar reflection)", hideFlags = HideFlags.DontSave };
                reflectionFreeWater.DisableKeyword("_PLANARREFLECTIONS_ON");
                reflectionFreeWater.SetFloat("_PlanarReflections", 0f);
            }
            ocean.OceanMaterial = low ? reflectionFreeWater : originalWater;
            ApplySimpleWater(tier == 0 && Stage >= OptimizationStage.Water);
        }

        private void ApplySimpleWater(bool replace)
        {
            if (replace == waterReplaced) return;
            if (replace)
            {
                Mesh mesh = Resources.Load<Mesh>("LowQuality/CreekSurface");
                Material material = Resources.Load<Material>("LowQuality/CreekWater");
                if (mesh == null || material == null) throw new InvalidOperationException("Prepare low-quality creek assets before testing Water stage.");
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
            if (ocean != null && originalWater != null) ocean.OceanMaterial = originalWater;
            if (reflectionFreeWater != null)
            {
                if (Application.isPlaying) Destroy(reflectionFreeWater);
                else DestroyImmediate(reflectionFreeWater);
            }
            reflectionFreeWater = null;
        }

        private void OnDestroy()
        {
            if (sortCamera != null) sortCamera.opaqueSortMode = originalSort;
            vegetation.Restore();
            foreach (var entry in reflections) if (entry.Key != null) entry.Key.enabled = entry.Value;
            RestoreWater();
        }
    }
}
