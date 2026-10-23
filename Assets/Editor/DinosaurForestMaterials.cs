using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace JurassicWorld.Editor
{
    /// <summary>Editor-only, absolute material settings shared by scene and prefab preparation.</summary>
    internal static class DinosaurForestMaterials
    {
        internal static readonly string[] Species = {
            "Tyrannosaurus Rex", "Velociraptor", "Pachycephalosaurus", "Protoceratops",
            "Sauropelta", "Baurusuchus", "Stegosaurus", "Rajasaurus", "Argentinosaurus", "Pteradactal"
        };

        private readonly struct Skin
        {
            internal readonly Color tint;
            internal readonly float smoothness, normal;
            internal Skin(float r, float g, float b, float smoothness, float normal)
            { tint = new Color(r, g, b, 1f); this.smoothness = smoothness; this.normal = normal; }
        }

        private static Skin Profile(string species)
        {
            switch (species)
            {
                case "Protoceratops": return new Skin(.80f, .78f, .73f, .12f, .55f);
                case "Sauropelta": return new Skin(.82f, .80f, .74f, .14f, .65f);
                case "Pachycephalosaurus": return new Skin(.86f, .83f, .78f, .16f, .70f);
                case "Baurusuchus": return new Skin(.86f, .85f, .80f, .16f, .70f);
                case "Velociraptor": return new Skin(.86f, .86f, .82f, .16f, .70f);
                case "Stegosaurus": return new Skin(.89f, .88f, .82f, .16f, .70f);
                case "Rajasaurus": return new Skin(.88f, .85f, .80f, .16f, .70f);
                case "Argentinosaurus": return new Skin(.88f, .86f, .80f, .14f, .70f);
                case "Pteradactal": return new Skin(.88f, .81f, .66f, .16f, .65f);
                case "Tyrannosaurus Rex": return new Skin(.88f, .86f, .81f, .16f, .70f);
                default: throw new ArgumentException("Unknown dinosaur: " + species);
            }
        }

        internal static bool IsSkin(Material material)
        {
            if (material == null) return false;
            string name = material.name.ToLowerInvariant();
            // Some suppliers combine eyes, teeth and claws in a single atlas.
            return !new[] { "eye", "cornea", "lens", "teeth", "tooth", "claw", "other", "secondary" }
                .Any(name.Contains) && material.renderQueue < 3000;
        }

        internal static void Apply(GameObject model, string species)
        {
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (!IsSkin(materials[i])) continue;
                    materials[i] = PreparedCopy(materials[i], species);
                    Configure(materials[i], species);
                }
                renderer.sharedMaterials = materials;
                // Transparent corneas keep their original shadow behavior.
                if (materials.Any(IsSkin))
                {
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                }
                EditorUtility.SetDirty(renderer);
                if (PrefabUtility.IsPartOfPrefabInstance(renderer))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
        }

        private static Material PreparedCopy(Material source, string species)
        {
            string folder = JurassicWorldSceneBuilder.GetDinosaurPreparedFolder(species);
            string path = AssetDatabase.GetAssetPath(source);
            if (path.StartsWith(folder + "/", StringComparison.Ordinal) && path.EndsWith(".mat")) return source;
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Skin must be a saved asset: " + source.name);
            JurassicWorldSceneBuilder.EnsureFolder(folder + "/ForestMaterials");
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId);
            string filename = source.name;
            foreach (char invalid in Path.GetInvalidFileNameChars()) filename = filename.Replace(invalid, '_');
            string target = folder + "/ForestMaterials/" + filename + "_" + guid.Substring(0, 8) + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(target);
            if (material == null)
            {
                material = new Material(source) { name = Path.GetFileNameWithoutExtension(target) };
                AssetDatabase.CreateAsset(material, target);
            }
            else EditorUtility.CopySerialized(source, material);
            material.name = Path.GetFileNameWithoutExtension(target);
            return material;
        }

        internal static void Configure(Material material, string species)
        {
            if (!IsSkin(material)) return;
            Skin skin = Profile(species);
            material.color = skin.tint;
            Set(material, "_Metallic", 0f);
            Set(material, "_Glossiness", skin.smoothness);
            Set(material, "_GlossMapScale", skin.smoothness);
            Set(material, "_BumpScale", skin.normal);
            if (material.HasProperty("_DetailNormalMapScale"))
                material.SetFloat("_DetailNormalMapScale", Mathf.Sign(material.GetFloat("_DetailNormalMapScale")) * .35f);
            Set(material, "_OcclusionStrength", .85f);
            if (material.HasProperty("_SpecColor")) material.SetColor("_SpecColor", new Color(.04f, .04f, .04f, 1f));
            if (material.HasProperty("_SpecGlossMap") && material.GetTexture("_SpecGlossMap") is Texture2D specular)
                material.SetTexture("_SpecGlossMap", ForestSpecular(specular, species));
            EditorUtility.SetDirty(material);
        }

        private static void Set(Material material, string property, float value)
        { if (material.HasProperty(property)) material.SetFloat(property, value); }

        private static Texture2D ForestSpecular(Texture2D source, string species)
        {
            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (sourcePath.EndsWith("_ForestSpecular.png", StringComparison.Ordinal)) return source;
            string folder = JurassicWorldSceneBuilder.GetDinosaurPreparedFolder(species) + "/ForestMaterials";
            JurassicWorldSceneBuilder.EnsureFolder(folder);
            string path = folder + "/" + AssetDatabase.AssetPathToGUID(sourcePath) + "_ForestSpecular.png";
            // Convert sampled RGB to low, non-metallic skin reflectance; preserve the smoothness alpha.
            // Linear readback avoids accidentally treating sRGB source specular maps as linear data.
            var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            var pixels = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                Color[] colors = pixels.GetPixels();
                for (int i = 0; i < colors.Length; i++)
                {
                    Color c = colors[i];
                    colors[i] = new Color(Mathf.Min(c.r * .25f, .08f), Mathf.Min(c.g * .25f, .08f), Mathf.Min(c.b * .25f, .08f), c.a);
                }
                pixels.SetPixels(colors);
                pixels.Apply();
                byte[] png = pixels.EncodeToPNG();
                if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(png))
                {
                    File.WriteAllBytes(path, png);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                }
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (importer.sRGBTexture || importer.alphaSource != TextureImporterAlphaSource.FromInput || importer.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    importer.sRGBTexture = false;
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(pixels);
            }
        }

        internal static void ApplyPreparedAssets()
        {
            foreach (string species in Species)
            {
                string folder = JurassicWorldSceneBuilder.GetDinosaurPreparedFolder(species);
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
                    Configure(AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)), species);
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject prefab = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        Apply(prefab, species);
                        PrefabUtility.SaveAsPrefabAsset(prefab, path);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(prefab); }
                }
            }
        }
    }
}
