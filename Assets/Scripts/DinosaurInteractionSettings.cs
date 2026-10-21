using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld
{
    [DefaultExecutionOrder(-1000)]
    public sealed class DinosaurInteractionSettings : MonoBehaviour
    {
        [InspectorName("允许玩家互动"), Tooltip("仅启动时读取；关闭后保留恐龙所选的日常漫游方式。")]
        public bool allowOffRouteInteraction = true;
        [Tooltip("0 uses a new seed on Play; any other value replays the same spawn layout.")]
        public int spawnSeed;
        [Header("Forest interaction space")]
        [Min(.1f)] public float spaceSearchInterval = .5f;
        [Min(.5f)] public float spaceFailureTimeout = 2f;
        public float[] spaceSearchRadii = { 4f, 8f, 12f, 16f };
        [Min(4)] public int spaceSearchDirections = 8;
        [Min(.5f)] public float safeExitRetryInterval = 2f;
        [Header("Follow exit reservation")]
        [Min(.1f)] public float exitReviewInterval = .5f;
        [Min(2f)] public float exitLookAhead = 8f;
        [Min(.5f)] public float exitSampleSpacing = 2f;
        [Min(.1f)] public float exitRouteDeviation = 1f;
        [Range(1f, 90f)] public float exitHeadingChange = 15f;
        [Min(.05f)] public float exitPlanningBudgetMs = .5f;
        [Min(1)] public int exitPathQueriesPerFrame = 2;
        [Min(1)] public int exitPoseChecksPerFrame = 32;
        public float[] exitAngles = { 0f, 15f, 30f, 45f, 60f, 90f, 135f, 180f };
        [HideInInspector] public NavMeshData navigationData;
        [HideInInspector] public Bounds[] solidObstacles = Array.Empty<Bounds>();
        [HideInInspector] public float navigationRadius;
        [HideInInspector] public int navigationAgentTypeId;
        public bool EnabledAtStartup { get; private set; }
        public bool NavigationReady => navigationInstance.valid;
        private NavMeshDataInstance navigationInstance;
        internal Rect? activityBoundsOverride;
        public string LastPoseObstacle { get; private set; }

        private void Awake() => EnabledAtStartup = allowOffRouteInteraction;
        private void OnEnable()
        {
            if (Application.isPlaying && navigationData != null)
                navigationInstance = NavMesh.AddNavMeshData(navigationData);
        }
        private void OnDisable()
        {
            if (navigationInstance.valid) navigationInstance.Remove();
        }

        public bool PoseBlocked(Vector2 position, Vector2 forward, DinosaurCoreBody body)
            => PoseBlockReason(position, forward, body) != null;

        // Uses the same baked large-rock geometry as movement, including rocks without colliders.
        public bool IsRockSightBlocked(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length <= Mathf.Epsilon) return false;
            var ray = new Ray(from, delta / length);
            foreach (Bounds rock in solidObstacles)
                if (rock.Contains(from) || rock.IntersectRay(ray, out float distance) && distance < length) return true;
            return false;
        }

        public string PoseBlockReason(Vector2 position, Vector2 forward, DinosaurCoreBody body)
        {
            LastPoseObstacle = null;
            Vector2 right = new Vector2(forward.y, -forward.x);
            Rect area = activityBoundsOverride.HasValue ? activityBoundsOverride.Value : WorldBounds.DinosaurActivityXZ;
            foreach (var band in body.bands)
            {
                Bounds b = band.bounds;
                Vector2 center = position + right * b.center.x + forward * b.center.z;
                Vector2 extent = new Vector2(Mathf.Abs(right.x) * b.extents.x + Mathf.Abs(forward.x) * b.extents.z,
                    Mathf.Abs(right.y) * b.extents.x + Mathf.Abs(forward.y) * b.extents.z);
                if (center.x - extent.x < area.xMin || center.x + extent.x > area.xMax
                    || center.y - extent.y < area.yMin || center.y + extent.y > area.yMax) return "Boundary";
            }
            foreach (Bounds obstacle in solidObstacles)
                if (CoreIntersectsSolid(position, forward, body, obstacle))
                {
                    LastPoseObstacle = $"Solid at {obstacle.center:F2}";
                    return "Rock / solid obstacle";
                }
            if (!TerrainTreeObstacleRegistry.IsCoreBlocked(position, forward, body)) return null;
            LastPoseObstacle = TerrainTreeObstacleRegistry.LastCoreObstacle;
            return "Tree";
        }

        internal void CollectStaticOverlaps(Vector2 position, Vector2 forward, DinosaurCoreBody body, HashSet<int> overlaps)
        {
            overlaps.Clear();
            TerrainTreeObstacleRegistry.CollectCoreOverlaps(position, forward, body, overlaps);
            for (int i = 0; i < solidObstacles.Length; i++)
                if (CoreIntersectsSolid(position, forward, body, solidObstacles[i])) overlaps.Add(-i - 1);
        }

        private static bool CoreIntersectsSolid(Vector2 position, Vector2 forward, DinosaurCoreBody body, Bounds obstacle)
        {
            Vector2 right = new Vector2(forward.y, -forward.x);
            foreach (var band in body.bands)
            {
                Bounds b = band.bounds;
                if (obstacle.max.y <= band.layer * .5f || obstacle.min.y >= (band.layer + 1) * .5f) continue;
                Vector2 center = position + right * b.center.x + forward * b.center.z;
                Vector2 extent = new Vector2(Mathf.Abs(right.x) * b.extents.x + Mathf.Abs(forward.x) * b.extents.z,
                    Mathf.Abs(right.y) * b.extents.x + Mathf.Abs(forward.y) * b.extents.z);
                Vector2 delta = new Vector2(obstacle.center.x, obstacle.center.z) - center;
                if (Mathf.Abs(delta.x) >= extent.x + obstacle.extents.x || Mathf.Abs(delta.y) >= extent.y + obstacle.extents.z) continue;
                if (Mathf.Abs(Vector2.Dot(delta, right)) >= b.extents.x + Mathf.Abs(right.x) * obstacle.extents.x + Mathf.Abs(right.y) * obstacle.extents.z) continue;
                if (Mathf.Abs(Vector2.Dot(delta, forward)) >= b.extents.z + Mathf.Abs(forward.x) * obstacle.extents.x + Mathf.Abs(forward.y) * obstacle.extents.z) continue;
                return true;
            }
            return false;
        }

        public bool StepBlocked(Vector2 from, Vector2 to, Vector2 fromForward, Vector2 toForward, DinosaurCoreBody body)
            => StepBlockReason(from, to, fromForward, toForward, body) != null;

        public string StepBlockReason(Vector2 from, Vector2 to, Vector2 fromForward, Vector2 toForward, DinosaurCoreBody body)
        {
            float angle = Vector2.SignedAngle(fromForward, toForward);
            int samples = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / .1f), Mathf.CeilToInt(Mathf.Abs(angle) / 2f));
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples, radians = angle * t * Mathf.Deg2Rad;
                Vector2 heading = new Vector2(fromForward.x * Mathf.Cos(radians) - fromForward.y * Mathf.Sin(radians),
                    fromForward.x * Mathf.Sin(radians) + fromForward.y * Mathf.Cos(radians));
                string reason = PoseBlockReason(Vector2.Lerp(from, to, t), heading, body);
                if (reason != null) return reason;
            }
            return null;
        }
    }
}
