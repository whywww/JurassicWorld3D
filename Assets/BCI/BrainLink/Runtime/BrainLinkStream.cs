using System;
using System.Text;
using UnityEngine;

namespace JurassicWorld.BCI.BrainLink
{
    // Complete callback snapshots only. Missing fields must never become implicit zeroes.
    public sealed class BrainLinkStream
    {
        [Serializable] private sealed class Packet
        {
            public double attention = double.MinValue;
            public double signal = double.MinValue;
            public string mode;
        }

        private readonly StringBuilder pending = new StringBuilder();
        private bool discarding;
        private double sampleAt = double.NegativeInfinity;
        public int Attention { get; private set; } = -1;
        public int Signal { get; private set; } = -1;
        public string Mode { get; private set; } = "unknown";
        public int InvalidPackets { get; private set; }
        public double Age(double now) => now - sampleAt;

        public void Reset()
        {
            pending.Clear(); discarding = false;
            Invalidate(); Mode = "unknown";
        }

        private void Invalidate()
        {
            Attention = Signal = -1;
            sampleAt = double.NegativeInfinity;
        }

        public void Feed(byte[] bytes, int count, double now)
        {
            for (int i = 0; i < count; i++)
            {
                char c = (char)bytes[i];
                if (c == '\n')
                {
                    if (!discarding && pending.Length > 0) Parse(pending.ToString(), now);
                    pending.Clear(); discarding = false;
                }
                else if (c != '\r' && !discarding)
                {
                    if (pending.Length >= 16384)
                    { pending.Clear(); discarding = true; InvalidPackets++; Invalidate(); }
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
                    throw new ArgumentException("Not an object");
                var packet = new Packet();
                JsonUtility.FromJsonOverwrite(json, packet);
                if (!IntegerInRange(packet.attention, 0, 100) || !IntegerInRange(packet.signal, 0, 200)
                    || (packet.mode != "simulate" && packet.mode != "serial"))
                    throw new ArgumentException("Not a complete EEG callback");
                Attention = (int)packet.attention; Signal = (int)packet.signal;
                Mode = packet.mode; sampleAt = now;
            }
            catch (ArgumentException) { InvalidPackets++; Invalidate(); }
        }

        private static bool IntegerInRange(double value, int min, int max)
            => value >= min && value <= max && value == Math.Truncate(value);

        public Vector2 ReadMovement(double now, int threshold, double timeout)
        {
            if (Signal != 0 || Attention < 1 || Age(now) >= timeout) return Vector2.zero;
            return new Vector2(0f, Mathf.Clamp01((Attention - threshold) / (float)(100 - threshold)));
        }
    }
}
