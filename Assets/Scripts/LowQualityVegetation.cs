using System.Collections.Generic;
using UnityEngine;

namespace JurassicWorld
{
    // Low-only shared variants. Every renderer, mesh, transform and authored LOD stays in place.
    internal sealed class LowQualityVegetation
    {
        private sealed class Binding
        {
            internal MeshRenderer renderer;
            internal Material[] materials;
            internal MaterialPropertyBlock properties;
        }
        private readonly List<Binding> bindings = new List<Binding>();
        private readonly Dictionary<Material, Material> variants = new Dictionary<Material, Material>();
        private readonly Dictionary<Renderer, bool> hiddenRenderers = new Dictionary<Renderer, bool>();
        internal int EligiblePlants { get; private set; }
        internal int HiddenPlants { get; private set; }
        internal int SkippedPlants { get; private set; }
        internal const int DefaultReduction = 0;

        internal void Apply(bool enabled, bool simplify, int reduction)
        {
            Restore();
            if (!enabled) return;
            foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                Material[] originals = renderer.sharedMaterials;
                var replacements = (Material[])originals.Clone();
                bool changed = false, allMapped = originals.Length > 0;
                for (int i = 0; i < originals.Length; i++)
                {
                    string resource = Resource(originals[i], simplify);
                    if (resource == null) { allMapped = false; continue; }
                    if (!variants.TryGetValue(originals[i], out var variant))
                    {
                        Shader shader = Resources.Load<Shader>(resource);
                        if (shader == null) throw new System.InvalidOperationException("Low vegetation shader missing: " + resource);
                        variant = new Material(originals[i]) { shader = shader, enableInstancing = true, hideFlags = HideFlags.DontSave };
                        if (variant.HasProperty("_JWInstanceColor"))
                            variant.SetColor("_JWInstanceColor", originals[i].HasProperty("_TreeInstanceColor") ? originals[i].GetColor("_TreeInstanceColor") : Color.white);
                        variants.Add(originals[i], variant);
                    }
                    replacements[i] = variant; changed = true;
                }
                if (!changed) continue;
                var binding = new Binding { renderer = renderer, materials = originals };
                var plant = renderer.GetComponentInParent<InteractiveFoliage>();
                if (allMapped && plant != null && plant.convertedTerrainTree)
                {
                    binding.properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(binding.properties);
                    // PrepareRuntime adds these two values. Rest shaders do not use the lightmap color;
                    // retain the tint as an instanced property instead of an unrelated non-instanced block.
                    var block = new MaterialPropertyBlock(); block.SetColor("_JWInstanceColor", plant.instanceColor);
                    renderer.SetPropertyBlock(block);
                }
                renderer.sharedMaterials = replacements;
                bindings.Add(binding);
            }
            var manifest = Resources.Load<LowQualityPlantManifest>("LowQuality/SoftPlants");
            EligiblePlants = manifest != null ? manifest.entries.Length : 0;
            if (reduction > 0) Thin(reduction);
        }

        private void Thin(int reduction)
        {
            var manifest = Resources.Load<LowQualityPlantManifest>("LowQuality/SoftPlants");
            if (manifest == null) throw new System.InvalidOperationException("Prepare the soft plant list before testing thinning.");
            EligiblePlants = manifest.entries.Length;
            int count = EligiblePlants * reduction / 100;
            GameObject[] roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < count; i++)
            {
                var entry = manifest.entries[i];
                Transform owner = Resolve(roots, entry.hierarchy);
                if (owner == null || owner.name != entry.name || (owner.position-entry.position).sqrMagnitude > .0001f ||
                    owner.GetComponentInParent<SceneTreeObstacle>() != null || owner.GetComponentInParent<DinosaurAgent>() != null ||
                    owner.GetComponentsInChildren<SceneTreeObstacle>(true).Length > 0 || owner.GetComponentsInChildren<Collider>(true).Length > 0 ||
                    owner.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>(true).Length > 0)
                { SkippedPlants++; continue; }
                Renderer[] renderers = owner.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length != entry.renderers) { SkippedPlants++; continue; }
                foreach (var renderer in renderers)
                {
                    if (!hiddenRenderers.ContainsKey(renderer)) hiddenRenderers.Add(renderer, renderer.forceRenderingOff);
                    renderer.forceRenderingOff = true;
                }
                HiddenPlants++;
            }
            if (SkippedPlants > 0) Debug.LogWarning($"Low plant list has {SkippedPlants} stale entries; rebuild it before accepting performance results.");
        }

        private static Transform Resolve(GameObject[] roots, string path)
        {
            string[] parts = path.Split('/');
            if (!int.TryParse(parts[0], out int index) || index < 0 || index >= roots.Length) return null;
            Transform current = roots[index].transform;
            for (int i=1;i<parts.Length;i++)
            {
                if (!int.TryParse(parts[i],out index) || index < 0 || index >= current.childCount) return null;
                current=current.GetChild(index);
            }
            return current;
        }

        private static string Resource(Material material, bool simplify)
        {
            if (material == null) return null;
            switch (material.shader.name)
            {
                case "Jurassic World/Interactive Tree Leaves": return "LowQuality/" + (simplify ? "Cheap" : "Batch") + "TreeLeaves";
                case "Jurassic World/Interactive Tree Physical Bark": return "LowQuality/" + (simplify ? "Cheap" : "Batch") + "PhysicalBark";
                case "Jurassic World/Interactive Bark Sample": return "LowQuality/" + (simplify ? "Cheap" : "Batch") + "BarkSample";
                case "Jurassic World/Foliage Cutout Double Sided":
                case "Jurassic World/Vegetation Prototype Cutout": return simplify ? "LowQuality/CheapFoliage" : null;
                default: return null;
            }
        }

        internal void Restore()
        {
            foreach (var entry in hiddenRenderers) if (entry.Key != null) entry.Key.forceRenderingOff = entry.Value;
            hiddenRenderers.Clear(); EligiblePlants = HiddenPlants = SkippedPlants = 0;
            foreach (var binding in bindings)
            {
                if (binding.renderer == null) continue;
                binding.renderer.sharedMaterials = binding.materials;
                if (binding.properties != null)
                    binding.renderer.SetPropertyBlock(binding.properties.isEmpty ? null : binding.properties);
            }
            bindings.Clear();
            foreach (var material in variants.Values)
            {
                if (Application.isPlaying) Object.Destroy(material); else Object.DestroyImmediate(material);
            }
            variants.Clear();
        }
    }
}
