using System;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace JurassicWorld.BCI.MindWave
{
    // Framing and state are separate from sockets, allowing deterministic timeout and split-packet tests.
    public sealed class ThinkGearStream
    {
        [Serializable] private sealed class Sense { public double attention = double.MinValue; }
        [Serializable] private sealed class Packet
        {
            public double poorSignalLevel = double.MinValue;
            public Sense eSense;
            public bool isAuthorized;
        }

        private readonly StringBuilder pending = new StringBuilder();
        private readonly double validityTimeout;
        private bool discarding;
        private int attention = -1, signal = -1;
        private double attentionAt = double.NegativeInfinity, signalAt = double.NegativeInfinity;
        public int Attention => attention;
        public int Signal => signal;
        public int InvalidPackets { get; private set; }
        public bool? Authorization { get; private set; }
        public double AttentionAge(double now) => now - attentionAt;
        public double SignalAge(double now) => now - signalAt;

        public ThinkGearStream(double sampleTimeout = 3) { validityTimeout = sampleTimeout; }

        public void Reset()
        {
            pending.Clear(); discarding = false;
            attention = signal = -1;
            attentionAt = signalAt = double.NegativeInfinity;
            Authorization = null;
        }

        public void Feed(byte[] bytes, int count, double now)
        {
            for (int i = 0; i < count; i++)
            {
                char c = (char)bytes[i]; // Protocol keys and numeric values are ASCII.
                if (c == '\r')
                {
                    if (!discarding && pending.Length > 0) Parse(pending.ToString(), now);
                    pending.Clear(); discarding = false;
                }
                else if (c != '\n' && !discarding)
                {
                    if (pending.Length >= 65536)
                    {
                        pending.Clear(); discarding = true; InvalidPackets++;
                    }
                    else pending.Append(c);
                }
            }
        }

        private void Parse(string json, double now)
        {
            try
            {
                json = json.Trim();
                if (!json.StartsWith("{", StringComparison.Ordinal) || !json.EndsWith("}", StringComparison.Ordinal))
                    throw new ArgumentException("Not a JSON object.");
                var packet = new Packet();
                JsonUtility.FromJsonOverwrite(json, packet);
                if (SignalAge(now) >= validityTimeout) InvalidateAttention();
                if (Regex.IsMatch(json, "\"isAuthorized\"\\s*:\\s*(true|false)"))
                {
                    Authorization = packet.isAuthorized;
                    if (!packet.isAuthorized) InvalidateAttention();
                }
                if (packet.poorSignalLevel != double.MinValue)
                {
                    signal = IsIntegerInRange(packet.poorSignalLevel, 0, 200) ? (int)packet.poorSignalLevel : -1;
                    signalAt = now;
                    // Recovery never resurrects a pre-noise attention value.
                    if (signal != 0) InvalidateAttention();
                }
                if (packet.eSense != null && packet.eSense.attention != double.MinValue)
                {
                    if (IsIntegerInRange(packet.eSense.attention, 1, 100) && signal == 0 && SignalAge(now) < validityTimeout)
                    {
                        attention = (int)packet.eSense.attention;
                        attentionAt = now;
                    }
                    else InvalidateAttention();
                }
            }
            catch (ArgumentException) { InvalidPackets++; }
        }

        private static bool IsIntegerInRange(double value, int min, int max)
            => value >= min && value <= max && value == Math.Truncate(value);

        private void InvalidateAttention() { attention = -1; attentionAt = double.NegativeInfinity; }

        public Vector2 ReadMovement(double now, int threshold, double timeout)
        {
            if (Authorization == false || signal != 0 || attention < 1
                || AttentionAge(now) >= timeout || SignalAge(now) >= timeout) return Vector2.zero;
            return new Vector2(0f, Mathf.Clamp01((attention - threshold) / (float)(100 - threshold)));
        }
    }
}
