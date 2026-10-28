using System.IO;
using UnityEditor;
using UnityEngine;

namespace JurassicWorld.Editor
{
    public static class LowQualityCreekAssets
    {
        [MenuItem("Jurassic World/Performance/Low/Prepare Creek Assets %&#6")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Stop Play before preparing creek assets.");
            const string folder = "Assets/Resources/LowQuality";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            Mesh mesh = MainCreekIntegration.LowQualitySurface();
            const string meshPath = folder + "/CreekSurface.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing == null) AssetDatabase.CreateAsset(mesh, meshPath);
            else { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); }
            var shader = Shader.Find("Jurassic World/Low Creek Water");
            if (shader == null) throw new System.InvalidOperationException("Low creek shader missing.");
            const string materialPath = folder + "/CreekWater.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); }
            var original = AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/Creek/Prepared/CrestCreekWater.mat");
            material.SetTexture("_BumpMap", original.GetTexture("_Normals"));
            material.SetTexture("_Reflection", original.GetTexture("_ReflectionCubemapOverride"));
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssets();
            Debug.Log("JW_LOW_CREEK_PREPARED: baked authored river strip; no scene or terrain saved.");
        }
    }
}
