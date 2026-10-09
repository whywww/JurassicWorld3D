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
        public static void GenerateAndVerify()
        {
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                Directory.CreateDirectory("Assets/UI/DinosaurPortraits");
                foreach (DinosaurAgent agent in UnityEngine.Object.FindObjectsOfType<DinosaurAgent>())
                {
                    string path = "Assets/UI/DinosaurPortraits/" + agent.name + ".png";
                    Capture(agent, path);
                    AssetDatabase.ImportAsset(path);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    importer.textureType = TextureImporterType.Sprite;
                    importer.mipmapEnabled = false;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                    var settings = new SerializedObject(agent);
                    settings.FindProperty("minimapPortrait").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    settings.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                NavigationAcceptance.InstallAndVerify();
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void Capture(DinosaurAgent agent, string path)
        {
            GameObject clone = UnityEngine.Object.Instantiate(agent.GetComponent<DinosaurFbxLoader>().ModelRoot);
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
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                Transform head = clone.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.ToLowerInvariant().Contains("head") && !t.name.ToLowerInvariant().Contains("end"));
                Vector3 center = head != null ? head.position : bounds.center;
                float frame = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) * 0.16f;
                Debug.Log("PORTRAIT_HEAD " + agent.name + " " + (head != null ? head.name : "fallback") + " frame=" + frame);
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = frame;
                camera.cullingMask = 1 << 31;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.045f, 0.075f, 0.065f);
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = frame * 20f;
                camera.transform.position = center + new Vector3(1f, 0.25f, 1f).normalized * frame * 5f;
                camera.transform.LookAt(center);
                Vector3 offset = agent.name == "Tyrannosaurus Rex" ? camera.transform.right * frame * 0.3f - camera.transform.up * frame * 0.05f : camera.transform.up * frame * 0.2f;
                camera.transform.position += offset;
                camera.orthographicSize = frame * 0.7f;
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