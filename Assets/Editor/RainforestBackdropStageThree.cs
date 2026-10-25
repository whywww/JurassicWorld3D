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
    public static class RainforestBackdropStageThree
    {
        internal const string Folder = "Assets/Environment/Backdrop";
        internal const string Source = Folder + "/Source/CoastalCliff01/coastal_cliff_01_4k.fbx";
        internal const string Output = "Reports/Backdrop/StageThree";
        internal const string RootName = "Rainforest Backdrop";
        private const string Prepared = Folder + "/Prepared";
        private const string PrefabPath = Prepared + "/RainforestBackdrop.prefab";

        [MenuItem("Jurassic World/Environment/Backdrop/Apply Stage 3 - Forest and Ridges")]
        public static void ApplyMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != "Assets/Scenes/Main.unity")
                throw new InvalidOperationException("Stop Play and open Main.unity first.");
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
            Debug.Log("BACKDROP_STAGE_THREE_READY: Main saved; pause for manual Play review.");
        }

        public static void ApplyConfigured()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) return;
            GameObject root = GameObject.Find(RootName);
            if (root == null) root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            BindCamera(root.GetComponent<RainforestBackdrop>(), Camera.main);
        }

        private static void ApplyCurrent(bool capture)
        {
            Camera camera = Camera.main;
            Terrain terrain = Terrain.activeTerrain;
            if (camera == null || terrain == null) throw new InvalidOperationException("Main Camera / Terrain missing.");
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
            if (source == null) throw new FileNotFoundException("Download the cliff FBX first.", Source);
            Directory.CreateDirectory(Output);
            if (!File.Exists(Output + "/Main.Before.unity"))
            {
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Output + "/Main.Before.unity", true);
                File.WriteAllText(Output + "/Camera.Before.json", JsonUtility.ToJson(new CameraSettings
                { far = camera.farClipPlane, layers = camera.layerCullDistances }, true));
                if (capture) Capture(camera, "Before");
            }
            int dinosaurs = Object.FindObjectsOfType<DinosaurAgent>().Length;
            TerrainData terrainData = terrain.terrainData;
            int trees = terrainData.treeInstanceCount;
            foreach (string folder in new[] { Prepared, Prepared + "/Meshes", Prepared + "/Materials", Prepared + "/Textures" })
                Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            int layer = EnsureLayer();
            MeshFilter sourceLod = source.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name.EndsWith("LOD3", StringComparison.Ordinal));
            if (sourceLod == null) throw new InvalidOperationException("Expected source cliff LOD3.");
            Mesh cliffMesh = PreparedMesh("CliffLOD3", sourceLod);
            Texture2D cliffTexture = PrepareCliffTexture();
            Material cliffMaterial = AssetDatabase.LoadAssetAtPath<Material>(Prepared + "/Materials/CliffHaze.mat");
            if (cliffMaterial == null)
                cliffMaterial = Surface("CliffHaze", cliffTexture, Color.white, 0f, 0.32f, 0.78f, 0.35f, 180f, 500f);
            GameObject prior = GameObject.Find(RootName);
            if (prior != null && PrefabUtility.IsPartOfPrefabInstance(prior))
                PrefabUtility.UnpackPrefabInstance(prior, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var root = prior != null ? prior : new GameObject(RootName);
            root.layer = layer;
            Transform oldCanopy = root.transform.Find("Outer Forest Canopy");
            if (oldCanopy != null) Object.DestroyImmediate(oldCanopy.gameObject);
            var canopy = new GameObject("Outer Forest Canopy");
            canopy.transform.SetParent(root.transform, false);
            canopy.layer = layer;
            Transform existingRidges = root.transform.Find("Distant Cliff Ridges");
            bool buildRidges = existingRidges == null;
            var ridges = buildRidges ? new GameObject("Distant Cliff Ridges") : existingRidges.gameObject;
            ridges.transform.SetParent(root.transform, false);
            ridges.layer = layer;
            Vector3 center = terrain.transform.position + terrainData.size * 0.5f;
            center.y = 0f;
            var random = new System.Random(731903);
            GameObject[] prototypes = terrainData.treePrototypes.Take(4).Select(p => p.prefab).ToArray();
            var treeMeshes = new Mesh[prototypes.Length];
            for (int type = 0; type < prototypes.Length; type++)
            {
                MeshFilter filter = prototypes[type].GetComponentInChildren<MeshFilter>();
                treeMeshes[type] = PreparedMesh("CanopyTree" + type, filter);
            }
            RainforestBoundaryTransition.Populate(canopy.transform, terrain, prototypes, treeMeshes, layer);
            RainforestLightForest.PopulateExtras(root.transform,terrain,layer);
            for (int index = 0; buildRidges && index < 12; index++)
            {
                float degrees = index * 30f + (float)random.NextDouble() * 7f - 3.5f;
                float angle = degrees * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                float radius = 315f + (float)random.NextDouble() * 35f;
                GameObject cliff = Geometry("Ridge " + index.ToString("00"), ridges.transform, cliffMesh, new[] { cliffMaterial }, layer);
                Vector3 position = center + direction * radius;
                position.y = -18f - (float)random.NextDouble() * 5f;
                cliff.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, degrees + 180f, 0f));
                cliff.transform.localScale = new Vector3(2.4f + (float)random.NextDouble() * 0.8f,
                    4.5f + (float)random.NextDouble() * 5f, 3f + (float)random.NextDouble() * 3f);
            }
            Material groundMaterial = Surface("LightForestGround", terrainData.terrainLayers[0].diffuseTexture,
                new Color(0.95f, 0.92f, 0.84f), 0f, 0f, 0.95f, 0.7f, 42f, 180f);
            groundMaterial.SetFloat("_VertexTint",1f);
            Transform oldSkirt = root.transform.Find("Unwalkable Forest Skirt");
            if (oldSkirt != null) Object.DestroyImmediate(oldSkirt.gameObject);
            GameObject skirt = Geometry("Unwalkable Forest Skirt", root.transform,
                RainforestBoundaryTransition.SkirtMesh(terrain), new[] { groundMaterial }, layer);
            skirt.transform.position = new Vector3(center.x, 0f, center.z);
            var binder = root.GetComponent<RainforestBackdrop>();
            if (binder == null) binder = root.AddComponent<RainforestBackdrop>();
            binder.backdropLayer = layer;
            binder.foregroundDistance = 150f;
            binder.backdropDistance = 600f;
            // Prefab owns geometry only; scene camera is bound after saving.
            binder.viewCamera = null;
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
            BindCamera(binder, camera);
            EditorUtility.SetDirty(binder);
            EditorUtility.SetDirty(camera);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            if (terrain.terrainData != terrainData || terrainData.treeInstanceCount != trees || Object.FindObjectsOfType<DinosaurAgent>().Length != dinosaurs)
                throw new InvalidOperationException("Stage three changed protected forest/dinosaur content.");
            if (capture) Capture(camera, "After");
            Debug.Log($"BACKDROP_STAGE_THREE_APPLIED: {canopy.transform.childCount} transition trees, 12 preserved LOD3 ridges; dinosaurs={dinosaurs}, terrain trees={trees}; protected content unchanged.");
        }

        [Serializable] private sealed class CameraSettings { public float far; public float[] layers; }

        private static void BindCamera(RainforestBackdrop backdrop, Camera camera)
        {
            if (backdrop == null || camera == null) throw new InvalidOperationException("Backdrop camera binding missing.");
            backdrop.viewCamera = camera;
            backdrop.ApplyCamera();
            if (PrefabUtility.IsPartOfPrefabInstance(backdrop))
                PrefabUtility.RecordPrefabInstancePropertyModifications(backdrop);
            int bit = 1 << backdrop.backdropLayer;
            foreach (Camera other in Object.FindObjectsOfType<Camera>())
                if (other != camera) other.cullingMask &= ~bit;
            foreach (Crest.OceanPlanarReflection reflection in Object.FindObjectsOfType<Crest.OceanPlanarReflection>())
            {
                var settings = new SerializedObject(reflection);
                settings.FindProperty("_reflectionLayers").intValue &= ~bit;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static int EnsureLayer()
        {
            int existing = LayerMask.NameToLayer("Backdrop");
            if (existing >= 0) return existing;
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tags.FindProperty("layers");
            for (int i = 30; i >= 8; i--)
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                {
                    layers.GetArrayElementAtIndex(i).stringValue = "Backdrop";
                    tags.ApplyModifiedPropertiesWithoutUndo();
                    return i;
                }
            throw new InvalidOperationException("No unused layer for Backdrop.");
        }

        internal static Mesh PreparedMesh(string name, MeshFilter filter)
        {
            string path = Prepared + "/Meshes/" + name + ".asset";
            Mesh result = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (result != null) return result;
            if (filter == null || filter.sharedMesh == null) throw new InvalidOperationException("Backdrop source mesh missing: " + name);
            result = Object.Instantiate(filter.sharedMesh);
            result.name = name;
            Matrix4x4 matrix = filter.transform.localToWorldMatrix;
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            Vector3[] vertices = result.vertices, normals = result.normals;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                if (normals.Length == vertices.Length) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
            }
            Bounds bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (Vector3 vertex in vertices) bounds.Encapsulate(vertex);
            Vector3 offset = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            for (int i = 0; i < vertices.Length; i++) vertices[i] -= offset;
            result.vertices = vertices;
            if (normals.Length == vertices.Length) result.normals = normals;
            result.RecalculateBounds();
            AssetDatabase.CreateAsset(result, path);
            return result;
        }

        private static Texture2D PrepareCliffTexture()
        {
            const string path = Prepared + "/Textures/CliffDiffuse.jpg";
            if (!File.Exists(path))
            {
                if (!AssetDatabase.CopyAsset(Folder + "/Source/CoastalCliff01/textures/coastal_cliff_01_diff_4k.jpg", path))
                    throw new InvalidOperationException("Cliff diffuse copy failed.");
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.maxTextureSize = 1024;
                importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material Surface(string name, Texture texture, Color tint, float cutoff, float min, float max, float saturation, float near, float far)
        {
            string path = Prepared + "/Materials/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Jurassic World/Backdrop Haze");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Backdrop haze shader unavailable.");
            if (material == null)
            {
                material = new Material(shader) { enableInstancing = true };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", QualitySettings.activeColorSpace == ColorSpace.Linear ? tint.linear : tint);
            material.SetFloat("_Cutoff", cutoff);
            material.SetColor("_HazeColor", QualitySettings.activeColorSpace == ColorSpace.Linear ? RenderSettings.fogColor.linear : RenderSettings.fogColor);
            material.SetFloat("_HazeMin", min);
            material.SetFloat("_HazeMax", max);
            material.SetFloat("_HazeNear", near);
            material.SetFloat("_HazeFar", far);
            material.SetFloat("_Saturation", saturation);
            EditorUtility.SetDirty(material);
            return material;
        }

        internal static GameObject Geometry(string name, Transform parent, Mesh mesh, Material[] materials, int layer)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.layer = layer;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return go;
        }

        public static void Validate()
        {
            var backdrops = Object.FindObjectsOfType<RainforestBackdrop>();
            if (backdrops.Length != 1) throw new InvalidOperationException("Expected one backdrop root.");
            RainforestBackdrop backdrop = backdrops[0];
            if (backdrop.viewCamera == null || backdrop.viewCamera.farClipPlane != 600f ||
                backdrop.transform.Find("Outer Forest Canopy").childCount > RainforestBoundaryTransition.MaximumTrees ||
                backdrop.transform.Find("Outer Forest Canopy").childCount <= 128 ||
                backdrop.transform.Find("Distant Cliff Ridges").childCount != 12 ||
                backdrop.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidOperationException("Backdrop camera / geometry counts / collider invariant failed.");
            foreach (MeshRenderer renderer in backdrop.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.gameObject.layer != backdrop.backdropLayer || renderer.shadowCastingMode != ShadowCastingMode.Off ||
                    renderer.sharedMaterials.Any(m => m == null || ShaderUtil.ShaderHasError(m.shader)))
                    throw new InvalidOperationException("Invalid backdrop renderer: " + renderer.name);
            RainforestBoundaryTransition.Validate(backdrop.transform.Find("Outer Forest Canopy"), Terrain.activeTerrain);
            RainforestLightForest.Validate(backdrop.transform,Terrain.activeTerrain);
            Debug.Log("BACKDROP_STAGE_THREE_VALID: one root, graded boundary forest, 12 LOD3 ridges, no colliders or cast shadows.");
        }

        internal static void Capture(Camera camera, string suffix, int views = 10)
        {
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
            float aspect = camera.aspect;
            var target = new RenderTexture(1280, 720, 24);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var atmosphere = Object.FindObjectOfType<RainforestAtmosphereController>();
            try
            {
                camera.targetTexture = target;
                camera.aspect = 1280f / 720f;
                for (int view = 0; view < views; view++)
                {
                    Vector3 eye = view < 8 ? new Vector3(0f, 1.7f, 0f) : new Vector3(view == 8 ? 84f : -84f, 1.7f, 65f);
                    float yaw = view < 8 ? view * 45f : view == 8 ? 45f : 315f;
                    camera.transform.SetPositionAndRotation(eye, Quaternion.Euler(-8f, yaw, 0f));
                    if (atmosphere != null && atmosphere.cloudRenderer != null) atmosphere.cloudRenderer.transform.position = eye;
                    camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
                    File.WriteAllBytes(Output + "/View-" + view.ToString("00") + "." + suffix + ".png", pixels.EncodeToPNG());
                }
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.targetTexture = oldTarget; camera.aspect = aspect;
                if (atmosphere != null && atmosphere.cloudRenderer != null) atmosphere.cloudRenderer.transform.position = position;
                RenderTexture.active = oldActive;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }

        public static void InspectBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Directory.CreateDirectory(Output);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
            if (model == null) throw new FileNotFoundException("Stage-three model missing.", Source);
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                Debug.Log($"BACKDROP_SOURCE: {filter.name}; vertices={filter.sharedMesh.vertexCount}; triangles={filter.sharedMesh.triangles.Length / 3}; bounds={filter.sharedMesh.bounds}; transform={filter.transform.localToWorldMatrix}");
            Terrain terrain = Terrain.activeTerrain;
            Debug.Log($"BACKDROP_TERRAIN: origin={terrain.transform.position}, size={terrain.terrainData.size}, trees={terrain.terrainData.treeInstanceCount}");
            foreach (TreePrototype prototype in terrain.terrainData.treePrototypes.Take(8))
            {
                Debug.Log("BACKDROP_TREE: " + AssetDatabase.GetAssetPath(prototype.prefab));
                foreach (MeshFilter filter in prototype.prefab.GetComponentsInChildren<MeshFilter>(true).Take(5))
                    if (filter.sharedMesh != null) Debug.Log($"BACKDROP_TREE_MESH: {filter.name}; v={filter.sharedMesh.vertexCount}; bounds={filter.sharedMesh.bounds}");
            }
        }
    }
}
