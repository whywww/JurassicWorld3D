using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class BarkTextureSample
    {
        const string Source = "Assets/Environment/Vegetation/Trees/Source/PolyHaven/PineBark/";
        const string Textures = "Assets/Environment/Vegetation/Trees/Prepared/Textures/PineBark/";
        const string MaterialPath = "Assets/Environment/Vegetation/Trees/Prepared/Materials/PineBark_Sample.mat";
        const string Report = "Reports/Atmosphere/PineBarkSample";

        public static void ApplyBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Apply(false);
        }

        public static void ApplyThreeNearSpawn() => Apply(true);

        public static void ApplyThreeBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Apply(true);
        }

        static void Apply(bool nearSpawn)
        {
            if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/Main.unity")
                throw new InvalidOperationException("Open Main before applying bark samples.");
            Directory.CreateDirectory(Textures); Directory.CreateDirectory(Report);
            AssetDatabase.Refresh();
            var roughImporter = (TextureImporter)AssetImporter.GetAtPath(Source + "pine_bark_rough_4k.png");
            roughImporter.sRGBTexture = false; roughImporter.SaveAndReimport();
            Color[] colors = Read(AssetDatabase.LoadAssetAtPath<Texture>(Source + "pine_bark_diff_4k.png"), false);
            Color[] roughness = Read(AssetDatabase.LoadAssetAtPath<Texture>(Source + "pine_bark_rough_4k.png"), true);
            for (int i = 0; i < colors.Length; i++) colors[i].a = 1f - roughness[i].r;
            var packed = new Texture2D(2048, 2048, TextureFormat.RGBA32, true);
            packed.SetPixels(colors); packed.Apply();
            File.WriteAllBytes(Textures + "ColorSmoothness.png", packed.EncodeToPNG()); Object.DestroyImmediate(packed);
            File.Copy(Source + "pine_bark_nor_gl_4k.png", Textures + "Normal.png", true);
            AssetDatabase.Refresh();
            foreach (string name in new[] { "ColorSmoothness", "Normal" })
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(Textures + name + ".png");
                importer.textureType = name == "Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = name != "Normal"; importer.maxTextureSize = 2048;
                importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Repeat;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.SaveAndReimport();
            }
            string materialPath = nearSpawn ? MaterialPath.Replace("_Sample", "_SpawnSample") : MaterialPath;
            bool Matches(Material m) => m != null && (AssetDatabase.GetAssetPath(m) == materialPath ||
                (nearSpawn ? !IsExistingSample(m) && m.HasProperty("_MainTex") && m.shader.name.Contains("Bark") &&
                AssetDatabase.GetAssetPath(m.GetTexture("_MainTex")).Contains("NatureStarterKit2/Nature/tree0") : IsTree02Bark(m)));
            Vector3 spawn = nearSpawn ? Camera.main.transform.position : new Vector3(14.7f, 0f, 11.5f);
            InteractiveFoliage[] selected = Object.FindObjectsOfType<InteractiveFoliage>().Where(f => f.originals.Any(o => o.materials.Any(Matches)))
                .OrderBy(f => f.originals.Any(o => o.materials.Any(m => m != null && AssetDatabase.GetAssetPath(m) == materialPath)) ? 0 : 1)
                .ThenBy(f => Vector2.Distance(new Vector2(f.transform.position.x, f.transform.position.z), new Vector2(spawn.x, spawn.z)))
                .Take(nearSpawn ? 3 : 1).ToArray();
            if (selected.Length != (nearSpawn ? 3 : 1)) throw new InvalidOperationException("Not enough nearby NSK trees.");
            Material previous = selected[0].originals.SelectMany(o => o.materials).First(Matches);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null) { material = new Material(previous); AssetDatabase.CreateAsset(material, materialPath); }
            material.shader = Shader.Find("Jurassic World/Interactive Bark Sample");
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture>(Textures + "ColorSmoothness.png"));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture>(Textures + "Normal.png"));
            material.SetFloat("_AlbedoGain", 1f); material.SetFloat("_NormalStrength", .7f);
            material.mainTextureScale = new Vector2(2f, 4f);
            material.SetFloat("_FoliageActive", 0f); EditorUtility.SetDirty(material);
            foreach (InteractiveFoliage foliage in selected)
            {
                for (int i = 0; i < foliage.originals.Length; i++)
                {
                    var item = foliage.originals[i];
                    Material[] next = item.materials.Select(m => Matches(m) ? material : m).ToArray();
                    item.materials = next; foliage.originals[i] = item;
                    item.renderer.sharedMaterials = item.renderer.sharedMaterials.Select(m => Matches(m) ? material : m).ToArray();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(item.renderer);
                }
                EditorUtility.SetDirty(foliage);
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
            Object.FindObjectOfType<RainforestSkyFill>().Apply();
            Camera camera = Camera.main; Vector3 position = camera.transform.position; Quaternion rotation = camera.transform.rotation;
            Vector3 light = -RenderSettings.sun.transform.forward; light.y = 0; light.Normalize();

            try
            {
                int shot = 0;
                foreach (var foliage in selected)
                {
                    var original = foliage.originals.First(o => o.materials.Any(m => m == material));
                    Vector3 target = new Vector3(original.renderer.transform.position.x, original.renderer.bounds.min.y + 1f, original.renderer.transform.position.z);
                    camera.transform.position = target + light * 1.6f + Vector3.Cross(Vector3.up, light) * .4f;
                    camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position);
                    VegetationLightingRepair.Capture(camera, "BarkSample");
                    File.Copy(VegetationLightingRepair.Output + "/BarkSample.png", Report + (nearSpawn ? "/Spawn-" + (++shot) + ".png" : "/Sample.png"), true);
                }
            }
            finally { camera.transform.SetPositionAndRotation(position, rotation); }
            string summary = string.Join("\n", selected.Select(f => f.name + " at " + f.transform.position));
            File.WriteAllText(Report + (nearSpawn ? "/SpawnSamples.txt" : "/Sample.txt"), summary +
                "\nPine Bark (Dimitrios Savva, CC0): https://polyhaven.com/a/pine_bark. Rest/contact bark only; 2K runtime maps; other scene content unchanged.\n");
            Debug.Log("BARK_SAMPLE_READY: " + summary);

        }

        static bool IsExistingSample(Material m) => m != null && (AssetDatabase.GetAssetPath(m) == MaterialPath || AssetDatabase.GetAssetPath(m).EndsWith("/BarkBrown02_Sample.mat"));

        static bool IsTree02Bark(Material m) => m != null && (IsExistingSample(m) ||
            (m.HasProperty("_MainTex") && m.shader.name.Contains("Bark") &&
            AssetDatabase.GetAssetPath(m.GetTexture("_MainTex")).Contains("tree02_Textures")));

        static Color[] Read(Texture source, bool linear)
        {
            RenderTexture old = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(2048, 2048, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            var image = new Texture2D(2048, 2048, TextureFormat.RGBA32, false, linear);
            try
            {
                Graphics.Blit(source, rt); RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, 2048, 2048), 0, 0); image.Apply(); return image.GetPixels();
            }
            finally { RenderTexture.active = old; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(image); }
        }
    }
}
