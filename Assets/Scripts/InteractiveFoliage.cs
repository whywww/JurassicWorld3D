using System;
using UnityEngine;

namespace JurassicWorld
{
    public sealed class InteractiveFoliage : MonoBehaviour
    {
        public Bounds localBounds;
        public Renderer[] renderers = Array.Empty<Renderer>();
        [Min(.25f)] public float nodeSpacing = 1.5f;
        [Range(2, 16)] public int maximumAxisNodes = 16;
        // Stored by the editor for exact restoration, including embedded prefab materials/meshes.
        [Serializable] public struct OriginalRenderer
        {
            public Renderer renderer;
            public Mesh mesh;
            public Mesh additionalStreams;
            public Material[] materials;
            public int staticFlags;
        }
        public OriginalRenderer[] originals = Array.Empty<OriginalRenderer>();
        public bool convertedTerrainTree;
        public Color instanceColor = Color.white;
        public Color instanceLightmapColor = Color.white;
        internal Bounds WorldBounds { get; private set; }
        internal Vector3[] displacement, velocity;
        private Vector3[] forces, worldNodes;
        private Color[] pixels;
        private Texture3D field;
        private MaterialPropertyBlock block;
        private int nx, ny, nz;
        private float padding;
        private Matrix4x4 rootToWorld, worldToRoot;
        private bool hasRootMatrices;
        private bool initialized;
        private bool fieldActive;
        private Bounds[] restBounds;
        private Mesh[] interactionMeshes;
        private Material[][] interactionMaterials;
        private MaterialPropertyBlock[] restingProperties;
        private float appliedLeaf = float.NaN, appliedBranch = float.NaN;
        private MeshFilter[] filters;
        private SceneTreeObstacle trunk;
        internal Mesh[] InteractionMeshes => interactionMeshes;
        private static readonly int FieldId = Shader.PropertyToID("_FoliageField");
        private static readonly int WorldToRootId = Shader.PropertyToID("_FoliageWorldToRoot");
        private static readonly int MinId = Shader.PropertyToID("_FoliageFieldMin");
        private static readonly int SizeId = Shader.PropertyToID("_FoliageFieldSize");
        private static readonly int DimensionsId = Shader.PropertyToID("_FoliageFieldDimensions");
        private static readonly int LeafId = Shader.PropertyToID("_LeafDisplacement");
        private static readonly int BranchId = Shader.PropertyToID("_BranchDisplacement");

        private void Awake() => PrepareRuntime();

        // Also used by isolated editor previews without invoking Unity lifecycle messages.
        internal void PrepareRuntime()
        {
            // The spatial index and WorldBounds already assume a static plant root.
            rootToWorld = transform.localToWorldMatrix; worldToRoot = transform.worldToLocalMatrix;
            hasRootMatrices = true;
            Vector3 min = transform.TransformPoint(localBounds.min);
            // Bounds is a value type: mutating the property getter only changes a temporary copy.
            Bounds worldBounds = new Bounds(min, Vector3.zero);
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                worldBounds.Encapsulate(transform.TransformPoint(new Vector3(x == 0 ? localBounds.min.x : localBounds.max.x,
                    y == 0 ? localBounds.min.y : localBounds.max.y, z == 0 ? localBounds.min.z : localBounds.max.z)));
            worldBounds.Expand(FoliageInteractionSystem.MaximumDisplacement * 2f);
            WorldBounds = worldBounds;
            trunk = GetComponent<SceneTreeObstacle>();
            if (trunk != null)
            {
                foreach (var section in trunk.trunkSections)
                {
                    Vector3 centre = transform.position + new Vector3(section.center.x, 0f, section.center.y);
                    centre.y = (section.bottom + section.top) * .5f;
                    worldBounds.Encapsulate(new Bounds(centre, new Vector3(section.radius * 2f,
                        section.top - section.bottom, section.radius * 2f)));
                }
                worldBounds.Expand(.3f);
                WorldBounds = worldBounds;
            }
            block = new MaterialPropertyBlock();
            restBounds = new Bounds[renderers.Length];
            filters = new MeshFilter[renderers.Length];
            interactionMeshes = new Mesh[renderers.Length];
            interactionMaterials = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                filters[i] = renderer.GetComponent<MeshFilter>();
                interactionMeshes[i] = ((MeshRenderer)renderer).additionalVertexStreams;
                interactionMaterials[i] = renderer.sharedMaterials;
                if (i < originals.Length)
                {
                    filters[i].sharedMesh = originals[i].mesh;
                    ((MeshRenderer)renderer).additionalVertexStreams = originals[i].additionalStreams;
                    renderer.sharedMaterials = originals[i].materials;
                }
                restBounds[i] = renderer.localBounds;
            }
            if (convertedTerrainTree)
                foreach (Renderer renderer in renderers)
                {
                    if (renderer == null) continue;
                    renderer.GetPropertyBlock(block);
                    block.SetColor("_TreeInstanceColor", instanceColor);
                    block.SetColor("_TreeInstanceLightmapColor", instanceLightmapColor);
                    renderer.SetPropertyBlock(block);
                }
        }

        // Lazy: distant or untouched trees never allocate/upload a displacement texture.
        private void Initialize()
        {
            if (initialized) return;
            if (!hasRootMatrices)
            {
                rootToWorld = transform.localToWorldMatrix; worldToRoot = transform.worldToLocalMatrix;
                hasRootMatrices = true;
            }
            Vector3 scale = transform.lossyScale;
            nx = Nodes(localBounds.size.x * Mathf.Abs(scale.x));
            ny = Nodes(localBounds.size.y * Mathf.Abs(scale.y));
            nz = Nodes(localBounds.size.z * Mathf.Abs(scale.z));
            int length = nx * ny * nz;
            displacement = new Vector3[length]; velocity = new Vector3[length]; forces = new Vector3[length];
            worldNodes = new Vector3[length]; pixels = new Color[length];
            Vector3 step = new Vector3(localBounds.size.x / (nx - 1), localBounds.size.y / (ny - 1), localBounds.size.z / (nz - 1));
            for (int z = 0; z < nz; z++) for (int y = 0; y < ny; y++) for (int x = 0; x < nx; x++)
                worldNodes[x + nx * (y + ny * z)] = rootToWorld.MultiplyPoint3x4(localBounds.min + Vector3.Scale(step, new Vector3(x, y, z)));
            padding = Mathf.Min(1.5f, Vector3.Scale(step, scale).magnitude * .5f);
            field = new Texture3D(nx, ny, nz, TextureFormat.RGBAHalf, false)
            { name = name + " Contact Field", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            initialized = true;
        }
        private int Nodes(float metres) => Mathf.Clamp(Mathf.CeilToInt(metres / nodeSpacing) + 1, 2, maximumAxisNodes);

        private void Bind()
        {
            if (restingProperties == null) restingProperties = new MaterialPropertyBlock[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                if (restingProperties[i] == null) restingProperties[i] = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(restingProperties[i]);
                renderer.sharedMaterials = interactionMaterials[i];
                ((MeshRenderer)renderer).additionalVertexStreams = interactionMeshes[i];
                Bounds bounds = restBounds[i];
                Vector3 scale = renderer.transform.lossyScale;
                float minScale = Mathf.Max(.001f, Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                bounds.Expand(FoliageInteractionSystem.MaximumDisplacement * 2f / minScale);
                renderer.localBounds = bounds;
                renderer.GetPropertyBlock(block);
                block.SetTexture(FieldId, field);
                block.SetMatrix(WorldToRootId, transform.worldToLocalMatrix);
                block.SetVector(MinId, localBounds.min);
                block.SetVector(SizeId, new Vector3(Mathf.Max(.001f, localBounds.size.x), Mathf.Max(.001f, localBounds.size.y), Mathf.Max(.001f, localBounds.size.z)));
                block.SetVector(DimensionsId, new Vector3(nx, ny, nz));
                block.SetFloat("_FoliageActive", 1f);
                renderer.SetPropertyBlock(block);
            }
            fieldActive = true;
            appliedLeaf = appliedBranch = float.NaN;
        }

        internal void ClearForces() { if (forces != null) Array.Clear(forces, 0, forces.Length); }
        internal bool Accumulate(FoliageContact contact)
        {
            Initialize();
            var pushField = contact.PreparePush();
            bool touched = false;
            // A body can hit the rigid stem below the leaves. Transfer that contact to
            // the flexible crown; the trunk mask and movement obstacle stay anchored.
            Vector3 trunkPush = Vector3.zero;
            if (trunk != null)
                foreach (var section in trunk.trunkSections)
                {
                    float bottom = Mathf.Max(section.bottom, contact.SweptBounds.min.y);
                    float top = Mathf.Min(section.top, contact.SweptBounds.max.y);
                    if (bottom > top) continue;
                    int samples = Mathf.Max(1, Mathf.CeilToInt((top - bottom) / .5f));
                    for (int sample = 0; sample <= samples; sample++)
                    {
                        Vector3 point = transform.position + new Vector3(section.center.x, 0f, section.center.y);
                        point.y = Mathf.Lerp(bottom, top, (float)sample / samples);
                        Vector3 push = pushField.Push(point, section.radius + .15f);
                        push.y = 0f;
                        if (push.sqrMagnitude > trunkPush.sqrMagnitude) trunkPush = push;
                    }
                }
            if (trunkPush.sqrMagnitude > 1e-8f)
            {
                for (int i = 0; i < forces.Length; i++) forces[i] += trunkPush;
                touched = true;
            }
            Bounds sweep = contact.SweptBounds; sweep.Expand(padding * 2f);
            Bounds local = new Bounds(worldToRoot.MultiplyPoint3x4(sweep.min), Vector3.zero);
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                local.Encapsulate(worldToRoot.MultiplyPoint3x4(new Vector3(x == 0 ? sweep.min.x : sweep.max.x,
                    y == 0 ? sweep.min.y : sweep.max.y, z == 0 ? sweep.min.z : sweep.max.z)));
            Vector3 min = localBounds.min, size = localBounds.size;
            Vector3Int lo = new Vector3Int(NodeIndex(local.min.x, min.x, size.x, nx, false), NodeIndex(local.min.y, min.y, size.y, ny, false), NodeIndex(local.min.z, min.z, size.z, nz, false));
            Vector3Int hi = new Vector3Int(NodeIndex(local.max.x, min.x, size.x, nx, true), NodeIndex(local.max.y, min.y, size.y, ny, true), NodeIndex(local.max.z, min.z, size.z, nz, true));
            for (int z = lo.z; z <= hi.z; z++) for (int y = lo.y; y <= hi.y; y++) for (int x = lo.x; x <= hi.x; x++)
            {
                int i = x + nx * (y + ny * z);
                Vector3 push = pushField.Push(worldNodes[i], padding);
                forces[i] += push; touched |= push.sqrMagnitude > 1e-8f;
            }
            if (touched && !fieldActive) Bind();
            return touched;
        }
        private static int NodeIndex(float value, float min, float size, int count, bool upper)
        {
            float index = (value - min) / Mathf.Max(.001f, size) * (count - 1);
            return Mathf.Clamp(upper ? Mathf.CeilToInt(index) : Mathf.FloorToInt(index), 0, count - 1);
        }

        internal bool Simulate(float dt, FoliageInteractionSystem settings)
        {
            if (!initialized) return false;
            float omega = 2f * Mathf.PI * settings.frequency;
            // Fixed upper integration step; clamp stalled frames rather than explode the spring.
            dt = Mathf.Min(dt, .1f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / (1f / 120f)));
            // Avoid a feedback loop: a slow frame must not multiply spring work indefinitely.
            // Preserve the authored 30/60/144 FPS steps. Higher frequencies retain finer integration.
            int budget = Mathf.Max(4, Mathf.CeilToInt(dt * settings.frequency * 16f));
            steps = Mathf.Min(steps, budget);
            float step = dt / steps, maximum = Mathf.Min(settings.leafDisplacement, FoliageInteractionSystem.MaximumDisplacement);
            float stiffness = omega * omega, damping = 2f * settings.dampingRatio * omega;
            bool moving = false;
            for (int i = 0; i < displacement.Length; i++)
            {
                // Untouched grid nodes are exactly at rest; avoid integrating them every frame.
                if (forces[i].sqrMagnitude == 0f && displacement[i].sqrMagnitude == 0f && velocity[i].sqrMagnitude == 0f) continue;
                Vector3 target = Vector3.ClampMagnitude(forces[i], 1f) * maximum;
                FoliageSpringSolver.Integrate(ref displacement[i], ref velocity[i], target, steps, step, stiffness, damping, maximum);
                moving |= target.sqrMagnitude > 1e-8f || displacement[i].sqrMagnitude > 1e-6f || velocity[i].sqrMagnitude > 1e-5f;
                pixels[i] = new Color(displacement[i].x, displacement[i].y, displacement[i].z, 0f);
            }
            if (!moving) { ResetField(); return false; }
            field.SetPixels(pixels); field.Apply(false, false);
            float branch = Mathf.Min(settings.branchDisplacement, maximum);
            if (appliedLeaf != maximum || appliedBranch != branch)
            {
                foreach (Renderer renderer in renderers)
                {
                    if (renderer == null) continue;
                    renderer.GetPropertyBlock(block);
                    block.SetFloat(LeafId, maximum);
                    block.SetFloat(BranchId, branch);
                    renderer.SetPropertyBlock(block);
                }
                appliedLeaf = maximum; appliedBranch = branch;
            }
            return true;
        }

        public void ResetField()
        {
            if (!initialized) return;
            Array.Clear(displacement, 0, displacement.Length); Array.Clear(velocity, 0, velocity.Length); ClearForces();
            Array.Clear(pixels, 0, pixels.Length); field.SetPixels(pixels); field.Apply(false, false);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                // Leaving interaction uniforms/textures in an otherwise empty block prevents instancing.
                if (fieldActive && restingProperties[i] != null)
                    renderer.SetPropertyBlock(restingProperties[i].isEmpty ? null : restingProperties[i]);
                if (i < originals.Length)
                {
                    filters[i].sharedMesh = originals[i].mesh;
                    ((MeshRenderer)renderer).additionalVertexStreams = originals[i].additionalStreams;
                    renderer.sharedMaterials = originals[i].materials;
                }
                renderer.localBounds = restBounds[i];
            }
            fieldActive = false;
        }
        private void OnDisable() { if (Application.isPlaying) ResetField(); }
        private void OnDestroy()
        {
            if (field == null) return;
            if (Application.isPlaying) Destroy(field); else DestroyImmediate(field);
        }
    }
}
