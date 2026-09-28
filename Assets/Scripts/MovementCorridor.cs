using System.Collections.Generic;
using UnityEngine;

namespace JurassicWorld
{
    /// <summary>
    /// Phase 1 的两条闭合中心线。西侧与东侧各由轻 S 去程、向外错开 7m 的回程和两端
    /// 半径 3.5m 的回转弧组成。出生、行走、标记和净空校验共用此几何。
    /// Vector2 一律表示世界坐标 (x, z)。
    /// </summary>
    public static class MovementCorridor
    {
        public const int MainRoute = 0;
        public const int ForegroundRoute = 1; // 旧调用名；现在表示东侧闭合通道
        public const int MinRouteCount = 2;
        public const float MinSwingMeters = 10f;
        public const float TurnRadius = 3.5f;
        public const float ReturnOffset = 7f;
        public const float CenterlineTreeClearance = 4f;
        public static int RouteCount => Routes.Length;

        private const int SamplesPerSegment = 24;
        private const int SamplesPerTurn = 24;
        private static readonly string[] Names = { "West Loop（西侧）", "East Loop（东侧）" };

        // 首尾竖向切线接回转弧；中间的横向摆动保持轻 S。
        private static readonly Vector2[][] Outbound =
        {
            new[]
            {
                new Vector2(-13f, -27f), new Vector2(-13f, -20f),
                new Vector2(-18f, -8f), new Vector2(-10f, 10f),
                new Vector2(-15f, 27f), new Vector2(-11f, 37f), new Vector2(-11f, 44f),
            },
            new[]
            {
                new Vector2(13f, -27f), new Vector2(13f, -20f),
                new Vector2(18f, -8f), new Vector2(10f, 10f),
                new Vector2(15f, 27f), new Vector2(11f, 37f), new Vector2(11f, 44f),
            },
        };

        public struct CorridorSample
        {
            public Vector2 Position;
            public Vector2 Tangent;
            public float Distance;
            public Vector2 LeftNormal => new Vector2(-Tangent.y, Tangent.x);
        }

        private sealed class RouteData
        {
            public CorridorSample[] Samples;
            public float Length;
        }

        private static readonly RouteData[] Routes = { BuildRoute(0), BuildRoute(1) };

        private static RouteData Route(int index)
        {
            if (index < 0 || index >= RouteCount)
                throw new System.ArgumentOutOfRangeException(nameof(index));
            return Routes[index];
        }

        private static RouteData BuildRoute(int route)
        {
            Vector2[] outbound = Outbound[route];
            int outward = route == MainRoute ? -1 : 1;
            var points = new List<Vector2>();
            AppendSpline(points, outbound, false);

            Vector2 north = outbound[outbound.Length - 1];
            float northCenterX = north.x + outward * TurnRadius;
            float northStart = outward < 0 ? 0f : Mathf.PI;
            float northEnd = outward < 0 ? Mathf.PI : 0f;
            for (int i = 1; i <= SamplesPerTurn; i++)
            {
                float angle = Mathf.Lerp(northStart, northEnd, (float)i / SamplesPerTurn);
                points.Add(new Vector2(northCenterX + TurnRadius * Mathf.Cos(angle),
                    north.y + TurnRadius * Mathf.Sin(angle)));
            }

            var inbound = new Vector2[outbound.Length];
            for (int i = 0; i < outbound.Length; i++)
            {
                Vector2 source = outbound[outbound.Length - 1 - i];
                inbound[i] = new Vector2(source.x + outward * ReturnOffset, source.y);
            }
            AppendSpline(points, inbound, true);

            Vector2 south = outbound[0];
            float southCenterX = south.x + outward * TurnRadius;
            float southStart = outward < 0 ? Mathf.PI : 0f;
            float southEnd = outward < 0 ? 2f * Mathf.PI : -Mathf.PI;
            for (int i = 1; i < SamplesPerTurn; i++)
            {
                float angle = Mathf.Lerp(southStart, southEnd, (float)i / SamplesPerTurn);
                points.Add(new Vector2(southCenterX + TurnRadius * Mathf.Cos(angle),
                    south.y + TurnRadius * Mathf.Sin(angle)));
            }

            var samples = new CorridorSample[points.Count];
            float distance = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                if (i > 0) distance += Vector2.Distance(points[i - 1], points[i]);
                Vector2 tangent = (points[(i + 1) % points.Count]
                    - points[(i - 1 + points.Count) % points.Count]).normalized;
                samples[i] = new CorridorSample
                {
                    Position = points[i], Tangent = tangent, Distance = distance,
                };
            }
            return new RouteData
            {
                Samples = samples,
                Length = distance + Vector2.Distance(points[points.Count - 1], points[0]),
            };
        }

        private static void AppendSpline(List<Vector2> output, Vector2[] controls, bool skipFirst)
        {
            for (int i = 0; i < controls.Length - 1; i++)
            {
                Vector2 p0 = controls[Mathf.Max(0, i - 1)];
                Vector2 p1 = controls[i];
                Vector2 p2 = controls[i + 1];
                Vector2 p3 = controls[Mathf.Min(controls.Length - 1, i + 2)];
                for (int s = 0; s < SamplesPerSegment; s++)
                {
                    if (skipFirst && i == 0 && s == 0) continue;
                    output.Add(CatmullRom(p0, p1, p2, p3, (float)s / SamplesPerSegment));
                }
            }
            output.Add(controls[controls.Length - 1]);
        }

        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * p1) + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        public static string GetRouteName(int route) { Route(route); return Names[route]; }
        public static float Length(int route) => Route(route).Length;
        public static int SampleCount(int route) => Route(route).Samples.Length;
        public static CorridorSample GetSample(int route, int index) => Route(route).Samples[index];

        public static void GetSampleAt(int route, float distance, out Vector2 position,
            out Vector2 tangent)
        {
            RouteData data = Route(route);
            CorridorSample[] samples = data.Samples;
            float wrapped = Mathf.Repeat(distance, data.Length);
            int lo = 0;
            int hi = samples.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (samples[mid].Distance <= wrapped) lo = mid;
                else hi = mid - 1;
            }
            int next = (lo + 1) % samples.Length;
            float end = next == 0 ? data.Length : samples[next].Distance;
            float span = end - samples[lo].Distance;
            float u = span > 1e-6f ? (wrapped - samples[lo].Distance) / span : 0f;
            position = Vector2.Lerp(samples[lo].Position, samples[next].Position, u);
            tangent = Vector2.Lerp(samples[lo].Tangent, samples[next].Tangent, u).normalized;
        }

        // 兼容尚未启用的旧行为调用；半宽恒为零，不再定义走廊边界。
        public static void GetSampleAt(int route, float distance, out Vector2 position,
            out Vector2 tangent, out float halfLeft, out float halfRight)
        {
            GetSampleAt(route, distance, out position, out tangent);
            halfLeft = 0f;
            halfRight = 0f;
        }

        public static Vector2 PointAt(int route, float distance)
        {
            GetSampleAt(route, distance, out Vector2 position, out _);
            return position;
        }

        // 旧签名中的横向参数不再影响位置，所有落点严格位于中心线。
        public static Vector2 PointAt(int route, float distance, float lateral) => PointAt(route, distance);
        public static Vector2 ClampLateral(int route, float distance, float lateral, float margin)
            => PointAt(route, distance);

        public static void ProjectToCorridor(int route, Vector2 xz, out float distance,
            out float lateral)
        {
            CorridorSample[] samples = Route(route).Samples;
            float routeLength = Length(route);
            float bestSquared = float.MaxValue;
            distance = 0f;
            lateral = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                int next = (i + 1) % samples.Length;
                Vector2 start = samples[i].Position;
                Vector2 delta = samples[next].Position - start;
                float spanSquared = delta.sqrMagnitude;
                if (spanSquared < 1e-10f) continue;
                float t = Mathf.Clamp01(Vector2.Dot(xz - start, delta) / spanSquared);
                Vector2 closest = start + delta * t;
                float squared = (xz - closest).sqrMagnitude;
                if (squared >= bestSquared) continue;
                bestSquared = squared;
                float segmentLength = Mathf.Sqrt(spanSquared);
                distance = samples[i].Distance + segmentLength * t;
                if (distance >= routeLength) distance = 0f;
                Vector2 tangent = delta / segmentLength;
                lateral = Vector2.Dot(xz - closest, new Vector2(-tangent.y, tangent.x));
            }
        }

        public static void ProjectUnclamped(int route, Vector2 xz, out float distance,
            out float lateral) => ProjectToCorridor(route, xz, out distance, out lateral);
        public static float MinDistanceToRoute(Vector2 xz, int route)
        {
            ProjectToCorridor(route, xz, out float distance, out _);
            return Vector2.Distance(xz, PointAt(route, distance));
        }
        public static float SignedDistanceToCorridor(int route, float x, float z)
            => MinDistanceToRoute(new Vector2(x, z), route);
        public static bool IsInsideCorridor(int route, float x, float z, float margin)
            => SignedDistanceToCorridor(route, x, z) <= margin;

        // 旧岔口入口保留为无连接状态；Phase 1 的两条路线不换轨。
        public static int JunctionCount => 0;
        public static int FindJunction(Vector2 xz, float radius) => -1;
        public static int JunctionRouteCount(int index) => 0;
        public static int GetJunctionRoute(int index, int k)
            => throw new System.InvalidOperationException("Phase 1 routes have no junctions.");

        public struct CorridorMetrics
        {
            public Rect CenterlineBounds;
            public Rect OuterBounds;
            public float SwingX;
            public float SpanZ;
            public float SeamGap;
            public float SeamTurnDegrees;
        }

        public static CorridorMetrics Measure(int route)
        {
            CorridorSample[] samples = Route(route).Samples;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < samples.Length; i++)
            {
                minX = Mathf.Min(minX, samples[i].Position.x);
                maxX = Mathf.Max(maxX, samples[i].Position.x);
                minZ = Mathf.Min(minZ, samples[i].Position.y);
                maxZ = Mathf.Max(maxZ, samples[i].Position.y);
            }
            Rect bounds = Rect.MinMaxRect(minX, minZ, maxX, maxZ);
            return new CorridorMetrics
            {
                CenterlineBounds = bounds, OuterBounds = bounds,
                SwingX = maxX - minX, SpanZ = maxZ - minZ,
                SeamGap = Vector2.Distance(samples[samples.Length - 1].Position, samples[0].Position),
                SeamTurnDegrees = Vector2.Angle(samples[samples.Length - 1].Tangent, samples[0].Tangent),
            };
        }

        public static string Describe()
        {
            return $"闭合中心线：西侧 {Length(MainRoute):F1}m，东侧 {Length(ForegroundRoute):F1}m";
        }
    }
}
