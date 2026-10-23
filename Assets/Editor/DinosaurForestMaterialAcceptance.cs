using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class DinosaurForestMaterialAcceptance
    {
        private const string Output = "Logs/acceptance/dinosaur-skins";

        // Batch entry waits for the sky environment to update before any camera renders.
        // Run without -quit: this entry exits after the deferred verification completes.
        public static void RunBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            DynamicGI.UpdateEnvironment();
            int frames = 0;
            EditorApplication.CallbackFunction ready = null;
            ready = () =>
            {
                if (++frames < 45) { EditorApplication.QueuePlayerLoopUpdate(); return; }
                EditorApplication.update -= ready;
                ApplyAndVerify();
                if (AssetDatabase.IsValidFolder("Assets/DinosaurSkinReviewBaseline"))
                    AssetDatabase.DeleteAsset("Assets/DinosaurSkinReviewBaseline");
                EditorApplication.Exit(0);
            };
            EditorApplication.update += ready;
        }

        [MenuItem("Jurassic World/Scene/Blend Dinosaur Skins with Forest")]
        public static void ApplyAndVerify()
        {
            try { Run(); }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        private static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save current scene edits first.");
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/Main.unity") scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Directory.CreateDirectory(Output);
            var models = Object.FindObjectsOfType<DinosaurAgent>(true)
                .ToDictionary(a => a.gameObject, a => a.name);
            foreach (var flyer in Object.FindObjectsOfType<PteradactalFlightController>(true))
                models.Add(flyer.gameObject, "Pteradactal");
            Require(models.Values.Contains("Protoceratops"), "Main must contain the reference Protoceratops.");
            var transforms = models.Keys.SelectMany(m => m.GetComponentsInChildren<Transform>(true))
                .ToDictionary(t => t, t => EditorJsonUtility.ToJson(t));
            var behaviours = models.Keys.SelectMany(m => m.GetComponentsInChildren<MonoBehaviour>(true))
                .Where(b => b != null).ToDictionary(b => b, b => EditorJsonUtility.ToJson(b));
            var protectedMaterials = models.Keys.SelectMany(m => m.GetComponentsInChildren<Renderer>(true))
                .SelectMany(r => r.sharedMaterials).Where(m => m != null && !DinosaurForestMaterials.IsSkin(m))
                .Distinct().ToDictionary(m => m, m => EditorJsonUtility.ToJson(m));
            string lighting = LightingSignature();
            int quality = QualitySettings.GetQualityLevel();
            var report = new List<string>();
            try
            {
                QualitySettings.SetQualityLevel(2, true);
                foreach (var model in models) CaptureViews(model.Key, model.Value, "before", true);
                DinosaurForestMaterials.ApplyPreparedAssets();
                foreach (var model in models) DinosaurForestMaterials.Apply(model.Key, model.Value);
                AssetDatabase.SaveAssets();

                foreach (var model in models)
                {
                    Verify(model.Key, model.Value, report);
                    CaptureViews(model.Key, model.Value, "after", true);
                }
                for (int tier = 0; tier < 2; tier++)
                {
                    QualitySettings.SetQualityLevel(tier, true);
                    foreach (var model in models) CaptureViews(model.Key, model.Value, "after", false);
                }

                // Absolute settings and generated texture bytes must survive repeated application.
                var skins = models.Keys.SelectMany(m => m.GetComponentsInChildren<Renderer>(true))
                    .SelectMany(r => r.sharedMaterials).Where(DinosaurForestMaterials.IsSkin).Distinct()
                    .ToDictionary(m => m, m => EditorJsonUtility.ToJson(m));
                DinosaurForestMaterials.ApplyPreparedAssets();
                foreach (var model in models) DinosaurForestMaterials.Apply(model.Key, model.Value);
                foreach (var material in skins)
                    Require(material.Value == EditorJsonUtility.ToJson(material.Key), "Repeated application changed " + material.Key.name);
                foreach (var item in protectedMaterials)
                    Require(item.Value == EditorJsonUtility.ToJson(item.Key), "Eye/teeth/claw material changed: " + item.Key.name);
                foreach (var item in transforms)
                    Require(item.Value == EditorJsonUtility.ToJson(item.Key), "Model transform changed: " + item.Key.name);
                foreach (var item in behaviours)
                    Require(item.Value == EditorJsonUtility.ToJson(item.Key), "Dinosaur behavior changed: " + item.Key.name);
                foreach (var model in models)
                    foreach (var material in model.Key.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(DinosaurForestMaterials.IsSkin).Distinct())
                        VerifyRepreparation(material, model.Value);
                Require(lighting == LightingSignature(), "Scene lighting changed.");
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                report.Add("PASS: prepared assets and scene reapplied; source/default material reconstruction matches calibrated settings.");
                report.Add("PASS: protected materials, transforms, behaviors and lighting unchanged.");
                report.Add("Low tier intentionally has no shadows in the existing quality settings.");
                File.WriteAllLines(Output + "/report.txt", report);
                Debug.Log("DINOSAUR_FOREST_MATERIALS_PASS: " + models.Count + " scene dinosaurs; captures in " + Output);
            }
            finally { QualitySettings.SetQualityLevel(quality, true); }
        }

        private static string LightingSignature()
        {
            return string.Join("|", RenderSettings.ambientMode, RenderSettings.ambientLight,
                RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor,
                RenderSettings.ambientIntensity, RenderSettings.reflectionIntensity, RenderSettings.fog,
                RenderSettings.fogColor, RenderSettings.fogDensity, RenderSettings.skybox == null ? "" : EditorJsonUtility.ToJson(RenderSettings.skybox),
                string.Join(";", Object.FindObjectsOfType<Light>().OrderBy(l => l.GetInstanceID()).Select(EditorJsonUtility.ToJson)));
        }

        private static void VerifyRepreparation(Material expected, string species)
        {
            var rebuilt = new Material(BaselineMaterial(expected));
            try
            {
                rebuilt.name = expected.name;
                rebuilt.color = Color.white;
                foreach (string property in new[] { "_Glossiness", "_GlossMapScale", "_BumpScale", "_Metallic" })
                    if (rebuilt.HasProperty(property)) rebuilt.SetFloat(property, 1f);
                DinosaurForestMaterials.Configure(rebuilt, species);
                Require(rebuilt.color == expected.color, species + ": preparation lost tint.");
                foreach (string property in new[] { "_Glossiness", "_GlossMapScale", "_BumpScale", "_Metallic", "_OcclusionStrength", "_DetailNormalMapScale" })
                    if (expected.HasProperty(property))
                        Require(Mathf.Approximately(expected.GetFloat(property), rebuilt.GetFloat(property)), species + ": preparation lost " + property);
                if (expected.HasProperty("_SpecGlossMap"))
                    Require(expected.GetTexture("_SpecGlossMap") == rebuilt.GetTexture("_SpecGlossMap"), species + ": specular packing is not repeatable.");
            }
            finally { Object.DestroyImmediate(rebuilt); }
        }

        private static void Verify(GameObject model, string species, List<string> report)
        {
            int bodies = 0;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    Require(material != null && material.shader != null && material.shader.isSupported, species + ": unsupported material.");
                    Require(ShaderUtil.GetShaderMessages(material.shader).All(m => m.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error), species + ": shader compilation error.");
                    if (!DinosaurForestMaterials.IsSkin(material)) continue;
                    bodies++;
                    Require(AssetDatabase.GetAssetPath(material).StartsWith(JurassicWorldSceneBuilder.GetDinosaurPreparedFolder(species) + "/"), species + ": skin still uses supplier material.");
                    Require(material.mainTexture != null, species + ": missing albedo.");
                    Require(material.GetFloat("_Glossiness") <= .16f, species + ": excessive smoothness.");
                    Require(renderer.receiveShadows && renderer.shadowCastingMode == ShadowCastingMode.On, species + ": missing body shadows.");
                    report.Add(species + " | " + material.name + " | tint=" + material.color + " | smoothness=" + material.GetFloat("_Glossiness"));
                }
            }
            Require(bodies > 0, species + ": no body material calibrated.");
        }

        private static void CaptureViews(GameObject model, string species, string stage, bool allViews)
        {
            // Temporary copies give every animal the same unobstructed clearing and pose.
            // The third view retains its forest spawn to inspect canopy shadows.
            GameObject clone = Object.Instantiate(model);
            clone.hideFlags = HideFlags.HideAndDontSave;
            var originalRenderers = model.GetComponentsInChildren<Renderer>(true);
            bool[] enabled = originalRenderers.Select(r => r.enabled).ToArray();
            try
            {
                clone.SetActive(true);
                foreach (var renderer in originalRenderers) renderer.enabled = false;
                foreach (var animation in clone.GetComponentsInChildren<Animation>())
                    if (animation.clip != null) animation.clip.SampleAnimation(animation.gameObject, 0f);
                var renderers = clone.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                if (renderers.Length == 0) return;
                if (stage == "before")
                    foreach (var renderer in renderers)
                        renderer.sharedMaterials = renderer.sharedMaterials.Select(BaselineMaterial).ToArray();
                Bounds bounds = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                float extent = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                Vector3 originalPosition = clone.transform.position;
                Vector3 side = (-model.transform.right * .95f + model.transform.forward * .3f).normalized;
                Vector3 offset = new Vector3(-bounds.center.x, 0f, -bounds.center.z);
                if (species != "Pteradactal" && Terrain.activeTerrain != null)
                    offset.y = Terrain.activeTerrain.SampleHeight(Vector3.zero) + Terrain.activeTerrain.transform.position.y - bounds.min.y;
                clone.transform.position += offset;
                Capture(bounds.center + offset, side, extent, species, stage, "near", 1.35f);
                if (!allViews) return;
                Capture(bounds.center + offset, side, extent, species, stage, "far", 2.4f);
                clone.transform.position = originalPosition;
                Capture(bounds.center, -side, extent, species, stage, "forest", 1.6f);
            }
            finally
            {
                Object.DestroyImmediate(clone);
                for (int i = 0; i < originalRenderers.Length; i++) originalRenderers[i].enabled = enabled[i];
            }
        }

        private static Material BaselineMaterial(Material material)
        {
            if (material == null) return null;
            string path = AssetDatabase.GetAssetPath(material);
            string guid = AssetDatabase.AssetPathToGUID(path);
            // Supplier copies keep their source GUID in the filename.
            string stem = Path.GetFileNameWithoutExtension(path);
            if (path.Contains("/ForestMaterials/"))
            {
                string prefix = stem.Split('_').Last();
                if (stem.Length > 32 && stem[32] == '_') guid = stem.Substring(0, 32);
                else if (AssetDatabase.IsValidFolder("Assets/DinosaurSkinReviewBaseline"))
                {
                    string match = AssetDatabase.FindAssets("t:Material", new[] { "Assets/DinosaurSkinReviewBaseline" })
                        .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileName(p).StartsWith(prefix, StringComparison.Ordinal));
                    if (match != null) return AssetDatabase.LoadAssetAtPath<Material>(match);
                }
            }
            return AssetDatabase.LoadAssetAtPath<Material>("Assets/DinosaurSkinReviewBaseline/" + guid + ".mat") ?? material;
        }

        private static void Capture(Vector3 center, Vector3 direction, float size, string species, string stage, string view, float distance)
        {
            var holder = new GameObject("Temporary Dinosaur Skin Camera") { hideFlags = HideFlags.HideAndDontSave };
            var target = RenderTexture.GetTemporary(1136, 648, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            var pixels = new Texture2D(1136, 648, TextureFormat.RGB24, false);
            try
            {
                Camera camera = holder.AddComponent<Camera>();
                if (Camera.main != null) camera.CopyFrom(Camera.main);
                camera.enabled = false;
                camera.fieldOfView = 42f;
                camera.aspect = 1136f / 648f;
                camera.nearClipPlane = .05f;
                camera.targetTexture = target;
                camera.transform.position = center + direction * size * distance + Vector3.up * size * .32f;
                camera.transform.LookAt(center);
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1136, 648), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Output + "/" + species.Replace(' ', '_') + "_" + stage + "_" + QualitySettings.names[QualitySettings.GetQualityLevel()] + "_" + view + ".png", pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(holder);
                Object.DestroyImmediate(pixels);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
