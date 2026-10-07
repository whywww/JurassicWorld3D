using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace JurassicWorld.Editor
{
    /// <summary>Prepares the five approved tree species used by the central forest.</summary>
    public static class PolyHavenFiveTreePreview
    {
        private const string Source = "Assets/ThirdParty/VegetationDiversity/PolyHaven/";
        private const string Prepared = "Assets/ThirdParty/VegetationDiversity/Prepared/";
        private const string Prefabs = "Assets/VegetationDiversity/Prefabs/";
        private const string Materials = "Assets/VegetationDiversity/Materials/";

        private sealed class TreeSpec
        {
            internal string Id, Name;
            internal TreeSpec(string id, string name) { Id = id; Name = name; }
        }

        private static readonly TreeSpec[] Trees =
        {
            new TreeSpec("tree_small_02", "TreeSmall02"),
            new TreeSpec("island_tree_03", "IslandTree03"),
            new TreeSpec("island_tree_01", "IslandTree01"),
            new TreeSpec("jacaranda_tree", "JacarandaTree"),
            new TreeSpec("fir_tree_01", "FirTree01"),
        };

        [MenuItem("Jurassic World/Assets/Prepare Five Forest Tree Species")]
        private static void PrepareAssetsMenu() => JurassicMenuGuard.Run(PrepareAssets, false);

        [MenuItem("Jurassic World/Assets/Prepare Five Forest Tree Species", true)]
        private static bool PrepareAssetsMenuEnabled() => JurassicMenuGuard.CanRun;

        public static void PrepareAssets()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (TreeSpec spec in Trees)
            {
                string folder = Source + spec.Name + "/textures/";
                foreach (string file in Directory.GetFiles(folder))
                {
                    if (file.EndsWith(".meta") || !file.Contains("_nor_gl_")) continue;
                    string path = file.Replace('\\', '/');
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    if (importer.textureType != TextureImporterType.NormalMap || importer.maxTextureSize != 1024)
                    {
                        importer.textureType = TextureImporterType.NormalMap;
                        importer.maxTextureSize = 1024;
                        importer.SaveAndReimport();
                    }
                }
                string modelPath = Prepared + spec.Name + "_LODs.fbx";
                var modelImporter = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                if (modelImporter == null) throw new FileNotFoundException("Prepared model missing", modelPath);
                if (!modelImporter.isReadable || modelImporter.importCameras || modelImporter.importLights)
                {
                    modelImporter.isReadable = true;
                    modelImporter.importCameras = false;
                    modelImporter.importLights = false;
                    modelImporter.SaveAndReimport();
                }
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                var root = new GameObject(spec.Name);
                try
                {
                    var body = (GameObject)PrefabUtility.InstantiatePrefab(model);
                    body.transform.SetParent(root.transform, false);
                    foreach (LODGroup group in body.GetComponentsInChildren<LODGroup>(true)) UnityEngine.Object.DestroyImmediate(group);
                    foreach (Collider collider in body.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                    foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.sharedMaterials = renderer.sharedMaterials.Select(m => MakeMaterial(spec, m.name)).ToArray();
                        renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                        renderer.receiveShadows = true;
                    }
                    var lods = new List<LOD>();
                    float[] thresholds = { .35f, .12f, .015f };
                    for (int level = 0; level < 3; level++)
                    {
                        Renderer[] renderers = body.GetComponentsInChildren<Renderer>(true)
                            .Where(r => r.name.EndsWith("_LOD" + level, StringComparison.Ordinal)).ToArray();
                        if (renderers.Length == 0) throw new InvalidOperationException("Missing LOD " + spec.Name + " " + level);
                        lods.Add(new LOD(thresholds[level], renderers));
                    }
                    var lodGroup = root.AddComponent<LODGroup>();
                    lodGroup.SetLODs(lods.ToArray());
                    lodGroup.RecalculateBounds();
                    PrefabUtility.SaveAsPrefabAsset(root, Prefabs + spec.Name + ".prefab");
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
                Debug.Log("FIVE_TREE_ASSETS_READY: 5 native-scale prefabs, three LODs each.");
        }

        private static Material MakeMaterial(TreeSpec spec, string importedName)
        {
            string stem = importedName.Replace(" (Instance)", "");
            if (spec.Name == "TreeSmall02")
                stem = stem.Replace("_branches", "_branch").Replace("_trunk", "");
            string folder = Source + spec.Name + "/textures/";
            bool foliage = stem.Contains("leaves") || stem.Contains("branch") || stem.Contains("twig");
            string albedo = TexturePath(folder, stem, "diff");
            if (albedo == null && spec.Name == "FirTree01")
            {
                // The official FBX shares the bark atlas with trunk and dead-branch materials.
                stem = spec.Id + "_bark";
                albedo = TexturePath(folder, stem, "diff");
                foliage = false;
            }
            if (albedo == null) throw new FileNotFoundException("No albedo for " + importedName);
            string path = Materials + "Preview_" + importedName + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(foliage ? "Jurassic World/Vegetation Prototype Cutout" : "Standard");
            if (shader == null) throw new InvalidOperationException("Vegetation shader missing.");
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            // The existing route checker recognizes solid wood by material name.
            material.name = "Preview_" + importedName + (foliage ? "_Foliage" : "_Trunk");
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(albedo));
            string alpha = TexturePath(folder, stem, "alpha");
            if (foliage)
            {
                material.SetTexture("_AlphaTex", alpha == null ? Texture2D.whiteTexture : AssetDatabase.LoadAssetAtPath<Texture2D>(alpha));
                material.SetFloat("_Cutoff", .34f);
                material.SetFloat("_Smoothness", .1f);
            }
            else material.SetFloat("_Glossiness", .12f);
            string normal = TexturePath(folder, stem, "nor_gl");
            if (normal != null)
            {
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
                material.EnableKeyword("_NORMALMAP");
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static string TexturePath(string folder, string stem, string channel)
        {
            return Directory.GetFiles(folder, stem + "_" + channel + "_1k.*")
                .Where(p => !p.EndsWith(".meta")).Select(p => p.Replace('\\', '/')).FirstOrDefault();
        }

    }
}
