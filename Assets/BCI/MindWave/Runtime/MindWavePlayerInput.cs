using System;
using JurassicWorld.PlayerInputs;
using UnityEngine;

namespace JurassicWorld.BCI.MindWave
{
    [DisallowMultipleComponent]
    public sealed class MindWavePlayerInput : MonoBehaviour, IBrainControlInput
    {
        private ThinkGearClient client;
        private string configurationError;
        private MindWaveSettings runtimeSettings;
        private bool showDiagnostics;

        [SerializeField] private MindWaveSettings settings = new MindWaveSettings { source = "Connector" };

        private void OnEnable()
        {
            if (!BrainInputSelection.CanStart(this)) return;
            PlayerMovementInput.Register(this, ReadMovement);
            configurationError = null;
            showDiagnostics = settings.showDiagnostics;
            try
            {
                // Clone saved parameters: command-line backend overrides never change the scene.
                var configuration = JsonUtility.FromJson<MindWaveSettings>(JsonUtility.ToJson(settings));
                MindWaveSettings.ApplyArguments(configuration);
                configuration.Validate();
                runtimeSettings = configuration;
                if (configuration.source == "Original") throw new ArgumentException("请关闭 MindWave 组件以停用；后端应为 Connector 或 TcpSimulator。");
                client = new ThinkGearClient(configuration);
                client.Start();
            }
            catch (Exception error)
            {
                configurationError = "Invalid MindWave configuration: " + error.Message;
                Debug.LogError("[MindWave] " + configurationError, this);
            }
        }

        private Vector2 ReadMovement() => client != null && isActiveAndEnabled ? client.ReadMovement() : Vector2.zero;

        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.F9)) showDiagnostics = !showDiagnostics;
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || !showDiagnostics) return;
            var configuration = runtimeSettings ?? settings;
            string description = configurationError ?? (configuration.source + " " + configuration.host + ":" + configuration.Port
                + "\n" + (client != null ? client.Describe() : "Inactive / selection conflict") + "\nforward target: " + ReadMovement().y.ToString("F2")
                + " x player max speed   [F9: hide]");
            GUI.Box(new Rect(12, 160, 460, 140), "MindWave\n" + description);
        }

        private void OnDisable() { client?.Dispose(); client = null; PlayerMovementInput.Unregister(this); }
        private void OnDestroy() => OnDisable();
    }
}
