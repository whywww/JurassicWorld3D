using System;

namespace JurassicWorld.BCI.BrainLink
{
    [Serializable]
    public sealed class BrainLinkSettings
    {
        public string host = "127.0.0.1";
        public int port = 13856;
        public int attentionThreshold = 40;
        public float sampleTimeout = 3f;
        public bool showDiagnostics = true;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(host) || port < 1 || port > 65535
                || attentionThreshold < 0 || attentionThreshold >= 100
                || float.IsNaN(sampleTimeout) || float.IsInfinity(sampleTimeout) || sampleTimeout < 1f)
                throw new ArgumentException("Invalid BrainLink host, port, threshold (0–99) or timeout (>=1s).");
        }
    }
}
