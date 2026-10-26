using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class CreekWetnessStageFour
    {
        internal const string Folder = "Assets/Environment/Creek/Prepared/Wetness";
        internal const string Output = "Reports/Creek/StageFour";
        internal const string RootName = "Stage 4 - Creek Wet Surfaces";
        internal const string SettingsPath = Folder + "/WetSurfaces.asset";

        [MenuItem("Jurassic World/Environment/Creek/Apply Stage 4 - Wet Surfaces")]
        public static void ApplyMenu()
        {
            RequireMain();
            ApplyCurrent(true);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }

        public static void ApplyBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            ApplyCurrent(true);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Validate();
            Debug.Log("CREEK_STAGE_FOUR_READY: Saved Main; pause for manual Play review.");
        }

        public static void ApplyConfigured()
        {
            var settings = AssetDatabase.LoadAssetAtPath<CreekWetnessSettings>(SettingsPath);
            if (settings != null && settings.enabled) ApplyCurrent(false);
        }

        private static void RequireMain()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != "Assets/Scenes/Main.unity")
                throw new InvalidOperationException("Stop Play and open Main.unity first.");
        }

        private static void ApplyCurrent(bool capture)
        {
            GameObject creek = GameObject.Find("Main East to North Creek");
            Terrain terrain = Terrain.activeTerrain;
            if (creek == null || terrain == null) throw new InvalidOperationException("Saved creek / terrain missing.");
            Transform[] points = creek.GetComponentsInChildren<Transform>().Where(p => p.name.StartsWith("Flow Point ", StringComparison.Ordinal)).ToArray();
            if (points.Length < 3) throw new InvalidOperationException("Creek spline missing.");
            Directory.CreateDirectory(Output);
            if (!File.Exists(Output + "/Main.Before.unity"))
            {
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Output + "/Main.Before.unity", true);
                File.Copy(AssetDatabase.GetAssetPath(terrain.terrainData), Output + "/Terrain.Before.asset", false);
                if (capture) Capture(points, "Before");
            }
            string terrainBefore = TerrainFingerprint(terrain.terrainData, false);
            string navBefore = HashFile("Assets/Navigation/TRexInteractionNavMesh.asset");
            int dinosaurs = Object.FindObjectsOfType<DinosaurAgent>().Length;
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            var settings = AssetDatabase.LoadAssetAtPath<CreekWetnessSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<CreekWetnessSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            settings.enabled = true;
            Transform prior = creek.transform.Find(RootName);
            GameObject root = prior != null ? prior.gameObject : new GameObject(RootName);
            root.transform.SetParent(creek.transform, false);
            var state = root.GetComponent<CreekWetSurfaceState>();
            if (state == null) state = root.AddComponent<CreekWetSurfaceState>();
            TerrainLayer originalBank = state.originalBank != null ? state.originalBank :
                terrain.terrainData.terrainLayers.FirstOrDefault(l => l.name == "JungleCreekBank");
            if (originalBank == null) throw new InvalidOperationException("Original creek-bank layer missing.");
            Texture2D mask = MakeWetMask(points, terrain);
            Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
            Vector4 region = new Vector4(origin.x, origin.z, size.x, size.z);
            var priorBindings = state.bindings.Where(b => b.renderer != null).ToDictionary(b => b.renderer, b => b.originals);
            var bindings = new List<CreekWetSurfaceState.Binding>();
            int rocks = 0, ferns = 0;
            foreach (Renderer renderer in creek.GetComponentsInChildren<Renderer>(true))
            {
                bool rock = renderer.name == "Creek Mossy Rock", fern = renderer.name == "Creek Fern";
                if (!rock && !fern) continue;
                Material[] originals = priorBindings.TryGetValue(renderer, out Material[] saved) ? saved : renderer.sharedMaterials;
                var materials = new Material[originals.Length];
                for (int slot = 0; slot < originals.Length; slot++)
                    materials[slot] = WetMaterial(originals[slot], rock, mask, region, settings);
                var previousBinding = state.bindings.FirstOrDefault(b => b.renderer == renderer);
                bindings.Add(new CreekWetSurfaceState.Binding { renderer = renderer, originals = originals,
                    originalReflection = previousBinding != null ? previousBinding.originalReflection : renderer.reflectionProbeUsage });
                renderer.sharedMaterials = materials;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                EditorUtility.SetDirty(renderer);
                if (rock) rocks++; else ferns++;
            }
            TerrainLayer wetBank = WetBank(originalBank, settings);
            TerrainLayer[] layers = terrain.terrainData.terrainLayers;
            int index = Array.FindIndex(layers, l => l == originalBank || l == state.wetBank);
            if (index < 0) throw new InvalidOperationException("Creek-bank layer slot missing.");
            layers[index] = wetBank;
            terrain.terrainData.terrainLayers = layers;
            PaintWetBank(terrain, index, mask, state);
            EditorUtility.SetDirty(terrain.terrainData);
            state.terrain = terrain;
            state.originalBank = originalBank;
            state.wetBank = wetBank;
            state.settings = settings;
            state.bindings = bindings.ToArray();
            for (int i = 0; i < 2; i++)
            {
                string name = "Baked Creek Reflection " + (i + 1);
                Transform child = root.transform.Find(name);
                GameObject go = child != null ? child.gameObject : new GameObject(name);
                go.transform.SetParent(root.transform, false);
                Vector3 position = points[i == 0 ? 3 : points.Length - 5].position + Vector3.up * 2.5f;
                go.transform.position = position;
                ReflectionProbe probe = go.GetComponent<ReflectionProbe>();
                if (probe == null) probe = go.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Custom;
                probe.customBakedTexture = BakeReflection(position, i);
                probe.boxProjection = true;
                probe.size = new Vector3(25f, 12f, 68f);
                probe.blendDistance = 7f;
                probe.importance = 2;
                probe.intensity = 0.9f;
                probe.cullingMask = 1;
            }
            if (TerrainFingerprint(terrain.terrainData, false) != terrainBefore || HashFile("Assets/Navigation/TRexInteractionNavMesh.asset") != navBefore ||
                Object.FindObjectsOfType<DinosaurAgent>().Length != dinosaurs)
                throw new InvalidOperationException("Wetness changed terrain geometry/details/trees, navigation or dinosaur count.");
            File.WriteAllText(Output + "/TerrainInvariant.txt", terrainBefore + "\nNavigation: " + navBefore + "\n");
            foreach (Object changed in new Object[] { settings, state, wetBank }) EditorUtility.SetDirty(changed);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            if (capture) Capture(points, "After");
            Debug.Log($"CREEK_STAGE_FOUR_APPLIED: {rocks} rock renderers, {ferns} fern renderers, local bank blending, two static reflection probes; terrain geometry/vegetation and navigation unchanged.");
        }

        private static Material WetMaterial(Material original, bool rock, Texture2D mask, Vector4 region, CreekWetnessSettings settings)
        {
            if (original == null) throw new InvalidOperationException("Creek source material missing.");
            string path = Folder + "/" + (rock ? "Rock_" : "Fern_") + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original)) + ".mat";
            Shader shader = Shader.Find(rock ? "Jurassic World/Creek Wet Rock" : "Jurassic World/Creek Wet Fern");
            if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Wet surface shader unavailable.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(original);
                material.name = "Creek Wet " + original.name;
                material.shader = shader;
                material.enableInstancing = true;
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_WetMask", mask);
            material.SetVector("_WetRegion", region);
            material.SetFloat("_WetStrength", rock ? settings.rockWetness : settings.leafWetness);
            if (rock) material.SetFloat("_HasSmoothnessMap", original.GetTexture("_MetallicGlossMap") != null ? 1f : 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D MakeWetMask(Transform[] points, Terrain terrain)
        {
            const string path = Folder + "/CreekWetMask.png";
            // Regenerate this owned data texture when reapplying, preserving its .meta/GUID.
            {
                const int n = 512;
                var texture = new Texture2D(n, n, TextureFormat.RGBA32, false, true);
                var pixels = new Color32[n * n];
                Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        var p = new Vector2(origin.x + (x + 0.5f) * size.x / n, origin.z + (y + 0.5f) * size.z / n);
                        float distance = float.MaxValue;
                        for (int segment = 0; segment < points.Length - 1; segment++)
                        {
                            Vector2 a = new Vector2(points[segment].position.x, points[segment].position.z);
                            Vector2 b = new Vector2(points[segment + 1].position.x, points[segment + 1].position.z);
                            Vector2 ab = b - a;
                            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.001f, ab.sqrMagnitude));
                            distance = Mathf.Min(distance, Vector2.Distance(p, a + ab * t));
                        }
                        float weight = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.1f, 5f, distance))) *
                            (0.8f + 0.2f * Mathf.PerlinNoise(p.x * 0.8f + 13f, p.y * 0.8f));
                        byte value = (byte)Mathf.RoundToInt(weight * 255f);
                        pixels[y * n + x] = new Color32(value, value, value, 255);
                    }
                texture.SetPixels32(pixels); texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.sRGBTexture = false; importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.isReadable = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static TerrainLayer WetBank(TerrainLayer original, CreekWetnessSettings settings)
        {
            const string path = Folder + "/CreekWetMud.terrainlayer";
            TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = Object.Instantiate(original); layer.name = "CreekWetMud";
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.diffuseTexture = MudTexture(original.diffuseTexture);
            Vector4 tint = original.diffuseRemapMax;
            layer.diffuseRemapMax = new Vector4(tint.x * 0.92f, tint.y * 0.92f, tint.z * 0.92f, tint.w);
            layer.smoothness = settings.mudSmoothness;
            layer.metallic = 0f;
            return layer;
        }

        private static Texture2D MudTexture(Texture2D source)
        {
            const string path = Folder + "/MudAlbedoSmoothness.png";
            {
                RenderTexture previous = RenderTexture.active;
                var target = RenderTexture.GetTemporary(1024, 1024, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var texture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false, true);
                try
                {
                    Graphics.Blit(source, target);
                    RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
                    Color[] pixels = texture.GetPixels();
                    for (int y = 0; y < 1024; y++)
                        for (int x = 0; x < 1024; x++)
                        {
                            int index = y * 1024 + x;
                            Color linear = pixels[index].linear;
                            float luminance = linear.grayscale;
                            linear.r = Mathf.Lerp(linear.r, luminance, 0.25f) * 0.60f;
                            linear.g = Mathf.Lerp(linear.g, luminance, 0.25f) * 0.63f;
                            linear.b = Mathf.Lerp(linear.b, luminance, 0.25f) * 0.65f;
                            pixels[index] = linear.gamma;
                            pixels[index].a = 0.45f + 0.4f * Mathf.PerlinNoise(x * 0.018f, y * 0.018f);
                        }
                    texture.SetPixels(pixels); texture.Apply();
                    File.WriteAllBytes(path, texture.EncodeToPNG());
                }
                finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(texture); }
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.sRGBTexture = true; importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false; importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void PaintWetBank(Terrain terrain, int slot, Texture2D mask, CreekWetSurfaceState state)
        {
            TerrainData data = terrain.terrainData;
            int width = data.alphamapWidth, height = data.alphamapHeight, count = data.alphamapLayers;
            float[,,] weights = data.GetAlphamaps(0, 0, width, height);
            if (string.IsNullOrEmpty(state.originalWeightsBackup))
            {
                state.originalWeightsBackup = Output + "/OriginalControls-" + Guid.NewGuid().ToString("N") + ".bin";
                using (var writer = new BinaryWriter(File.Create(state.originalWeightsBackup)))
                {
                    writer.Write(height); writer.Write(width); writer.Write(count);
                    byte[] bytes = new byte[Buffer.ByteLength(weights)];
                    Buffer.BlockCopy(weights, 0, bytes, 0, bytes.Length); writer.Write(bytes);
                }
            }
            float[,,] before = (float[,,])weights.Clone();
            int painted = 0;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float wet = mask.GetPixelBilinear((x + 0.5f) / width, (y + 0.5f) / height).r;
                    if (wet <= 0f) continue;
                    float target = 0.60f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, wet));
                    float prior = weights[y, x, slot];
                    float scale = (1f - target) / Mathf.Max(0.00001f, 1f - prior);
                    for (int channel = 0; channel < count; channel++)
                        weights[y, x, channel] = channel == slot ? target : weights[y, x, channel] * scale;
                    painted++;
                }
            data.SetAlphamaps(0, 0, weights);
            float[,,] after = data.GetAlphamaps(0, 0, width, height);
            float maximumOutsideChange = 0f;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (mask.GetPixelBilinear((x + 0.5f) / width, (y + 0.5f) / height).r <= 0f)
                        for (int channel = 0; channel < count; channel++)
                            maximumOutsideChange = Mathf.Max(maximumOutsideChange, Mathf.Abs(after[y,x,channel]-before[y,x,channel]));
            if (maximumOutsideChange > 1f / 255f + 0.00001f)
                throw new InvalidOperationException("Terrain paint outside the wet strip changed.");
            Debug.Log($"CREEK_WET_PAINT: affected pixels={painted}; maximum outside change={maximumOutsideChange}");
        }

        private static Cubemap BakeReflection(Vector3 position, int index)
        {
            string path = Folder + "/CreekReflection" + index + ".asset";
            Cubemap cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            if (cube == null)
            {
                cube = new Cubemap(128, TextureFormat.RGBAHalf, true) { name = "Baked Creek Reflection " + index };
                AssetDatabase.CreateAsset(cube, path);
            }
            var go = new GameObject("Temporary Creek Reflection Camera", typeof(Camera));
            go.hideFlags = HideFlags.HideAndDontSave;
            Camera camera = go.GetComponent<Camera>();
            camera.enabled = false; camera.clearFlags = CameraClearFlags.Skybox;
            camera.cullingMask = 1; camera.allowHDR = true;
            camera.nearClipPlane = 0.1f; camera.farClipPlane = 150f;
            go.transform.position = position;
            var hidden = new Dictionary<Renderer, bool>();
            foreach (DinosaurAgent dinosaur in Object.FindObjectsOfType<DinosaurAgent>())
                foreach (Renderer renderer in dinosaur.GetComponentsInChildren<Renderer>(true))
                {
                    hidden[renderer] = renderer.forceRenderingOff;
                    renderer.forceRenderingOff = true;
                }
            try
            {
                if (!camera.RenderToCubemap(cube)) throw new InvalidOperationException("Static creek reflection capture failed.");
                EditorUtility.SetDirty(cube);
                return cube;
            }
            finally
            {
                foreach (var item in hidden) if (item.Key != null) item.Key.forceRenderingOff = item.Value;
                Object.DestroyImmediate(go);
            }
        }

        internal static string HashFile(string path)
        {
            using (var hash = SHA256.Create())
            using (var file = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(file));
        }

        internal static string TerrainFingerprint(TerrainData data, bool includeWeights = true)
        {
            using (var memory = new MemoryStream())
            using (var writer = new BinaryWriter(memory))
            using (var hash = SHA256.Create())
            {
                void Add(Array array)
                {
                    byte[] bytes = new byte[Buffer.ByteLength(array)];
                    Buffer.BlockCopy(array, 0, bytes, 0, bytes.Length);
                    writer.Write(bytes);
                }
                writer.Write(data.heightmapResolution);
                Add(data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution));
                if (includeWeights) Add(data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight));
                foreach (TreeInstance tree in data.treeInstances)
                {
                    writer.Write(tree.position.x); writer.Write(tree.position.y); writer.Write(tree.position.z);
                    writer.Write(tree.prototypeIndex); writer.Write(tree.rotation);
                    writer.Write(tree.widthScale); writer.Write(tree.heightScale);
                    writer.Write(tree.color.r); writer.Write(tree.color.g); writer.Write(tree.color.b); writer.Write(tree.color.a);
                    writer.Write(tree.lightmapColor.r); writer.Write(tree.lightmapColor.g); writer.Write(tree.lightmapColor.b); writer.Write(tree.lightmapColor.a);
                }
                for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
                    Add(data.GetDetailLayer(0, 0, data.detailWidth, data.detailHeight, layer));
                writer.Flush();
                return BitConverter.ToString(hash.ComputeHash(memory.ToArray()));
            }
        }

        public static void Validate()
        {
            var states = Object.FindObjectsOfType<CreekWetSurfaceState>();
            if (states.Length != 1) throw new InvalidOperationException("Expected one wet-surface state.");
            var state = states[0];
            if (state.bindings.Length == 0 || state.terrain == null || state.wetBank == null ||
                !state.terrain.terrainData.terrainLayers.Contains(state.wetBank) ||
                state.GetComponentsInChildren<ReflectionProbe>().Length != 2)
                throw new InvalidOperationException("Wet-surface bindings / bank / reflection probes missing.");
            foreach (var binding in state.bindings)
                if (binding.renderer == null || binding.renderer.sharedMaterials.Any(m =>
                    m == null || !AssetDatabase.GetAssetPath(m).StartsWith(Folder + "/", StringComparison.Ordinal) ||
                    ShaderUtil.ShaderHasError(m.shader)))
                    throw new InvalidOperationException("Wet surface material reference invalid.");
            foreach (ReflectionProbe probe in state.GetComponentsInChildren<ReflectionProbe>())
                if (probe.mode != ReflectionProbeMode.Custom || probe.customBakedTexture == null)
                    throw new InvalidOperationException("Reflection must be static and baked.");
            Texture2D mask = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/CreekWetMask.png");
            if (mask.GetPixelBilinear(0.1f, 0.1f).r > 0.001f || mask.GetPixelBilinear(0.5f, 0.5f).r > 0.001f)
                throw new InvalidOperationException("Wet mask leaks far away from the creek.");
            Debug.Log("CREEK_STAGE_FOUR_VALID: localized mask, materials, bank layer and static reflections passed.");
        }

        [MenuItem("Jurassic World/Environment/Creek/Restore Stage 4 Original Surfaces")]
        public static void RestoreMenu()
        {
            RequireMain();
            var state = Object.FindObjectOfType<CreekWetSurfaceState>();
            if (state == null) return;
            if (!File.Exists(state.originalWeightsBackup))
                throw new FileNotFoundException("Original local paint backup is required to restore wet surfaces.", state.originalWeightsBackup);
            using (var check = new BinaryReader(File.OpenRead(state.originalWeightsBackup)))
            {
                TerrainData data = state.terrain.terrainData;
                if (check.ReadInt32() != data.alphamapHeight || check.ReadInt32() != data.alphamapWidth || check.ReadInt32() != data.alphamapLayers)
                    throw new InvalidOperationException("Terrain paint layout changed; use the scene/terrain checkpoint instead.");
            }
            foreach (var binding in state.bindings)
                if (binding.renderer != null)
                {
                    binding.renderer.sharedMaterials = binding.originals;
                    binding.renderer.reflectionProbeUsage = binding.originalReflection;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(binding.renderer);
                    EditorUtility.SetDirty(binding.renderer);
                }
            TerrainLayer[] layers = state.terrain.terrainData.terrainLayers;
            for (int i = 0; i < layers.Length; i++) if (layers[i] == state.wetBank) layers[i] = state.originalBank;
            state.terrain.terrainData.terrainLayers = layers;
            using (var reader = new BinaryReader(File.OpenRead(state.originalWeightsBackup)))
            {
                int height = reader.ReadInt32(), width = reader.ReadInt32(), count = reader.ReadInt32();
                TerrainData data = state.terrain.terrainData;
                if (data.alphamapWidth != width || data.alphamapHeight != height || data.alphamapLayers != count)
                    throw new InvalidOperationException("Terrain paint layout changed; restore from the scene/terrain checkpoint instead.");
                var original = new float[height, width, count];
                byte[] bytes = reader.ReadBytes(Buffer.ByteLength(original));
                Buffer.BlockCopy(bytes, 0, original, 0, bytes.Length);
                float[,,] current = data.GetAlphamaps(0, 0, width, height);
                Texture2D mask = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/CreekWetMask.png");
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        if (mask.GetPixelBilinear((x + 0.5f) / width, (y + 0.5f) / height).r > 0f)
                            for (int channel = 0; channel < count; channel++) current[y, x, channel] = original[y, x, channel];
                data.SetAlphamaps(0, 0, current);
            }
            EditorUtility.SetDirty(state.terrain.terrainData);
            state.settings.enabled = false; EditorUtility.SetDirty(state.settings);
            Object.DestroyImmediate(state.gameObject);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
        }

        internal static void Capture(Transform[] points, string suffix)
        {
            Camera camera = Camera.main;
            var backdrop = Object.FindObjectOfType<RainforestBackdrop>();
            if (backdrop != null) backdrop.ApplyCamera();
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
            float aspect = camera.aspect;
            var target = new RenderTexture(1280, 720, 24);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var atmosphere = Object.FindObjectOfType<RainforestAtmosphereController>();
            try
            {
                camera.targetTexture = target; camera.aspect = 1280f / 720f;
                for (int view = 0; view < 3; view++)
                {
                    Vector3 point = points[new[] { 2, points.Length / 2, points.Length - 3 }[view]].position;
                    Vector3 eye = point + new Vector3(3.8f, 1.75f, -2.6f);
                    Vector3 aim = point + new Vector3(0.5f, 0.15f, 1.5f);
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
                    if (atmosphere != null) atmosphere.cloudRenderer.transform.position = eye;
                    camera.Render(); RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
                    File.WriteAllBytes(Output + "/Creek-" + view + "." + suffix + ".png", pixels.EncodeToPNG());
                }
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.targetTexture = oldTarget; camera.aspect = aspect;
                if (atmosphere != null) atmosphere.cloudRenderer.transform.position = position;
                RenderTexture.active = oldActive; target.Release();
                Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }

        public static void InspectBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            GameObject creek = GameObject.Find("Main East to North Creek");
            if (creek == null) throw new InvalidOperationException("Creek missing.");
            foreach (Renderer renderer in creek.GetComponentsInChildren<Renderer>(true).Where(r => r.name.Contains("Rock") || r.name.Contains("Fern")).Take(12))
                Debug.Log("WET_SOURCE: " + renderer.name + " at " + renderer.bounds.center + "; " +
                    string.Join("; ", renderer.sharedMaterials.Where(m => m != null).Select(m => AssetDatabase.GetAssetPath(m) + " / " + m.shader.name)));
            foreach (TerrainLayer layer in Terrain.activeTerrain.terrainData.terrainLayers)
                Debug.Log("WET_LAYER: " + AssetDatabase.GetAssetPath(layer) + "; smooth=" + layer.smoothness);
            var data = Terrain.activeTerrain.terrainData;
            var weights = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            int slot = data.terrainLayers.Length - 1;
            float min = 1f, max = 0f; int positive = 0;
            for (int y = 0; y < data.alphamapHeight; y++)
                for (int x = 0; x < data.alphamapWidth; x++)
                { min = Mathf.Min(min, weights[y,x,slot]); max = Mathf.Max(max,weights[y,x,slot]); if(weights[y,x,slot] > 0.01f) positive++; }
            Debug.Log($"WET_BANK_WEIGHT: slot={slot}, min={min}, max={max}, positive={positive}");
            Debug.Log("WET_PROBES: " + Object.FindObjectsOfType<ReflectionProbe>().Length);
        }
    }
}
