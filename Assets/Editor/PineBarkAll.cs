using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class PineBarkAll
    {
        internal const string MaterialPath = "Assets/Environment/Vegetation/Trees/Prepared/Materials/PineBark_Global.mat";
        const string Report = "Reports/Atmosphere/PineBarkAll";

        internal static bool IsNskBark(Material m)
        {
            if (m == null || m.shader == null || !m.shader.name.Contains("Bark")) return false;
            string path = AssetDatabase.GetAssetPath(m);
            return path == MaterialPath || path.EndsWith("/PineBark_Sample.mat") || path.EndsWith("/PineBark_SpawnSample.mat") ||
                path.EndsWith("/BarkBrown02_Sample.mat") || (m.HasProperty("_MainTex") &&
                (AssetDatabase.GetAssetPath(m.GetTexture("_MainTex")).Contains("NatureStarterKit2/Nature/tree0") ||
                 AssetDatabase.GetAssetPath(m.GetTexture("_MainTex")).Contains("Prepared/Textures/PineBark/")));
        }

        public static void VerifyBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            var remaining = Object.FindObjectsOfType<MeshRenderer>(true).SelectMany(r => r.sharedMaterials)
                .Concat(Object.FindObjectsOfType<InteractiveFoliage>(true).SelectMany(f => f.originals).SelectMany(o => o.materials))
                .Concat(Terrain.activeTerrain.terrainData.treePrototypes.SelectMany(p => p.prefab.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials))
                .Where(IsNskBark).Where(m => m != material).ToArray();
            if (remaining.Length > 0) throw new InvalidOperationException("Old NSK bark references remain: " + remaining.Length);
            Debug.Log("PINE_BARK_ALL_VERIFIED: no old NSK bark references in scene, rest/contact or terrain prototypes.");
        }

        public static void ApplyBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity"); Apply();
        }

        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().path != "Assets/Scenes/Main.unity")
                throw new InvalidOperationException("Open Main and stop Play first.");
            Directory.CreateDirectory(Report);
            var terrain = Terrain.activeTerrain;
            TreeInstance[] instances = terrain.terrainData.treeInstances;
            string signature = FoliageInteractionPreparation.TreeSignature(instances, terrain.terrainData.treePrototypes);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/Vegetation/Trees/Prepared/Materials/PineBark_SpawnSample.mat"));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            var owners = new HashSet<GameObject>();
            foreach (Renderer r in Object.FindObjectsOfType<MeshRenderer>(true))
            {
                if (!r.sharedMaterials.Any(IsNskBark)) continue;
                r.sharedMaterials = r.sharedMaterials.Select(m => IsNskBark(m) ? material : m).ToArray();
                PrefabUtility.RecordPrefabInstancePropertyModifications(r); EditorUtility.SetDirty(r);
                var f = r.GetComponentInParent<InteractiveFoliage>(); owners.Add(f != null ? f.gameObject : r.gameObject);
            }
            foreach (InteractiveFoliage f in Object.FindObjectsOfType<InteractiveFoliage>(true))
            {
                bool changed = false;
                for (int i = 0; i < f.originals.Length; i++)
                {
                    var original = f.originals[i];
                    if (!original.materials.Any(IsNskBark)) continue;
                    original.materials = original.materials.Select(m => IsNskBark(m) ? material : m).ToArray(); f.originals[i] = original; changed = true;
                }
                if (changed) EditorUtility.SetDirty(f);
            }
            var prefabs = terrain.terrainData.treePrototypes.Select(p => p.prefab).Distinct().ToArray();
            foreach (GameObject prefab in prefabs)
            {
                if (!prefab.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(IsNskBark))) continue;
                string path = AssetDatabase.GetAssetPath(prefab);
                if (!path.StartsWith("Assets/Environment/")) throw new InvalidOperationException("Refuse to modify third-party prefab: " + path);
                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (Renderer r in contents.GetComponentsInChildren<Renderer>(true))
                        r.sharedMaterials = r.sharedMaterials.Select(m => IsNskBark(m) ? material : m).ToArray();
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            terrain.terrainData.RefreshPrototypes(); terrain.Flush();
            if (FoliageInteractionPreparation.TreeSignature(terrain.terrainData.treeInstances, terrain.terrainData.treePrototypes) != signature)
                throw new InvalidOperationException("Tree layout changed.");
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
            Object.FindObjectOfType<RainforestSkyFill>().Apply();
            VegetationLightingRepair.Capture(Camera.main, "PineBarkAll");
            File.Copy(VegetationLightingRepair.Output + "/PineBarkAll.png", Report + "/All.png", true);
            string summary = "NSK scene trees=" + owners.Count + "; terrain trees=" + instances.Length + "; tree layout unchanged.\n" +
                "Pine Bark applied to NSK trunks only, including idle/contact materials. Leaves, other species and lighting preserved.\n";
            File.WriteAllText(Report + "/Summary.txt", summary); Debug.Log("PINE_BARK_ALL_READY: " + summary);
        }
    }
}
