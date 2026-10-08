using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace JurassicWorld.Editor
{
    // Follow the model's length/width and route heading. Foliage is not a solid obstacle.
    internal sealed class RouteObstacleClearance
    {
        internal const float SafetyMargin = 1f, MaxRelocation = 12f;
        private const float CellSize = 8f;
        internal const float HeightLayer = .5f;
        private readonly Terrain terrain;
        private readonly Dictionary<Vector2Int, List<Sweep>> cells = new Dictionary<Vector2Int, List<Sweep>>();
        private readonly Dictionary<Vector2Int, List<Vector2>> routeCells = new Dictionary<Vector2Int, List<Vector2>>();
        private readonly List<Obstacle> obstacles = new List<Obstacle>();
        private readonly Dictionary<Mesh, MeshGeometry> meshCache = new Dictionary<Mesh, MeshGeometry>();
        private readonly TreeInstance[] trees;
        private float samplePadding, bodyBottom = float.MaxValue, bodyTop = float.MinValue;

        private sealed class Sweep { internal Vector2 Center, Right, Forward, Extents; internal int Layer; internal bool Core; }
        private sealed class BodyEnvelope { internal bool Core; internal Bounds Full; internal readonly Dictionary<int, Bounds> Layers = new Dictionary<int, Bounds>(); }
        private static readonly Dictionary<string, BodyEnvelope> BodyCache = new Dictionary<string, BodyEnvelope>();
        private sealed class Obstacle
        {
            internal Transform Target, Companion;
            internal bool IsTree;
            internal int TreeIndex = -1;
            internal Vector2 Position, Planned;
            internal float RuntimeTrunkRadius, Reach;
            internal string Name;
            internal readonly Dictionary<int, List<Vector2>> StemPoints = new Dictionary<int, List<Vector2>>();
            internal readonly List<Vector2[]> Shapes = new List<Vector2[]>();
            internal readonly Dictionary<Vector2[], int> ShapeLayers = new Dictionary<Vector2[], int>();
            internal readonly List<TreeTrunkSection> Sections = new List<TreeTrunkSection>();
        }
        private sealed class MeshGeometry { internal Vector3[] Vertices; internal int[][] Indices; }

        private RouteObstacleClearance(Terrain source, bool collectObstacles = true)
        {
            terrain = source; trees = source.terrainData.treeInstances;
            var bodies = new List<BodyEnvelope>();
            foreach (DinosaurAgent agent in UnityEngine.Object.FindObjectsOfType<DinosaurAgent>())
            {
                if (!agent.isActiveAndEnabled || !new SerializedObject(agent).FindProperty("groundObstacleAware").boolValue) continue;
                BodyEnvelope envelope = MeasureBody(agent); bodies.Add(envelope);
                BodyEnvelope core = MeasureBody(agent, true); bodies.Add(core);
                BakeCoreProfile(agent, core);
                Bounds bounds = envelope.Full;
                bodyBottom = Mathf.Min(bodyBottom, agent.MovementHeight + bounds.min.y - SafetyMargin);
                bodyTop = Mathf.Max(bodyTop, agent.MovementHeight + bounds.max.y + SafetyMargin);
            }
            if (bodies.Count == 0) throw new InvalidOperationException("No enabled dinosaur model for clearance.");
            for (int route = 0; route < MovementCorridor.RouteCount; route++)
            for (int i = 0; i < MovementCorridor.SampleCount(route); i++)
            {
                var sample = MovementCorridor.GetSample(route, i);
                var next = MovementCorridor.GetSample(route, (i + 1) % MovementCorridor.SampleCount(route));
                var previous = MovementCorridor.GetSample(route, (i + MovementCorridor.SampleCount(route) - 1) % MovementCorridor.SampleCount(route));
                float gap = Mathf.Max(Vector2.Distance(sample.Position, next.Position), Vector2.Distance(sample.Position, previous.Position)) * .5f;
                samplePadding = Mathf.Max(samplePadding, gap);
                var cell = Cell(sample.Position);
                if (!routeCells.TryGetValue(cell, out var points)) routeCells.Add(cell, points = new List<Vector2>());
                points.Add(sample.Position);
                Vector2 forward = sample.Tangent, right = new Vector2(forward.y, -forward.x);
                float angle = Mathf.Max(Vector2.Angle(forward, next.Tangent), Vector2.Angle(forward, previous.Tangent)) * Mathf.Deg2Rad;
                foreach (BodyEnvelope envelope in bodies)
                foreach (var layer in envelope.Layers)
                {
                    Bounds body = layer.Value;
                    // Cover translation and rotation between adjacent samples, including the seam.
                    float radius = Mathf.Max(new Vector2(body.min.x, body.min.z).magnitude, new Vector2(body.max.x, body.max.z).magnitude);
                    float padding = (envelope.Core ? 0f : SafetyMargin) + gap + radius * angle;
                    var sweep = new Sweep { Center = sample.Position + right * body.center.x + forward * body.center.z,
                        Right = right, Forward = forward, Extents = new Vector2(body.extents.x + padding, body.extents.z + padding), Layer = layer.Key, Core = envelope.Core };
                    float reach = sweep.Extents.magnitude;
                    AddSweep(sweep, sweep.Center - Vector2.one * reach, sweep.Center + Vector2.one * reach);
                }
            }
            if (collectObstacles) CollectObstacles();
        }

        internal static void BakeCoreProfile(DinosaurAgent agent)
        {
            BodyCache.Clear();
            BakeCoreProfile(agent, MeasureBody(agent, true));
        }

        private static void BakeCoreProfile(DinosaurAgent agent, BodyEnvelope core)
        {
            var profile = agent.GetComponent<DinosaurCoreBody>() ?? agent.gameObject.AddComponent<DinosaurCoreBody>();
            profile.bands = core.Layers.OrderBy(l => l.Key).Select(l => new DinosaurCoreBody.Band { layer = l.Key, bounds = l.Value }).ToArray();
            profile.coreBones = CoreBones(agent);
            EditorUtility.SetDirty(profile);
        }

        private static BodyEnvelope MeasureBody(DinosaurAgent agent, bool core = false)
        {
            string key = agent.GetInstanceID() + "|" + agent.transform.lossyScale + "|" + agent.MovementHeight + "|" + agent.VisualYaw + "|" + core;
            if (BodyCache.TryGetValue(key, out var cached)) return cached;
            var loader = agent.GetComponent<DinosaurFbxLoader>();
            if (loader == null || loader.ModelRoot == null) throw new InvalidOperationException("Missing baked model: " + agent.name);
            var holder = new GameObject("Route body measurement") { hideFlags = HideFlags.HideAndDontSave };
            holder.transform.localScale = agent.transform.lossyScale;
            GameObject model = UnityEngine.Object.Instantiate(loader.ModelRoot, holder.transform, false);
            model.hideFlags = HideFlags.HideAndDontSave;
            Bounds bounds = new Bounds(); bool first = true;
            var envelope = new BodyEnvelope { Core = core };
            float margin = core ? 0f : SafetyMargin;
            string[] coreBones = core ? CoreBones(agent) : Array.Empty<string>();
            Quaternion visualCorrection = Quaternion.Euler(0f, -agent.VisualYaw, 0f);
            Action measure = () =>
            {
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh = null; bool baked = false;
                    if (renderer is SkinnedMeshRenderer skin) { mesh = new Mesh(); skin.BakeMesh(mesh); baked = true; }
                    else mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) continue;
                    float[] weights = null;
                    if (core)
                    {
                        var coreSkin = renderer as SkinnedMeshRenderer;
                        if (coreSkin == null) { if (baked) UnityEngine.Object.DestroyImmediate(mesh); continue; }
                        bool[] selectedBones = coreSkin.bones.Select(b => coreBones.Contains(b.name)).ToArray();
                        weights = coreSkin.sharedMesh.boneWeights.Select(w =>
                            (selectedBones[w.boneIndex0] ? w.weight0 : 0f) + (selectedBones[w.boneIndex1] ? w.weight1 : 0f) +
                            (selectedBones[w.boneIndex2] ? w.weight2 : 0f) + (selectedBones[w.boneIndex3] ? w.weight3 : 0f)).ToArray();
                        if (weights.Length != mesh.vertexCount || !selectedBones.Any(b => b)) throw new InvalidOperationException("Core skin mapping missing: " + agent.name);
                    }
                    MeshGeometry geometry = ReadMesh(mesh);
                    var world = new Vector3[geometry.Vertices.Length];
                    for (int v = 0; v < world.Length; v++)
                    {
                        Vector3 point = visualCorrection * renderer.transform.TransformPoint(geometry.Vertices[v]);
                        world[v] = point + Vector3.up * agent.MovementHeight;
                        if (!core) { if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point); }
                    }
                    foreach (int[] indices in geometry.Indices)
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                    {
                        var triangle = new List<Vector3> { world[indices[i]], world[indices[i + 1]], world[indices[i + 2]] };
                        if (core)
                        {
                            triangle = ClipCore(triangle, new[] { weights[indices[i]], weights[indices[i + 1]], weights[indices[i + 2]] });
                            if (triangle.Count == 0) continue;
                            foreach (Vector3 v in triangle) { Vector3 point = v - Vector3.up * agent.MovementHeight; if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point); }
                        }
                        int bottom = Mathf.FloorToInt((triangle.Min(p => p.y) - margin) / HeightLayer);
                        int top = Mathf.FloorToInt((triangle.Max(p => p.y) + margin) / HeightLayer);
                        for (int layer = bottom; layer <= top; layer++)
                        {
                            var clipped = ClipHeight(ClipHeight(triangle, layer * HeightLayer - margin, true), (layer + 1) * HeightLayer + margin, false);
                            foreach (Vector3 point in clipped)
                            {
                                if (!envelope.Layers.TryGetValue(layer, out var band)) band = new Bounds(point, Vector3.zero);
                                else band.Encapsulate(point);
                                envelope.Layers[layer] = band;
                            }
                        }
                    }
                    if (baked) UnityEngine.Object.DestroyImmediate(mesh);
                }
            };
            try
            {
                measure();
                var animation = model.GetComponentInChildren<Animation>(true);
                string hint = new SerializedObject(loader).FindProperty("moveAnimationHint").stringValue;
                if (animation == null) throw new InvalidOperationException("Missing movement animation: " + agent.name);
                AnimationClip selected = null, rootMotion = null, firstClip = null;
                string[] hints = { hint, "run", "walk", "fly", "animation" };
                foreach (AnimationState state in animation)
                {
                    if (firstClip == null) firstClip = state.clip;
                    if (!hints.Any(value => !string.IsNullOrEmpty(value) && state.name.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                    if (state.name.IndexOf("_RM", StringComparison.OrdinalIgnoreCase) >= 0) { if (rootMotion == null) rootMotion = state.clip; continue; }
                    selected = state.clip; break;
                }
                selected = selected ?? rootMotion ?? firstClip;
                if (selected == null) throw new InvalidOperationException("Missing movement clip " + hint + ": " + agent.name);
                // Match DinosaurAgent.FindClip: unused turn/root-motion clips must not widen the corridor.
                for (int frame = 0; frame < 16; frame++) { selected.SampleAnimation(animation.gameObject, selected.length * frame / 16f); measure(); }
                if (first) throw new InvalidOperationException("Empty model geometry: " + agent.name);
                envelope.Full = bounds; BodyCache[key] = envelope;
                Debug.Log($"ROUTE_BODY_ENVELOPE {agent.name}: center={bounds.center} size={bounds.size}; clip={selected.name}, 16 poses, heightLayers={envelope.Layers.Count}, layer={HeightLayer}m, margin={margin}m, core={core}.");
                return envelope;
            }
            finally { UnityEngine.Object.DestroyImmediate(holder); }
        }

        private static string[] CoreBones(DinosaurAgent agent)
        {
            if (agent.name.Contains("Tyrannosaurus")) return new[] { "jt_Spine1_C", "jt_Spine2_C", "jt_Hips_C", "jt_Hip_R", "jt_Hip_L" };
            if (agent.name.Contains("Velociraptor")) return new[] { "Root", "Spine_1", "Spine_2", "Spine_3", "Pelvis.R", "Pelvis.L", "Hip.R", "Hip.L", "Breast.R", "Breast.L" };
            if (agent.name == "Stegosaurus") return new[] { "Root", "Spine1", "Spine2", "Spine3", "Pelvis", "Hip.L", "Hip.R", "Shoulder.L", "Shoulder.R", "Gut" };
            throw new InvalidOperationException("Explicit core bone mapping required for " + agent.name);
        }
        private static List<Vector3> ClipCore(List<Vector3> points, float[] weights)
        {
            var result = new List<Vector3>();
            for (int i = 0; i < points.Count; i++)
            {
                int next = (i + 1) % points.Count;
                if (weights[i] >= .5f) result.Add(points[i]);
                if ((weights[i] >= .5f) != (weights[next] >= .5f)) result.Add(Vector3.Lerp(points[i], points[next], (.5f - weights[i]) / (weights[next] - weights[i])));
            }
            return result;
        }
        private static MeshGeometry ReadMesh(Mesh mesh)
        {
            using (var data = MeshUtility.AcquireReadOnlyMeshData(mesh))
            {
                var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp);
                data[0].GetVertices(vertices);
                var geometry = new MeshGeometry { Vertices = vertices.ToArray(), Indices = new int[data[0].subMeshCount][] };
                vertices.Dispose();
                for (int i = 0; i < geometry.Indices.Length; i++)
                {
                    var indices = new NativeArray<int>(data[0].GetSubMesh(i).indexCount, Allocator.Temp);
                    data[0].GetIndices(indices, i); geometry.Indices[i] = indices.ToArray(); indices.Dispose();
                }
                return geometry;
            }
        }
        private static Vector2Int Cell(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.y / CellSize));
        private void AddSweep(Sweep sweep, Vector2 min, Vector2 max)
        {
            Vector2Int a = Cell(min), b = Cell(max);
            for (int x = a.x; x <= b.x; x++) for (int z = a.y; z <= b.y; z++)
            {
                var key = new Vector2Int(x, z);
                if (!cells.TryGetValue(key, out var values)) cells.Add(key, values = new List<Sweep>());
                values.Add(sweep);
            }
        }

        private static bool IsWood(Material material)
        {
            if (material == null) return false;
            string text = (material.name + " " + material.shader.name).ToLowerInvariant();
            if (text.Contains("leaf") || text.Contains("leaves") || text.Contains("foliage") || text.Contains("cutout")
                || text.Contains("transparent") || material.IsKeywordEnabled("_ALPHATEST_ON")) return false;
            return text.Contains("bark") || text.Contains("trunk") || text.Contains("wood") || text.Contains("stem")
                || text.Contains("branch") || material.name == "Banana_tiglajzdy";
        }

        private void AddMeshes(Obstacle item, Transform root, Matrix4x4 rootMatrix, bool woodOnly)
        {
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled || filter.sharedMesh == null) continue;
                var lod = filter.GetComponentInParent<LODGroup>();
                if (lod != null && lod.GetLODs().Length > 0 && !lod.GetLODs()[0].renderers.Contains(renderer)) continue;
                Mesh mesh = filter.sharedMesh;
                if (!meshCache.TryGetValue(mesh, out var geometry)) meshCache.Add(mesh, geometry = ReadMesh(mesh));
                Matrix4x4 matrix = rootMatrix * root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Material[] materials = renderer.sharedMaterials;
                for (int submesh = 0; submesh < geometry.Indices.Length; submesh++)
                {
                    if (woodOnly && (submesh >= materials.Length || !IsWood(materials[submesh]))) continue;
                    var layerPoints = new Dictionary<int, List<Vector2>>();
                    int[] indices = geometry.Indices[submesh];
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                    {
                        var triangle = new List<Vector3>(3);
                        for (int j = 0; j < 3; j++) triangle.Add(matrix.MultiplyPoint3x4(geometry.Vertices[indices[i + j]]));
                        int bottom = Mathf.Max(Mathf.FloorToInt(bodyBottom / HeightLayer), Mathf.FloorToInt(triangle.Min(p => p.y) / HeightLayer));
                        int top = Mathf.Min(Mathf.FloorToInt(bodyTop / HeightLayer), Mathf.FloorToInt(triangle.Max(p => p.y) / HeightLayer));
                        for (int layer = bottom; layer <= top; layer++)
                        {
                            var clipped = ClipHeight(ClipHeight(triangle, layer * HeightLayer, true), (layer + 1) * HeightLayer, false);
                            if (!layerPoints.TryGetValue(layer, out var points)) layerPoints[layer] = points = new List<Vector2>();
                            foreach (Vector3 point in clipped) points.Add(new Vector2(point.x, point.z) - item.Position);
                        }
                    }
                    foreach (var layer in layerPoints)
                    {
                        if (!item.StemPoints.TryGetValue(layer.Key, out var stemPoints)) item.StemPoints[layer.Key] = stemPoints = new List<Vector2>();
                        stemPoints.AddRange(layer.Value);
                        Vector2[] hull = Hull(layer.Value);
                        if (hull.Length >= 3) { item.Shapes.Add(hull); item.ShapeLayers[hull] = layer.Key; }
                    }
                }
            }
        }
        private static List<Vector3> ClipHeight(List<Vector3> polygon, float height, bool above)
        {
            var result = new List<Vector3>();
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector3 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                bool insideA = above ? a.y >= height : a.y <= height, insideB = above ? b.y >= height : b.y <= height;
                if (insideA) result.Add(a);
                if (insideA != insideB) result.Add(Vector3.Lerp(a, b, (height - a.y) / (b.y - a.y)));
            }
            return result;
        }
        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        private static Vector2[] Hull(List<Vector2> points)
        {
            var sorted = points.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToArray();
            if (sorted.Length < 3) return sorted;
            var hull = new List<Vector2>();
            foreach (Vector2 point in sorted)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], point - hull[hull.Count - 1]) <= 0f) hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }
            int lower = hull.Count;
            for (int i = sorted.Length - 2; i >= 0; i--)
            {
                while (hull.Count > lower && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], sorted[i] - hull[hull.Count - 1]) <= 0f) hull.RemoveAt(hull.Count - 1);
                hull.Add(sorted[i]);
            }
            hull.RemoveAt(hull.Count - 1); return hull.ToArray();
        }

        // Central woody column only: lateral branches cannot expand this footprint.
        private TreeTrunkSection[] AddStemSections(Obstacle item, Transform root, Matrix4x4 matrix, float radius)
        {
            var wood = new Obstacle { Position = item.Position };
            AddMeshes(wood, root, matrix, true);
            Vector3 origin = matrix.MultiplyPoint3x4(Vector3.zero);
            Vector2 center = new Vector2(origin.x, origin.z) - item.Position;
            var sections = new List<TreeTrunkSection>();
            foreach (var layer in wood.StemPoints)
            {
                // Clip the wood section to the central stem column. Hulls include branches, so
                // only central-column vertices define a stem radius, capped at its authored width.
                var points = layer.Value.Where(p => (p - center).sqrMagnitude <= radius * radius * 2.25f).ToArray();
                float r = points.Length > 0 ? Mathf.Min(radius, points.Max(p => (p - center).magnitude)) : radius;
                r = Mathf.Max(.04f, r);
                var section = new TreeTrunkSection { center = center, radius = r, bottom = layer.Key * HeightLayer, top = (layer.Key + 1) * HeightLayer };
                sections.Add(section);
                int before = item.Shapes.Count; AddCircle(item, center, r);
                item.ShapeLayers[item.Shapes[before]] = layer.Key;
            }
            if (sections.Count == 0)
                for (int l = Mathf.FloorToInt(origin.y / HeightLayer); l < Mathf.CeilToInt(bodyTop / HeightLayer); l++)
                {
                    sections.Add(new TreeTrunkSection { center = center, radius = radius, bottom = l * HeightLayer, top = (l + 1) * HeightLayer });
                    int before = item.Shapes.Count; AddCircle(item, center, radius); item.ShapeLayers[item.Shapes[before]] = l;
                }
            item.Sections.AddRange(sections);
            var marker = root.GetComponent<SceneTreeObstacle>();
            if (marker != null && root.gameObject.scene.IsValid()) marker.trunkSections = sections.ToArray();
            return sections.ToArray();
        }
        private static void AddCircle(Obstacle item, Vector2 center, float radius)
        {
            if (radius <= 0f) return;
            var polygon = new Vector2[16];
            for (int i = 0; i < polygon.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / polygon.Length;
                polygon[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius / Mathf.Cos(Mathf.PI / polygon.Length);
            }
            item.Shapes.Add(polygon);
        }

        private void CollectObstacles()
        {
            TreePrototype[] prototypes = terrain.terrainData.treePrototypes;
            for (int i = 0; i < trees.Length; i++)
            {
                TreeInstance tree = trees[i]; if (tree.widthScale <= 0f) continue;
                GameObject prefab = prototypes[tree.prototypeIndex].prefab;
                if (prefab == null) throw new InvalidOperationException("Missing terrain tree prototype.");
                Vector3 world = terrain.transform.TransformPoint(Vector3.Scale(tree.position, terrain.terrainData.size));
                var item = new Obstacle { IsTree = true, TreeIndex = i, Position = new Vector2(world.x, world.z), RuntimeTrunkRadius = .7f * tree.widthScale, Name = "Terrain tree " + i };
                Matrix4x4 matrix = Matrix4x4.TRS(world, Quaternion.Euler(0f, tree.rotation * Mathf.Rad2Deg, 0f) * prefab.transform.localRotation,
                    Vector3.Scale(prefab.transform.localScale, new Vector3(tree.widthScale, tree.heightScale, tree.widthScale)));
                AddStemSections(item, prefab.transform, matrix, item.RuntimeTrunkRadius); FinishObstacle(item);
            }
            foreach (SceneTreeObstacle tree in UnityEngine.Object.FindObjectsOfType<SceneTreeObstacle>())
            {
                var item = new Obstacle { IsTree = true, Target = tree.transform, Position = new Vector2(tree.transform.position.x, tree.transform.position.z),
                    RuntimeTrunkRadius = tree.Radius, Name = tree.name };
                AddStemSections(item, tree.transform, tree.transform.localToWorldMatrix, tree.Radius); FinishObstacle(item);
            }
            GameObject props = GameObject.Find("Environment Props");
            if (props != null) foreach (Transform child in props.transform)
                if (child.name.StartsWith("Rock_", StringComparison.Ordinal)) AddRock(child,
                    child.name == "Rock_Large_00" ? GameObject.Find(VegetationIslandSeeder.RootName + "/C_RightRock")?.transform : null);
            foreach (string path in new[] { "Understory Pass/Rocks", "Ground Pass Rocks", "Ground Ecology" })
            {
                GameObject root = GameObject.Find(path); if (root == null) continue;
                foreach (Transform child in root.transform)
                    if (path != "Ground Ecology" || child.name == "Small moss rock") AddRock(child, null);
            }
        }
        private void AddRock(Transform target, Transform companion)
        {
            if (!target.gameObject.activeInHierarchy) return;
            var item = new Obstacle { Target = target, Companion = companion, Position = new Vector2(target.position.x, target.position.z), Name = target.name };
            // Planting follows the rock, but does not enlarge its obstacle geometry.
            AddMeshes(item, target, target.localToWorldMatrix, false);
            FinishObstacle(item);
        }
        private void FinishObstacle(Obstacle item)
        {
            if (item.Shapes.Count == 0) throw new InvalidOperationException("Missing solid obstacle geometry: " + item.Name);
            foreach (var shape in item.Shapes) foreach (var p in shape) item.Reach = Mathf.Max(item.Reach, p.magnitude);
            obstacles.Add(item);
        }

        private static bool Overlaps(Sweep sweep, Vector2[] polygon, Vector2 origin)
        {
            var points = new Vector2[polygon.Length];
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 delta = polygon[i] + origin - sweep.Center;
                points[i] = new Vector2(Vector2.Dot(delta, sweep.Right), Vector2.Dot(delta, sweep.Forward));
                minX = Mathf.Min(minX, points[i].x); maxX = Mathf.Max(maxX, points[i].x);
                minZ = Mathf.Min(minZ, points[i].y); maxZ = Mathf.Max(maxZ, points[i].y);
            }
            if (minX >= sweep.Extents.x || maxX <= -sweep.Extents.x || minZ >= sweep.Extents.y || maxZ <= -sweep.Extents.y) return false;
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 edge = points[(i + 1) % points.Length] - points[i], axis = new Vector2(-edge.y, edge.x);
                float min = float.MaxValue, max = float.MinValue;
                foreach (Vector2 p in points) { float dot = Vector2.Dot(p, axis); min = Mathf.Min(min, dot); max = Mathf.Max(max, dot); }
                float extent = Mathf.Abs(axis.x) * sweep.Extents.x + Mathf.Abs(axis.y) * sweep.Extents.y;
                if (min >= extent || max <= -extent) return false;
            }
            return true;
        }
        private bool IsClear(Obstacle item, Vector2 position)
        {
            Vector2Int a = Cell(position - Vector2.one * item.Reach), b = Cell(position + Vector2.one * item.Reach);
            var visited = new HashSet<Sweep>();
            for (int x = a.x; x <= b.x; x++) for (int z = a.y; z <= b.y; z++)
            {
                if (!cells.TryGetValue(new Vector2Int(x, z), out var sweeps)) continue;
                foreach (Sweep sweep in sweeps)
                {
                    if (sweep.Core != item.IsTree || !visited.Add(sweep)) continue;
                    if (item.IsTree)
                    {
                        foreach (var section in item.Sections)
                        {
                            if (section.top <= sweep.Layer * HeightLayer || section.bottom >= (sweep.Layer + 1) * HeightLayer) continue;
                            Vector2 delta = section.center + position - sweep.Center;
                            float dx = Mathf.Max(Mathf.Abs(Vector2.Dot(delta, sweep.Right)) - sweep.Extents.x, 0f);
                            float dz = Mathf.Max(Mathf.Abs(Vector2.Dot(delta, sweep.Forward)) - sweep.Extents.y, 0f);
                            if (dx * dx + dz * dz < section.radius * section.radius) return false;
                        }
                    }
                    else foreach (Vector2[] shape in item.Shapes)
                        if ((!item.ShapeLayers.TryGetValue(shape, out int layer) || layer == sweep.Layer) && Overlaps(sweep, shape, position)) return false;
                }
            }
            return true;
        }
        private Vector2 FindPlacement(Obstacle item, List<Vector2> occupied, int ownIndex)
        {
            if (IsClear(item, item.Position)) return item.Position;
            Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
            for (float distance = .25f; distance <= MaxRelocation; distance += .25f)
            for (int step = 0; step < 64; step++)
            {
                float angle = step * 2f * Mathf.PI / 64f;
                Vector2 p = item.Position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                if (p.x < origin.x + 2f || p.x > origin.x + size.x - 2f || p.y < origin.z + 2f || p.y > origin.z + size.z - 2f
                    || MainCreekIntegration.NearCreek(p.x, p.y, 6f)) continue;
                if (!IsClear(item, p)) continue;
                bool crowded = false;
                for (int other = 0; other < occupied.Count; other++)
                    if (other != ownIndex && (p - occupied[other]).sqrMagnitude < 2.25f) { crowded = true; break; }
                if (crowded) continue;
                return p;
            }
            throw new InvalidOperationException($"No safe local placement within {MaxRelocation}m: {item.Name} at {item.Position}; nothing saved.");
        }
        internal static void Rebuild(Terrain terrain)
        {
            var clearance = new RouteObstacleClearance(terrain);
            var occupied = clearance.obstacles.Select(o => o.Position).ToList();
            for (int i = 0; i < clearance.obstacles.Count; i++)
            {
                Obstacle item = clearance.obstacles[i];
                item.Planned = clearance.FindPlacement(item, occupied, i); occupied[i] = item.Planned;
            }
            var beforeTrees = (TreeInstance[])clearance.trees.Clone();
            var positions = new Dictionary<Transform, Vector3>();
            int movedTrees = 0, movedProps = 0; float maxShift = 0f;
            try
            {
                foreach (Obstacle item in clearance.obstacles)
                {
                    if ((item.Planned - item.Position).sqrMagnitude < 1e-6f) continue;
                    maxShift = Mathf.Max(maxShift, Vector2.Distance(item.Planned, item.Position));
                    if (item.TreeIndex >= 0)
                    {
                        TreeInstance tree = clearance.trees[item.TreeIndex];
                        Vector3 local = terrain.transform.InverseTransformPoint(new Vector3(item.Planned.x, 0f, item.Planned.y));
                        Vector3 size = terrain.terrainData.size;
                        tree.position = new Vector3(local.x / size.x, terrain.terrainData.GetInterpolatedHeight(local.x / size.x, local.z / size.z) / size.y, local.z / size.z);
                        clearance.trees[item.TreeIndex] = tree; movedTrees++;
                    }
                    else
                    {
                        Shift(item.Target, item.Planned - item.Position, terrain, positions);
                        if (item.Companion != null) Shift(item.Companion, item.Planned - item.Position, terrain, positions);
                        movedProps++;
                    }
                }
                if (movedTrees > 0) terrain.terrainData.SetTreeInstances(clearance.trees, false);
                TerrainTreeObstacleRegistry.Invalidate(); Validate(terrain);
                if (movedTrees > 0) EditorUtility.SetDirty(terrain.terrainData);
                foreach (Transform target in positions.Keys) EditorUtility.SetDirty(target);
                Debug.Log($"ROUTE_FOREST_CLEARANCE_OK: movedTrees={movedTrees} movedProps={movedProps} maxShift={maxShift:F2}m limit={MaxRelocation}m; obstacles={clearance.obstacles.Count}, foliage retained.");
            }
            catch
            {
                if (movedTrees > 0) terrain.terrainData.SetTreeInstances(beforeTrees, false);
                foreach (var pair in positions) pair.Key.position = pair.Value;
                TerrainTreeObstacleRegistry.Invalidate(); throw;
            }
        }
        private static void Shift(Transform target, Vector2 delta, Terrain terrain, Dictionary<Transform, Vector3> before)
        {
            before.Add(target, target.position);
            Vector3 next = target.position + new Vector3(delta.x, 0f, delta.y);
            next.y += terrain.SampleHeight(next) - terrain.SampleHeight(target.position); target.position = next;
        }
        // Preview tools can probe new trees without moving existing scene obstacles.
        internal static Func<Transform, Vector2, bool> CreatePlacementProbe(Terrain terrain, Transform previewRoot)
        {
            var clearance = new RouteObstacleClearance(terrain);
            var preview = clearance.obstacles.Where(item => item.Target != null && item.Target.IsChildOf(previewRoot))
                .ToDictionary(item => item.Target);
            return (tree, position) => clearance.IsClear(preview[tree], position);
        }

        // Reuse the animated body sweeps while checking candidates at their actual ground height,
        // scale and orientation. Never relocate existing scene obstacles during planting.
        internal static Func<Transform, bool> CreateTreeCandidateProbe(Terrain terrain)
        {
            var clearance = new RouteObstacleClearance(terrain, false);
            return tree =>
            {
                var marker = tree.GetComponent<SceneTreeObstacle>();
                var item = new Obstacle { IsTree = true, Position = new Vector2(tree.position.x, tree.position.z),
                    RuntimeTrunkRadius = marker.Radius, Name = tree.name };
                clearance.AddStemSections(item, tree, tree.localToWorldMatrix, marker.Radius);
                foreach (var shape in item.Shapes) foreach (var p in shape) item.Reach = Mathf.Max(item.Reach, p.magnitude);
                return clearance.IsClear(item, item.Position);
            };
        }

        internal static Func<Vector2, float, bool> CreateTrunkCandidateProbe(Terrain terrain)
        {
            var clearance = new RouteObstacleClearance(terrain, false);
            return (position, radius) =>
            {
                var item = new Obstacle { IsTree = true, Position = position, Reach = radius * 1.03f };
                AddCircle(item, Vector2.zero, radius);
                item.Sections.Add(new TreeTrunkSection { center = Vector2.zero, radius = radius, bottom = -100f, top = 100f });
                return clearance.IsClear(item, position);
            };
        }

        internal static Func<Vector2, float, bool> CreateSolidCandidateProbe(Terrain terrain)
        {
            var clearance = new RouteObstacleClearance(terrain, false);
            return (p, r) => { var item = new Obstacle { Position = p, Reach = r * 1.03f }; AddCircle(item, Vector2.zero, r); return clearance.IsClear(item, p); };
        }
        internal static void BakeRuntimeProfiles(GameObject root)
        {
            var terrain = Terrain.activeTerrain;
            var clearance = new RouteObstacleClearance(terrain, false);
            clearance.bodyBottom = -1f; clearance.bodyTop = 32f;
            foreach (var marker in UnityEngine.Object.FindObjectsOfType<SceneTreeObstacle>())
            {
                var item = new Obstacle { Position = new Vector2(marker.transform.position.x, marker.transform.position.z), IsTree = true };
                clearance.AddStemSections(item, marker.transform, marker.transform.localToWorldMatrix, marker.Radius);
            }
            var profiles = root.GetComponent<ForestTrunkProfiles>() ?? root.AddComponent<ForestTrunkProfiles>();
            profiles.prototypes = terrain.terrainData.treePrototypes.Select(p => {
                var item = new Obstacle { IsTree = true };
                Matrix4x4 matrix = Matrix4x4.TRS(Vector3.zero, p.prefab.transform.localRotation, p.prefab.transform.localScale);
                return new ForestTrunkProfiles.Prototype { prefab = p.prefab,
                    sections = clearance.AddStemSections(item, p.prefab.transform, matrix, .7f) };
            }).ToArray();
            TerrainTreeObstacleRegistry.Invalidate();
        }
        internal static void VerifyHeightLayers(Terrain terrain)
        {
            var probe = CreateTreeCandidateProbe(terrain);
            var root = new GameObject("Height layer regression probe");
            root.transform.position = new Vector3(96f, 0f, -96f);
            root.AddComponent<SceneTreeObstacle>().SetRadius(.1f);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(root.transform, true);
            var material = new Material(Shader.Find("Standard")) { name = "Regression_Trunk" };
            cube.GetComponent<Renderer>().sharedMaterial = material;
            Vector2 p = MovementCorridor.PointAt(0, MovementCorridor.Length(0) * .18f);
            try
            {
                cube.transform.position = new Vector3(p.x, 20f, p.y);
                if (!probe(root.transform)) throw new InvalidOperationException("High wood above the dinosaur was incorrectly excluded.");
                cube.transform.position = new Vector3(p.x, 2f, p.y);
                if (!probe(root.transform)) throw new InvalidOperationException("Lateral branches expanded the stem exclusion.");
                material.name = "Regression_Foliage";
                if (!probe(root.transform)) throw new InvalidOperationException("Decorative foliage was treated as a solid obstacle.");
                if (CreateTrunkCandidateProbe(terrain)(p, .1f)) throw new InvalidOperationException("Runtime trunk clearance was not retained.");
                foreach (var agent in UnityEngine.Object.FindObjectsOfType<DinosaurAgent>())
                {
                    var core = agent.GetComponent<DinosaurCoreBody>(); if (core == null) continue;
                    foreach (var band in core.bands)
                    {
                        var inside = new TreeTrunkSection { center = new Vector2(band.bounds.center.x, band.bounds.center.z), radius = .1f, bottom = band.layer * .5f + .01f, top = (band.layer + 1) * .5f - .01f };
                        if (!core.Intersects(Vector2.zero, Vector2.up, inside)) throw new InvalidOperationException("Core interior accepted a trunk.");
                        inside.center.x = band.bounds.max.x + inside.radius + .001f;
                        if (core.Intersects(Vector2.zero, Vector2.up, inside)) throw new InvalidOperationException("Extra safety distance still applies outside the core.");
                    }
                }
                var groundcover = GameObject.Find(CentralForestDensification.RootName + "/Stage 1/Route Groundcover");
                if (groundcover == null || groundcover.GetComponentsInChildren<Collider>().Length > 0 || groundcover.GetComponentsInChildren<SceneTreeObstacle>().Length > 0)
                    throw new InvalidOperationException("Route groundcover registered solid obstacles.");
                Debug.Log("ROUTE_CORE_REGRESSION_OK: core interior trunks rejected, 1mm beyond core+stem radius allowed; lateral wood/foliage allowed; route groundcover non-solid; zero extra margin.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(material); }
        }

        internal static void Validate(Terrain terrain)
        {
            var clearance = new RouteObstacleClearance(terrain);
            foreach (Obstacle item in clearance.obstacles)
                if (!clearance.IsClear(item, item.Position)) throw new InvalidOperationException($"Directional route clearance failed: {item.Name} at {item.Position}.");
            Debug.Log($"ROUTE_MODEL_CLEARANCE_OK: {clearance.obstacles.Count} trunks and rocks; trees use directional animated torso/hips, {HeightLayer}m layers +0m; solid props retain full body +{SafetyMargin}m, foliage/lateral branches excluded from trees.");
        }
    }
}
