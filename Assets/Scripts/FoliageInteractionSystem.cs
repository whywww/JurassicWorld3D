using System.Collections.Generic;
using UnityEngine;
using System.Runtime.CompilerServices;
using Unity.Profiling;

[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]

namespace JurassicWorld
{
    [DefaultExecutionOrder(1200)]
    public sealed class FoliageInteractionSystem : MonoBehaviour
    {
        public const float MaximumDisplacement = .45f;
        private const float CellSize = 8f;
        [Min(.01f)] public float leafDisplacement = .45f;
        [Min(.01f)] public float branchDisplacement = .20f;
        [Min(.1f)] public float frequency = 2f;
        [Range(.05f, 1f)] public float dampingRatio = .35f;
        public bool interactionsEnabled = true;
        internal static FoliageInteractionSystem Instance { get; private set; }
        private readonly Dictionary<Vector2Int, List<InteractiveFoliage>> cells = new Dictionary<Vector2Int, List<InteractiveFoliage>>();
        private readonly HashSet<InteractiveFoliage> active = new HashSet<InteractiveFoliage>();
        private readonly List<InteractiveFoliage> settled = new List<InteractiveFoliage>();
        private readonly List<InteractiveFoliage> candidates = new List<InteractiveFoliage>();
        private readonly HashSet<InteractiveFoliage> queried = new HashSet<InteractiveFoliage>();
        private bool wasEnabled = true;
        private bool qualityAllowsInteractions = true;
        internal bool ContactSamplingEnabled => interactionsEnabled && qualityAllowsInteractions && isActiveAndEnabled;
        private Camera interactionCamera;
        private float interactionDistanceSquared = float.PositiveInfinity, resetDistanceSquared = float.PositiveInfinity;
        internal int ActiveTreeCount => active.Count;
        private static readonly ProfilerMarker UpdateMarker = new ProfilerMarker("JurassicWorld.FoliageInteraction");

        private void Awake() { Instance = this; }
        private void Start()
        {
            var trees = FindObjectsByType<InteractiveFoliage>(FindObjectsSortMode.None);
            foreach (InteractiveFoliage tree in trees) Register(tree);
            // Reserve once, so the first mass contact does not grow collections inside LateUpdate.
            int count = trees.Length;
            active.EnsureCapacity(count); queried.EnsureCapacity(count);
            candidates.Capacity = count; settled.Capacity = count;
        }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        internal void ApplyQuality(int tier, Camera camera)
        {
            bool allowed = tier != 0 || LowQualityRendering.Stage == LowQualityRendering.OptimizationStage.Baseline;
            if (qualityAllowsInteractions != allowed)
            {
                ClearInteraction();
                qualityAllowsInteractions = allowed;
            }
            interactionCamera = camera;
            float distance = tier >= 2 ? 85f : tier == 1 ? 55f : 35f;
            interactionDistanceSquared = distance * distance;
            resetDistanceSquared = (distance + 10f) * (distance + 10f);
        }

        internal void Register(InteractiveFoliage tree)
        {
            Bounds bounds = tree.WorldBounds;
            Vector2Int min = Cell(bounds.min), max = Cell(bounds.max);
            for (int x = min.x; x <= max.x; x++) for (int z = min.y; z <= max.y; z++)
            {
                Vector2Int key = new Vector2Int(x, z);
                if (!cells.TryGetValue(key, out var bucket)) cells.Add(key, bucket = new List<InteractiveFoliage>());
                if (!bucket.Contains(tree)) bucket.Add(tree);
            }
        }

        private static Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));
        private void LateUpdate() { using (UpdateMarker.Auto()) Step(Time.deltaTime); }

        internal void Step(float deltaTime)
        {
            if (!interactionsEnabled || !qualityAllowsInteractions || !enabled)
            {
                if (wasEnabled) ClearInteraction();
                wasEnabled = false; return;
            }
            if (!wasEnabled)
                foreach (var source in DinosaurFoliageInteractor.Sources) source.ResetHistory();
            wasEnabled = true;
            if (deltaTime <= 0f) return;
            bool useDistanceBudget = interactionCamera != null;
            Vector3 eye = useDistanceBudget ? interactionCamera.transform.position : Vector3.zero;
            foreach (InteractiveFoliage tree in active) tree.ClearForces();
            foreach (DinosaurFoliageInteractor source in DinosaurFoliageInteractor.Sources)
            {
                if (source == null || source.contacts == null) continue;
                foreach (FoliageContact contact in source.contacts)
                {
                    if (!contact.valid) continue;
                    Bounds bounds = contact.SweptBounds; bounds.Expand(MaximumDisplacement * 2f + 3f);
                    Query(bounds);
                    foreach (InteractiveFoliage tree in candidates)
                    {
                        if (tree == null || !tree.isActiveAndEnabled) continue;
                        // Only visual leaf bending is budgeted. Trunk/navigation obstacles are independent.
                        if (useDistanceBudget && tree.WorldBounds.SqrDistance(eye) > interactionDistanceSquared) continue;
                        if (tree.Accumulate(contact)) active.Add(tree);
                    }
                }
            }
            settled.Clear();
            foreach (InteractiveFoliage tree in active)
            {
                if (tree != null && useDistanceBudget && tree.WorldBounds.SqrDistance(eye) > resetDistanceSquared)
                { tree.ResetField(); settled.Add(tree); continue; }
                if (tree == null || !tree.isActiveAndEnabled || !tree.Simulate(deltaTime, this)) settled.Add(tree);
            }
            foreach (InteractiveFoliage tree in settled) active.Remove(tree);
        }

        private void Query(Bounds bounds)
        {
            candidates.Clear(); queried.Clear();
            Vector2Int min = Cell(bounds.min), max = Cell(bounds.max);
            for (int x = min.x; x <= max.x; x++) for (int z = min.y; z <= max.y; z++)
                if (cells.TryGetValue(new Vector2Int(x, z), out var bucket))
                    foreach (InteractiveFoliage tree in bucket)
                        if (tree != null && bounds.Intersects(tree.WorldBounds) && queried.Add(tree)) candidates.Add(tree);
        }

        public void ClearInteraction()
        {
            foreach (InteractiveFoliage tree in active) if (tree != null) tree.ResetField();
            active.Clear();
            foreach (var source in DinosaurFoliageInteractor.Sources) source.ResetHistory();
        }
        private void OnDisable() => ClearInteraction();
    }
}
