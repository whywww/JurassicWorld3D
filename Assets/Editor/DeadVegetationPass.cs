using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    /// <summary>Repeatable, local dressing pass for the west autumn grove.</summary>
    internal static class DeadVegetationPass
    {
        internal const string RootName = "Autumn Deadwood Ecology Belt";
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string MeshFolder = "Assets/Terrain/DeadVegetation";
        private const string MossMeshPath = MeshFolder + "/AutumnMoss.asset";
        private const string LogPath = "Assets/Vegetation/Deadwood/DeadwoodLogTrunk02.prefab";
        private const string StumpPath = "Assets/Vegetation/Deadwood/DeadwoodStump01.prefab";
        private const string MossMaterialPath = "Assets/Terrain/GroundEcology/Moss.mat";
        private const int Seed = 20260930;

        // Seven neighboring autumn groups form one band, rather than a map-wide scatter.
        private static readonly string[] Groups =
        {
            "02_Autumn", "17_Autumn", "04_Autumn", "19_Autumn",
            "20_Autumn", "05_Autumn", "21_Autumn"
        };

        [MenuItem("Jurassic World/Rebuild Autumn Deadwood Belt in Main")]
        public static void RebuildMain()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            Terrain terrain = UnityEngine.Object.FindObjectOfType<Terrain>();
            if (terrain == null || GameObject.Find("Main East to North Creek") == null)
                throw new InvalidOperationException("Open the creek-integrated Main scene before rebuilding deadwood.");
            SeedActiveScene(terrain);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("AUTUMN_DEADWOOD_READY: saved directly into Main.unity.");
        }

        [MenuItem("Jurassic World/Fix Autumn Fallen Logs in Main")]
        public static void FixMainFallenLogs()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            Terrain terrain = UnityEngine.Object.FindObjectOfType<Terrain>();
            GameObject root = GameObject.Find(RootName);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AutumnGroundedLogAssetBuilder.PrefabPath);
            if (terrain == null || root == null || prefab == null)
                throw new InvalidOperationException("Main terrain, autumn belt, or tested log prefab is missing.");
            Transform[] oldLogs = root.transform.Cast<Transform>()
                .Where(t => t.name.StartsWith("Fallen log ", StringComparison.Ordinal)).ToArray();
            if (oldLogs.Length != 8)
                throw new InvalidOperationException("Expected exactly eight autumn fallen logs; found " + oldLogs.Length);
            foreach (Transform old in oldLogs)
            {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(old.gameObject);
                if (source != null && AssetDatabase.GetAssetPath(source) == AutumnGroundedLogAssetBuilder.PrefabPath)
                {
                    Debug.Log("AUTUMN_MAIN_LOG_ALREADY_FIXED: " + old.name);
                    continue;
                }
                GameObject replacement = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                replacement.name = old.name;
                replacement.transform.SetSiblingIndex(old.GetSiblingIndex());
                replacement.transform.position = new Vector3(old.position.x, 0f, old.position.z);
                replacement.transform.rotation = Quaternion.Euler(0f, old.eulerAngles.y, 0f);
                replacement.transform.localScale = old.localScale;
                LogContact contact = GroundFallenLog(replacement, terrain);
                if (contact.HighestGap > 0.10f || contact.DeepestBurial > 0.15f)
                    throw new InvalidOperationException($"Main log {old.name} failed contact: gap={contact.HighestGap:F3}, burial={contact.DeepestBurial:F3}");
                foreach (Collider collider in replacement.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(collider);
                GameObjectUtility.SetStaticEditorFlags(replacement, StaticEditorFlags.BatchingStatic);
                UnityEngine.Object.DestroyImmediate(old.gameObject);
                Debug.Log($"AUTUMN_MAIN_LOG_CONTACT {replacement.name} gap={contact.HighestGap:F3}m burial={contact.DeepestBurial:F3}m");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("AUTUMN_MAIN_FALLEN_LOGS_FIXED: eight autumn logs saved to Main.unity.");
        }

        internal static void SeedActiveScene(Terrain terrain)
        {
            AutumnGroundedLogAssetBuilder.EnsureAssets();
            GameObject log = AssetDatabase.LoadAssetAtPath<GameObject>(LogPath);
            GameObject groundedLog = AssetDatabase.LoadAssetAtPath<GameObject>(AutumnGroundedLogAssetBuilder.PrefabPath);
            GameObject stump = AssetDatabase.LoadAssetAtPath<GameObject>(StumpPath);
            Material moss = AssetDatabase.LoadAssetAtPath<Material>(MossMaterialPath);
            if (log == null || groundedLog == null || stump == null || moss == null)
                throw new InvalidOperationException("The existing Poly Haven deadwood or moss material is missing.");

            List<Vector2> anchors = GetAutumnAnchors();
            List<Vector2> treeCenters = GetTreeCenters(terrain);
            var random = new System.Random(Seed);
            var occupied = new List<Vector2>();
            var root = new GameObject(RootName);
            int logs = 0, snags = 0, stumps = 0;

            try
            {
                for (int i = 0; i < anchors.Count; i++)
                {
                    Vector2 anchor = anchors[i];
                    Place(groundedLog, "Fallen log", anchor, terrain, treeCenters, occupied, random,
                        root.transform, 4.6f, 0.95f + 0.10f * (i % 3), false);
                    logs++;
                    if (i == 3)
                    {
                        Place(groundedLog, "Fallen log", anchor, terrain, treeCenters, occupied, random,
                            root.transform, 4.6f, 0.82f, false);
                        logs++;
                    }
                    if (i == 1 || i == 3 || i == 5)
                    {
                        Place(log, "Standing snag", anchor, terrain, treeCenters, occupied, random,
                            root.transform, 3.4f, 1.15f + 0.1f * (i % 2), true);
                        snags++;
                    }
                    if (i == 0 || i == 2 || i == 4 || i == 6)
                    {
                        Place(stump, "Old stump", anchor, terrain, treeCenters, occupied, random,
                            root.transform, 3.0f, 0.95f + 0.12f * (i % 3), false);
                        stumps++;
                    }
                }

                BuildMoss(terrain, anchors, occupied, moss, root.transform, random);
                GameObject old = GameObject.Find(RootName);
                if (old != null && old != root) UnityEngine.Object.DestroyImmediate(old);
                Debug.Log($"AUTUMN_DEADWOOD_BUILT logs={logs} snags={snags} stumps={stumps} moss=21 anchors={anchors.Count}.");
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(root);
                throw;
            }
        }

        private static List<Vector2> GetAutumnAnchors()
        {
            GameObject biomeRoot = GameObject.Find(Phase2VegetationSeeder.RootName);
            if (biomeRoot == null) throw new InvalidOperationException("Autumn vegetation groups are missing.");
            var anchors = new List<Vector2>();
            foreach (string name in Groups)
            {
                Transform group = biomeRoot.transform.Find(name);
                if (group == null) throw new InvalidOperationException("Missing autumn group: " + name);
                SceneTreeObstacle[] trees = group.GetComponentsInChildren<SceneTreeObstacle>();
                if (trees.Length == 0) throw new InvalidOperationException("Autumn group has no tree: " + name);
                Vector2 center = Vector2.zero;
                foreach (SceneTreeObstacle tree in trees)
                    center += new Vector2(tree.transform.position.x, tree.transform.position.z);
                center /= trees.Length;
                if (center.x > -18f) throw new InvalidOperationException("Autumn anchor crossed biome boundary: " + name);
                anchors.Add(center);
            }
            return anchors;
        }

        private static List<Vector2> GetTreeCenters(Terrain terrain)
        {
            var centers = new List<Vector2>();
            TerrainData data = terrain.terrainData;
            foreach (TreeInstance tree in data.treeInstances)
                centers.Add(new Vector2(terrain.transform.position.x + tree.position.x * data.size.x,
                    terrain.transform.position.z + tree.position.z * data.size.z));
            foreach (SceneTreeObstacle tree in UnityEngine.Object.FindObjectsOfType<SceneTreeObstacle>())
                centers.Add(new Vector2(tree.transform.position.x, tree.transform.position.z));
            return centers;
        }

        private static void Place(GameObject prefab, string label, Vector2 anchor, Terrain terrain,
            List<Vector2> trees, List<Vector2> occupied, System.Random random, Transform parent,
            float routeClearance, float scale, bool upright)
        {
            Vector2 spot = FindSpot(anchor, terrain, trees, occupied, random, routeClearance);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = label + " " + parent.childCount.ToString("D2");
            instance.transform.position = new Vector3(spot.x, 0f, spot.y);
            float yaw = (float)random.NextDouble() * 360f;
            instance.transform.rotation = upright
                ? Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, 84f + (float)random.NextDouble() * 10f)
                : Quaternion.Euler(0f, yaw, 0f);
            instance.transform.localScale *= scale;
            if (label == "Fallen log")
                GroundFallenLog(instance, terrain);
            else
            {
                Bounds bounds = RenderBounds(instance);
                float ground = GroundY(terrain, spot);
                instance.transform.position += Vector3.up * (ground - bounds.min.y - (upright ? 0.08f : 0.14f));
            }
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            GameObjectUtility.SetStaticEditorFlags(instance, StaticEditorFlags.BatchingStatic);
            occupied.Add(spot);
        }

        private static Vector2 FindSpot(Vector2 anchor, Terrain terrain, List<Vector2> trees,
            List<Vector2> occupied, System.Random random, float routeClearance)
        {
            for (int attempt = 0; attempt < 320; attempt++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float radius = 3.2f + (float)random.NextDouble() * 5f;
                Vector2 p = anchor + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (p.x < -75f || p.x > -18f || p.y < -72f || p.y > 80f) continue;
                if (terrain.terrainData.GetInterpolatedNormal((p.x - terrain.transform.position.x) / terrain.terrainData.size.x,
                        (p.y - terrain.transform.position.z) / terrain.terrainData.size.z).y < 0.86f) continue;
                if (trees.Any(t => Vector2.Distance(t, p) < 2.4f)) continue;
                if (occupied.Any(t => Vector2.Distance(t, p) < 3.4f)) continue;
                bool routeSafe = true;
                for (int route = 0; route < MovementCorridor.RouteCount; route++)
                    if (MovementCorridor.MinDistanceToRoute(p, route) < routeClearance + DinosaurAgent.TreeAvoidRadiusCap)
                        routeSafe = false;
                if (routeSafe) return p;
            }
            throw new InvalidOperationException("No safe deadwood position near autumn group " + anchor);
        }

        private static void BuildMoss(Terrain terrain, List<Vector2> anchors, List<Vector2> occupied,
            Material material, Transform parent, System.Random random)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            foreach (Vector2 anchor in anchors)
            for (int i = 0; i < 3; i++)
            {
                Vector2 nearest = occupied.OrderBy(p => Vector2.SqrMagnitude(p - anchor)).ElementAt(i);
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                Vector2 center = nearest + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (0.7f + i * 0.45f);
                float radius = 0.35f + (float)random.NextDouble() * 0.35f;
                Vector2 u = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Vector2 v = new Vector2(-u.y, u.x) * 0.65f;
                int first = vertices.Count;
                foreach (Vector2 p in new[] { center - u - v, center - u + v, center + u + v, center + u - v })
                    vertices.Add(new Vector3(p.x, GroundY(terrain, p) + 0.025f, p.y));
                triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            }

            if (!AssetDatabase.IsValidFolder(MeshFolder)) AssetDatabase.CreateFolder("Assets/Terrain", "DeadVegetation");
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MossMeshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "Autumn deadwood moss" };
                AssetDatabase.CreateAsset(mesh, MossMeshPath);
            }
            else mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);

            var moss = new GameObject("Moss around deadwood");
            moss.transform.SetParent(parent, false);
            moss.AddComponent<MeshFilter>().sharedMesh = mesh;
            moss.AddComponent<MeshRenderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(moss, StaticEditorFlags.BatchingStatic);
        }

        private static float GroundY(Terrain terrain, Vector2 p) =>
            terrain.SampleHeight(new Vector3(p.x, 0f, p.y)) + terrain.transform.position.y;

        internal struct LogContact
        {
            internal float HighestGap;
            internal float DeepestBurial;
        }

        // The lowest renderer bound belongs to a dangling branch on this mesh. Sample the
        // underside of the central trunk in seven longitudinal bands instead. Try several
        // rotations around its long axis so a protruding branch cannot prop up the trunk.
        internal static LogContact GroundFallenLog(GameObject instance, Terrain terrain)
        {
            MeshFilter filter = instance.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault();
            if (filter == null || filter.sharedMesh == null)
                throw new InvalidOperationException("Fallen log has no LOD0 mesh.");
            Mesh mesh = filter.sharedMesh;
            Bounds bounds = mesh.bounds;
            var vertices = new Vector3[mesh.vertexCount];
            using (var data = Mesh.AcquireReadOnlyMeshData(mesh))
            using (var nativeVertices = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp))
            {
                data[0].GetVertices(nativeVertices);
                nativeVertices.CopyTo(vertices);
            }

            Quaternion yaw = instance.transform.rotation;
            Vector3 origin = instance.transform.position;
            float bestScore = float.PositiveInfinity;
            Quaternion bestRotation = yaw;
            float bestShift = 0f;
            LogContact bestContact = default;
            for (int roll = 0; roll < 360; roll += 15)
            {
                Vector3[] profile = TrunkProfile(vertices, bounds, roll);
                instance.transform.rotation = yaw * Quaternion.Euler(roll, 0f, 0f);
                float[] deltas = ContactDeltas(filter.transform, terrain, profile);
                float slope = (deltas[6] - deltas[0]) / (bounds.size.x * instance.transform.lossyScale.x);
                float pitch = Mathf.Clamp(Mathf.Atan(slope) * Mathf.Rad2Deg, -7f, 7f);
                instance.transform.rotation = yaw * Quaternion.Euler(roll, 0f, pitch);
                deltas = ContactDeltas(filter.transform, terrain, profile);
                float[] ordered = (float[])deltas.Clone();
                Array.Sort(ordered);
                float median = ordered[3];
                float[] trunk = deltas.Where(d => Mathf.Abs(d - median) <= 0.12f).ToArray();
                if (trunk.Length < 5) continue;
                float min = trunk.Min(), max = trunk.Max();
                float shift = (min + max) * 0.5f - 0.02f;
                float gap = Mathf.Max(0f, shift - min);
                float burial = Mathf.Max(0f, max - shift);
                float score = Mathf.Max(gap / 0.10f, burial / 0.15f) + (7 - trunk.Length) * 0.025f;
                if (score >= bestScore) continue;
                bestScore = score;
                bestRotation = instance.transform.rotation;
                bestShift = shift;
                bestContact = new LogContact { HighestGap = gap, DeepestBurial = burial };
            }
            if (float.IsPositiveInfinity(bestScore))
                throw new InvalidOperationException("No continuous trunk profile could be grounded.");
            instance.transform.rotation = bestRotation;
            instance.transform.position = origin + Vector3.up * bestShift;
            Debug.Log($"DEADWOOD_CONTACT gap={bestContact.HighestGap:F3}m burial={bestContact.DeepestBurial:F3}m roll={bestRotation.eulerAngles.x:F0}");
            return bestContact;
        }

        private static Vector3[] TrunkProfile(Vector3[] vertices, Bounds bounds, float roll)
        {
            Quaternion rotation = Quaternion.Euler(roll, 0f, 0f);
            float centerZ = (rotation * bounds.center).z;
            var profile = new Vector3[7];
            for (int band = 0; band < profile.Length; band++)
            {
                float x = Mathf.Lerp(bounds.min.x + 0.18f, bounds.max.x - 0.18f, band / 6f);
                var candidates = new List<Vector3>();
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 rotated = rotation * vertex;
                    if (Mathf.Abs(vertex.x - x) < 0.18f && Mathf.Abs(rotated.z - centerZ) < 0.30f)
                        candidates.Add(vertex);
                }
                if (candidates.Count < 8)
                    throw new InvalidOperationException("Too few trunk vertices for contact band " + band);
                candidates.Sort((a, b) => (rotation * a).y.CompareTo((rotation * b).y));
                profile[band] = candidates[Mathf.FloorToInt(candidates.Count * 0.30f)];
            }
            return profile;
        }

        private static float[] ContactDeltas(Transform meshTransform, Terrain terrain, Vector3[] profile)
        {
            var deltas = new float[profile.Length];
            for (int i = 0; i < profile.Length; i++)
            {
                Vector3 world = meshTransform.TransformPoint(profile[i]);
                deltas[i] = GroundY(terrain, new Vector2(world.x, world.z)) - world.y;
            }
            return deltas;
        }

        private static Bounds RenderBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("Empty deadwood prefab: " + go.name);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
