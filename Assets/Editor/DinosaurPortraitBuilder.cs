using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    public static class DinosaurPortraitBuilder
    {
        internal sealed class PortraitFraming
        {
            internal readonly string[] HeadBones;
            internal readonly float OrthographicScale;
            internal readonly Vector3 ViewDirection;
            internal readonly float VerticalOffset;

            internal PortraitFraming(string[] headBones, float orthographicScale, Vector3 viewDirection, float verticalOffset)
            {
                HeadBones = headBones;
                OrthographicScale = orthographicScale;
                ViewDirection = viewDirection;
                VerticalOffset = verticalOffset;
            }

            internal static readonly PortraitFraming Default =
                new PortraitFraming(null, 0.7f, new Vector3(1f, 0.25f, 1f), 0.2f);
        }

        public static void GenerateAndVerify()
        {
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                foreach (DinosaurAgent agent in UnityEngine.Object.FindObjectsOfType<DinosaurAgent>())
                {
                    string folder = JurassicWorldSceneBuilder.GetDinosaurPreparedFolder(agent.name);
                    JurassicWorldSceneBuilder.EnsureFolder(folder);
                    string path = folder + "/" + agent.name + ".png";
                    CaptureModel(agent.GetComponent<DinosaurFbxLoader>().ModelRoot, agent.name, path);
                    ConfigurePortraitImport(path);
                    var settings = new SerializedObject(agent);
                    settings.FindProperty("minimapPortrait").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    settings.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                NavigationAcceptance.InstallAndVerify();
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        internal static void ConfigurePortraitImport(string path)
        {
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        internal static void EnsureHeadshotIfMissing(GameObject modelRoot, string agentName, string path, PortraitFraming framing)
        {
            if (File.Exists(path) && AssetDatabase.LoadAssetAtPath<Sprite>(path) != null) return;
            JurassicWorldSceneBuilder.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            CaptureModel(modelRoot, agentName, path, framing);
            ConfigurePortraitImport(path);
            Debug.Log("DINOSAUR_HEADSHOT_READY: " + path);
        }

        internal static void BindPortrait(DinosaurAgent agent, string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("Portrait Sprite import failed: " + path);
            var settings = new SerializedObject(agent);
            SerializedProperty property = settings.FindProperty("minimapPortrait");
            if (property == null) throw new InvalidOperationException("DinosaurAgent.minimapPortrait field missing.");
            property.objectReferenceValue = sprite;
            settings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(agent);
        }

        [MenuItem("Jurassic World/Assets/Generate Pachycephalosaurus and Sauropelta Headshots")]
        public static void GeneratePachycephalosaurusAndSauropeltaHeadshots()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before generating headshots.");
            const string scenePath = "Assets/Scenes/Main.unity";
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty)
                throw new InvalidOperationException("Save the current scene before generating headshots.");
            if (scene.path != scenePath) scene = EditorSceneManager.OpenScene(scenePath);

            DinosaurAgent pachycephalosaurus = FindSingleAgent("Pachycephalosaurus");
            DinosaurAgent sauropelta = FindSingleAgent("Sauropelta");
            CaptureAgentModel(pachycephalosaurus, PachycephalosaurusSceneIntegration.PortraitPath, PachycephalosaurusSceneIntegration.PortraitSettings);
            CaptureAgentModel(sauropelta, SauropeltaSceneIntegration.PortraitPath, SauropeltaSceneIntegration.PortraitSettings);
            BindPortrait(pachycephalosaurus, PachycephalosaurusSceneIntegration.PortraitPath);
            BindPortrait(sauropelta, SauropeltaSceneIntegration.PortraitPath);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("DINOSAUR_HEADSHOTS_READY: Pachycephalosaurus, Sauropelta");
        }

        private static DinosaurAgent FindSingleAgent(string name)
        {
            DinosaurAgent[] matches = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>(true)
                .Where(agent => agent.name == name).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("Expected exactly one " + name + " in Main, found " + matches.Length + ".");
            return matches[0];
        }

        private static void CaptureAgentModel(DinosaurAgent agent, string path, PortraitFraming framing)
        {
            DinosaurFbxLoader loader = agent.GetComponent<DinosaurFbxLoader>();
            if (loader == null || loader.ModelRoot == null)
                throw new InvalidOperationException("ModelRoot missing for " + agent.name + ".");
            JurassicWorldSceneBuilder.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            CaptureModel(loader.ModelRoot, agent.name, path, framing);
            ConfigurePortraitImport(path);
        }

        internal static void CaptureModel(GameObject modelRoot, string agentName, string path)
            => CaptureModel(modelRoot, agentName, path, PortraitFraming.Default);

        internal static void CaptureModel(GameObject modelRoot, string agentName, string path, PortraitFraming framing)
        {
            framing = framing ?? PortraitFraming.Default;
            GameObject clone = UnityEngine.Object.Instantiate(modelRoot);
            var cameraObject = new GameObject("Portrait Camera", typeof(Camera));
            var lightObject = new GameObject("Portrait Light", typeof(Light));
            var target = new RenderTexture(128, 128, 24);
            var pixels = new Texture2D(128, 128, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            bool fog = RenderSettings.fog;
            try
            {
                clone.transform.position = new Vector3(10000f, 10000f, 10000f);
                clone.transform.rotation = Quaternion.identity;
                foreach (Transform child in clone.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
                var renderers = clone.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                    throw new InvalidOperationException("Portrait model has no renderers: " + agentName);
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);

                Vector3 center;
                string headLabel;
                if (framing.HeadBones == null || framing.HeadBones.Length == 0)
                {
                    Transform head = clone.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.ToLowerInvariant().Contains("head") && !t.name.ToLowerInvariant().Contains("end"));
                    center = head != null ? head.position : bounds.center;
                    headLabel = head != null ? head.name : "fallback";
                }
                else
                {
                    Transform[] all = clone.GetComponentsInChildren<Transform>(true);
                    var bones = new Transform[framing.HeadBones.Length];
                    for (int i = 0; i < framing.HeadBones.Length; i++)
                    {
                        Transform bone = all.FirstOrDefault(t => t.name == framing.HeadBones[i]);
                        if (bone == null)
                            throw new InvalidOperationException("Portrait head bone missing: " + framing.HeadBones[i] + " in " + agentName);
                        bones[i] = bone;
                    }
                    center = Vector3.zero;
                    foreach (Transform bone in bones) center += bone.position;
                    center /= bones.Length;
                    headLabel = string.Join(",", framing.HeadBones);
                }

                float frame = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) * 0.16f;
                if (frame <= 0.0001f)
                    throw new InvalidOperationException("Portrait bounds are empty: " + agentName);
                Debug.Log("PORTRAIT_HEAD " + agentName + " " + headLabel + " frame=" + frame);
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.cullingMask = 1 << 31;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.045f, 0.075f, 0.065f);
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = frame * 20f;
                camera.transform.position = center + framing.ViewDirection.normalized * frame * 5f;
                camera.transform.LookAt(center);
                Vector3 offset = agentName == "Tyrannosaurus Rex" ? camera.transform.right * frame * 0.3f - camera.transform.up * frame * 0.05f : camera.transform.up * frame * framing.VerticalOffset;
                camera.transform.position += offset;
                camera.orthographicSize = frame * framing.OrthographicScale;
                Light light = lightObject.GetComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.5f;
                light.cullingMask = 1 << 31;
                light.transform.rotation = Quaternion.Euler(35f, -35f, 0f);
                RenderSettings.fog = false;
                target.Create();
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                RenderSettings.fog = fog;
                RenderTexture.active = previous;
                target.Release();
                UnityEngine.Object.DestroyImmediate(clone);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(lightObject);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(pixels);
            }
        }
    }
}