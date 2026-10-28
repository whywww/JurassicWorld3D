using System;
using JurassicWorld.PlayerInputs;
using UnityEngine;

namespace JurassicWorld.BCI.BrainLink
{
    [DisallowMultipleComponent]
    public sealed class BrainLinkPlayerInput : MonoBehaviour, IBrainControlInput
    {
        [SerializeField] private BrainLinkSettings settings = new BrainLinkSettings();
        private BrainLinkClient client;
        private string error;
        private bool showDiagnostics;
        public BrainLinkClient Client => client;

        private void OnEnable()
        {
            if (!BrainInputSelection.CanStart(this)) return;
            PlayerMovementInput.Register(this, ReadMovement);
            showDiagnostics = settings.showDiagnostics; error = null;
            try
            {
                var configuration = JsonUtility.FromJson<BrainLinkSettings>(JsonUtility.ToJson(settings));
                client = new BrainLinkClient(configuration);
                client.Start();
            }
            catch (Exception exception) { error = exception.Message; Debug.LogError("[BrainLink] " + error, this); }
        }

        private Vector2 ReadMovement() => client != null && isActiveAndEnabled ? client.ReadMovement() : Vector2.zero;
        private void Update() { if (Input.GetKeyDown(KeyCode.F9)) showDiagnostics = !showDiagnostics; }
        private void OnGUI()
        {
            if (!Application.isPlaying || !showDiagnostics) return;
            GUI.Box(new Rect(12, 160, 520, 145), "BrainLink Pro\n" + settings.host + ":" + settings.port
                + "\n" + (error ?? (client != null ? client.Describe() : "Inactive / selection conflict"))
                + "\nforward target: " + ReadMovement().y.ToString("F2") + " x player max speed   [F9: hide]");
        }

        private void OnDisable()
        {
            client?.Dispose(); client = null;
            PlayerMovementInput.Unregister(this);
        }
        private void OnDestroy() => OnDisable();
    }
}
