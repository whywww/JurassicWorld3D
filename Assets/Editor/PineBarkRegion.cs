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
    public static class PineBarkRegion
    {
        const string MaterialPath = "Assets/Environment/Vegetation/Trees/Prepared/Materials/PineBark_SpawnSample.mat";
        const string Report = "Reports/Atmosphere/PineBarkRegion";
        static bool Bark(Material m) => m != null && m.shader.name.Contains("Bark") &&
            (AssetDatabase.GetAssetPath(m) == MaterialPath || (m.HasProperty("_MainTex") &&
            AssetDatabase.GetAssetPath(m.GetTexture("_MainTex")).Contains("NatureStarterKit2/Nature/tree0")));

        public static void ApplyBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            string[] names = { "GapTree_64", "tree04_0342", "tree04_0152" };
            Vector3[] recorded = { new Vector3(56.46f, 0, -11.79f), new Vector3(53.17f, 0, -20.74f), new Vector3(52.52f, 0, -20.48f) };
            var anchors = Enumerable.Range(0, 3).Select(i => Object.FindObjectsOfType<InteractiveFoliage>().Where(f => f.name == names[i])
                .OrderBy(f => (f.transform.position - recorded[i]).sqrMagnitude).First()).ToArray();
            Vector3 center = anchors.Aggregate(Vector3.zero, (sum, f) => sum + f.transform.position) / 3f;
            bool Inside(Vector3 p) => Vector2.Distance(new Vector2(p.x, p.z), new Vector2(center.x, center.z)) <= 15f;
            var terrain = Terrain.activeTerrain;
            int retained = terrain.terrainData.treeInstances.Count(t => Inside(terrain.transform.position + Vector3.Scale(t.position, terrain.terrainData.size)));
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null) throw new InvalidOperationException("Pine Bark sample missing.");
            // Restore only accidental assignments outside the intended circle, using the
            // authored rest/contact bark variants. The original three samples are inside.
            var defaults = AssetDatabase.LoadAssetAtPath<TargetVegetationLightingProfile>(TargetVegetationLighting.ProfilePath).entries
                .Where(e => e.category == "NSK trunk").Select(e => e.material).ToArray();
            Material DefaultBark(Renderer r, bool contact)
            {
                Mesh mesh = r.GetComponent<MeshFilter>().sharedMesh;
                string path = AssetDatabase.GetAssetPath(mesh);
                string guid = AssetDatabase.AssetPathToGUID(path);
                string tree = Path.GetFileNameWithoutExtension(path);
                return defaults.First(m => AssetDatabase.GetAssetPath(m.GetTexture("_MainTex")).Contains("/" + tree + "_Textures/") &&
                    Path.GetFileName(AssetDatabase.GetAssetPath(m)).StartsWith(guid) != contact);
            }
            foreach (Renderer r in Object.FindObjectsOfType<MeshRenderer>())
            {
                var f = r.GetComponentInParent<InteractiveFoliage>();
                if (Inside(f != null ? f.transform.position : r.transform.position) || !r.sharedMaterials.Contains(material)) continue;
                Material original = DefaultBark(r, f != null);
                r.sharedMaterials = r.sharedMaterials.Select(m => m == material ? original : m).ToArray();
                PrefabUtility.RecordPrefabInstancePropertyModifications(r); EditorUtility.SetDirty(r);
            }
            foreach (InteractiveFoliage f in Object.FindObjectsOfType<InteractiveFoliage>().Where(f => !Inside(f.transform.position)))
            {
                for (int i = 0; i < f.originals.Length; i++)
                {
                    var item = f.originals[i];
                    if (!item.materials.Contains(material)) continue;
                    Material original = DefaultBark(item.renderer, false);
                    item.materials = item.materials.Select(m => m == material ? original : m).ToArray(); f.originals[i] = item;
                    EditorUtility.SetDirty(f);
                }
            }
            Directory.CreateDirectory(Report);
            var changed = new HashSet<GameObject>();
            foreach (Renderer r in Object.FindObjectsOfType<MeshRenderer>())
            {
                var foliage = r.GetComponentInParent<InteractiveFoliage>();
                GameObject owner = foliage != null ? foliage.gameObject : r.gameObject;
                if (!Inside(owner.transform.position) || !r.sharedMaterials.Any(Bark)) continue;
                r.sharedMaterials = r.sharedMaterials.Select(m => Bark(m) ? material : m).ToArray();
                PrefabUtility.RecordPrefabInstancePropertyModifications(r); EditorUtility.SetDirty(r); changed.Add(owner);
            }
            foreach (InteractiveFoliage f in Object.FindObjectsOfType<InteractiveFoliage>().Where(f => Inside(f.transform.position)))
            {
                for (int i = 0; i < f.originals.Length; i++)
                {
                    var item = f.originals[i];
                    item.materials = item.materials.Select(m => Bark(m) ? material : m).ToArray(); f.originals[i] = item;
                }
                EditorUtility.SetDirty(f);
            }
            if (retained > 0)
            {
                const string folder = "Assets/Environment/Vegetation/Trees/Prepared/Prefabs/PineBarkRegion";
                Directory.CreateDirectory(folder); AssetDatabase.Refresh();
                TerrainData data = terrain.terrainData;
                TreeInstance[] instances = data.treeInstances;
                var prototypes = data.treePrototypes.ToList();
                var replacements = new Dictionary<int, int>();
                for (int i = 0; i < instances.Length; i++)
                {
                    var tree = instances[i];
                    if (!Inside(terrain.transform.position + Vector3.Scale(tree.position, data.size))) continue;
                    if (prototypes[tree.prototypeIndex].prefab.GetComponentsInChildren<Renderer>()
                        .Any(r => r.sharedMaterials.Contains(material))) continue;
                    if (!replacements.TryGetValue(tree.prototypeIndex, out int replacement))
                    {
                        var source = prototypes[tree.prototypeIndex];
                        GameObject contents = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(source.prefab));
                        GameObject prefab;
                        try
                        {
                            foreach (Renderer r in contents.GetComponentsInChildren<Renderer>())
                                r.sharedMaterials = r.sharedMaterials.Select(m => Bark(m) ? material : m).ToArray();
                            prefab = PrefabUtility.SaveAsPrefabAsset(contents, folder + "/" + source.prefab.name + ".prefab");
                        }
                        finally { PrefabUtility.UnloadPrefabContents(contents); }
                        replacement = prototypes.Count;
                        prototypes.Add(new TreePrototype { prefab = prefab, bendFactor = source.bendFactor });
                        replacements[tree.prototypeIndex] = replacement;
                    }
                    tree.prototypeIndex = replacement; instances[i] = tree;
                }
                data.treePrototypes = prototypes.ToArray(); data.SetTreeInstances(instances, false); EditorUtility.SetDirty(data);
                foreach (var ledger in Object.FindObjectsOfType<FoliageTerrainConversion>())
                {
                    ledger.retainedSignature = FoliageInteractionPreparation.TreeSignature(data.treeInstances, data.treePrototypes);
                    EditorUtility.SetDirty(ledger);
                }
                AssetDatabase.SaveAssets();
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Object.FindObjectOfType<RainforestSkyFill>().Apply();
            Camera camera = Camera.main; Vector3 position = camera.transform.position; Quaternion rotation = camera.transform.rotation;
            try
            {
                Vector3 target = center + Vector3.up * 2.7f;
                camera.transform.position = center + new Vector3(12f, 3.2f, -10f);
                camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position);
                VegetationLightingRepair.Capture(camera, "PineBarkRegion");
                File.Copy(VegetationLightingRepair.Output + "/PineBarkRegion.png", Report + "/Region.png", true);
            }
            finally { camera.transform.SetPositionAndRotation(position, rotation); }
            string summary = "Center=" + center + "; radius=15m; NSK trees=" + (changed.Count + retained) + " (includes three samples; terrain trees=" + retained + ").\n" +
                string.Join("\n", changed.OrderBy(o => o.name).Select(o => o.name + " at " + o.transform.position));
            File.WriteAllText(Report + "/Region.txt", summary); Debug.Log("PINE_REGION_READY: " + summary);
        }
    }
}
