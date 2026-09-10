using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    public static class JurassicProjectSetup
    {
        [MenuItem("Jurassic World/Create MVP Scene")]
        public static void CreateScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Jurassic World MVP scene created.");
        }

        [MenuItem("Jurassic World/Validate MVP")]
        public static void ValidateProject()
        {
            string modelPath = Path.Combine(Application.streamingAssetsPath, DinosaurGltfLoader.RelativeModelPath);
            if (!File.Exists(modelPath))
                throw new FileNotFoundException("Missing dinosaur GLB", modelPath);

            byte[] bytes = File.ReadAllBytes(modelPath);
            string json = ExtractGlbJson(bytes);
            string[] expected = { "idle", "run", "roar", "bite", "attack_tail" };
            foreach (string clip in expected)
            {
                if (!json.Contains("\"name\":\"" + clip + "\""))
                    throw new InvalidDataException("Missing animation in GLB: " + clip);
            }

            string pterodactylPath = Path.Combine(Application.streamingAssetsPath, "Dinosaur/Pteradactal.glb");
            if (!File.Exists(pterodactylPath))
                throw new FileNotFoundException("Missing pterodactyl GLB", pterodactylPath);
            if (!ExtractGlbJson(File.ReadAllBytes(pterodactylPath)).Contains("PixCap Animation"))
                throw new InvalidDataException("Missing pterodactyl flight animation.");

            string[] fbxAssets =
            {
                "Assets/Resources/Dinosaurs/Rajasaurus/source/Rajasaurus.fbx",
                "Assets/Resources/Dinosaurs/Velociraptor/Raptor/Raptor_Animated_FBX.fbx",
                "Assets/Resources/Dinosaurs/Argentinosaurus/source/Argentinosaurus.fbx"
            };
            foreach (string assetPath in fbxAssets)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab == null)
                    throw new FileNotFoundException("Missing imported FBX", assetPath);

                AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(assetPath)
                    .OfType<AnimationClip>()
                    .Where(clip => !clip.name.StartsWith("__preview__"))
                    .ToArray();
                if (clips.Length == 0)
                    throw new InvalidDataException("No animation clips imported from: " + assetPath);
                GameObject instance = Object.Instantiate(prefab);
                if (instance.GetComponentInChildren<Animation>(true) == null || instance.GetComponentInChildren<Renderer>(true) == null)
                    throw new InvalidDataException("FBX lacks a runtime Animation component or renderer: " + assetPath);
                Object.DestroyImmediate(instance);
                Debug.Log(assetPath + " animations: " + string.Join(", ", clips.Select(clip => clip.name)));
            }

            if (!EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == "Assets/Scenes/Main.unity"))
                throw new InvalidDataException("Main scene is not enabled in build settings.");

            Debug.Log("JURASSIC_MVP_VALIDATION_OK: five dinosaur models, animations, scene and scripts are present.");
        }

        private static string ExtractGlbJson(byte[] bytes)
        {
            if (bytes.Length < 20 || System.BitConverter.ToUInt32(bytes, 0) != 0x46546C67)
                throw new InvalidDataException("File is not a valid GLB container.");
            int jsonLength = (int)System.BitConverter.ToUInt32(bytes, 12);
            return System.Text.Encoding.UTF8.GetString(bytes, 20, jsonLength).TrimEnd(' ', '\0');
        }
    }
}
