using System;
using JurassicWorld.PlayerInputs;
using UnityEngine;

namespace JurassicWorld.BCI.MindWave
{
    public sealed class MindWaveBootstrap : MonoBehaviour
    {
        private MindWaveSettings settings;
        private ThinkGearClient client;
        private string configurationError;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            MindWaveSettings configuration = null;
            string error = null;
            try { configuration = MindWaveSettings.Load(); }
            catch (Exception exception) { error = "Invalid MindWave configuration: " + exception.Message; }
            if (configuration != null && configuration.source == "Original") return;
            var root = new GameObject("MindWave Input (optional)");
            DontDestroyOnLoad(root);
            var input = root.AddComponent<MindWaveBootstrap>();
            input.settings = configuration;
            input.configurationError = error;
            // Registration happens before scene OnEnable, including EMOTIV's connection startup.
            PlayerMovementInput.Register(input, input.ReadMovement);
            if (error != null) Debug.LogError("[MindWave] " + error);
            else
            {
                input.client = new ThinkGearClient(configuration);
                input.client.Start();
                Debug.Log("[MindWave] " + configuration.source + " -> " + configuration.host + ":" + configuration.Port);
            }
#endif
        }

        private Vector2 ReadMovement() => client != null && isActiveAndEnabled ? client.ReadMovement() : Vector2.zero;

        private void Update()
        {
            if (settings != null && UnityEngine.Input.GetKeyDown(KeyCode.F9))
                settings.showDiagnostics = !settings.showDiagnostics;
        }

        private void OnGUI()
        {
            if (settings != null && !settings.showDiagnostics) return;
            string description = configurationError ?? (settings.source + " " + settings.host + ":" + settings.Port
                + "\n" + client.Describe() + "\nforward target: " + ReadMovement().y.ToString("F2")
                + " x player max speed   [F9: hide]");
            GUI.Box(new Rect(12, 160, 460, 140), "MindWave\n" + description);
        }

        private void OnDisable() { client?.Dispose(); }
        private void OnDestroy()
        {
            client?.Dispose();
            PlayerMovementInput.Unregister(this);
        }
    }
}
