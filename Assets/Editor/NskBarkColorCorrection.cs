using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class NskBarkColorCorrection
    {
        private const string Output = "Reports/Atmosphere/BarkColor";

        public static void ApplyBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Directory.CreateDirectory(Output);
            var profile = AssetDatabase.LoadAssetAtPath<TargetVegetationLightingProfile>(TargetVegetationLighting.ProfilePath);
            var entries = profile.entries.Where(e => e.category == "NSK trunk").ToArray();
            if (entries.Length != 8) throw new InvalidOperationException("Expected eight rest/contact NSK bark materials.");
            var protectedEntries = profile.entries.Except(entries).ToDictionary(e => e.material, e => EditorJsonUtility.ToJson(e.material));
            var atmosphere = Object.FindObjectOfType<RainforestAtmosphereController>();
            if (!Mathf.Approximately(atmosphere.profile.skyFillStrength, .03f)) throw new InvalidOperationException("Sky fill must stay at 3%.");
            string environment = EditorJsonUtility.ToJson(atmosphere.profile);
            Object.FindObjectOfType<RainforestSkyFill>().Apply();
            Camera camera = atmosphere.viewCamera;
            Vector3 position = camera.transform.position; Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            Vector3 lightSide = -RenderSettings.sun.transform.forward; lightSide.y = 0f; lightSide.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, lightSide);
            var poses = Enumerable.Range(1, 4).SelectMany(i =>
            {
                string name = "tree0" + i;
                Renderer r = Object.FindObjectsOfType<MeshRenderer>().Where(x => x.sharedMaterials.Any(m => m != null && m.HasProperty("_MainTex") &&
                    AssetDatabase.GetAssetPath(m.GetTexture("_MainTex")).Contains(name + "_Textures")))
                    .OrderBy(x => (x.transform.position - new Vector3(14.7f, 1f, 11.5f)).sqrMagnitude).First();
                Vector3 target = new Vector3(r.transform.position.x, r.bounds.min.y + .85f, r.transform.position.z);
                return new[] { (label: "Lit", direction: lightSide), (label: "Side", direction: side), (label: "Shade", direction: -lightSide) }
                    .Select(view => (name: name + "-" + view.label, position: target + view.direction * 1.5f, target));
            }).ToArray();
            void Capture(string suffix)
            {
                foreach (var view in poses)
                {
                    camera.transform.SetPositionAndRotation(view.position, Quaternion.LookRotation(view.target - view.position));
                    string name = view.name + "-" + suffix;
                    VegetationLightingRepair.Capture(camera, "BarkColor-" + name);
                    File.Copy(VegetationLightingRepair.Output + "/BarkColor-" + name + ".png", Output + "/" + name + ".png", true);
                }
            }
            try
            {
                camera.fieldOfView = 45f;
                Capture("Before");
                foreach (var entry in entries) { entry.albedoGain = 2f; entry.material.SetFloat("_AlbedoGain", 2f); EditorUtility.SetDirty(entry.material); }
                EditorUtility.SetDirty(profile);
                Capture("After");
                foreach (var entry in protectedEntries)
                    if (EditorJsonUtility.ToJson(entry.Key) != entry.Value) throw new InvalidOperationException("Unrelated material changed.");
                if (EditorJsonUtility.ToJson(atmosphere.profile) != environment) throw new InvalidOperationException("Environment changed.");
                foreach (var entry in entries) AssetDatabase.SaveAssetIfDirty(entry.material);
                AssetDatabase.SaveAssetIfDirty(profile);
                File.WriteAllText(Output + "/Checks.txt", "PASS: eight NSK rest/contact bark gains set to 2; other category materials and environment unchanged. Four trees captured at fixed lit/side/shaded views. No scene save or bake.\nManual Play pending.\n");
                Debug.Log("NSK_BARK_COLOR_READY: only gain 4 -> 2; manual Play pending.");
            }
            finally { camera.fieldOfView = fov; camera.transform.SetPositionAndRotation(position, rotation); }
        }
    }
}
