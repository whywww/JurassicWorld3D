using System.Collections.Generic;
using UnityEngine;

namespace JurassicWorld
{
    /// <summary>
    /// 固定地图种子生成的六条连续闭合环线。
    /// 出生、行走、地表土路、标记和净空校验共用此几何。
    /// Vector2 一律表示世界坐标 (x, z)。
    /// </summary>
    public static class MovementCorridor
    {
        public const int MainRoute = 0;
        public const int ForegroundRoute = 1; // 旧调用名；现在表示东侧闭合通道
        public const int MinRouteCount = 6;
        public const float MinRouteLength = 60f;
        public const float MinSwingMeters = 10f;
        public const float CenterlineTreeClearance = 4f;
        public static int RouteCount => Routes.Length;

        private const int SamplesPerSegment = 48;
        public const int LayoutSeed = 271828;
        private static readonly string[] Names =
        {
            "West Meander（西侧曲环）", "East Grove（东侧短环）",
            "South Pocket（南侧小环）", "North Sweep（北侧长环）",
            "Northeast Loop（东北斜环）", "Southeast Loop（东南斜环）",
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
            public bool Closed;
        }

        private static readonly RouteData[] Routes = BuildRoutes(LayoutSeed);

        private static RouteData[] BuildRoutes(int seed)
        {
            var random = new System.Random(seed);
            var routes = new RouteData[MinRouteCount];
            Vector2[] centers =
            {
                new Vector2(-25f, 8f), new Vector2(26f, 35f),
                new Vector2(-21f, -21f), new Vector2(-15f, 49f),
            };
            Vector2[] radii =
            {
                new Vector2(19f, 36f), new Vector2(17f, 24f),
                new Vector2(18f, 14f), new Vector2(26f, 13f),
            };
            for (int route = 0; route < 4; route++)
            {
                Vector2 center = centers[route] + new Vector2(Range(random, -3f, 3f), Range(random, -3f, 3f));
                float rotation = Range(random, -.3f, .3f);
                float phase = Range(random, 0f, 2f * Mathf.PI);
                int count = random.Next(7, 11);
                var controls = new Vector2[count];
                for (int i = 0; i < count; i++)
                {
                    float angle = phase + 2f * Mathf.PI * i / count;
                    float scale = Range(random, .84f, 1.08f);
                    Vector2 local = new Vector2(Mathf.Cos(angle) * radii[route].x,
                        Mathf.Sin(angle) * radii[route].y) * scale;
                    controls[i] = center + new Vector2(local.x * Mathf.Cos(rotation) - local.y * Mathf.Sin(rotation),
                        local.x * Mathf.Sin(rotation) + local.y * Mathf.Cos(rotation));
                }
                routes[route] = BuildRoute(controls, true);
            }
            for (int route = 4; route < routes.Length; route++)
            {
                Vector2 start = route == 4 ? new Vector2(-34f, -20f) : new Vector2(-34f, 53f);
                Vector2 end = route == 4 ? new Vector2(31f, 52f) : new Vector2(31f, -21f);
                start += new Vector2(Range(random, -2f, 2f), Range(random, -2f, 2f));
                end += new Vector2(Range(random, -2f, 2f), Range(random, -2f, 2f));
                Vector2 forward = (end - start).normalized;
                Vector2 normal = new Vector2(-forward.y, forward.x);
                float radius = Range(random, 9f, 12f);
                var controls = new List<Vector2>();
                // Separate outgoing and returning sides, with broad semicircular bends.
                for (int i = 0; i <= 4; i++)
                    controls.Add(Vector2.Lerp(start, end, i / 4f)
                        + normal * (radius + (i == 0 || i == 4 ? 0f : Range(random, -2f, 2f))));
                for (int i = 1; i <= 4; i++)
                {
                    float angle = Mathf.PI * i / 4f;
                    controls.Add(end + normal * radius * Mathf.Cos(angle) + forward * radius * Mathf.Sin(angle));
                }
                for (int i = 3; i >= 0; i--)
                    controls.Add(Vector2.Lerp(start, end, i / 4f)
                        - normal * (radius + (i == 0 ? 0f : Range(random, -2f, 2f))));
                for (int i = 1; i < 4; i++)
                {
                    float angle = Mathf.PI + Mathf.PI * i / 4f;
                    controls.Add(start + normal * radius * Mathf.Cos(angle) + forward * radius * Mathf.Sin(angle));
                }
                routes[route] = BuildRoute(controls.ToArray(), true);
            }
            return routes;
        }

        private static float Range(System.Random random, float min, float max)
            => Mathf.Lerp(min, max, (float)random.NextDouble());

        private static RouteData Route(int index)
        {
            if (index < 0 || index >= RouteCount)
                throw new System.ArgumentOutOfRangeException(nameof(index));
            return Routes[index];
        }

        private static RouteData BuildRoute(Vector2[] controls, bool closed)
        {
            var points = new List<Vector2>();
            int segments = closed ? controls.Length : controls.Length - 1;
            for (int i = 0; i < segments; i++)
            {
                Vector2 p0 = controls[closed ? (i - 1 + controls.Length) % controls.Length : Mathf.Max(0, i - 1)];
                Vector2 p1 = controls[i];
                Vector2 p2 = controls[(i + 1) % controls.Length];
                Vector2 p3 = controls[closed ? (i + 2) % controls.Length : Mathf.Min(controls.Length - 1, i + 2)];
                for (int s = 0; s < SamplesPerSegment; s++)
                    points.Add(CatmullRom(p0, p1, p2, p3, (float)s / SamplesPerSegment));
            }
            if (!closed) points.Add(controls[controls.Length - 1]);

            var samples = new CorridorSample[points.Count];
            float distance = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                if (i > 0) distance += Vector2.Distance(points[i - 1], points[i]);
                int previous = closed ? (i - 1 + points.Count) % points.Count : Mathf.Max(0, i - 1);
                int next = closed ? (i + 1) % points.Count : Mathf.Min(points.Count - 1, i + 1);
                samples[i] = new CorridorSample
                {
                    Position = points[i], Tangent = (points[next] - points[previous]).normalized, Distance = distance,
                };
            }
            return new RouteData
            {
                Samples = samples, Closed = closed,
                Length = distance + (closed ? Vector2.Distance(points[points.Count - 1], points[0]) : 0f),
            };
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
        public static bool IsClosed(int route) => Route(route).Closed;
        public static float TravelDistance(int route, float distance)
        {
            float length = Length(route);
            return Mathf.Repeat(distance, length);
        }
        public static float Length(int route) => Route(route).Length;
        public static int SampleCount(int route) => Route(route).Samples.Length;
        public static CorridorSample GetSample(int route, int index) => Route(route).Samples[index];

        public static void GetSampleAt(int route, float distance, out Vector2 position,
            out Vector2 tangent)
        {
            RouteData data = Route(route);
            CorridorSample[] samples = data.Samples;
            float wrapped = data.Closed ? Mathf.Repeat(distance, data.Length) : Mathf.Clamp(distance, 0f, data.Length);
            int lo = 0;
            int hi = samples.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (samples[mid].Distance <= wrapped) lo = mid;
                else hi = mid - 1;
            }
            int next = data.Closed ? (lo + 1) % samples.Length : Mathf.Min(lo + 1, samples.Length - 1);
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
            for (int i = 0; i < samples.Length - (IsClosed(route) ? 0 : 1); i++)
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
                if (IsClosed(route) && distance >= routeLength) distance = 0f;
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

        // 交叉处保持各自的路线，不换轨。
        public static int JunctionCount => 0;
        public static int FindJunction(Vector2 xz, float radius) => -1;
        public static int JunctionRouteCount(int index) => 0;
        public static int GetJunctionRoute(int index, int k)
            => throw new System.InvalidOperationException("Routes have no switching junctions.");

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
            return "不规则中心线：" + string.Join("；", parts);
        }
    }

    /// <summary>由不同中心线的近距离采样生成的通行区。同一区域同一时刻只允许一只恐龙进入。</summary>
    public static class MovementCorridorCrossings
    {
        private const float ZoneRadius = 11.5f;
        private const float WaitingClearance = 0.5f;

        private sealed class CrossingGate
        {
            public int RouteA, RouteB;
            public DinosaurAgent Owner;
        }

        private sealed class Zone
        {
            public int RouteA, RouteB;
            public Vector2 Center;
            public float Radius = ZoneRadius;
            public CrossingGate Gate;
        }

        private static readonly List<Zone> Zones = BuildZones();
        private static readonly CrossingGate[] Gates = CollectGates();
        private static readonly List<CrossingGate> RequestedGates = new List<CrossingGate>();
        private static DinosaurAgent trafficOwner;

        private static CrossingGate[] CollectGates()
        {
            var gates = new List<CrossingGate>();
            foreach (Zone zone in Zones)
                if (!gates.Contains(zone.Gate)) gates.Add(zone.Gate);
            return gates.ToArray();
        }
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
            trafficOwner = null;
            foreach (Zone zone in Zones) zone.Gate.Owner = null;
        }

        private static List<Zone> BuildZones()
        {
            var zones = new List<Zone>();
            for (int a = 0; a < MovementCorridor.RouteCount; a++)
            for (int b = a + 1; b < MovementCorridor.RouteCount; b++)
            {
                var gate = new CrossingGate { RouteA = a, RouteB = b };
                for (int i = 0; i < MovementCorridor.SampleCount(a); i += 4)
                for (int j = 0; j < MovementCorridor.SampleCount(b); j += 4)
                {
                    Vector2 pa = MovementCorridor.GetSample(a, i).Position;
                    Vector2 pb = MovementCorridor.GetSample(b, j).Position;
                    // Include close approaches as well as exact intersections. This keeps a
                    // waiting dinosaur clear of the other route even at an acute crossing.
                    if ((pa - pb).sqrMagnitude > 64f) continue;
                    Vector2 center = (pa + pb) * .5f;
                    if (zones.Exists(zone => zone.Gate == gate && (zone.Center - center).sqrMagnitude < 100f))
                        continue;
                    zones.Add(new Zone { RouteA = a, RouteB = b, Center = center, Gate = gate });
                }
            }
            // Share right of way across a pair's local conflict regions, without expanding
            // their circles into a giant area that can swallow the entire map.
            return zones;
        }

        // A waiting root must also be clear of every point the owner can traverse.
        // Circles alone can leave it within collision range at an acute crossing.
        private static bool IsProtected(CrossingGate gate, DinosaurAgent agent,
            DinosaurAgent peer, Vector2 position)
        {
            int otherRoute = gate.RouteA == agent.RouteIndex ? gate.RouteB : gate.RouteA;
            return IsConflictPosition(agent.RouteIndex, position, otherRoute,
                agent.CollisionRadius + peer.CollisionRadius);
        }

        private static bool IsConflictPosition(int route, Vector2 position,
            int otherRoute, float combinedRadius)
            // Debug circles can cover almost an entire short loop. Reserving them
            // prevents its owner from ever releasing a gate, starving other routes.
            // Reserve only where a stopped root could obstruct the peer's path.
            => MovementCorridor.MinDistanceToRoute(position, otherRoute)
                < combinedRadius + WaitingClearance;

        // Root-to-root spawn spacing is insufficient: a stationary occupant can
        // obstruct the owner's exit much later. At most one may start inside
        // the protected region shared by their routes.
        public static bool CanSpawnTogether(int route, Vector2 position, float radius,
            int peerRoute, Vector2 peerPosition, float peerRadius)
        {
            float combinedRadius = radius + peerRadius;
            return Vector2.Distance(position, peerPosition) >= combinedRadius
                && !(IsConflictPosition(route, position, peerRoute, combinedRadius)
                    && IsConflictPosition(peerRoute, peerPosition, route, combinedRadius));
        }

        public static bool IsInside(int route, Vector2 position)
            => IsInside(route, position, -1);

        public static bool IsInside(int route, Vector2 position, int otherRoute)
        {
            foreach (Zone zone in Zones)
                if ((zone.RouteA == route || zone.RouteB == route) &&
                    (otherRoute < 0 || zone.RouteA == otherRoute || zone.RouteB == otherRoute) &&
                    (zone.Center - position).sqrMagnitude < zone.Radius * zone.Radius)
                    return true;
            return false;
        }

        public static bool CanAdvance(DinosaurAgent agent, DinosaurAgent[] peers, Vector2 next)
        {
            int peerCount = 0;
            foreach (var peer in peers) if (peer != null && peer != agent) peerCount++;
            if (peerCount >= 2) return CanAdvanceTrafficGroup(agent, peers, next);
            // Check each shared gate once, then reserve atomically after all checks pass.
            RequestedGates.Clear();
            foreach (CrossingGate gate in Gates)
            {
                if (gate.RouteA != agent.RouteIndex && gate.RouteB != agent.RouteIndex) continue;
                int otherRoute = gate.RouteA == agent.RouteIndex ? gate.RouteB : gate.RouteA;
                DinosaurAgent peer = null;
                foreach (DinosaurAgent candidate in peers)
                    if (candidate != null && candidate != agent && candidate.RouteIndex == otherRoute)
                    {
                        peer = candidate;
                        break;
                    }
                if (peer == null) continue;
                Vector2 peerPosition = new Vector2(peer.transform.position.x, peer.transform.position.z);
                if (gate.Owner != null)
                {
                    DinosaurAgent owner = gate.Owner;
                    Vector2 ownerPosition = new Vector2(owner.transform.position.x,
                        owner.transform.position.z);
                    if (!owner.isActiveAndEnabled || (owner != agent && owner != peer)
                        || !IsProtected(gate, owner, owner == agent ? peer : agent, ownerPosition))
                        gate.Owner = null;
                }
                if (!IsProtected(gate, agent, peer, next)) continue;
                if (gate.Owner == null)
                {
                    Vector2 selfPosition = new Vector2(agent.transform.position.x, agent.transform.position.z);
                    // Occupancy takes precedence. An approaching peer cannot preempt
                    // an agent already inside, irrespective of name or lane index.
                    if (IsProtected(gate, peer, agent, peerPosition)
                        && !IsProtected(gate, agent, peer, selfPosition))
                        gate.Owner = peer;
                }
                if (gate.Owner != null && gate.Owner != agent) return false;
                RequestedGates.Add(gate);
            }
            // Reserve all overlapping zones only after every zone grants passage.
            // A waiting dinosaur must not hold one zone while requesting another.
            foreach (CrossingGate gate in RequestedGates) gate.Owner = agent;
            return true;
        }

        private static bool InTrafficRegion(DinosaurAgent agent, DinosaurAgent[] peers, Vector2 point, DinosaurAgent extraPeer = null)
        {
            foreach (var peer in peers)
                if (peer != null && peer != agent && peer.RouteIndex != agent.RouteIndex
                    && IsConflictPosition(agent.RouteIndex, point, peer.RouteIndex, agent.CollisionRadius + peer.CollisionRadius)) return true;
            return extraPeer != null && extraPeer != agent && extraPeer.RouteIndex != agent.RouteIndex
                && IsConflictPosition(agent.RouteIndex, point, extraPeer.RouteIndex, agent.CollisionRadius + extraPeer.CollisionRadius);
        }

        private static bool CanAdvanceTrafficGroup(DinosaurAgent agent, DinosaurAgent[] peers, Vector2 next)
        {
            // One lease across the connected traffic group prevents three agents
            // from occupying separate pair gates and blocking each other's exits.
            if (trafficOwner != null)
            {
                Vector2 ownerPoint = new Vector2(trafficOwner.transform.position.x, trafficOwner.transform.position.z);
                if (!trafficOwner.isActiveAndEnabled || !InTrafficRegion(trafficOwner, peers, ownerPoint, agent)) trafficOwner = null;
            }
            if (!InTrafficRegion(agent, peers, next)) return true;
            if (trafficOwner == null) trafficOwner = agent;
            return trafficOwner == agent;
        }

        public static bool ShouldKeepMoving(DinosaurAgent agent, Vector2 point)
            => trafficOwner == agent || IsInside(agent.RouteIndex, point);

    }
}
