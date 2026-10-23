using System;
using UnityEngine;

namespace JurassicWorld.BCI.MindWave
{
    [Serializable]
    public sealed class MindWaveSettings
    {
        public string source = "Original";
        public string host = "127.0.0.1";
        public int simulatorPort = 13855;
        public int connectorPort = 13854;
        public int attentionThreshold = 40;
        public float sampleTimeout = 3f;
        public bool showDiagnostics = true;
        // Legacy TGC streams after configuration; some TGSP servers require authorization first.
        public bool requestAuthorization;
        public string appName = "JurassicWorld";
        public string appKey = "b789b7a7f21e29a3907b8d584af1e7455e894a56";

        public int Port => source == "Connector" ? connectorPort : simulatorPort;

        public void Validate()
        {
            if (source != "Original" && source != "TcpSimulator" && source != "Connector")
                throw new ArgumentException("Source must be Original, TcpSimulator or Connector.");
            if (string.IsNullOrWhiteSpace(host) || simulatorPort < 1 || simulatorPort > 65535
                || connectorPort < 1 || connectorPort > 65535 || attentionThreshold < 0 || attentionThreshold >= 100
                || float.IsNaN(sampleTimeout) || float.IsInfinity(sampleTimeout) || sampleTimeout < 1f)
                throw new ArgumentException("Invalid host, port, threshold or sample timeout.");
            if (string.IsNullOrWhiteSpace(appName) || appName.Length > 255 || appKey == null
                || !System.Text.RegularExpressions.Regex.IsMatch(appKey, "\\A[0-9a-fA-F]{40}\\z"))
                throw new ArgumentException("TGSP requires an app name and a 40-hex application identifier.");
        }

        public static MindWaveSettings Load()
        {
            var result = new MindWaveSettings();
            var asset = Resources.Load<TextAsset>("MindWaveSettings");
            if (asset != null) JsonUtility.FromJsonOverwrite(asset.text, result);
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "-mindwaveSource") result.source = args[++i];
                else if (args[i] == "-mindwavePort")
                {
                    int port = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    result.simulatorPort = result.connectorPort = port;
                }
            }
            result.Validate();
            return result;
        }
    }
}
