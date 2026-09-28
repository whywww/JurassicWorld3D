using System.Collections.Generic;
using UnityEngine;

namespace JurassicWorld
{
    /// <summary>
    /// Phase 1 的六条闭合中心线。各由轻 S 去程、向外错开 7m 的回程和两端
    /// 半径 3.5m 的回转弧组成。出生、行走、标记和净空校验共用此几何。
    /// Vector2 一律表示世界坐标 (x, z)。
    /// </summary>
    public static class MovementCorridor
    {
        public const int MainRoute = 0;
        public const int ForegroundRoute = 1; // 旧调用名；现在表示东侧闭合通道
        public const int MinRouteCount = 6;
        public const float MinRouteLength = 120f;
        public const float MinSwingMeters = 10f;
        public const float TurnRadius = 3.5f;
        public const float ReturnOffset = 7f;
        public const float CenterlineTreeClearance = 4f;
        public static int RouteCount => Routes.Length;

        private const int SamplesPerSegment = 24;
        private const int SamplesPerTurn = 24;
        private static readonly string[] Names =
        {
            "West Loop（西侧）", "East Loop（东侧）", "Outer West（西外侧）",
            "Outer East（东外侧）", "South Traverse（南侧）", "North Traverse（北侧）",
        };

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
            new[]
            {
                new Vector2(-35f, -34f), new Vector2(-35f, -25f),
                new Vector2(-40f, -7f), new Vector2(-33f, 13f),
                new Vector2(-38f, 32f), new Vector2(-34f, 49f), new Vector2(-34f, 58f),
            },
            new[]
            {
                new Vector2(35f, -34f), new Vector2(35f, -25f),
                new Vector2(40f, -7f), new Vector2(33f, 13f),
                new Vector2(38f, 32f), new Vector2(34f, 49f), new Vector2(34f, 58f),
            },
            new[]
            {
                new Vector2(-39f, -39f), new Vector2(-31f, -39f),
                new Vector2(-16f, -36f), new Vector2(0f, -39f),
                new Vector2(16f, -36f), new Vector2(31f, -39f), new Vector2(39f, -39f),
            },
            new[]
            {
                new Vector2(-39f, 58f), new Vector2(-31f, 58f),
                new Vector2(-16f, 61f), new Vector2(0f, 58f),
                new Vector2(16f, 61f), new Vector2(31f, 58f), new Vector2(39f, 58f),
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

        private static readonly RouteData[] Routes = BuildRoutes();

        private static RouteData[] BuildRoutes()
        {
            var routes = new RouteData[Outbound.Length];
            for (int i = 0; i < routes.Length; i++) routes[i] = BuildRoute(i);
            return routes;
        }

        private static RouteData Route(int index)
        {
            if (index < 0 || index >= RouteCount)
                throw new System.ArgumentOutOfRangeException(nameof(index));
            return Routes[index];
        }

        private static RouteData BuildRoute(int route)
        {
            Vector2[] outbound = Outbound[route];
            int outward = route == MainRoute || route == 2 || route == 4 ? -1 : 1;
            Vector2 lateral = route < 4 ? Vector2.right : Vector2.up;
            Vector2 forward = route < 4 ? Vector2.up : Vector2.right;
            var points = new List<Vector2>();
            AppendSpline(points, outbound, false);

            Vector2 north = outbound[outbound.Length - 1];
            Vector2 northCenter = north + lateral * outward * TurnRadius;
            float northStart = outward < 0 ? 0f : Mathf.PI;
            float northEnd = outward < 0 ? Mathf.PI : 0f;
            for (int i = 1; i <= SamplesPerTurn; i++)
            {
                float angle = Mathf.Lerp(northStart, northEnd, (float)i / SamplesPerTurn);
                points.Add(northCenter + lateral * TurnRadius * Mathf.Cos(angle)
                    + forward * TurnRadius * Mathf.Sin(angle));
            }

            var inbound = new Vector2[outbound.Length];
            for (int i = 0; i < outbound.Length; i++)
            {
                Vector2 source = outbound[outbound.Length - 1 - i];
                inbound[i] = source + lateral * outward * ReturnOffset;
            }
            AppendSpline(points, inbound, true);

            Vector2 south = outbound[0];
            Vector2 southCenter = south + lateral * outward * TurnRadius;
            float southStart = outward < 0 ? Mathf.PI : 0f;
            float southEnd = outward < 0 ? 2f * Mathf.PI : -Mathf.PI;
            for (int i = 1; i < SamplesPerTurn; i++)
            {
                float angle = Mathf.Lerp(southStart, southEnd, (float)i / SamplesPerTurn);
                points.Add(southCenter + lateral * TurnRadius * Mathf.Cos(angle)
                    + forward * TurnRadius * Mathf.Sin(angle));
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
            var parts = new string[RouteCount];
            for (int route = 0; route < RouteCount; route++)
                parts[route] = $"{GetRouteName(route)} {Length(route):F1}m";
            return "闭合中心线：" + string.Join("；", parts);
        }
    }

    /// <summary>由不同中心线的近距离采样生成的通行区。同一区域同一时刻只允许一只恐龙进入。</summary>
    public static class MovementCorridorCrossings
    {
        private const float ConflictDistance = 8f;
        private const float ZoneRadius = 11.5f;
        private const float ApproachMargin = 4f;

        private sealed class Zone
        {
            public int RouteA, RouteB;
            public Vector2 Center;
            public float Radius = ZoneRadius;
            public DinosaurAgent Owner;
        }

        private static readonly List<Zone> Zones = BuildZones();
        public static int ZoneCount => Zones.Count;
        public static void GetZone(int index, out int routeA, out int routeB, out Vector2 center,
            out float radius)
        {
            Zone zone = Zones[index];
            routeA = zone.RouteA;
            routeB = zone.RouteB;
            center = zone.Center;
            radius = zone.Radius;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOwners()
        {
            foreach (Zone zone in Zones) zone.Owner = null;
        }

        private static List<Zone> BuildZones()
        {
            var zones = new List<Zone>();
            for (int a = 0; a < MovementCorridor.RouteCount; a++)
            for (int b = a + 1; b < MovementCorridor.RouteCount; b++)
            for (int i = 0; i < MovementCorridor.SampleCount(a); i += 4)
            {
                Vector2 pa = MovementCorridor.GetSample(a, i).Position;
                for (int j = 0; j < MovementCorridor.SampleCount(b); j += 4)
                {
                    Vector2 pb = MovementCorridor.GetSample(b, j).Position;
                    if ((pa - pb).sqrMagnitude > ConflictDistance * ConflictDistance) continue;
                    Vector2 midpoint = (pa + pb) * 0.5f;
                    Zone existing = null;
                    foreach (Zone zone in zones)
                        if (zone.RouteA == a && zone.RouteB == b &&
                            (zone.Center - midpoint).sqrMagnitude < 100f)
                        {
                            existing = zone;
                            break;
                        }
                    if (existing == null)
                        zones.Add(new Zone { RouteA = a, RouteB = b, Center = midpoint });
                }
            }
            bool merged;
            do
            {
                merged = false;
                for (int i = 0; i < zones.Count && !merged; i++)
                for (int j = i + 1; j < zones.Count; j++)
                {
                    Zone first = zones[i], second = zones[j];
                    if (first.RouteA != second.RouteA || first.RouteB != second.RouteB ||
                        Vector2.Distance(first.Center, second.Center) >= first.Radius + second.Radius)
                        continue;
                    Vector2 center = (first.Center + second.Center) * 0.5f;
                    first.Radius = Mathf.Max(Vector2.Distance(center, first.Center) + first.Radius,
                        Vector2.Distance(center, second.Center) + second.Radius);
                    first.Center = center;
                    zones.RemoveAt(j);
                    merged = true;
                    break;
                }
            } while (merged);
            return zones;
        }

        public static bool IsInside(int route, Vector2 position)
        {
            foreach (Zone zone in Zones)
                if ((zone.RouteA == route || zone.RouteB == route) &&
                    (zone.Center - position).sqrMagnitude < zone.Radius * zone.Radius)
                    return true;
            return false;
        }

        public static bool CanAdvance(DinosaurAgent agent, DinosaurAgent[] peers, Vector2 next)
        {
            foreach (Zone zone in Zones)
            {
                if (zone.RouteA != agent.RouteIndex && zone.RouteB != agent.RouteIndex) continue;
                if ((zone.Center - next).sqrMagnitude >= zone.Radius * zone.Radius) continue;
                int otherRoute = zone.RouteA == agent.RouteIndex ? zone.RouteB : zone.RouteA;
                DinosaurAgent peer = null;
                foreach (DinosaurAgent candidate in peers)
                    if (candidate != null && candidate.RouteIndex == otherRoute)
                    {
                        peer = candidate;
                        break;
                    }
                if (peer == null) continue;
                Vector2 peerPosition = new Vector2(peer.transform.position.x, peer.transform.position.z);
                float peerDistance = Vector2.Distance(zone.Center, peerPosition);
                if (zone.Owner != null)
                {
                    Vector2 ownerPosition = new Vector2(zone.Owner.transform.position.x,
                        zone.Owner.transform.position.z);
                    if (Vector2.Distance(zone.Center, ownerPosition) >= zone.Radius)
                        zone.Owner = null;
                }
                if (zone.Owner == null)
                {
                    Vector2 selfPosition = new Vector2(agent.transform.position.x, agent.transform.position.z);
                    if (peerDistance < zone.Radius &&
                        Vector2.Distance(zone.Center, selfPosition) >= zone.Radius)
                        zone.Owner = peer;
                    else if (peerDistance < zone.Radius + ApproachMargin &&
                             peer.CurrentState == DinosaurAgent.DinosaurState.RandomWalk &&
                             peer.LaneIndex < agent.LaneIndex)
                        zone.Owner = peer;
                    else
                        zone.Owner = agent;
                }
                if (zone.Owner != agent) return false;
            }
            return true;
        }
    }
}
