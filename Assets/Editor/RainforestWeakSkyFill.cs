using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    /// <summary>Only bakes/applies the weak diffuse fill. Does not reapply the atmosphere or reflection cube.</summary>
    public static class RainforestWeakSkyFill
    {
        private const string Output = "Reports/Atmosphere/WeakSkyFill";

        [MenuItem("Jurassic World/Environment/Atmosphere/Apply Weak Sky Fill")]
        public static void ApplyMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().path != "Assets/Scenes/Main.unity")
                throw new InvalidOperationException("Open Main and stop Play before applying sky fill.");
            ApplyCurrent();
        }

        public static void ApplyBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            ApplyCurrent();
        }

        private static void ApplyCurrent()
        {
            Directory.CreateDirectory(Output);
            var atmosphere = Object.FindObjectOfType<RainforestAtmosphereController>();
            if (atmosphere == null || atmosphere.profile == null) throw new InvalidOperationException("Atmosphere missing.");
            RainforestAtmosphereProfile profile = atmosphere.profile;
            if (!File.Exists(Output + "/Main.Before.unity"))
            {
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), Output + "/Main.Before.unity", true);
                File.WriteAllText(Output + "/Profile.Before.json", JsonUtility.ToJson(profile, true));
            }
            var before = RenderSettings.ambientProbe;
            if (profile.skyFillBaseProbe == null || profile.skyFillBaseProbe.Length != 27)
            {
                profile.skyFillBaseProbe = new float[27];
                for (int channel = 0; channel < 3; channel++) for (int i = 0; i < 9; i++)
                    profile.skyFillBaseProbe[channel * 9 + i] = before[channel, i];
            }
            profile.skyFillCoefficients = BakeSkyDiffuse(profile.skyMaterial);
            RainforestSkyFill fill = atmosphere.GetComponent<RainforestSkyFill>();
            if (fill == null) fill = atmosphere.gameObject.AddComponent<RainforestSkyFill>();
            fill.profile = profile;
            float chosenStrength = profile.skyFillStrength;
            Camera camera = atmosphere.viewCamera;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            int quality = QualitySettings.GetQualityLevel();
            try
            {
                QualitySettings.SetQualityLevel(1, true);
                Terrain terrain = Terrain.activeTerrain;
                camera.transform.position = new Vector3(14.7f, terrain.SampleHeight(new Vector3(14.7f, 0, 11.5f)) + terrain.transform.position.y + 1.7f, 11.5f);
                camera.transform.rotation = Quaternion.Euler(3f, 175f, 0f);
                CaptureStrengths(camera, fill, "Forest");
                SetDinosaurView(camera, atmosphere.sun);
                CaptureStrengths(camera, fill, "Dinosaur");
                // Respect the ground brightness ceiling at the two predeclared sample regions.
                float rise = Mathf.Max(RegionRise("Forest", new RectInt(970, 580, 240, 60)),
                    RegionRise("Forest", new RectInt(680, 560, 220, 80)));
                if (rise > 0.10f && chosenStrength > 0.05f) chosenStrength = 0.05f;
                File.WriteAllText(Output + "/GroundBudget.txt", "10% fill maximum ground sample rise: " + rise.ToString("P2") +
                    "; selected skyFillStrength=" + chosenStrength + ". Samples: (970,580,240,60), (680,560,220,80), top-left origin.\n");
                Debug.Log("WEAK_SKY_FILL_GROUND: 10% sample rise=" + rise.ToString("P2") + "; selected=" + chosenStrength);
            }
            finally
            {
                profile.skyFillStrength = chosenStrength;
                fill.Apply();
                QualitySettings.SetQualityLevel(quality, true);
                camera.transform.SetPositionAndRotation(position, rotation);
            }
            EditorUtility.SetDirty(profile); EditorUtility.SetDirty(fill);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log("WEAK_SKY_FILL_READY: only weak diffuse fill applied; manual Play review pending.");
        }

        internal static void VerifyRuntime()
        {
            RainforestSkyFill fill = Object.FindObjectOfType<RainforestSkyFill>();
            if (fill == null) return;
            var profile = fill.profile;
            var probe = RenderSettings.ambientProbe;
            if (Object.FindObjectsOfType<RainforestSkyFill>().Length != 1)
                throw new InvalidOperationException("Duplicate sky fill component.");
            float strength = profile.skyFillStrength;
            try
            {
                profile.skyFillStrength = 0f; fill.Apply();
                if (RenderSettings.ambientMode != AmbientMode.Skybox)
                    throw new InvalidOperationException("Zero fill did not restore baseline mode.");
                for (int channel = 0; channel < 3; channel++) for (int i = 0; i < 9; i++)
                    if (Mathf.Abs(RenderSettings.ambientProbe[channel, i] - profile.skyFillBaseProbe[channel * 9 + i]) > 0.00001f)
                        throw new InvalidOperationException("Zero fill did not restore baseline probe.");
            }
            finally { profile.skyFillStrength = strength; fill.Apply(); }
            fill.Apply(); // Repeated application must not accumulate light.
            for (int channel = 0; channel < 3; channel++) for (int i = 0; i < 9; i++)
                if (Mathf.Abs(probe[channel, i] - (profile.skyFillBaseProbe[channel * 9 + i] + profile.skyFillStrength * profile.skyFillCoefficients[channel * 9 + i])) > 0.00001f)
                    throw new InvalidOperationException("Weak fill differs between edit mode and Play.");
            for (int tier = 0; tier < 3; tier++)
            {
                QualitySettings.SetQualityLevel(tier, true);
                Object.FindObjectOfType<RainforestAtmosphereController>().ApplyQuality(tier);
                if (Mathf.Abs(RenderSettings.ambientProbe[1, 0] - probe[1, 0]) > 0.00001f)
                    throw new InvalidOperationException("Quality tier changed fill strength.");
            }
            Debug.Log("WEAK_SKY_FILL_RUNTIME_PASS: zero restores baseline; repeated apply, edit/Play and all quality tiers match.");
        }

        private static void CaptureStrengths(Camera camera, RainforestSkyFill fill, string view)
        {
            foreach (float strength in new[] { 0f, 0.05f, 0.10f, 0.15f })
            {
                fill.profile.skyFillStrength = strength; fill.Apply();
                VegetationLightingRepair.Capture(camera, "WeakFill-" + view + "-" + Mathf.RoundToInt(strength * 100).ToString("D2"));
                File.Copy(VegetationLightingRepair.Output + "/WeakFill-" + view + "-" + Mathf.RoundToInt(strength * 100).ToString("D2") + ".png",
                    Output + "/" + view + "-" + Mathf.RoundToInt(strength * 100).ToString("D2") + ".png", true);
            }
        }

        private static void SetDinosaurView(Camera camera, Light sun)
        {
            DinosaurAgent agent = Object.FindObjectsOfType<DinosaurAgent>().FirstOrDefault(a => a.name.Contains("Tyrannosaurus") || a.name.Contains("TRex"))
                ?? Object.FindObjectsOfType<DinosaurAgent>().First();
            Renderer[] skins = agent.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (skins.Length == 0) skins = agent.GetComponentsInChildren<Renderer>();
            if (skins.Length == 0) throw new InvalidOperationException("Dinosaur has no rendered model for comparison.");
            Bounds bounds = skins[0].bounds;
            foreach (Renderer skin in skins) bounds.Encapsulate(skin.bounds);
            Vector3 target = bounds.center;
            Vector3 offset = sun.transform.forward;
            offset.y = 0.12f; offset.Normalize();
            camera.transform.position = target + offset * Mathf.Max(5f, bounds.extents.magnitude * 1.9f);
            camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position);
        }

        private static float RegionRise(string view, RectInt region)
        {
            float Mean(string suffix)
            {
                var image = new Texture2D(2, 2);
                try
                {
                    image.LoadImage(File.ReadAllBytes(Output + "/" + view + "-" + suffix + ".png"));
                    Color[] colors = image.GetPixels(region.x, image.height - region.y - region.height, region.width, region.height);
                    return colors.Average(c => c.grayscale);
                }
                finally { Object.DestroyImmediate(image); }
            }
            return Mean("10") / Mathf.Max(0.001f, Mean("00")) - 1f;
        }

        private static float[] BakeSkyDiffuse(Material sky)
        {
            var go = new GameObject("Weak Sky Diffuse Capture", typeof(Camera), typeof(Skybox)) { hideFlags = HideFlags.HideAndDontSave };
            Camera camera = go.GetComponent<Camera>();
            camera.enabled = false; camera.clearFlags = CameraClearFlags.Skybox; camera.cullingMask = 0;
            camera.allowHDR = true; camera.fieldOfView = 90f; camera.aspect = 1f;
            go.GetComponent<Skybox>().material = sky;
            var target = new RenderTexture(32, 32, 24, RenderTextureFormat.ARGBHalf);
            var pixels = new Texture2D(32, 32, TextureFormat.RGBAFloat, false, true);
            RenderTexture previous = RenderTexture.active;
            var probe = new SphericalHarmonicsL2();
            float weightSum = 0f;
            try
            {
                camera.targetTexture = target;
                foreach (Vector3 face in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
                {
                    camera.transform.rotation = Quaternion.LookRotation(face, Mathf.Abs(face.y) > 0.9f ? Vector3.forward : Vector3.up);
                    camera.Render(); RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
                    Color[] colors = pixels.GetPixels();
                    for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                    {
                        float u = (x + 0.5f) / 32f, v = (y + 0.5f) / 32f;
                        Vector3 direction = camera.ViewportPointToRay(new Vector3(u, v, 0f)).direction;
                        float weight = 4f / (32 * 32 * Mathf.Pow(1f + Mathf.Pow(2f*u-1f, 2) + Mathf.Pow(2f*v-1f, 2), 1.5f));
                        Color color = colors[x + y * 32];
                        float peak = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
                        if (peak > 1f) color *= 1f / peak;
                        if (direction.y < 0f) color *= new Color(0.32f, 0.4f, 0.25f);
                        probe.AddDirectionalLight(direction, color, weight / Mathf.PI);
                        weightSum += weight;
                    }
                }
                float[] coefficients = new float[27];
                for (int channel = 0; channel < 3; channel++) for (int i = 0; i < 9; i++)
                    coefficients[channel * 9 + i] = probe[channel, i] * (4f * Mathf.PI / weightSum);
                return coefficients;
            }
            finally
            {
                RenderTexture.active = previous; Object.DestroyImmediate(go);
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }
    }
}
