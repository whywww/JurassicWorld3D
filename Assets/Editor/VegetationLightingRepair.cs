using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class VegetationLightingRepair
    {
        internal const string Output = "Reports/Atmosphere/VegetationLighting";
        private const string Main = "Assets/Scenes/Main.unity";

        public static void Repair()
        {
            RestoreMaterialOnly();
        }

        public static void RestoreMaterialOnly()
        {
            Directory.CreateDirectory(Output);
            EditorSceneManager.OpenScene(Main);
            if (!File.Exists(Output + "/Main.BeforeRepair.unity"))
            {
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), Output + "/Main.BeforeRepair.unity", true);
                string terrainPath = AssetDatabase.GetAssetPath(Terrain.activeTerrain.terrainData);
                File.Copy(terrainPath, Output + "/Terrain.BeforeMaterialVariants.asset", false);
            }
            var profile = AssetDatabase.LoadAssetAtPath<RainforestAtmosphereProfile>(RainforestAtmosphereStageOne.ProfilePath);
            profile.ambientIntensity = 0.8f;
            profile.vegetationLightingEnabled = true;
            EditorUtility.SetDirty(profile);
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = profile.ambientIntensity;
            // Clear only the cached probe introduced by the previous repair. Do not
            // install a synthetic ambient probe or a runtime GI override.
            RenderSettings.ambientProbe = new SphericalHarmonicsL2();
            Camera camera = Camera.main;
            Vector3 previousPosition = camera.transform.position;
            Quaternion previousRotation = camera.transform.rotation;
            camera.GetComponent<RainforestImageFinish>().Configure(profile.finishingShader, profile.exposure, profile.bloom, 1);
            QualitySettings.SetQualityLevel(1, true);
            SetReviewView(camera);
            RecordGlobalState("GlobalRestored");
            Capture(camera, "10-GlobalRestored");
            camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
            ApplyConfiguredToCurrentScene(profile);
            RainforestAtmosphereStageOne.ValidateCurrentSceneIfConfigured();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            SetReviewView(camera);
            RecordGlobalState("MaterialOnly");
            Capture(camera, "11-MaterialOnly");
            camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
            Debug.Log("VEGETATION_MATERIAL_ONLY_READY: restored stage-one ambient/finish; only foliage materials corrected. Manual review pending.");
        }

        private static void RecordGlobalState(string label)
        {
            Light sun = RenderSettings.sun;
            var probe = RenderSettings.ambientProbe;
            File.AppendAllText(Output + "/MaterialOnlyGlobals.txt", label + ": mode=" + RenderSettings.ambientMode +
                ", intensity=" + RenderSettings.ambientIntensity + ", SH=" + probe[0, 0] + "," + probe[1, 0] + "," + probe[2, 0] +
                ", sun=" + sun.intensity + ", shadow=" + sun.shadowStrength + ", shadows=" + QualitySettings.shadows +
                ", sunEuler=" + sun.transform.eulerAngles + ", color=" + sun.color + ", skyExposure=" + RenderSettings.skybox.GetFloat("_Exposure") + "\n");
        }

        internal static void ApplyConfiguredToCurrentScene(RainforestAtmosphereProfile profile)
        {
            if (!profile.vegetationLightingEnabled) return;
            const string prepared = RainforestAtmosphereStageOne.Folder + "/Prepared/VegetationLighting";
            Directory.CreateDirectory(prepared + "/Materials");
            Directory.CreateDirectory(prepared + "/Prefabs");
            AssetDatabase.Refresh();
            var materials = new Dictionary<Material, Material>();
            Material Variant(Material source)
            {
                if (source == null || source.shader == null) return source;
                string shaderName = source.shader.name;
                if (!shaderName.Contains("Tree Creator") && !shaderName.StartsWith("Jurassic World/Interactive Tree ")) return source;
                string sourcePath = AssetDatabase.GetAssetPath(source);
                if (sourcePath.StartsWith(prepared + "/"))
                {
                    source.name = Path.GetFileNameWithoutExtension(sourcePath);
                    EditorUtility.SetDirty(source);
                    return source;
                }
                if (materials.TryGetValue(source, out Material cached)) return cached;
                bool leaves = shaderName.Contains("Leaves");
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId);
                string path = prepared + "/Materials/" + guid + "_" + localId + ".mat";
                Material result = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool created = result == null;
                if (result == null)
                {
                    result = new Material(source);
                    AssetDatabase.CreateAsset(result, path);
                }
                result.name = Path.GetFileNameWithoutExtension(path);
                result.shader = Shader.Find("Jurassic World/Interactive Tree " + (leaves ? "Leaves" : "Bark"));
                result.shaderKeywords = Array.Empty<string>();
                result.enableInstancing = false;
                result.SetFloat("_FoliageActive", 0f);
                if (created)
                {
                    result.SetFloat("_OcclusionStrength", leaves ? 0.7f : 0.65f);
                    result.SetFloat("_AlbedoGain", leaves ? 1.45f : 2f);
                    result.SetFloat("_ShadowStrength", 0.8f);
                }
                EditorUtility.SetDirty(result);
                materials.Add(source, result);
                return result;
            }
            foreach (Renderer renderer in Object.FindObjectsOfType<Renderer>())
            {
                Material[] before = renderer.sharedMaterials, after = before.Select(Variant).ToArray();
                if (before.SequenceEqual(after)) continue;
                renderer.sharedMaterials = after;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                EditorUtility.SetDirty(renderer);
            }
            foreach (InteractiveFoliage foliage in Object.FindObjectsOfType<InteractiveFoliage>())
            {
                for (int i = 0; i < foliage.originals.Length; i++)
                {
                    var original = foliage.originals[i];
                    original.materials = original.materials.Select(Variant).ToArray();
                    foliage.originals[i] = original;
                }
                EditorUtility.SetDirty(foliage);
            }
            // Terrain instances keep their exact transforms/colours and original mesh assets.
            // Only their prefab material references change; no terrain or navigation rebake.
            Terrain terrain = Terrain.activeTerrain;
            TerrainData data = terrain.terrainData;
            TreeInstance[] trees = data.treeInstances;
            TreePrototype[] oldPrototypes = data.treePrototypes;
            string layout = FoliageInteractionPreparation.TreeSignature(trees, oldPrototypes);
            var newPrototypes = new TreePrototype[oldPrototypes.Length];
            var prefabMap = new Dictionary<GameObject, GameObject>();
            bool changed = false;
            for (int i = 0; i < oldPrototypes.Length; i++)
            {
                GameObject source = oldPrototypes[i].prefab, target = source;
                string sourcePath = AssetDatabase.GetAssetPath(source);
                if (!sourcePath.StartsWith(prepared + "/") && source.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials)
                    .Any(m => m != null && m.shader.name.Contains("Tree Creator")))
                {
                    string targetPath = prepared + "/Prefabs/" + source.name + ".prefab";
                    GameObject contents = PrefabUtility.LoadPrefabContents(sourcePath);
                    try
                    {
                        foreach (Renderer renderer in contents.GetComponentsInChildren<Renderer>())
                            renderer.sharedMaterials = renderer.sharedMaterials.Select(Variant).ToArray();
                        target = PrefabUtility.SaveAsPrefabAsset(contents, targetPath);
                        foreach (MeshFilter sourceMesh in source.GetComponentsInChildren<MeshFilter>())
                            if (!target.GetComponentsInChildren<MeshFilter>().Any(f => f.sharedMesh == sourceMesh.sharedMesh))
                                throw new InvalidOperationException("Material variant changed a tree mesh.");
                    }
                    finally { PrefabUtility.UnloadPrefabContents(contents); }
                    prefabMap[source] = target;
                    changed = true;
                }
                newPrototypes[i] = new TreePrototype { prefab = target, bendFactor = oldPrototypes[i].bendFactor };
            }
            if (changed)
            {
                data.treePrototypes = newPrototypes;
                data.SetTreeInstances(trees, false);
                if (FoliageInteractionPreparation.TreeSignature(data.treeInstances, oldPrototypes) != layout)
                    throw new InvalidOperationException("Material update changed tree layout.");
                EditorUtility.SetDirty(data);
                foreach (FoliageTerrainConversion ledger in Object.FindObjectsOfType<FoliageTerrainConversion>())
                {
                    ledger.retainedSignature = FoliageInteractionPreparation.TreeSignature(data.treeInstances, data.treePrototypes);
                    for (int i = 0; i < ledger.removed.Length; i++)
                    {
                        var removed = ledger.removed[i];
                        if (prefabMap.TryGetValue(removed.prototype, out GameObject replacement)) removed.prototype = replacement;
                        ledger.removed[i] = removed;
                    }
                    EditorUtility.SetDirty(ledger);
                }
            }
            TargetVegetationLighting.ApplySaved();
            if (Object.FindObjectOfType<FoliageTerrainConversion>() != null) FoliageInteractionPreparation.ValidateCurrent();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("VEGETATION_MATERIAL_VARIANTS: " + materials.Count + " materials; tree instance layout and source meshes unchanged.");
        }

        private static void SetReviewView(Camera camera)
        {
            Terrain terrain = Terrain.activeTerrain;
            camera.transform.position = new Vector3(14.7f, terrain.SampleHeight(new Vector3(14.7f, 0, 11.5f)) + terrain.transform.position.y + 1.7f, 11.5f);
            camera.transform.rotation = Quaternion.Euler(3f, 175f, 0f);
        }

        internal static void VerifyRuntime()
        {
            var controller = Object.FindObjectOfType<RainforestAtmosphereController>();
            if (controller == null || !controller.profile.vegetationLightingEnabled) return;
            RainforestSkyFill skyFill = Object.FindObjectOfType<RainforestSkyFill>();
            if (skyFill != null) RainforestWeakSkyFill.VerifyRuntime();
            if ((skyFill == null && RenderSettings.ambientMode != AmbientMode.Skybox) || !Mathf.Approximately(RenderSettings.ambientIntensity, 0.8f))
                throw new InvalidOperationException("Stage-one ambient settings were overridden at runtime.");
            Camera camera = controller.viewCamera;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            int quality = QualitySettings.GetQualityLevel();
            SetReviewView(camera);
            QualitySettings.SetQualityLevel(1, true); controller.ApplyQuality(1);
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
            InteractiveFoliage[] foliage = Object.FindObjectsOfType<InteractiveFoliage>().Where(f => f.renderers.Any(r =>
                r != null && GeometryUtility.TestPlanesAABB(planes, r.bounds) && r.sharedMaterials.Any(m => m != null && m.HasProperty("_AlbedoGain"))))
                .Take(12).ToArray();
            if (foliage.Length == 0) throw new InvalidOperationException("No Tree Creator foliage in the review view.");
            try
            {
                foreach (InteractiveFoliage f in foliage)
                {
                    typeof(InteractiveFoliage).GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f, null);
                    f.ResetField();
                    var active = (Material[][])typeof(InteractiveFoliage).GetField("interactionMaterials", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f);
                    for (int r = 0; r < f.originals.Length; r++) for (int m = 0; m < f.originals[r].materials.Length; m++)
                    {
                        Material idle = f.originals[r].materials[m], contact = active[r][m];
                        if (idle == null || !idle.HasProperty("_AlbedoGain")) continue;
                        if (idle.shader != contact.shader || idle.GetTexture("_MainTex") != contact.GetTexture("_MainTex"))
                            throw new InvalidOperationException("Idle/contact foliage texture or shader mismatch: " + f.name + " " + idle.shader.name + " / " + contact.shader.name);
                        foreach (string property in new[] { "_OcclusionStrength", "_AlbedoGain", "_Cutoff", "_ShadowStrength", "_InstanceTintStrength", "_NormalStrength" })
                            if (idle.HasProperty(property) && !Mathf.Approximately(idle.GetFloat(property), contact.GetFloat(property)))
                                throw new InvalidOperationException("Idle/contact foliage lighting mismatch: " + property);
                    }
                }
                Capture(camera, "07-RuntimeIdle");
                foreach (InteractiveFoliage f in foliage)
                    typeof(InteractiveFoliage).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f, null);
                Capture(camera, "08-RuntimeContactZeroDisplacement");
                foreach (InteractiveFoliage f in foliage) f.ResetField();
                for (int tier = 0; tier < 3; tier++)
                {
                    QualitySettings.SetQualityLevel(tier, true); controller.ApplyQuality(tier);
                    Capture(camera, "09-Review-" + new[] { "Low", "Medium", "High" }[tier]);
                }
                File.WriteAllText(Output + "/RuntimeChecks.txt", "PASS: authored ambient/fill settings preserved on Play startup; " + foliage.Length +
                    " visible foliage groups have matched rest/contact shaders, textures and lighting parameters; zero-displacement contact switch and all three quality tiers rendered.\nManual visual approval pending.\n");
                Debug.Log("VEGETATION_LIGHTING_RUNTIME_PASS: original global ambient preserved; rest/contact match; all quality tiers rendered.");
            }
            finally
            {
                foreach (InteractiveFoliage f in foliage) f.ResetField();
                QualitySettings.SetQualityLevel(quality, true); controller.ApplyQuality(quality);
                camera.transform.SetPositionAndRotation(position, rotation);
            }
        }

        public static void Diagnose()
        {
            Directory.CreateDirectory(Output);
            EditorSceneManager.OpenScene(Main);
            Camera camera = Camera.main;
            Terrain terrain = Terrain.activeTerrain;
            camera.transform.position = new Vector3(14.7f, terrain.SampleHeight(new Vector3(14.7f, 0, 11.5f)) + terrain.transform.position.y + 1.7f, 11.5f);
            camera.transform.rotation = Quaternion.Euler(3f, 175f, 0f);
            QualitySettings.SetQualityLevel(1, true);
            RainforestImageFinish finish = camera.GetComponent<RainforestImageFinish>();
            Light sun = RenderSettings.sun;
            float intensity = RenderSettings.ambientIntensity;
            var report = new StringBuilder();
            report.AppendLine("Ambient intensity: " + intensity);
            var probe = RenderSettings.ambientProbe;
            for (int channel = 0; channel < 3; channel++)
                report.AppendLine("Ambient SH channel " + channel + ": " + string.Join(",", Enumerable.Range(0, 9).Select(i => probe[channel, i].ToString("G5"))));
            Capture(camera, "01-Before");
            finish.enabled = false;
            Capture(camera, "02-NoFinish");
            finish.enabled = true;
            RenderSettings.ambientIntensity = 1f;
            Capture(camera, "03-AmbientOne");
            RenderSettings.ambientIntensity = intensity;
            sun.shadows = LightShadows.None;
            Capture(camera, "04-NoRealtimeShadows");
            sun.shadows = LightShadows.Soft;
            var diagnosticProbe = new SphericalHarmonicsL2();
            diagnosticProbe.AddAmbientLight(new Color(0.15f, 0.17f, 0.14f));
            RenderSettings.ambientProbe = diagnosticProbe;
            Capture(camera, "05-ExplicitAmbientProbe");
            RenderSettings.ambientProbe = probe;

            foreach (Material material in Object.FindObjectsOfType<Renderer>().SelectMany(r => r.sharedMaterials)
                .Concat(terrain.terrainData.treePrototypes.SelectMany(p => p.prefab.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials)))
                .Where(m => m != null).Distinct().Where(m => m.shader.name.Contains("Tree") || m.name.Contains("bush")))
                report.AppendLine(material.name + " | " + material.shader.name + " | " + AssetDatabase.GetAssetPath(material));
            foreach (Mesh mesh in Object.FindObjectsOfType<MeshFilter>().Where(f => f.sharedMesh != null && f.name.ToLowerInvariant().Contains("tree"))
                .Select(f => f.sharedMesh).Distinct().Take(8))
            {
                Color[] colors = mesh.colors;
                if (colors.Length > 0) report.AppendLine(mesh.name + " vertex alpha min=" + colors.Min(c => c.a) + " average=" + colors.Average(c => c.a));
            }
            File.WriteAllText(Output + "/Diagnosis.txt", report.ToString());
            Debug.Log("VEGETATION_LIGHTING_DIAGNOSIS_DONE: " + Output);
        }

        internal static void Capture(Camera camera, string name)
        {
            RenderTexture previous = RenderTexture.active, oldTarget = camera.targetTexture;
            float aspect = camera.aspect;
            var target = new RenderTexture(1350, 670, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(1350, 670, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.aspect = 1350f / 670f;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1350, 670), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Output + "/" + name + ".png", pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget; camera.aspect = aspect;
                RenderTexture.active = previous;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }
    }
}
