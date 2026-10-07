using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    internal static class MainSceneStartup
    {
        private const string MainScene = "Assets/Scenes/Main.unity";
        private const string SessionKey = "JurassicWorld.MainSceneStartupChecked";

        static MainSceneStartup()
        {
            if (!Application.isBatchMode)
                EditorApplication.delayCall += OpenMainIfEditorIsEmpty;
        }

        [MenuItem("Jurassic World/Open Main Scene")]
        private static void OpenMainMenu() => JurassicMenuGuard.Run(OpenMain, true);

        [MenuItem("Jurassic World/Open Main Scene", true)]
        private static bool OpenMainMenuEnabled() => JurassicMenuGuard.CanRun;

        private static void OpenMain()
        {
            EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);
        }

        private static void OpenMainIfEditorIsEmpty()
        {
            if (SessionState.GetBool(SessionKey, false) || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            SessionState.SetBool(SessionKey, true);

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !string.IsNullOrEmpty(scene.path) || scene.isDirty)
                return;

            GameObject[] roots = scene.GetRootGameObjects();
            if (roots.Length != 2)
                return;
            bool camera = false;
            bool light = false;
            foreach (GameObject root in roots)
            {
                camera |= root.name == "Main Camera";
                light |= root.name == "Directional Light";
            }
            if (camera && light)
                OpenMain();
        }
    }
    // Menu-only checks leave production and -executeMethod entry points unattended.
    internal static class JurassicMenuGuard
    {
        internal static bool CanRun => !EditorApplication.isPlayingOrWillChangePlaymode;

        internal static void Run(System.Action action, bool saveModifiedScenes)
        {
            if (!CanRun) return;
            if (saveModifiedScenes && !Application.isBatchMode &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            action();
        }
    }
}
