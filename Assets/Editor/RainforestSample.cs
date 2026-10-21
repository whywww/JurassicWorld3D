using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class RainforestSample
    {
        internal const string RootName = "Central Rainforest Sample";
        internal const string Output = "Tools/VegetationDiversity/RainforestSample";
        internal const string EnabledPath = "Assets/Editor/RainforestSampleEnabled.json";
        internal static readonly Rect Area = Rect.MinMaxRect(-25f, -10f, 25f, 45f);
        private const int Seed = 731716;
        private sealed class ReservedPose
        {
            internal Vector2 position, forward;
            internal DinosaurCoreBody body;
        }
        private sealed class Stem { internal Vector2 position; internal float radius; }

        [MenuItem("Jurassic World/Environment/Rainforest Sample/Build and Verify")]
        public static void BuildAndVerify()
        {
            RequireClean();
            EditorSceneManager.OpenScene(CentralForestDensification.ScenePath);
            Directory.CreateDirectory(Output);
            string checkpoint = Output + "/Main_BeforeSample.unity";
            if (!File.Exists(checkpoint)) File.Copy(CentralForestDensification.ScenePath, checkpoint);
            FoliageInteractionPreparation.RestoreCurrent();
            RemoveOwned();
            var before = new Snapshot();
            Apply(); before.Check(); string first = Signature();
            RemoveOwned(); Apply(); before.Check();
            if (first != Signature()) throw new InvalidOperationException("Sample layout is not deterministic.");
            RemoveOwned(); before.Check();
            Apply(); before.Check();
            File.WriteAllText(EnabledPath, "{\"enabled\":true}\n");
            AssetDatabase.ImportAsset(EnabledPath);
            RemoveOwned(); SeedConfigured();
            if (first != Signature()) throw new InvalidOperationException("Bake hook changed the sample layout.");
            before.Check();
            FoliageInteractionPreparation.SeedConfigured();
            Save();
            Debug.Log("RAINFOREST_SAMPLE_BUILD_OK: deterministic build, undo and Bake hook; existing terrain and poses preserved.");
        }

        [MenuItem("Jurassic World/Environment/Rainforest Sample/Remove Sample")]
        public static void Remove()
        {
            RequireClean(); EditorSceneManager.OpenScene(CentralForestDensification.ScenePath);
            FoliageInteractionPreparation.RestoreCurrent(); RemoveOwned();
            if (File.Exists(EnabledPath)) AssetDatabase.DeleteAsset(EnabledPath);
            RebuildNavigation(); FoliageInteractionPreparation.SeedConfigured(); Save();
        }
        [MenuItem("Jurassic World/Environment/Rainforest Sample/Build and Verify", true)]
        [MenuItem("Jurassic World/Environment/Rainforest Sample/Remove Sample", true)]
        private static bool MenuEnabled() => JurassicMenuGuard.CanRun;

        private static void RequireClean()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the scene and exit Play before rebuilding the rainforest sample.");
        }
        private static void Save()
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
        }
        internal static void SeedConfigured() { if (File.Exists(EnabledPath)) { RemoveOwned(); Apply(); } }
        private static void RemoveOwned()
        {
            var root = GameObject.Find(RootName); if (root != null) Object.DestroyImmediate(root);
            TerrainTreeObstacleRegistry.Invalidate();
        }
        internal static void RebuildNavigation()
        {
            var rex = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex");
            var body = new SerializedObject(rex).FindProperty("interactionBody").objectReferenceValue as DinosaurCoreBody;
            TRexInteractionBake.RebuildObstaclesAndNavigation(Object.FindObjectOfType<DinosaurInteractionSettings>(), body);
        }

        private static List<Stem> Stems(Terrain terrain)
        {
            var result = new List<Stem>();
            foreach (var tree in terrain.terrainData.treeInstances)
            {
                if (tree.widthScale <= 0f) continue;
                Vector3 p = terrain.transform.position + Vector3.Scale(tree.position, terrain.terrainData.size);
                result.Add(new Stem { position = XZ(p), radius = .7f * tree.widthScale });
            }
            foreach (var tree in Object.FindObjectsOfType<SceneTreeObstacle>())
                result.Add(new Stem { position = XZ(tree.transform.position), radius = tree.Radius });
            return result;
        }

        private static void Apply()
        {
            Terrain terrain = Terrain.activeTerrain;
            var settings = Object.FindObjectOfType<DinosaurInteractionSettings>();
            RebuildNavigation();
            var nav = NavMesh.AddNavMeshData(settings.navigationData);
            var root = new GameObject(RootName);
            var layout = root.AddComponent<RainforestSampleLayout>();
            var holder = new GameObject("Planting clearance probe") { hideFlags = HideFlags.HideAndDontSave };
            var probe = holder.AddComponent<DinosaurCoreBody>();
            try
            {
                var rex = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex");
                var profile = new SerializedObject(rex).FindProperty("interactionProfile").objectReferenceValue as DinosaurInteractionProfile;
                var filter = new NavMeshQueryFilter { agentTypeID = settings.navigationAgentTypeId, areaMask = 1 };
                var reserved = new List<ReservedPose>();
                var centers = new List<Vector3>(); var headings = new List<Quaternion>();
                foreach (Vector2 desired in new[] { new Vector2(-14f, 5f), new Vector2(14f, 28f) })
                {
                    bool found = false;
                    var sites = Grid(2f).OrderBy(p => (p - desired).sqrMagnitude);
                    foreach (Vector2 site in sites)
                    {
                        if (MainCreekIntegration.NearCreek(site.x, site.y, 2f) || centers.Any(p => Vector2.Distance(XZ(p), site) < 16f)) continue;
                        if (!NavMesh.SamplePosition(new Vector3(site.x, rex.MovementHeight, site.y), out var hit, .5f, filter)) continue;
                        for (int direction = 0; direction < 8; direction++)
                        {
                            Quaternion rotation = Quaternion.Euler(0f, direction * 45f - rex.VisualYaw, 0f);
                            var candidate = PerformancePoses(profile, hit.position, rotation, rex.VisualYaw, root.transform);
                            if (candidate.Any(p => settings.PoseBlocked(p.position, p.forward, p.body)))
                            { foreach (var body in candidate.Select(p => p.body).Distinct()) Object.DestroyImmediate(body.gameObject); continue; }
                            centers.Add(hit.position); headings.Add(rotation); reserved.AddRange(candidate); found = true; break;
                        }
                        if (found) break;
                    }
                    if (!found) throw new InvalidOperationException("No existing space fits a complete rainforest interaction; existing obstacles were retained.");
                }
                layout.clearingPositions = centers.ToArray(); layout.clearingRotations = headings.ToArray();
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(centers[0], centers[1], filter, path) || path.status != NavMeshPathStatus.PathComplete)
                    throw new InvalidOperationException("Sample clearings have no connecting path.");
                // Offset intermediate targets produce bends without imposing a rectangular clearing.
                var corridor = new List<Vector3>();
                var anchors = new[] { centers[0], new Vector3(-4f, rex.MovementHeight, 14f), new Vector3(5f, rex.MovementHeight, 21f), centers[1] };
                for (int i = 1; i < anchors.Length; i++)
                {
                    if (!NavMesh.SamplePosition(anchors[i], out var hit, 3f, filter)
                        || !NavMesh.CalculatePath(i == 1 ? centers[0] : corridor[corridor.Count - 1], hit.position, filter, path)
                        || path.status != NavMeshPathStatus.PathComplete) throw new InvalidOperationException("Curved sample connector is disconnected.");
                    foreach (var p in path.corners) if (corridor.Count == 0 || Vector3.Distance(corridor[corridor.Count - 1], p) > .1f) corridor.Add(p);
                }
                layout.corridor = corridor.ToArray();
                var walk = profile.Find(InteractionMotionRole.Walk);
                var walkBody = new GameObject("Reserved walking body").AddComponent<DinosaurCoreBody>(); walkBody.transform.SetParent(root.transform, false);
                walkBody.bands = walk.bodyBands;
                var walking = new List<ReservedPose>();
                for (int i = 1; i < corridor.Count; i++)
                {
                    Vector2 delta = XZ(corridor[i] - corridor[i - 1]);
                    int count = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / .5f));
                    for (int j = 0; j <= count; j++) walking.Add(new ReservedPose { position = XZ(Vector3.Lerp(corridor[i - 1], corridor[i], j / (float)count)), forward = delta.normalized, body = walkBody });
                }
                var stems = Stems(terrain); layout.originalTrees = stems.Count(s => Area.Contains(s.position));
                var spawnReservations = new List<ReservedPose>();
                foreach (var agent in Object.FindObjectsOfType<DinosaurAgent>())
                {
                    var spawnBody = new SerializedObject(agent).FindProperty("interactionBody").objectReferenceValue as DinosaurCoreBody;
                    if (spawnBody == null) spawnBody = agent.GetComponent<DinosaurCoreBody>();
                    if (spawnBody == null) continue;
                    Vector3 forward = agent.transform.rotation * Quaternion.Euler(0f, agent.VisualYaw, 0f) * Vector3.forward;
                    for (int step = 0; step <= 12; step++)
                        spawnReservations.Add(new ReservedPose { position = XZ(agent.transform.position + forward * (.5f * step)),
                            forward = XZ(forward).normalized, body = spawnBody });
                }
                layout.referenceDensity = ReferenceDensity(stems, out string reference);
                var trunkSafe = RouteObstacleClearance.CreateTrunkCandidateProbe(terrain);
                var treeSafe = RouteObstacleClearance.CreateTreeCandidateProbe(terrain,
                    Object.FindObjectsOfType<DinosaurAgent>().SingleOrDefault(a => a.name == "Stegosaurus"));
                Func<Vector2, bool> siteSafe = p => !MainCreekIntegration.NearCreek(p.x, p.y, 2f)
                    && !settings.solidObstacles.Any(b => p.x > b.min.x - 1f && p.x < b.max.x + 1f && p.y > b.min.z - 1f && p.y < b.max.z + 1f);
                int baseArea = 0, clearingCells = 0, corridorCells = 0;
                var sitesToPlant = new List<Vector2>();
                foreach (Vector2 p in Grid(1f))
                {
                    if (!siteSafe(p)) continue; baseArea++;
                    if (BlockedBy(reserved, p, .85f)) { clearingCells++; continue; }
                    if (BlockedBy(walking, p, .85f) || BlockedBy(spawnReservations, p, .85f) || !trunkSafe(p, .85f)) { corridorCells++; continue; }
                    sitesToPlant.Add(p);
                }
                layout.clearingArea = clearingCells; layout.corridorArea = corridorCells; layout.plantableArea = sitesToPlant.Count;
                int retainedPlantable = stems.Count(s => Area.Contains(s.position) && siteSafe(s.position)
                    && !BlockedBy(reserved, s.position, .85f) && !BlockedBy(walking, s.position, .85f)
                    && !BlockedBy(spawnReservations, s.position, .85f) && trunkSafe(s.position, .85f));
                int target = Mathf.RoundToInt(layout.referenceDensity * layout.plantableArea * .65f);
                var random = new System.Random(Seed);
                // Shuffle deterministic sites, then grow small clusters around accepted stems.
                var candidates = sitesToPlant.SelectMany(p => new[] { p + new Vector2(-.25f, -.25f), p + new Vector2(.25f, -.25f),
                    p + new Vector2(-.25f, .25f), p + new Vector2(.25f, .25f) }).OrderBy(_ => random.Next()).ToList();
                var trees = new GameObject("Trees"); trees.transform.SetParent(root.transform, false);
                int added = 0, woodRejected = 0, reservationRejected = 0;
                foreach (Vector2 site in candidates)
                {
                    if (added + retainedPlantable >= target) break;
                    Vector2 p = site + new Vector2((float)random.NextDouble() - .5f, (float)random.NextDouble() - .5f) * .5f;
                    if (!Area.Contains(p) || !siteSafe(p) || BlockedBy(reserved, p, .85f) || BlockedBy(walking, p, .85f) || BlockedBy(spawnReservations, p, .85f)
                        || !trunkSafe(p, .85f) || stems.Any(s => Vector2.Distance(s.position, p) < s.radius + 1.6f)) continue;
                    string treePath = random.Next(3) == 0 ? "Assets/VegetationDiversity/Prefabs/Pachira_B.prefab"
                        : "Assets/NatureStarterKit2/Nature/tree0" + (1 + random.Next(4)) + ".prefab";
                    var tree = CentralForestDensification.Instantiate(treePath, trees.transform);
                    tree.name = "RainforestTree_" + added;
                    tree.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                    tree.transform.localScale *= Mathf.Lerp(15f, 21f, (float)random.NextDouble()) / CentralForestDensification.BoundsOf(tree).size.y;
                    CentralForestDensification.Ground(tree, terrain, p);
                    foreach (var collider in tree.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
                    var marker = tree.GetComponent<SceneTreeObstacle>() ?? tree.AddComponent<SceneTreeObstacle>();
                    marker.SetRadius(.85f); marker.includeHardBranches = true;
                    if (!treeSafe(tree.transform)) { woodRejected++; Object.DestroyImmediate(tree); continue; }
                    if (marker.trunkSections.Any(s => BlockedBy(reserved, s.center + p, s.radius, s.bottom, s.top)
                        || BlockedBy(walking, s.center + p, s.radius, s.bottom, s.top)
                        || BlockedBy(spawnReservations, s.center + p, s.radius, s.bottom, s.top))) { reservationRejected++; Object.DestroyImmediate(tree); continue; }
                    stems.Add(new Stem { position = p, radius = .85f }); added++;
                }
                layout.plantedTrees = added;
                Debug.Log($"RAINFOREST_PLANTING: accepted={added}, target={target}, woodRejects={woodRejected}, performanceRejects={reservationRejected}");
                PlantUnderstory(root.transform, terrain, stems.Where(s => Area.Contains(s.position)).Select(s => s.position).ToArray(), random);
                RouteObstacleClearance.BakeRuntimeProfiles(root);
                TerrainTreeObstacleRegistry.Invalidate();
                foreach (var pose in reserved.Concat(walking))
                    if (settings.PoseBlocked(pose.position, pose.forward, pose.body)) throw new InvalidOperationException("Planting blocked a reserved performance or connector.");
                // Reserve data stays serialized as layout poses; helper bodies are never saved.
                foreach (var body in reserved.Select(p => p.body).Distinct()) Object.DestroyImmediate(body.gameObject);
                Object.DestroyImmediate(walkBody.gameObject);
                float ratio = (retainedPlantable + added) / Mathf.Max(1f, layout.plantableArea) / layout.referenceDensity;
                Directory.CreateDirectory(Output);
                File.WriteAllText(Output + "/DensityReport.md", $"# 中心雨林样板\n\n固定种子 {Seed}；X[-25,25] Z[-10,45]，总面积2750m²。\n\n有效地面 {baseArea}m²；互动林隙 {clearingCells}m²；通道及路线 {corridorCells}m²；可种植面积 {layout.plantableArea}m²。\n\n样板原有乔木 {layout.originalTrees}；可种植区原有 {retainedPlantable}；新增 {added}；目标 {target}；同口径外围密度比例 {ratio:P1}。\n\n林隙中心：{string.Join("; ", centers.Select(p => p.ToString("F2")))}。\n\n外围16×16m样方（棵/256m²）：\n{reference}\n");
                if (ratio < .60f || ratio > .70f) throw new InvalidOperationException($"Rainforest density target not met: {ratio:P1}; exclusions were not expanded.");
            }
            catch { Object.DestroyImmediate(root); TerrainTreeObstacleRegistry.Invalidate(); throw; }
            finally { Object.DestroyImmediate(holder); nav.Remove(); }
            RebuildNavigation();
        }

        private static List<ReservedPose> PerformancePoses(DinosaurInteractionProfile profile, Vector3 origin, Quaternion rotation, float yaw, Transform parent)
        {
            var result = new List<ReservedPose>();
            var introductions = new[] {
                new[] { InteractionMotionRole.Investigate, InteractionMotionRole.InvestigateOut, InteractionMotionRole.BigRoarStep },
                new[] { InteractionMotionRole.LookLeft, InteractionMotionRole.SideRoarLeft },
                new[] { InteractionMotionRole.LookRight, InteractionMotionRole.SideRoarRight } };
            foreach (var introduction in introductions)
            foreach (var bite in new[] { InteractionMotionRole.BiteCloseLeft, InteractionMotionRole.BiteCloseRight })
            {
                Vector3 actionOrigin = origin; Quaternion actionRotation = rotation;
                var outgoing = profile.Find(InteractionMotionRole.Walk);
                foreach (var role in introduction.Concat(new[] { InteractionMotionRole.Roar, InteractionMotionRole.LookForwardTwo,
                    bite, InteractionMotionRole.LookForwardTwo, InteractionMotionRole.Turn180Look }))
                {
                    var motion = profile.Find(role); if (motion == null) throw new InvalidOperationException("Missing performance: " + role);
                    var body = new GameObject("Reservation " + role).AddComponent<DinosaurCoreBody>(); body.transform.SetParent(parent, false);
                    var bands = motion.bodyBands.ToDictionary(b => b.layer, b => b.bounds);
                    foreach (var band in outgoing.bodyBands)
                    {
                        if (bands.TryGetValue(band.layer, out var bounds)) { bounds.Encapsulate(band.bounds); bands[band.layer] = bounds; }
                        else bands[band.layer] = band.bounds;
                    }
                    body.bands = bands.Select(b => new DinosaurCoreBody.Band { layer = b.Key, bounds = b.Value }).ToArray();
                    float duration = role == InteractionMotionRole.LookForwardTwo ? Mathf.Min(2f, motion.clip.length) : motion.clip.length;
                    DinosaurInteractionProfile.FootFrame end = motion.footFrames[0];
                    foreach (var frame in motion.footFrames.Where((f, i) => (i % 6 == 0 || i == motion.footFrames.Length - 1) && f.time <= duration + .001f))
                    {
                        Vector3 p = actionOrigin + actionRotation * Quaternion.Euler(0f, yaw, 0f) * new Vector3(frame.rootOffset.x, 0f, frame.rootOffset.y);
                        Vector3 f = Quaternion.Euler(0f, frame.yaw, 0f) * actionRotation * Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                        result.Add(new ReservedPose { position = XZ(p), forward = XZ(f), body = body }); end = frame;
                    }
                    actionOrigin += actionRotation * Quaternion.Euler(0f, yaw, 0f) * new Vector3(end.rootOffset.x, 0f, end.rootOffset.y);
                    actionRotation = Quaternion.Euler(0f, end.yaw, 0f) * actionRotation;
                    outgoing = motion;
                }
            }
            return result;
        }
        private static bool BlockedBy(List<ReservedPose> poses, Vector2 p, float radius, float bottom = -100f, float top = 100f)
            => poses.Any(pose => pose.body.Intersects(pose.position, pose.forward,
                new TreeTrunkSection { center = p, radius = radius, bottom = bottom, top = top }));
        private static IEnumerable<Vector2> Grid(float step)
        {
            for (float x = Area.xMin + step * .5f; x < Area.xMax; x += step)
            for (float z = Area.yMin + step * .5f; z < Area.yMax; z += step) yield return new Vector2(x, z);
        }
        private static float ReferenceDensity(List<Stem> stems, out string report)
        {
            var counts = new List<int>(); var text = new StringBuilder();
            foreach (float x in new[] { -82f, 82f }) foreach (float z in new[] { -64f, -32f, 0f, 32f, 64f })
            {
                int count = stems.Count(s => new Rect(x - 8f, z - 8f, 16f, 16f).Contains(s.position));
                counts.Add(count); text.AppendLine($"- ({x}, {z}): {count}");
            }
            counts.Sort(); report = text.ToString(); return (counts[4] + counts[5]) * .5f / 256f;
        }
        private static void PlantUnderstory(Transform parent, Terrain terrain, Vector2[] stems, System.Random random)
        {
            var group = new GameObject("Understory"); group.transform.SetParent(parent, false); int count = 0;
            foreach (var stem in stems) for (int j = 0; j < 5; j++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f, spread = Mathf.Lerp(1.5f, 4f, (float)random.NextDouble());
                Vector2 p = stem + new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * spread;
                if (!Area.Contains(p) || MainCreekIntegration.NearCreek(p.x, p.y, .5f)) continue;
                bool fern = random.NextDouble() < .75;
                string path = fern ? "Assets/Vegetation/Ferns/Fern02_" + "ABCD"[random.Next(4)] + ".prefab"
                    : "Assets/NatureStarterKit2/Nature/bush0" + (random.Next(6) + 1) + ".prefab";
                var item = CentralForestDensification.Instantiate(path, group.transform); item.name = "RainforestUnderstory_" + count++;
                item.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                item.transform.localScale *= Mathf.Lerp(.4f, .8f, (float)random.NextDouble()) / CentralForestDensification.BoundsOf(item).size.y;
                foreach (var collider in item.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
                foreach (var marker in item.GetComponentsInChildren<SceneTreeObstacle>()) Object.DestroyImmediate(marker);
                CentralForestDensification.Ground(item, terrain, p);
            }
        }
        private static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
        internal static string Signature() => string.Join("\n", GameObject.Find(RootName).GetComponentsInChildren<Transform>(true)
            .Select(t => t.name + t.position.ToString("F6") + t.rotation.ToString("F6") + t.lossyScale.ToString("F6")));
        private sealed class Snapshot
        {
            private readonly TerrainData data = Terrain.activeTerrain.terrainData;
            private readonly TreeInstance[] trees = Terrain.activeTerrain.terrainData.treeInstances;
            private readonly float[] heights, paint;
            private readonly Dictionary<Transform, string> poses;
            internal Snapshot()
            {
                heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution).Cast<float>().ToArray();
                paint = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight).Cast<float>().ToArray();
                poses = Object.FindObjectsOfType<Transform>().ToDictionary(t => t, t => t.position.ToString("F6") + t.rotation.ToString("F6") + t.lossyScale.ToString("F6"));
            }
            internal void Check()
            {
                if (!trees.SequenceEqual(data.treeInstances) || !heights.SequenceEqual(data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution).Cast<float>())
                    || !paint.SequenceEqual(data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight).Cast<float>())) throw new InvalidOperationException("Sample changed original Terrain data.");
                foreach (var p in poses) if (p.Key == null || p.Value != p.Key.position.ToString("F6") + p.Key.rotation.ToString("F6") + p.Key.lossyScale.ToString("F6"))
                    throw new InvalidOperationException("Sample changed an original object pose.");
            }
        }
    }
}
