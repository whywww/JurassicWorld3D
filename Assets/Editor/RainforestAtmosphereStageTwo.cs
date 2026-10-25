using System;
using System.Collections.Generic;
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
    /// <summary>Incremental mist/shaft authoring from the existing creek spline.</summary>
    public static class RainforestAtmosphereStageTwo
    {
        public const string RootName = "Stage 2 - Creek Air and Sunlight";
        private const string Main = "Assets/Scenes/Main.unity";
        private const string Prepared = "Assets/Environment/Atmosphere/Prepared/LocalEffects";
        private const string Output = "Reports/Atmosphere/StageTwo";

        [Serializable] private sealed class Before
        {
            public bool fog;
            public FogMode mode;
            public Color color;
            public float start, end;
        }

        [MenuItem("Jurassic World/Environment/Atmosphere/Apply Stage 2 - Creek Air and Sunlight")]
        public static void ApplyMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != Main)
                throw new InvalidOperationException("Stop Play and open Main.unity before applying stage two.");
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
            Validate();
            Debug.Log("ATMOSPHERE_STAGE_TWO_READY: Main saved; manual Play review required before stage three.");
        }

        public static void ApplyConfigured()
        {
            var profile = AssetDatabase.LoadAssetAtPath<RainforestAtmosphereProfile>(RainforestAtmosphereStageOne.ProfilePath);
            if (profile != null && profile.localAtmosphereEnabled) ApplyCurrent(false);
        }

        private static void ApplyCurrent(bool capture)
        {
            var atmosphere = Object.FindObjectOfType<RainforestAtmosphereController>();
            GameObject creek = GameObject.Find("Main East to North Creek");
            if (atmosphere == null || atmosphere.profile == null || creek == null)
                throw new InvalidOperationException("Stage one and the saved creek must exist. Do not run a whole-scene Bake.");
            Transform[] points = creek.GetComponentsInChildren<Transform>()
                .Where(t => t.name.StartsWith("Flow Point ", StringComparison.Ordinal)).ToArray();
            if (points.Length < 3) throw new InvalidOperationException("Existing creek flow points missing.");
            int dinosaurs = Object.FindObjectsOfType<DinosaurAgent>().Length;
            Terrain terrain = Terrain.activeTerrain;
            TerrainData terrainData = terrain.terrainData;
            int trees = terrainData.treeInstanceCount;
            Directory.CreateDirectory(Output);
            if (!File.Exists(Output + "/Before.json"))
            {
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Output + "/Main.Before.unity", true);
                File.Copy(RainforestAtmosphereStageOne.ProfilePath, Output + "/AfterRain.Before.asset", false);
                File.WriteAllText(Output + "/Before.json", JsonUtility.ToJson(new Before
                {
                    fog = RenderSettings.fog, mode = RenderSettings.fogMode, color = RenderSettings.fogColor,
                    start = RenderSettings.fogStartDistance, end = RenderSettings.fogEndDistance
                }, true));
                if (capture) Capture(atmosphere.viewCamera, points, "Before");
            }
            Directory.CreateDirectory(Prepared);
            AssetDatabase.Refresh();
            Texture2D mistTexture = EnsureMask(false);
            Texture2D shaftTexture = EnsureMask(true);
            Material mistMaterial = EnsureMaterial("CreekMist", "Jurassic World/Rainforest Soft Mist", mistTexture);
            Material shaftMaterial = EnsureMaterial("CanopySunlight", "Jurassic World/Rainforest Soft Sun Shaft", shaftTexture);
            var profile = atmosphere.profile;
            profile.localAtmosphereEnabled = true;
            shaftMaterial.SetColor("_Tint", new Color(1f, 0.96f, 0.84f, profile.shaftOpacity));
            int layer = LayerMask.NameToLayer("Atmosphere");
            if (layer < 0) throw new InvalidOperationException("Stage-one Atmosphere layer missing.");
            Transform existing = atmosphere.transform.Find(RootName);
            GameObject root = existing != null ? existing.gameObject : new GameObject(RootName);
            root.transform.SetParent(atmosphere.transform, false);
            root.layer = layer;
            RainforestAtmosphereEffects effects = root.GetComponent<RainforestAtmosphereEffects>();
            if (effects == null) effects = root.AddComponent<RainforestAtmosphereEffects>();
            var systems = new List<ParticleSystem>();
            for (int i = 1; i < points.Length - 1; i += 2)
            {
                string name = "Creek Mist " + i.ToString("00");
                Transform prior = root.transform.Find(name);
                GameObject patch = prior != null ? prior.gameObject : new GameObject(name);
                patch.transform.SetParent(root.transform, false);
                patch.layer = layer;
                Vector3 tangent = points[i + 1].position - points[i - 1].position;
                tangent.y = 0f;
                patch.transform.SetPositionAndRotation(points[i].position + Vector3.up * 0.65f, Quaternion.LookRotation(tangent));
                ParticleSystem system = patch.GetComponent<ParticleSystem>();
                if (system == null) system = patch.AddComponent<ParticleSystem>();
                ConfigureMist(system, mistMaterial, profile.mistOpacity, 7100u + (uint)i);
                systems.Add(system);
            }
            Mesh mesh = EnsureShaftMesh();
            Vector3 towardsSun = -atmosphere.sun.transform.forward;
            var shafts = new List<Renderer>();
            // Near creek openings; terrain/forest content remains untouched.
            int[] shaftPoints = { 2, points.Length / 2, points.Length - 3 };
            for (int i = 0; i < shaftPoints.Length; i++)
            {
                string name = "Canopy Sun Shaft " + (i + 1);
                Transform prior = root.transform.Find(name);
                GameObject shaft = prior != null ? prior.gameObject : new GameObject(name);
                shaft.transform.SetParent(root.transform, false);
                shaft.layer = layer;
                Vector3 bottom = points[shaftPoints[i]].position;
                bottom += new Vector3(i % 2 == 0 ? 2.3f : -2.3f, 0f, 0f);
                bottom.y = terrain.SampleHeight(bottom) + terrain.transform.position.y + 0.25f;
                // Move only the effect if a solid obstacle blocks this small shaft.
                Physics.SyncTransforms();
                for (int attempt = 0; attempt < 4 && Physics.Raycast(bottom, towardsSun, 9f, ~(1 << layer), QueryTriggerInteraction.Ignore); attempt++)
                    bottom += new Vector3(0.8f, 0f, 0.5f);
                bottom.y = terrain.SampleHeight(bottom) + terrain.transform.position.y + 0.25f;
                shaft.transform.SetPositionAndRotation(bottom, Quaternion.FromToRotation(Vector3.up, towardsSun));
                MeshFilter filter = shaft.GetComponent<MeshFilter>();
                if (filter == null) filter = shaft.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                MeshRenderer renderer = shaft.GetComponent<MeshRenderer>();
                if (renderer == null) renderer = shaft.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = shaftMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                shafts.Add(renderer);
            }
            effects.profile = profile;
            effects.viewCamera = atmosphere.viewCamera;
            effects.mist = systems.ToArray();
            effects.shafts = shafts.ToArray();
            atmosphere.localEffects = effects;
            atmosphere.viewCamera.depthTextureMode |= DepthTextureMode.Depth;
            foreach (Crest.OceanPlanarReflection reflection in Object.FindObjectsOfType<Crest.OceanPlanarReflection>())
            {
                var settings = new SerializedObject(reflection);
                settings.FindProperty("_reflectionLayers").intValue &= ~(1 << layer);
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (Camera camera in Object.FindObjectsOfType<Camera>())
                if (camera != atmosphere.viewCamera) camera.cullingMask &= ~(1 << layer);
            effects.ApplyQuality(1);
            foreach (ParticleSystem system in systems) system.Simulate(24f, true, true, true);
            if (capture) Capture(atmosphere.viewCamera, points, "After");
            foreach (Object changed in new Object[] { profile, atmosphere, effects, atmosphere.viewCamera, shaftMaterial })
                EditorUtility.SetDirty(changed);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            if (Object.FindObjectsOfType<DinosaurAgent>().Length != dinosaurs || terrain.terrainData != terrainData || terrainData.treeInstanceCount != trees)
                throw new InvalidOperationException("Protected scene content changed.");
            File.WriteAllText(Output + "/Placements.json", JsonUtility.ToJson(new Placements
            {
                mist = systems.Select(p => p.transform.position).ToArray(),
                shafts = shafts.Select(p => p.transform.position).ToArray()
            }, true));
            Debug.Log($"ATMOSPHERE_STAGE_TWO_APPLIED: {systems.Count} creek mist patches, {shafts.Count} shafts; dinosaurs={dinosaurs}, trees={trees}; terrain unchanged.");
        }

        [Serializable] private sealed class Placements { public Vector3[] mist, shafts; }

        private static void ConfigureMist(ParticleSystem system, Material material, float opacity, uint seed)
        {
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.useAutoRandomSeed = false;
            system.randomSeed = seed;
            var main = system.main;
            main.duration = 24f;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(18f, 24f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(2.8f, 4.4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.74f, 0.8f, 0.78f, opacity);
            main.maxParticles = 6;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.Pause;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1.6f, 0.35f, 5f);
            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0.24f;
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = 0.045f;
            velocity.y = 0.006f;
            velocity.z = 0.015f;
            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.2f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = 0.015f;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.maxParticleSize = 0.45f;
        }

        private static Texture2D EnsureMask(bool shaft)
        {
            string path = Prepared + (shaft ? "/ShaftSoftMask.png" : "/MistSoftNoise.png");
            if (!File.Exists(path))
            {
                var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false, true);
                var colors = new Color32[128 * 128];
                for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++)
                    {
                        float u = (x + 0.5f) / 128f, v = (y + 0.5f) / 128f;
                        float alpha;
                        if (shaft)
                            alpha = Mathf.Sin(v * Mathf.PI) * (0.72f + 0.28f * Mathf.Cos(u * Mathf.PI * 8f));
                        else
                        {
                            Vector2 p = new Vector2(u - 0.5f, v - 0.5f) * 2f;
                            float soft = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(1f - p.sqrMagnitude));
                            alpha = soft * (0.45f + 0.55f * Mathf.PerlinNoise(u * 4f + 17.3f, v * 4f + 8.1f));
                        }
                        colors[y * 128 + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                    }
                texture.SetPixels32(colors);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.sRGBTexture = false;
                importer.wrapMode = shaft ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material EnsureMaterial(string name, string shaderName, Texture2D texture)
        {
            string path = Prepared + "/" + name + ".mat";
            Shader shader = Shader.Find(shaderName);
            if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Shader not supported: " + shaderName);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_MainTex", texture);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh EnsureShaftMesh()
        {
            const string path = Prepared + "/SoftShaftCone.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            const int sides = 16, rings = 6;
            var vertices = new Vector3[(sides + 1) * rings];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new List<int>();
            for (int ring = 0; ring < rings; ring++)
                for (int side = 0; side <= sides; side++)
                {
                    float t = ring / (float)(rings - 1), angle = side / (float)sides * Mathf.PI * 2f;
                    int index = ring * (sides + 1) + side;
                    float radius = Mathf.Lerp(0.95f, 0.13f, t);
                    vertices[index] = new Vector3(Mathf.Cos(angle) * radius, t * 8f, Mathf.Sin(angle) * radius);
                    normals[index] = new Vector3(Mathf.Cos(angle), 0.1025f, Mathf.Sin(angle)).normalized;
                    uv[index] = new Vector2(side / (float)sides, t);
                    if (ring < rings - 1 && side < sides)
                    {
                        int next = index + sides + 1;
                        triangles.AddRange(new[] { index, next, index + 1, index + 1, next, next + 1 });
                    }
                }
            mesh = new Mesh { name = "Soft Sun Shaft Cone", vertices = vertices, normals = normals, uv = uv, triangles = triangles.ToArray() };
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        public static void Validate()
        {
            var effects = Object.FindObjectsOfType<RainforestAtmosphereEffects>();
            if (effects.Length != 1 || effects[0].mist.Length < 3 || effects[0].shafts.Length != 3)
                throw new InvalidOperationException("Stage-two references/counts are incomplete.");
            foreach (Renderer renderer in effects[0].mist.Select(p => p.GetComponent<Renderer>()).Concat(effects[0].shafts))
                if (renderer.gameObject.layer != LayerMask.NameToLayer("Atmosphere") || renderer.sharedMaterial == null ||
                    ShaderUtil.ShaderHasError(renderer.sharedMaterial.shader) || renderer.shadowCastingMode != ShadowCastingMode.Off)
                    throw new InvalidOperationException("Invalid effect renderer: " + renderer.name);
            // Camera.depthTextureMode is runtime state, not serialized in Main.unity.
            if (Application.isPlaying && (effects[0].viewCamera.depthTextureMode & DepthTextureMode.Depth) == 0)
                throw new InvalidOperationException("Depth texture is required for soft intersections.");
            Debug.Log("ATMOSPHERE_STAGE_TWO_VALID: particle/shaft shaders, depth and layer references passed.");
        }

        private static void Capture(Camera camera, Transform[] points, string suffix)
        {
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
            float aspect = camera.aspect;
            var target = new RenderTexture(1280, 720, 24);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var effects = Object.FindObjectOfType<RainforestAtmosphereEffects>();
            try
            {
                camera.targetTexture = target;
                camera.aspect = 1280f / 720f;
                for (int view = 0; view < 3; view++)
                {
                    int index = new[] { 1, points.Length / 2, points.Length - 3 }[view];
                    Vector3 eye = points[index].position + new Vector3(4f, 1.7f, -4f);
                    Vector3 aim = points[index].position + new Vector3(0f, 1.2f, 3f);
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
                    if (effects != null)
                    {
                        effects.ApplyQuality(2);
                        foreach (ParticleSystem system in effects.mist) system.Simulate(20f, true, true, true);
                    }
                    var controller = Object.FindObjectOfType<RainforestAtmosphereController>();
                    if (controller != null && controller.cloudRenderer != null) controller.cloudRenderer.transform.position = eye;
                    camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                    pixels.Apply();
                    File.WriteAllBytes(Output + "/Creek-" + (view + 1) + "." + suffix + ".png", pixels.EncodeToPNG());
                }
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.aspect = aspect;
                camera.transform.SetPositionAndRotation(position, rotation);
                var controller = Object.FindObjectOfType<RainforestAtmosphereController>();
                if (controller != null && controller.cloudRenderer != null) controller.cloudRenderer.transform.position = position;
                if (effects != null) effects.ApplyQuality(1);
                RenderTexture.active = previousActive;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }
    }
}
