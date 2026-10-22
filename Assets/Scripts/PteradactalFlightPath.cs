using System.Collections.Generic;
using UnityEngine;

namespace JurassicWorld
{
    // Curvature-limited Dubins connections: three analytic straight/arc segments, no waypoint corners.
    public sealed class PteradactalFlightPath
    {
        public struct Segment { public float length, curvature; }
        public readonly Segment[] segments;
        public readonly Vector2 start;
        public readonly float heading, startHeight, endHeight;
        public float Length { get; private set; }

        private PteradactalFlightPath(Vector2 p, float angle, float h0, float h1, float radius, string word, float a, float b, float c)
        {
            start = p; heading = angle; startHeight = h0; endHeight = h1;
            segments = new Segment[3];
            float[] lengths = { a, b, c };
            for (int i = 0; i < 3; i++)
            {
                segments[i] = new Segment { length = lengths[i] * radius, curvature = word[i] == 'L' ? 1f / radius : word[i] == 'R' ? -1f / radius : 0f };
                Length += segments[i].length;
            }
        }

        public void Sample(float distance, out Vector2 position, out float angle, out float height)
        {
            position = start; angle = heading;
            height = Mathf.Lerp(startHeight, endHeight, Mathf.SmoothStep(0f, 1f, Length > .001f ? distance / Length : 1f));
            foreach (var segment in segments)
            {
                float length = Mathf.Min(Mathf.Max(0f, distance), segment.length);
                if (Mathf.Abs(segment.curvature) < .00001f)
                    position += new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * length;
                else
                {
                    float next = angle + segment.curvature * length;
                    position += new Vector2(Mathf.Sin(next) - Mathf.Sin(angle), -Mathf.Cos(next) + Mathf.Cos(angle)) / segment.curvature;
                    angle = next;
                }
                distance -= length;
            }
        }

        public bool IsSafe(PteradactalAirspace airspace)
        {
            Vector3 previous = Vector3.zero; bool first = true;
            // The extra quarter meter covers the gap between half-meter samples.
            for (float s = 0f; s < Length + .5f; s += .5f)
            {
                Sample(Mathf.Min(s, Length), out var p, out _, out float h);
                Vector3 world = airspace.Position(p, h);
                if (!airspace.IsClear(world, airspace.RouteRadius + .25f)) return false;
                if (!first && Mathf.Abs(world.y - previous.y) > .1751f) return false;
                first = false; previous = world;
            }
            return true;
        }

        private static float Mod(float angle) => Mathf.Repeat(angle, Mathf.PI * 2f);

        public static List<PteradactalFlightPath> Connections(Vector2 start, float heading, float startHeight,
            Vector2 end, float endHeading, float endHeight, float radius)
        {
            Vector2 delta = (end - start) / radius;
            float d = delta.magnitude, theta = Mathf.Atan2(delta.y, delta.x);
            float a = Mod(heading - theta), b = Mod(endHeading - theta);
            float sa = Mathf.Sin(a), sb = Mathf.Sin(b), ca = Mathf.Cos(a), cb = Mathf.Cos(b), cab = Mathf.Cos(a - b);
            var result = new List<PteradactalFlightPath>(6);
            void Add(string word, float t, float p, float q) => result.Add(new PteradactalFlightPath(start, heading, startHeight, endHeight, radius, word, Mod(t), p, Mod(q)));
            float p2 = 2 + d * d - 2 * cab + 2 * d * (sa - sb);
            if (p2 >= 0) { float t = Mathf.Atan2(cb - ca, d + sa - sb); Add("LSL", -a + t, Mathf.Sqrt(p2), b - t); }
            p2 = 2 + d * d - 2 * cab + 2 * d * (-sa + sb);
            if (p2 >= 0) { float t = Mathf.Atan2(ca - cb, d - sa + sb); Add("RSR", a - t, Mathf.Sqrt(p2), -b + t); }
            p2 = -2 + d * d + 2 * cab + 2 * d * (sa + sb);
            if (p2 >= 0) { float p = Mathf.Sqrt(p2), t = Mathf.Atan2(-ca - cb, d + sa + sb) - Mathf.Atan2(-2f, p); Add("LSR", -a + t, p, -b + t); }
            p2 = d * d - 2 + 2 * cab - 2 * d * (sa + sb);
            if (p2 >= 0) { float p = Mathf.Sqrt(p2), t = Mathf.Atan2(ca + cb, d - sa - sb) - Mathf.Atan2(2f, p); Add("RSL", a - t, p, b - t); }
            float v = (6 - d * d + 2 * cab + 2 * d * (sa - sb)) / 8f;
            if (Mathf.Abs(v) <= 1) { float p = Mod(2 * Mathf.PI - Mathf.Acos(v)); float t = Mod(a - Mathf.Atan2(ca - cb, d - sa + sb) + p / 2); Add("RLR", t, p, a - b - t + p); }
            v = (6 - d * d + 2 * cab + 2 * d * (-sa + sb)) / 8f;
            if (Mathf.Abs(v) <= 1) { float p = Mod(2 * Mathf.PI - Mathf.Acos(v)); float t = Mod(-a - Mathf.Atan2(ca - cb, d + sa - sb) + p / 2); Add("LRL", t, p, b - a - t + p); }
            result.Sort((x, y) => x.Length.CompareTo(y.Length));
            return result;
        }
    }
}
