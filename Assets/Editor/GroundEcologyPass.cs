using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    /// <summary>Final, deterministic surface pass over the baked paths and vegetation islands.</summary>
    internal static class GroundEcologyPass
    {
        private const string RootName = "Ground Ecology";
        private const string AssetFolder = "Assets/Terrain/GroundEcology";
        private const float TerrainHalf = 100f;
        private const int Seed = 271834;

        [MenuItem("Jurassic World/Rebuild Dinosaur Routes")]
        public static void RebuildRoutes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before rebuilding routes.");
            if (EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the current scene before rebuilding routes.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Terrain terrain = UnityEngine.Object.FindObjectOfType<Terrain>();
            if (terrain == null) throw new InvalidOperationException("Dinosaur routes require the baked Terrain.");
            var anchors = CollectAnchors();
            if (anchors.Count == 0) throw new InvalidOperationException("Dinosaur routes require vegetation islands.");
            RouteObstacleClearance.Rebuild(terrain);
            RepaintRoutes(terrain);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("DINOSAUR_ROUTES_REBUILT: " + MovementCorridor.Describe());
        }

        internal static void RepaintRoutes(Terrain terrain)
        {
            var anchors = CollectAnchors();
            TerrainData data = terrain.terrainData;
            TerrainLayer[] priorLayers = data.terrainLayers;
            float[,,] priorPaint = priorLayers.Length > 4
                ? data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight) : null;
            // Reset the base paint so the previous routes are erased rather than painted over.
            TerrainEnvironmentSeeder.PaintGroundLayers(data);
            PaintAlphamap(data, anchors);
            if (priorPaint != null) RestoreExtraLayers(data, priorLayers, priorPaint);
            EditorUtility.SetDirty(terrain.terrainData);
        }

        internal static void RepaintLocalRegion(Terrain terrain)
        {
            TerrainData data = terrain.terrainData;
            float[,,] original = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            float[,,] saved = LocalForestRepair.Baseline.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            TerrainData baseline = UnityEngine.Object.Instantiate(data);
            try
            {
                TerrainEnvironmentSeeder.PaintGroundLayers(baseline);
                PaintAlphamap(baseline, CollectAnchors());
                float[,,] fresh = baseline.GetAlphamaps(0, 0, baseline.alphamapWidth, baseline.alphamapHeight);
                for (int z = 0; z < data.alphamapHeight; z++)
                for (int x = 0; x < data.alphamapWidth; x++)
                {
                    Vector3 p = terrain.transform.TransformPoint(new Vector3(
                        x / (float)(data.alphamapWidth - 1) * data.size.x, 0,
                        z / (float)(data.alphamapHeight - 1) * data.size.z));
                    float weight = LocalForestRepair.Weight(new Vector2(p.x, p.z));
                    if (weight <= 0f) continue;
                    // Preserve every creek/bank layer exactly; redistribute only the base four.
                    float available = 1f, savedBase = 0f;
                    for (int layer = 4; layer < data.alphamapLayers; layer++) available -= original[z, x, layer];
                    for (int layer = 0; layer < 4; layer++) savedBase += saved[z, x, layer];
                    for (int layer = 0; layer < 4; layer++)
                        original[z, x, layer] = available * Mathf.Lerp(savedBase > 0f ? saved[z, x, layer] / savedBase : (layer == 0 ? 1f : 0f), fresh[z, x, layer], weight);
                }
                data.SetAlphamaps(0, 0, original);
            }
            finally { UnityEngine.Object.DestroyImmediate(baseline); }
        }

        private static void RestoreExtraLayers(TerrainData data, TerrainLayer[] priorLayers, float[,,] priorPaint)
        {
            float[,,] basePaint = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            var layers = (TerrainLayer[])priorLayers.Clone();
            Array.Copy(data.terrainLayers, layers, 4);
            var result = new float[data.alphamapHeight, data.alphamapWidth, layers.Length];
            for (int z = 0; z < data.alphamapHeight; z++)
            for (int x = 0; x < data.alphamapWidth; x++)
            {
                float extra = 0f;
                for (int layer = 4; layer < layers.Length; layer++)
                {
                    result[z, x, layer] = priorPaint[z, x, layer];
                    extra += priorPaint[z, x, layer];
                }
                for (int layer = 0; layer < 4; layer++)
                    result[z, x, layer] = basePaint[z, x, layer] * Mathf.Clamp01(1f - extra);
            }
            data.terrainLayers = layers;
            data.SetAlphamaps(0, 0, result);
            Debug.Log($"DINOSAUR_ROUTES_PRESERVED: {layers.Length - 4} extra terrain layers (creek banks).");
        }

        [MenuItem("Jurassic World/Rebuild Ground Ecology")]
        public static void Rebuild()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Terrain terrain = UnityEngine.Object.FindObjectOfType<Terrain>();
            if (terrain == null) throw new InvalidOperationException("Ground Ecology requires the baked Terrain.");
            TerrainEnvironmentSeeder.PopulateGround(terrain.terrainData, terrain.transform);
            UnderstorySeeder.Seed(terrain.terrainData, terrain.transform);
            SeedSurface(terrain);
            EditorUtility.SetDirty(terrain.terrainData);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        internal static void SeedSurface(Terrain terrain)
        {
            RouteObstacleClearance.Rebuild(terrain);
            var anchors = CollectAnchors();
            if (anchors.Count == 0) throw new InvalidOperationException("Ground Ecology requires vegetation islands.");
            TerrainData data = terrain.terrainData;
            PaintAlphamap(data, anchors);
            ThinDetails(data, anchors);
            PlaceDebris(terrain, anchors);
            RouteObstacleClearance.Rebuild(terrain);
            Debug.Log($"[GroundEcology] Painted {MovementCorridor.RouteCount} paths and {anchors.Count} islands.");
        }

        private static List<Vector2> CollectAnchors()
        {
            var anchors = new List<Vector2>();
            AddChildren(VegetationIslandSeeder.RootName, anchors);
            AddChildren(Phase2VegetationSeeder.RootName, anchors);
            return anchors;
        }

        private static void AddChildren(string rootName, List<Vector2> anchors)
        {
            GameObject root = GameObject.Find(rootName);
            if (root == null) return;
            foreach (Transform child in root.transform)
            {
                if (rootName == VegetationIslandSeeder.RootName)
                    anchors.Add(new Vector2(child.position.x, child.position.z));
                else
                {
                    Vector2 sum = Vector2.zero;
                    int count = 0;
                    foreach (Transform member in child)
                    {
                        sum += new Vector2(member.position.x, member.position.z);
                        count++;
                    }
                    if (count > 0) anchors.Add(sum / count);
                }
            }
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static float Trail(Vector2 p)
        {
            float closest = float.MaxValue;
            for (int route = 0; route < MovementCorridor.RouteCount; route++)
                closest = Mathf.Min(closest, MovementCorridor.MinDistanceToRoute(p, route));
            float wobble = (Mathf.PerlinNoise(p.x * .24f + 11f, p.y * .24f + 7f) - .5f) * .9f;
            return 1f - Smooth((closest + wobble - .8f) / 3.1f);
        }

        private static float[,] BuildTrailMask(int width, int height)
        {
            var mask = new float[width, height];
            const float reach = 4.7f;
            int rx = Mathf.CeilToInt(reach * width / 200f);
            int rz = Mathf.CeilToInt(reach * height / 200f);
            for (int route = 0; route < MovementCorridor.RouteCount; route++)
            for (int sample = 0; sample < MovementCorridor.SampleCount(route); sample++)
            {
                Vector2 center = MovementCorridor.GetSample(route, sample).Position;
                int cx = Mathf.RoundToInt((center.x + TerrainHalf) / 200f * width - .5f);
                int cz = Mathf.RoundToInt((center.y + TerrainHalf) / 200f * height - .5f);
                for (int z = Mathf.Max(0, cz - rz); z <= Mathf.Min(height - 1, cz + rz); z++)
                for (int x = Mathf.Max(0, cx - rx); x <= Mathf.Min(width - 1, cx + rx); x++)
                {
                    Vector2 p = new Vector2((x + .5f) / width * 200f - TerrainHalf,
                                            (z + .5f) / height * 200f - TerrainHalf);
                    float wobble = (Mathf.PerlinNoise(p.x * .24f + 11f, p.y * .24f + 7f) - .5f) * .9f;
                    float value = 1f - Smooth((Vector2.Distance(p, center) + wobble - .8f) / 3.1f);
                    mask[x, z] = Mathf.Max(mask[x, z], value);
                }
            }
            return mask;
        }

        private static float Island(Vector2 p, List<Vector2> anchors)
        {
            float influence = 0f;
            float irregular = (Mathf.PerlinNoise(p.x * .18f + 19f, p.y * .18f + 23f) - .5f) * 2.2f;
            foreach (Vector2 anchor in anchors)
            {
                float radius = p.x < -18f ? 8.5f : 7f;
                float d = Vector2.Distance(p, anchor) + irregular;
                influence = Mathf.Max(influence, 1f - Smooth((d - 1.5f) / radius));
            }
            return influence;
        }

        private static void PaintAlphamap(TerrainData data, List<Vector2> anchors)
        {
            int w = data.alphamapWidth, h = data.alphamapHeight;
            if (data.alphamapLayers < 4) throw new InvalidOperationException("Ground Ecology expects four terrain layers.");
            float[,,] map = data.GetAlphamaps(0, 0, w, h);
            float[,] trails = BuildTrailMask(w, h);
            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                Vector2 p = new Vector2((x + .5f) / w * 200f - TerrainHalf,
                                        (z + .5f) / h * 200f - TerrainHalf);
                float trail = trails[x, z];
                float island = Island(p, anchors) * (1f - trail * .7f);
                float fleck = Mathf.PerlinNoise(p.x * .53f + 3f, p.y * .53f + 29f);
                float soil = Mathf.Clamp01(trail * (.68f + .12f * fleck) + island * (.25f + .15f * fleck));
                float litter = island * (.24f + .18f * (1f - fleck)) + trail * .08f;
                float moss = island * (.12f + .10f * fleck);
                float keep = Mathf.Clamp01(1f - soil - litter - moss);
                map[z, x, 0] = map[z, x, 0] * keep + moss;
                map[z, x, 1] = map[z, x, 1] * keep + soil;
                map[z, x, 2] = map[z, x, 2] * keep + litter;
                map[z, x, 3] = map[z, x, 3] * keep;
                float sum = 0f;
                for (int i = 0; i < data.alphamapLayers; i++) sum += map[z, x, i];
                for (int i = 0; i < data.alphamapLayers; i++) map[z, x, i] /= sum;
            }
            data.SetAlphamaps(0, 0, map);
        }

        private static void ThinDetails(TerrainData data, List<Vector2> anchors)
        {
            int res = data.detailResolution;
            DetailPrototype[] prototypes = data.detailPrototypes;
            float[,] trails = BuildTrailMask(res, res);
            float[,] islands = new float[res, res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
                islands[x, z] = Island(new Vector2((x + .5f) / res * 200f - TerrainHalf,
                                                   (z + .5f) / res * 200f - TerrainHalf), anchors);
            for (int layer = 0; layer < prototypes.Length; layer++)
            {
                int[,] density = data.GetDetailLayer(0, 0, res, res, layer);
                bool grass = prototypes[layer].prototypeTexture != null &&
                    prototypes[layer].prototypeTexture.name.IndexOf("grass", StringComparison.OrdinalIgnoreCase) >= 0;
                bool changed = false;
                for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    int old = density[x, z];
                    if (old == 0) continue;
                    float trail = trails[x, z];
                    float island = islands[x, z];
                    // Break up the old continuous lawn even between islands; the
                    // stronger local masks keep the path centre lowest.
                    float background = grass ? .20f + .35f * Mathf.PerlinNoise(
                        x * .018f + 2f, z * .018f + 7f) : 0f;
                    float sparse = grass ? background + trail * .92f + island * .45f
                                         : trail * .95f;
                    float rim = (1f - trail) * island * (grass ? .12f : .18f);
                    float target = old * Mathf.Clamp01(1f - sparse + rim);
                    float fraction = target - Mathf.Floor(target);
                    float noise = Mathf.PerlinNoise(x * .73f + layer * 17f, z * .73f + 8f);
                    int next = Mathf.Clamp(Mathf.FloorToInt(target) + (noise < fraction ? 1 : 0), 0, old + 1);
                    if (next == old) continue;
                    density[x, z] = next;
                    changed = true;
                }
                if (changed) data.SetDetailLayer(0, 0, layer, density);
            }
        }

        private static void PlaceDebris(Terrain terrain, List<Vector2> anchors)
        {
            GameObject old = GameObject.Find(RootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            GameObject root = new GameObject(RootName);
            var random = new System.Random(Seed);
            GameObject[] rocks = UnderstoryAssetLocator.FindRockPrefabs();
            for (int i = 0; i < anchors.Count; i++)
            {
                Vector2 anchor = anchors[i];
                for (int j = 0; j < 4; j++)
                {
                    float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    float radius = 1.7f + (float)random.NextDouble() * 5.8f;
                    Vector2 p = anchor + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    if (Mathf.Abs(p.x) > 88f || Mathf.Abs(p.y) > 88f || Trail(p) > .28f) continue;
                    if (rocks.Length == 0 || j != 0) continue;
                    GameObject rock = (GameObject)PrefabUtility.InstantiatePrefab(rocks[i % rocks.Length], root.transform);
                    rock.name = "Small moss rock";
                    rock.transform.position = new Vector3(p.x, terrain.SampleHeight(new Vector3(p.x, 0, p.y)) + terrain.transform.position.y - .08f, p.y);
                    rock.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                    rock.transform.localScale *= .25f + (float)random.NextDouble() * .22f;
                    foreach (Collider collider in rock.GetComponentsInChildren<Collider>(true))
                        UnityEngine.Object.DestroyImmediate(collider);
                }
            }
            BuildSurfaceMeshes(terrain, anchors, root.transform, random);
        }

        private static void BuildSurfaceMeshes(Terrain terrain, List<Vector2> anchors, Transform parent,
            System.Random random)
        {
            if (!AssetDatabase.IsValidFolder(AssetFolder))
                AssetDatabase.CreateFolder("Assets/Terrain", "GroundEcology");
            var leafVertices = new List<Vector3>(); var leafTriangles = new List<int>();
            var darkGrassVertices = new List<Vector3>(); var darkGrassTriangles = new List<int>();
            var mossVertices = new List<Vector3>(); var mossTriangles = new List<int>();
            var rootVertices = new List<Vector3>(); var rootTriangles = new List<int>();
            foreach (Vector2 anchor in anchors)
            {
                for (int i = 0; i < 18; i++)
                {
                    Vector2 p = Offset(anchor, random, 1.2f, 7.5f);
                    if (!Inside(p) || Trail(p) > .4f) continue;
                    float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    float length = .16f + (float)random.NextDouble() * .2f;
                    Vector2 forward = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * length;
                    Vector2 side = new Vector2(-forward.y, forward.x) * .32f;
                    AddQuad(terrain, leafVertices, leafTriangles, p - forward, p + side,
                        p + forward, p - side, .035f);
                    Vector2 grassSpot = Offset(anchor, random, 2f, 8f);
                    if (Inside(grassSpot) && Trail(grassSpot) < .5f)
                        AddGrassBlade(terrain, darkGrassVertices, darkGrassTriangles,
                            grassSpot, angle, .16f + (float)random.NextDouble() * .23f);
                }
                for (int i = 0; i < 3; i++)
                {
                    Vector2 p = Offset(anchor, random, .8f, 5.5f);
                    if (!Inside(p) || Trail(p) > .3f) continue;
                    float radius = .32f + (float)random.NextDouble() * .38f;
                    AddQuad(terrain, mossVertices, mossTriangles,
                        p + new Vector2(-radius, -radius * .6f),
                        p + new Vector2(-radius * .75f, radius),
                        p + new Vector2(radius, radius * .7f),
                        p + new Vector2(radius * .8f, -radius), .022f);
                }
                Vector2 start = Offset(anchor, random, .35f, 1.3f);
                Vector2 end = start + (start - anchor).normalized * (1.2f + (float)random.NextDouble() * 1.4f);
                if (Inside(start) && Inside(end) && Trail(start) < .25f && Trail(end) < .25f)
                {
                    Vector2 direction = (end - start).normalized;
                    Vector2 side = new Vector2(-direction.y, direction.x);
                    AddQuad(terrain, rootVertices, rootTriangles, start - side * .09f,
                        start + side * .09f, end + side * .025f, end - side * .025f, .052f);
                }
            }
            AttachMesh(parent, "Leaf litter and dead leaves", leafVertices, leafTriangles,
                new Color(.30f, .18f, .075f), "Leaves");
            AttachMesh(parent, "Darker island grass", darkGrassVertices, darkGrassTriangles,
                new Color(.10f, .24f, .075f), "DarkGrass");
            AttachMesh(parent, "Moss patches", mossVertices, mossTriangles,
                new Color(.12f, .23f, .09f), "Moss");
            AttachMesh(parent, "Exposed roots", rootVertices, rootTriangles,
                new Color(.21f, .13f, .075f), "Roots");
        }

        private static bool Inside(Vector2 p) => Mathf.Abs(p.x) < 90f && Mathf.Abs(p.y) < 90f;

        private static Vector2 Offset(Vector2 center, System.Random random, float min, float max)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float radius = min + (float)random.NextDouble() * (max - min);
            return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        private static void AddQuad(Terrain terrain, List<Vector3> vertices, List<int> triangles,
            Vector2 a, Vector2 b, Vector2 c, Vector2 d, float lift)
        {
            int first = vertices.Count;
            Vector2[] points = { a, b, c, d };
            foreach (Vector2 point in points)
                vertices.Add(new Vector3(point.x,
                    terrain.SampleHeight(new Vector3(point.x, 0f, point.y)) + terrain.transform.position.y + lift,
                    point.y));
            int[] indices = { 0, 1, 2, 0, 2, 3 };
            foreach (int index in indices) triangles.Add(first + index);
        }

        private static void AddGrassBlade(Terrain terrain, List<Vector3> vertices,
            List<int> triangles, Vector2 p, float angle, float height)
        {
            float ground = terrain.SampleHeight(new Vector3(p.x, 0f, p.y)) + terrain.transform.position.y;
            Vector2 side = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * .055f;
            int first = vertices.Count;
            Vector3 left = new Vector3(p.x - side.x, ground + .01f, p.y - side.y);
            Vector3 right = new Vector3(p.x + side.x, ground + .01f, p.y + side.y);
            Vector3 tip = new Vector3(p.x + side.y * .7f, ground + height, p.y - side.x * .7f);
            vertices.Add(left); vertices.Add(right); vertices.Add(tip);
            vertices.Add(left); vertices.Add(right); vertices.Add(tip);
            int[] indices = { 0, 2, 1, 3, 4, 5 };
            foreach (int index in indices) triangles.Add(first + index);
        }

        private static void AttachMesh(Transform parent, string name, List<Vector3> vertices,
            List<int> triangles, Color color, string assetName)
        {
            if (vertices.Count == 0) return;
            var generated = new Mesh { name = assetName };
            generated.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            generated.SetVertices(vertices);
            generated.SetTriangles(triangles, 0);
            generated.RecalculateNormals();
            string meshPath = AssetFolder + "/" + assetName + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null) { AssetDatabase.CreateAsset(generated, meshPath); mesh = generated; }
            else { EditorUtility.CopySerialized(generated, mesh); UnityEngine.Object.DestroyImmediate(generated); EditorUtility.SetDirty(mesh); }
            string materialPath = AssetFolder + "/" + assetName + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.color = color;
            material.SetFloat("_Glossiness", 0f);
            EditorUtility.SetDirty(material);
            GameObject surface = new GameObject(name);
            surface.transform.SetParent(parent);
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            surface.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
