using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JurassicWorld.Editor
{
    public static class JurassicProjectSetup
    {
        private const string MainScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Jurassic World/Create MVP Scene")]
        public static void CreateScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, MainScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Jurassic World MVP scene created.");
        }

        // 编辑态烘焙入口（任务 #3 可用 -executeMethod JurassicWorld.Editor.JurassicProjectSetup.BakeScene 调用）。
        // 打开 Main.unity → 由 JurassicWorldSceneBuilder 建造全部内容 → 标脏并保存场景与资产。
        [MenuItem("Jurassic World/Bake Scene (Edit Mode)")]
        public static void BakeScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            if (!File.Exists(MainScenePath))
            {
                Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(newScene, MainScenePath);
            }

            Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            JurassicWorldSceneBuilder.Build();

            if (!EditorBuildSettings.scenes.Any(s => s.path == MainScenePath))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes
                    .Concat(new[] { new EditorBuildSettingsScene(MainScenePath, true) }).ToArray();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("JURASSIC_SCENE_BAKED: 已在编辑态烘焙 Main.unity（相机 / 方向光 / 草地 / HUD / 5 只恐龙）。");
        }

        // 编辑态断言：不点 Play，打开烘焙后的 Main.unity，检查场景内已存在相机、方向光与 5 只挂
        // DinosaurAgent 的恐龙 GameObject。
        [MenuItem("Jurassic World/Validate MVP")]
        public static void ValidateProject()
        {
            if (!File.Exists(MainScenePath))
                throw new FileNotFoundException("缺少已烘焙的主场景，请先执行 Jurassic World/Bake Scene (Edit Mode)。", MainScenePath);

            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            if (Object.FindObjectOfType<Camera>() == null)
                throw new InvalidDataException("场景缺少相机（Main Camera）。");

            bool hasDirectionalLight = Object.FindObjectsOfType<Light>().Any(light => light.type == LightType.Directional);
            if (!hasDirectionalLight)
                throw new InvalidDataException("场景缺少方向光（Sun）。");

            DinosaurAgent[] agents = Object.FindObjectsOfType<DinosaurAgent>();
            if (agents.Length != 5)
                throw new InvalidDataException("期望场景中有 5 只挂 DinosaurAgent 的恐龙，实际找到 " + agents.Length + " 只。");

            if (agents.Any(agent => agent.GetComponent<DinosaurFbxLoader>() == null))
                throw new InvalidDataException("存在缺少 DinosaurFbxLoader 绑定器的恐龙。");

            if (!EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == MainScenePath))
                throw new InvalidDataException("Main scene is not enabled in build settings.");

            Debug.Log("JURASSIC_MVP_VALIDATION_OK: 编辑态校验通过（相机 / 方向光 / 5 只挂 DinosaurAgent 的恐龙）。");
        }
    }
}
