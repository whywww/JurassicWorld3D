using System.IO;
using UnityEditor;
using UnityEngine;

namespace JurassicWorld.BCI.MindWave.Editor
{
    public sealed class MindWaveSettingsWindow : EditorWindow
    {
        private const string Path = "Assets/BCI/MindWave/Resources/MindWaveSettings.json";
        private MindWaveSettings settings;
        private string message;

        [MenuItem("Jurassic World/BCI/MindWave Settings")]
        public static void Open() => GetWindow<MindWaveSettingsWindow>("MindWave");

        private void OnEnable()
        {
            settings = new MindWaveSettings();
            try { JsonUtility.FromJsonOverwrite(File.ReadAllText(Path), settings); }
            catch (System.Exception error) { message = error.Message; }
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Changes apply on the next Play / application restart. Rebuild to update packaged settings. F9 toggles diagnostics.", MessageType.Info);
            string[] sources = { "Original", "TcpSimulator", "Connector" };
            int selected = System.Array.IndexOf(sources, settings.source);
            settings.source = sources[EditorGUILayout.Popup("Input source", Mathf.Max(0, selected), sources)];
            settings.host = EditorGUILayout.TextField("Host", settings.host);
            settings.simulatorPort = EditorGUILayout.IntField("TCP simulator port", settings.simulatorPort);
            settings.connectorPort = EditorGUILayout.IntField("Connector port", settings.connectorPort);
            settings.attentionThreshold = EditorGUILayout.IntSlider("Attention threshold", settings.attentionThreshold, 0, 99);
            settings.sampleTimeout = EditorGUILayout.FloatField("Sample timeout (seconds)", settings.sampleTimeout);
            settings.showDiagnostics = EditorGUILayout.Toggle("Show diagnostics", settings.showDiagnostics);
            settings.requestAuthorization = EditorGUILayout.Toggle("Require TGSP authorization", settings.requestAuthorization);
            settings.appName = EditorGUILayout.TextField("TGSP app name", settings.appName);
            settings.appKey = EditorGUILayout.TextField("TGSP app identifier", settings.appKey);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Save settings"))
                {
                    try
                    {
                        settings.Validate();
                        File.WriteAllText(Path, JsonUtility.ToJson(settings, true) + "\n");
                        AssetDatabase.ImportAsset(Path);
                        message = "Saved. Original scene was not modified.";
                    }
                    catch (System.Exception error) { message = error.Message; }
                }
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.None);
        }
    }
}
