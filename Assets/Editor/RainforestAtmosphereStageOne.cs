using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    /// <summary>Incremental stage-one application. Does not rebake terrain, navigation or dinosaurs.</summary>
    [InitializeOnLoad]
    public static class RainforestAtmosphereStageOne
    {
        public const string Root = "Rainforest Atmosphere";
        public const string Folder = "Assets/Environment/Atmosphere";
        public const string ProfilePath = Folder + "/Prepared/AfterRain.asset";
        private const string Main = "Assets/Scenes/Main.unity";
        private const string Source = Folder + "/Source/Sky/kloofendal_48d_partly_cloudy_puresky_8k.hdr";
        private const string WaterPath = "Assets/Environment/Creek/Prepared/CrestCreekWater.mat";
        private const string Output = "Reports/Atmosphere/StageOne";
        private const string SmokeKey = "JurassicWorld.Atmosphere.StageOneSmoke";
        private static int smokeFrames;

        static RainforestAtmosphereStageOne()
        {
            EditorApplication.playModeStateChanged += SmokeStateChanged;
        }

        // Brief lifecycle/quality smoke, not a performance route or a manual-review substitute.
        public static void SmokeBatch() => BeginSmoke(true);

        public static void MaterialOnlySmokeBatch() => BeginSmoke(false);

        private static void BeginSmoke(bool applyAtmosphere)
        {
            EditorSceneManager.OpenScene(Main);
            ValidateCurrentSceneIfConfigured();
            var controller = Object.FindObjectOfType<RainforestAtmosphereController>();
            int beforeControllers = Object.FindObjectsOfType<RainforestAtmosphereController>().Length;
            int beforeDinosaurs = Object.FindObjectsOfType<DinosaurAgent>().Length;
            if (applyAtmosphere) ApplyCurrent(false);
            else
            {
                VegetationLightingRepair.ApplyConfiguredToCurrentScene(controller.profile);
                TargetVegetationLighting.CaptureTerrainTransition();
            }
            if (Object.FindObjectsOfType<RainforestAtmosphereController>().Length != beforeControllers ||
                Object.FindObjectsOfType<DinosaurAgent>().Length != beforeDinosaurs ||
                Camera.main.GetComponents<RainforestImageFinish>().Length != 1)
                throw new InvalidOperationException("Atmosphere application is not idempotent.");
            ValidateCurrentSceneIfConfigured();
            // Discard the temporary repeat-application check; use the saved scene for Play.
            EditorSceneManager.OpenScene(Main);
            controller = Object.FindObjectOfType<RainforestAtmosphereController>();
            SessionState.SetString(SmokeKey + ".Sky", File.ReadAllText(AssetDatabase.GetAssetPath(controller.profile.skyMaterial)));
            SessionState.SetString(SmokeKey + ".Cloud", File.ReadAllText(AssetDatabase.GetAssetPath(controller.profile.cloudMaterial)));
            SessionState.SetBool(SmokeKey, true);
            SessionState.SetBool(SmokeKey + ".Passed", false);
            SessionState.SetInt(SmokeKey + ".Tier", QualitySettings.GetQualityLevel());
            EditorApplication.isPlaying = true;
        }

        private static void SmokeStateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(SmokeKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                smokeFrames = 0;
                EditorApplication.update += SmokeTick;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(SmokeKey, false);
                try
                {
                    var controller = Object.FindObjectOfType<RainforestAtmosphereController>();
                    if (!SessionState.GetBool(SmokeKey + ".Passed", false) || controller == null ||
                        RenderSettings.skybox != controller.profile.skyMaterial ||
                        controller.cloudRenderer.sharedMaterial != controller.profile.cloudMaterial ||
                        File.ReadAllText(AssetDatabase.GetAssetPath(controller.profile.skyMaterial)) != SessionState.GetString(SmokeKey + ".Sky", "") ||
                        File.ReadAllText(AssetDatabase.GetAssetPath(controller.profile.cloudMaterial)) != SessionState.GetString(SmokeKey + ".Cloud", ""))
                        throw new InvalidOperationException("Play teardown did not restore authored materials.");
                    QualitySettings.SetQualityLevel(SessionState.GetInt(SmokeKey + ".Tier", 1), true);
                    File.WriteAllText(Output + "/Checks.txt", "PASS: shader/reference checks; repeat application has one controller and one image finish; protected dinosaur/tree counts unchanged; Low/Medium/High rendering; runtime sky/cloud clones; authored materials restored after Play.\nUser manual Play review is still pending.\n");
                    Debug.Log("ATMOSPHERE_STAGE_ONE_SMOKE_PASS: Quality tiers, material isolation, Play teardown and idempotence passed.");
                    EditorApplication.Exit(0);
                }
                catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
            }
        }

        private static void SmokeTick()
        {
            if (!EditorApplication.isPlaying) return;
            if (++smokeFrames < 12) return;
            EditorApplication.update -= SmokeTick;
            try
            {
                var controller = Object.FindObjectOfType<RainforestAtmosphereController>();
                if (controller == null || RenderSettings.skybox == controller.profile.skyMaterial ||
                    controller.cloudRenderer.sharedMaterial == controller.profile.cloudMaterial)
                    throw new InvalidOperationException("Runtime sky/cloud materials must be independent instances.");
                for (int tier = 0; tier < 3; tier++)
                {
                    QualitySettings.SetQualityLevel(tier, true);
                    controller.ApplyQuality(tier);
                    Capture(controller.viewCamera, new[] { new View
                    {
                        name = "Runtime-" + new[] { "Low", "Medium", "High" }[tier],
                        position = controller.viewCamera.transform.position, rotation = controller.viewCamera.transform.eulerAngles
                    } }, "Check");
                }
                VegetationLightingRepair.VerifyRuntime();
                TargetVegetationLighting.VerifyRuntime();
                SessionState.SetBool(SmokeKey + ".Passed", true);
            }
            catch (Exception exception) { Debug.LogException(exception); }
            finally { EditorApplication.isPlaying = false; }
        }

        [Serializable] private sealed class View
        {
            public string name;
            public Vector3 position, rotation;
        }

        [Serializable] private sealed class Baseline
        {
            public string skyGuid, reflectionGuid;
            public float ambientIntensity, reflectionIntensity;
            public int ambientMode, reflectionMode;
            public Vector3 sunRotation;
            public Color sunColor;
            public float sunIntensity, shadowStrength;
            public View[] views;
        }

        [MenuItem("Jurassic World/Environment/Atmosphere/Apply Stage 1 - After Rain")]
        public static void ApplyMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play before applying atmosphere.");
            if (SceneManager.GetActiveScene().path != Main)
                throw new InvalidOperationException("Open Main.unity before applying atmosphere.");
            ApplyCurrent(true);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }

        public static void ApplyBatch()
        {
            EditorSceneManager.OpenScene(Main);
            ApplyCurrent(true);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            ValidateCurrentSceneIfConfigured();
            Debug.Log("ATMOSPHERE_STAGE_ONE_READY: Saved Main; stop here for manual Play review.");
        }

        // Called only at the end of an explicitly requested full rebuild.
        public static void ApplyConfigured()
        {
            if (AssetDatabase.LoadAssetAtPath<RainforestAtmosphereProfile>(ProfilePath) != null)
                ApplyCurrent(false);
        }

        private static void ApplyCurrent(bool capture)
        {
            Camera camera = Camera.main;
            Light sun = Object.FindObjectsOfType<Light>().FirstOrDefault(l => l.type == LightType.Directional && l.name == "Sun");
            if (camera == null || sun == null) throw new InvalidOperationException("Main Camera / Sun missing.");
            if (!File.Exists(Source)) throw new FileNotFoundException("Download the stage-one HDR first.", Source);
            Directory.CreateDirectory(Output);
            Directory.CreateDirectory(Folder + "/Prepared");
            AssetDatabase.Refresh();
            int dinosaurs = Object.FindObjectsOfType<DinosaurAgent>().Length;
            Terrain terrain = Object.FindObjectOfType<Terrain>();
            TerrainData terrainData = terrain != null ? terrain.terrainData : null;
            int trees = terrainData != null ? terrainData.treeInstanceCount : 0;
            View[] views = MakeViews(camera, terrain);
            if (!File.Exists(Output + "/Before.json"))
            {
                // Snapshot the actual in-memory scene, including any existing manual edits.
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Output + "/Main.Before.unity", true);
                var baseline = new Baseline
                {
                    skyGuid = GuidOf(RenderSettings.skybox), reflectionGuid = GuidOf(RenderSettings.customReflectionTexture),
                    ambientIntensity = RenderSettings.ambientIntensity, reflectionIntensity = RenderSettings.reflectionIntensity,
                    ambientMode = (int)RenderSettings.ambientMode, reflectionMode = (int)RenderSettings.defaultReflectionMode,
                    sunRotation = sun.transform.eulerAngles, sunColor = sun.color,
                    sunIntensity = sun.intensity, shadowStrength = sun.shadowStrength, views = views
                };
                File.WriteAllText(Output + "/Before.json", JsonUtility.ToJson(baseline, true));
                File.Copy(WaterPath, Output + "/CrestCreekWater.Before.mat", false);
                if (capture) Capture(camera, views, "Before");
            }

            var importer = AssetImporter.GetAtPath(Source) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("HDR texture importer missing.");
            if (importer.textureShape != TextureImporterShape.Texture2D || importer.maxTextureSize != 8192 ||
                importer.isReadable || importer.wrapMode != TextureWrapMode.Repeat || importer.sRGBTexture ||
                !importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.CompressedHQ)
            {
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.textureType = TextureImporterType.Default;
                importer.maxTextureSize = 8192;
                importer.isReadable = false;
                importer.sRGBTexture = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            Texture2D panorama = AssetDatabase.LoadAssetAtPath<Texture2D>(Source);
            RainforestAtmosphereProfile profile = AssetDatabase.LoadAssetAtPath<RainforestAtmosphereProfile>(ProfilePath);
            bool newProfile = profile == null;
            if (newProfile)
            {
                profile = ScriptableObject.CreateInstance<RainforestAtmosphereProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            Material sky = EnsureMaterial(Folder + "/Prepared/AfterRainSky.mat", "Skybox/Panoramic");
            sky.SetTexture("_MainTex", panorama);
            sky.SetFloat("_Mapping", 1f);
            sky.SetFloat("_ImageType", 0f);
            sky.SetFloat("_Layout", 0f);
            sky.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 1f));
            sky.SetFloat("_Rotation", profile.skyRotation);
            sky.SetFloat("_Exposure", profile.skyExposure);
            sky.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
            Material cloud = EnsureMaterial(Folder + "/Prepared/CloudVeil.mat", "Jurassic World/Rainforest Cloud Veil");
            cloud.SetFloat("_Opacity", profile.cloudOpacity);
            cloud.SetVector("_Velocity", new Vector4(profile.cloudVelocity.x, profile.cloudVelocity.y, 0f, 0f));
            cloud.SetFloat("_Detail", 0f);
            profile.skyMaterial = sky;
            profile.cloudMaterial = cloud;
            profile.finishingShader = Shader.Find("Hidden/Jurassic World/Rainforest Finish");
            if (profile.finishingShader == null || !profile.finishingShader.isSupported)
                throw new InvalidOperationException("Finishing shader unavailable.");
            RenderSettings.skybox = sky;
            if (newProfile)
            {
                Vector3 toSun = FindSunDirection(sky);
                profile.sunEuler = Quaternion.LookRotation(-toSun, Vector3.up).eulerAngles;
                Debug.Log("ATMOSPHERE_SUN_DIRECTION: " + toSun + "; rotation " + profile.sunEuler);
            }
            sun.transform.rotation = Quaternion.Euler(profile.sunEuler);
            sun.color = profile.sunColor;
            sun.intensity = profile.sunIntensity;
            sun.shadowStrength = profile.shadowStrength;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = profile.ambientIntensity;
            camera.allowHDR = true;

            GameObject root = GameObject.Find(Root);
            if (root == null) root = new GameObject(Root);
            RainforestAtmosphereController controller = root.GetComponent<RainforestAtmosphereController>();
            if (controller == null) controller = root.AddComponent<RainforestAtmosphereController>();
            Transform cloudTransform = root.transform.Find("Slow Cloud Veil");
            GameObject cloudObject;
            if (cloudTransform == null)
            {
                cloudObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                cloudObject.name = "Slow Cloud Veil";
                cloudObject.transform.SetParent(root.transform);
                Object.DestroyImmediate(cloudObject.GetComponent<Collider>());
            }
            else cloudObject = cloudTransform.gameObject;
            cloudObject.layer = EnsureAtmosphereLayer();
            cloudObject.transform.position = camera.transform.position;
            cloudObject.transform.rotation = Quaternion.identity;
            cloudObject.transform.localScale = Vector3.one * 80f;
            Renderer cloudRenderer = cloudObject.GetComponent<Renderer>();
            cloudRenderer.sharedMaterial = cloud;
            cloudRenderer.shadowCastingMode = ShadowCastingMode.Off;
            cloudRenderer.receiveShadows = false;
            cloudRenderer.lightProbeUsage = LightProbeUsage.Off;
            cloudRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            camera.cullingMask |= 1 << cloudObject.layer;
            foreach (Crest.OceanPlanarReflection reflectionComponent in Object.FindObjectsOfType<Crest.OceanPlanarReflection>())
            {
                var settings = new SerializedObject(reflectionComponent);
                SerializedProperty mask = settings.FindProperty("_reflectionLayers");
                if (mask != null) mask.intValue &= ~(1 << cloudObject.layer);
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (Camera other in Object.FindObjectsOfType<Camera>())
                if (other != camera) other.cullingMask &= ~(1 << cloudObject.layer);
            RainforestImageFinish finish = camera.GetComponent<RainforestImageFinish>();
            if (finish == null) finish = camera.gameObject.AddComponent<RainforestImageFinish>();
            finish.Configure(profile.finishingShader, profile.exposure, profile.bloom, 1);
            controller.profile = profile;
            controller.sun = sun;
            controller.viewCamera = camera;
            controller.cloudRenderer = cloudRenderer;
            controller.imageFinish = finish;

            // Bake sky-only lighting/reflection once. Never re-render it every frame.
            Cubemap reflection = BakeSkyReflection(sky);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = reflection;
            VegetationLightingRepair.ApplyConfiguredToCurrentScene(profile);
            Material water = AssetDatabase.LoadAssetAtPath<Material>(WaterPath);
            if (water != null && water.HasProperty("_ReflectionCubemapOverride"))
            {
                water.SetTexture("_ReflectionCubemapOverride", reflection);
                water.SetFloat("_OverrideReflectionCubemap", 1f);
                water.EnableKeyword("_OVERRIDEREFLECTIONCUBEMAP_ON");
                if (water.HasProperty("_Skybox")) water.SetTexture("_Skybox", reflection);
                EditorUtility.SetDirty(water);
            }
            if (profile.skyFillCoefficients != null && profile.skyFillCoefficients.Length == 27)
            {
                RainforestSkyFill weakFill = root.GetComponent<RainforestSkyFill>();
                if (weakFill == null) weakFill = root.AddComponent<RainforestSkyFill>();
                weakFill.profile = profile;
                weakFill.Apply();
                EditorUtility.SetDirty(weakFill);
            }
            foreach (Object changed in new Object[] { profile, sky, cloud, sun, sun.transform, camera, finish, controller, cloudRenderer })
                EditorUtility.SetDirty(changed);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            if (Object.FindObjectsOfType<DinosaurAgent>().Length != dinosaurs ||
                (terrain != null && (terrain.terrainData != terrainData || terrain.terrainData.treeInstanceCount != trees)))
                throw new InvalidOperationException("Stage one changed protected scene content.");
            if (capture)
            {
                Capture(camera, views, "After");
                File.WriteAllText(Output + "/Applied.json", JsonUtility.ToJson(profile, true));
            }
            Debug.Log($"ATMOSPHERE_STAGE_ONE_APPLIED: dinosaurs={dinosaurs}, trees={trees}; protected content unchanged.");
        }

        public static void ValidateCurrentSceneIfConfigured()
        {
            if (AssetDatabase.LoadAssetAtPath<RainforestAtmosphereProfile>(ProfilePath) == null) return;
            var controllers = Object.FindObjectsOfType<RainforestAtmosphereController>();
            if (controllers.Length != 1) throw new InvalidOperationException("Expected one atmosphere controller.");
            var c = controllers[0];
            if (c.profile == null || c.sun == null || c.viewCamera == null || c.cloudRenderer == null || c.imageFinish == null ||
                RenderSettings.sun != c.sun || RenderSettings.skybox != c.profile.skyMaterial ||
                RenderSettings.skybox.GetTexture("_MainTex") == null || RenderSettings.customReflectionTexture == null)
                throw new InvalidOperationException("Incomplete stage-one atmosphere references.");
            foreach (Shader shader in new[] { c.profile.skyMaterial.shader, c.profile.cloudMaterial.shader, c.profile.finishingShader })
                if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
                    throw new InvalidOperationException("Atmosphere shader has errors: " + shader);
            Material water = AssetDatabase.LoadAssetAtPath<Material>(WaterPath);
            if (water == null || water.GetTexture("_ReflectionCubemapOverride") != RenderSettings.customReflectionTexture)
                throw new InvalidOperationException("Creek sky reflection does not match scene sky.");
            Debug.Log("ATMOSPHERE_STAGE_ONE_VALID: references, shaders, sun and creek sky are consistent.");
        }

        private static string GuidOf(Object asset) => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));

        private static Material EnsureMaterial(string path, string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Shader unavailable: " + shaderName);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            return material;
        }

        private static int EnsureAtmosphereLayer()
        {
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tags.FindProperty("layers");
            for (int i = 8; i < 32; i++) if (layers.GetArrayElementAtIndex(i).stringValue == "Atmosphere") return i;
            for (int i = 31; i >= 8; i--)
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                {
                    layers.GetArrayElementAtIndex(i).stringValue = "Atmosphere";
                    tags.ApplyModifiedPropertiesWithoutUndo();
                    return i;
                }
            throw new InvalidOperationException("No free layer for atmosphere.");
        }

        private static Camera SkyCamera(Material sky)
        {
            var go = new GameObject("Atmosphere Sky Capture", typeof(Camera), typeof(Skybox));
            go.hideFlags = HideFlags.HideAndDontSave;
            Camera camera = go.GetComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.cullingMask = 0;
            camera.allowHDR = true;
            camera.fieldOfView = 90f;
            camera.aspect = 1f;
            go.GetComponent<Skybox>().material = sky;
            return camera;
        }

        // Measure the rendered sky, avoiding assumptions about panorama UV orientation.
        private static Vector3 FindSunDirection(Material sky)
        {
            Camera camera = SkyCamera(sky);
            var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBHalf);
            var pixels = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
            RenderTexture previous = RenderTexture.active;
            float maximum = -1f;
            Vector3 direction = Vector3.up;
            try
            {
                camera.targetTexture = target;
                foreach (Vector3 face in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
                {
                    camera.transform.rotation = Quaternion.LookRotation(face, Mathf.Abs(face.y) > 0.9f ? Vector3.forward : Vector3.up);
                    camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                    Color[] colors = pixels.GetPixels();
                    for (int i = 0; i < colors.Length; i++)
                    {
                        float luminance = colors[i].grayscale;
                        Vector3 ray = camera.ViewportPointToRay(new Vector3((i % 64 + 0.5f) / 64f, (i / 64 + 0.5f) / 64f, 0f)).direction;
                        if (ray.y > 0.1f && luminance > maximum) { maximum = luminance; direction = ray; }
                    }
                }
                return direction;
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(camera.gameObject);
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }

        private static Cubemap BakeSkyReflection(Material sky)
        {
            const string path = Folder + "/Prepared/AfterRainReflection.asset";
            Cubemap cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            if (cube == null)
            {
                cube = new Cubemap(128, TextureFormat.RGBAHalf, true);
                AssetDatabase.CreateAsset(cube, path);
            }
            Camera camera = SkyCamera(sky);
            try
            {
                if (!camera.RenderToCubemap(cube)) throw new InvalidOperationException("Sky reflection capture failed.");
                EditorUtility.SetDirty(cube);
                return cube;
            }
            finally { Object.DestroyImmediate(camera.gameObject); }
        }

        private static View[] MakeViews(Camera camera, Terrain terrain)
        {
            View Ground(string name, float x, float z, Vector3 rotation)
            {
                var position = new Vector3(x, 1.7f, z);
                if (terrain != null) position.y = terrain.SampleHeight(position) + terrain.transform.position.y + 1.7f;
                return new View { name = name, position = position, rotation = rotation };
            }
            return new[]
            {
                new View { name = "01-Spawn", position = camera.transform.position, rotation = camera.transform.eulerAngles },
                Ground("02-Forest", 12f, 18f, new Vector3(-8f, 45f, 0f)),
                Ground("03-Creek", 56f, -15f, new Vector3(-8f, 25f, 0f)),
                Ground("04-Clearing", 0f, 0f, new Vector3(-18f, 210f, 0f)),
                Ground("05-Boundary", 78f, 65f, new Vector3(-12f, 40f, 0f))
            };
        }

        private static void Capture(Camera camera, View[] views, string suffix)
        {
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
            float aspect = camera.aspect;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.aspect = 1280f / 720f;
                foreach (View view in views)
                {
                    camera.transform.SetPositionAndRotation(view.position, Quaternion.Euler(view.rotation));
                    var atmosphere = Object.FindObjectOfType<RainforestAtmosphereController>();
                    if (atmosphere != null && atmosphere.cloudRenderer != null)
                        atmosphere.cloudRenderer.transform.position = view.position;
                    camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                    pixels.Apply();
                    File.WriteAllBytes(Output + "/" + view.name + "." + suffix + ".png", pixels.EncodeToPNG());
                }
            }
            finally
            {
                camera.targetTexture = oldTarget;
                camera.aspect = aspect;
                camera.transform.SetPositionAndRotation(position, rotation);
                var atmosphere = Object.FindObjectOfType<RainforestAtmosphereController>();
                if (atmosphere != null && atmosphere.cloudRenderer != null) atmosphere.cloudRenderer.transform.position = position;
                RenderTexture.active = oldActive;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }
    }
}
