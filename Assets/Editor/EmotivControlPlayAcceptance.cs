using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    // Uses a disabled input component so Play-mode isolation can be verified without Cortex.
    [InitializeOnLoad]
    public static class EmotivControlPlayAcceptance
    {
        private const string Key = "JurassicWorld.EmotivControlPlayAcceptance.";

        static EmotivControlPlayAcceptance()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("Jurassic World/Tests/Emotiv Control Play Isolation")]
        public static void Run()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            SessionState.SetString(Key + "originalScene", UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
            SessionState.SetBool(Key + "optionsEnabled", EditorSettings.enterPlayModeOptionsEnabled);
            SessionState.SetInt(Key + "options", (int)EditorSettings.enterPlayModeOptions);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/Settings/EmotivPlayAcceptance.asset");
            string scenePath = AssetDatabase.GenerateUniqueAssetPath("Assets/Settings/EmotivPlayAcceptance.unity");
            SessionState.SetString(Key + "asset", assetPath);
            SessionState.SetString(Key + "scene", scenePath);
            try
            {
                var settings = ScriptableObject.CreateInstance<EmotivControlSettings>();
                settings.moveSpeed = 7;
                settings.push = EmotivMovementAction.ForwardLeft;
                settings.pushSensitivity.useProjectValue = true;
                settings.pushSensitivity.value = 8;
                AssetDatabase.CreateAsset(settings, assetPath);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var holder = new GameObject("Emotiv Play isolation");
                holder.SetActive(false);
                var input = holder.AddComponent<EmotivPlayerInput>();
                input.enabled = false;
                var serialized = new SerializedObject(input);
                serialized.FindProperty("controlSettings").objectReferenceValue = settings;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                holder.SetActive(true);
                EditorSceneManager.SaveScene(holder.scene, scenePath);
                AssetDatabase.SaveAssetIfDirty(settings);
                SessionState.SetString(Key + "assetContents", File.ReadAllText(assetPath));
                SessionState.SetInt(Key + "cycle", 0);
                SessionState.SetBool(Key + "active", true);
                EditorSettings.enterPlayModeOptionsEnabled = false;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Fail(error); }
        }

        [MenuItem("Jurassic World/Tests/Emotiv Control Play Isolation", true)]
        private static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode;

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            try
            {
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    var input = UnityEngine.Object.FindObjectOfType<EmotivPlayerInput>();
                    var saved = AssetDatabase.LoadAssetAtPath<EmotivControlSettings>(SessionState.GetString(Key + "asset", ""));
                    var temporary = input.ControlSettings;
                    Require(temporary != saved && !EditorUtility.IsPersistent(temporary), "Play must use a transient copy");
                    Require(!EmotivControlSettingsEditor.CanEditSettings, "Both Inspectors must be read-only in Play");
                    Require(temporary.moveSpeed == 7 && temporary.push == EmotivMovementAction.ForwardLeft,
                        "Play applies values configured before Play");
                    Require(temporary.pushSensitivity.useProjectValue && temporary.pushSensitivity.value == 8,
                        "Play loads the saved per-command sensitivity override");
                    Require(input.ControlSettings.ResolveCommand("push", 1, 0) == new Vector2(-1, 1), "Saved mapping applies");
                    Require(saved.moveSpeed == 7 && saved.pushSensitivity.value == 8, "Saved source untouched during Play");
                    EditorApplication.delayCall += EditorApplication.ExitPlaymode;
                }
                else if (state == PlayModeStateChange.EnteredEditMode)
                {
                    var input = UnityEngine.Object.FindObjectOfType<EmotivPlayerInput>();
                    Require(EmotivControlSettingsEditor.CanEditSettings, "Inspectors become editable after Play");
                    Require(input.ControlSettings.moveSpeed == 7 && input.ControlSettings.push == EmotivMovementAction.ForwardLeft
                        && input.ControlSettings.pushSensitivity.value == 8, "Exit Play retains saved configuration");
                    Require(File.ReadAllText(SessionState.GetString(Key + "asset", ""))
                        == SessionState.GetString(Key + "assetContents", ""), "Play must not change asset file");
                    int cycle = SessionState.GetInt(Key + "cycle", 0) + 1;
                    SessionState.SetInt(Key + "cycle", cycle);
                    if (cycle < 3)
                    {
                        // Two consecutive fast-Play cycles verify copies cannot survive into the next session.
                        EditorSettings.enterPlayModeOptionsEnabled = true;
                        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
                        EditorApplication.delayCall += EditorApplication.EnterPlaymode;
                    }
                    else
                    {
                        SessionState.SetBool(Key + "active", false);
                        // Restore scenes only after the Play-mode transition has finished.
                        EditorApplication.delayCall += () =>
                        {
                            Cleanup();
                            Debug.Log("[EmotivControlPlayAcceptance] PASS: pre-Play configuration applied; Inspectors read-only in normal/fast Play; asset unchanged.");
                            if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(0);
                        };
                    }
                }
            }
            catch (Exception error) { Fail(error); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Fail(Exception error)
        {
            Debug.LogException(error);
            SessionState.SetBool(Key + "active", false);
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            EditorApplication.delayCall += () =>
            {
                Cleanup();
                if (Application.isBatchMode) EditorApplication.Exit(1);
            };
        }

        private static void Cleanup()
        {
            SessionState.SetBool(Key + "active", false);
            EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(Key + "optionsEnabled", false);
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(Key + "options", 0);
            string original = SessionState.GetString(Key + "originalScene", "");
            if (!string.IsNullOrEmpty(original)) EditorSceneManager.OpenScene(original);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(SessionState.GetString(Key + "scene", ""));
            AssetDatabase.DeleteAsset(SessionState.GetString(Key + "asset", ""));
        }
    }
}
