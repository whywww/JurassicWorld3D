using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace JurassicWorld.Editor
{
    /// <summary>Prepares the vegetation assets used by the production biome passes.</summary>
    internal static class VegetationDiversityPrototype
    {
        private const string Root = "Assets/Environment/Vegetation/Trees";
        private const string Materials = Root + "/Prepared/Materials";
        private const string Prefabs = Root + "/Prepared/Prefabs";
        private const string Source = Root + "/Source";
        private const string Prepared = Root + "/Prepared/Models";
        private const string Fab = Source + "/FabDesktopTrees60";
        private const string Banana = Source + "/SketchfabBananaTree/textures";
        private const string Island = Source + "/PolyHaven/IslandTree02/textures";
        private const string Pachira = Source + "/PolyHaven/PachiraAquatica01/textures";
        [MenuItem("Jurassic World/Assets/Prepare Biome Vegetation")]
        private static void PrepareAssetsMenu() => JurassicMenuGuard.Run(PrepareAssets, false);

        [MenuItem("Jurassic World/Assets/Prepare Biome Vegetation", true)]
        private static bool PrepareAssetsMenuEnabled() => JurassicMenuGuard.CanRun;

        public static void PrepareAssets()
        {
            EnsureFolder(Materials);
            EnsureFolder(Prefabs);
            Shader foliage = Shader.Find("Jurassic World/Vegetation Prototype Cutout");
            Shader standard = Shader.Find("Standard");
            if (foliage == null || standard == null) throw new InvalidOperationException("Vegetation shaders are missing.");

            // Fab: all ten source trees share these two texture sheets.
            MakeMaterial("Fab_Trunk", standard, Fab + "/T_Trees60_Trunk.png", null,
                Fab + "/T_Trees60_Trunk_normal.png", 0.12f);
            MakeMaterial("Fab_Leaf", foliage, Fab + "/T_Trees60_Leaf.png", null, null, 0.12f);

            MakeMaterial("Island_Trunk", standard, Island + "/island_tree_02_diff_1k.jpg", null,
                Island + "/island_tree_02_nor_gl_1k.exr", 0.1f);
            MakeMaterial("Island_Branches", foliage, Island + "/island_tree_02_branches_diff_1k.png", null,
                Island + "/island_tree_02_branches_nor_gl_1k.png", 0.1f);
            MakeMaterial("Island_Leaves", foliage, Island + "/island_tree_02_leaves_diff_1k.png",
                Island + "/island_tree_02_leaves_alpha_1k.png",
                Island + "/island_tree_02_leaves_nor_gl_1k.png", 0.08f);

            MakeMaterial("Pachira_Bark", standard, Pachira + "/pachira_aquatica_01_bark_diff_1k.png", null,
                Pachira + "/pachira_aquatica_01_bark_nor_gl_1k.png", 0.1f);
            MakeMaterial("Pachira_Leaves", foliage, Pachira + "/pachira_aquatica_01_leaves_diff_1k.png",
                Pachira + "/pachira_aquatica_01_leaves_alpha_1k.png",
                Pachira + "/pachira_aquatica_01_leaves_nor_gl_1k.png", 0.08f);

            string[] bananaIds = { "tiglajzdy", "sdbcdah", "slpmfhp", "sl0no0p", "pgfhf" };
            foreach (string id in bananaIds)
            {
                string albedo = Banana + $"/{id}_2K_Albedo.jpg";
                string opacity = Banana + $"/{id}_2K_Opacity.jpg";
                string normal = Banana + $"/{id}_2K_Normal.jpg";
                if (!File.Exists(opacity)) opacity = null;
                if (!File.Exists(normal)) normal = null;
                MakeMaterial("Banana_" + id, opacity == null ? standard : foliage, albedo, opacity, normal, 0.1f);
            }

            foreach (int number in new[] { 1, 5, 10 }) MakePrefab($"FabTree_{number}", number);
            MakePrefab("BananaTree", 0);
            MakePrefab("Pachira_B", 0);
            MakePrefab("IslandTree02", 0);
            AssetDatabase.SaveAssets();
            Debug.Log("VEG_ASSETS_READY: six prefabs prepared, including a three-level hero tree.");
        }

        private static Material MakeMaterial(string name, Shader shader, string albedo,
            string alpha, string normal, float smoothness)
        {
            string path = Materials + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_MainTex", RequireTexture(albedo, false));
            if (material.HasProperty("_AlphaTex"))
                material.SetTexture("_AlphaTex", alpha == null ? Texture2D.whiteTexture : RequireTexture(alpha, false));
            if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", 0.34f);
            if (normal != null)
            {
                material.SetTexture("_BumpMap", RequireTexture(normal, true));
                material.EnableKeyword("_NORMALMAP");
            }
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D RequireTexture(string path, bool normal)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new FileNotFoundException("Missing vegetation texture", path);
            bool changed = false;
            if (normal && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                changed = true;
            }
            // The prototype is viewed from the north forest edge, tens of metres away.
            if (importer.maxTextureSize > 1024)
            {
                importer.maxTextureSize = 1024;
                changed = true;
            }
            if (changed) importer.SaveAndReimport();
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) throw new InvalidOperationException("Texture import failed: " + path);
            return texture;
        }

        private static void MakePrefab(string name, int number)
        {
            bool hasLods = name == "IslandTree02" || name.StartsWith("FabTree_");
            string path = Prepared + "/" + name + (hasLods ? "_LODs" : "") + ".fbx";
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) throw new FileNotFoundException("Prepared FBX missing", path);
            var root = new GameObject(name);
            GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(model);
            body.transform.SetParent(root.transform, false);
            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                Material[] assigned = renderer.sharedMaterials;
                for (int i = 0; i < assigned.Length; i++)
                {
                    string sourceName = assigned[i] != null ? assigned[i].name.ToLowerInvariant() : string.Empty;
                    assigned[i] = ResolveMaterial(name, number, renderer.gameObject.name.ToLowerInvariant(), sourceName, i);
                }
                renderer.sharedMaterials = assigned;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            if (hasLods)
            {
                foreach (LODGroup importedGroup in body.GetComponentsInChildren<LODGroup>(true))
                    UnityEngine.Object.DestroyImmediate(importedGroup);
                Renderer[] lodRenderers = new Renderer[3];
                foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
                    for (int i = 0; i < 3; i++)
                        if (renderer.gameObject.name.Contains("LOD" + i)) lodRenderers[i] = renderer;
                if (lodRenderers.Any(r => r == null)) throw new InvalidOperationException("Hero LOD renderer missing.");
                LODGroup group = root.AddComponent<LODGroup>();
                group.SetLODs(new[]
                {
                    new LOD(name == "IslandTree02" ? 0.38f : 0.32f, new[] { lodRenderers[0] }),
                    new LOD(name == "IslandTree02" ? 0.14f : 0.10f, new[] { lodRenderers[1] }),
                    new LOD(0.025f, new[] { lodRenderers[2] }),
                });
                group.RecalculateBounds();
            }
            PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/" + name + ".prefab");
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static Material ResolveMaterial(string prefab, int number, string rendererName, string importedName, int slot)
        {
            string chosen;
            if (prefab.StartsWith("FabTree_")) chosen = slot == 0 ? "Fab_Trunk" : "Fab_Leaf";
            else if (prefab == "IslandTree02")
                chosen = importedName.Contains("leaves") || (importedName.Length == 0 && slot == 1) ? "Island_Leaves" :
                    importedName.Contains("branches") || (importedName.Length == 0 && slot == 2) ? "Island_Branches" : "Island_Trunk";
            else if (prefab.StartsWith("Pachira_"))
                chosen = rendererName.Contains("leaves") ? "Pachira_Leaves" : "Pachira_Bark";
            else if (prefab == "BananaTree")
            {
                string[] ids = { "tiglajzdy", "sdbcdah", "slpmfhp", "sl0no0p", "pgfhf" };
                chosen = "Banana_" + ids.FirstOrDefault(importedName.Contains);
                if (chosen == "Banana_") chosen = "Banana_" + ids[Mathf.Min(slot, ids.Length - 1)];
            }
            else throw new InvalidOperationException("Unknown prefab " + prefab);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(Materials + "/" + chosen + ".mat");
            if (material == null) throw new InvalidOperationException("Missing material " + chosen);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
