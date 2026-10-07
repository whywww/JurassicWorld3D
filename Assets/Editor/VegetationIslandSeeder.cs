using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    /// <summary>
    /// Authored clearing islands. This runs as part of BakeScene, so a later
    /// rebuild retains the composition. Props are visual only; route clearance is
    /// measured at their roots and all imported colliders are removed.
    /// </summary>
    internal static class VegetationIslandSeeder
    {
        internal const string RootName = "Phase 2 Vegetation Islands";

        [MenuItem("Jurassic World/Environment/Vegetation/Rebuild Midground Islands")]
        private static void RebuildMidgroundIslandsMenu() => JurassicMenuGuard.Run(RebuildMidgroundIslands, true);

        [MenuItem("Jurassic World/Environment/Vegetation/Rebuild Midground Islands", true)]
        private static bool RebuildMidgroundIslandsMenuEnabled() => JurassicMenuGuard.CanRun;

        public static void RebuildMidgroundIslands()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            GameObject old = GameObject.Find(RootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            Seed();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static readonly Vector2[] FernOffsets =
        {
            new Vector2(-1.8f, -0.5f), new Vector2(-0.9f, 1.1f),
            new Vector2(0.4f, -1.5f), new Vector2(1.4f, 0.7f),
            new Vector2(2.1f, -0.6f),
        };

        private static readonly Vector2[] BushOffsets =
        {
            new Vector2(-1.2f, 0.2f), new Vector2(0.9f, 0.8f),
            new Vector2(1.5f, -1.0f),
        };

        internal static void Seed()
        {
            Terrain terrain = UnityEngine.Object.FindObjectOfType<Terrain>();
            GameObject[] trees = NatureStarterKitLocator.FindTreePrefabs();
            GameObject[] bushes = NatureStarterKitLocator.FindDetailMeshes()
                .Where(p => p.name.IndexOf("bush", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(p => p.name, StringComparer.Ordinal).ToArray();
            GameObject[] ferns = UnderstoryAssetLocator.FindFernPrefabs();
            GameObject[] deadwood = UnderstoryAssetLocator.FindDeadwoodPrefabs();
            if (terrain == null || trees.Length == 0 || bushes.Length == 0 || ferns.Length == 0)
                throw new InvalidOperationException("[VegetationIslands] Missing terrain, tree, bush, or fern assets.");

            var root = new GameObject(RootName);

            // The two middle islands stagger in depth around the dinosaur's
            // initial view; neither sits on a route or directly on a spawn.
            BuildIsland(root.transform, terrain, "A1_DinosaurFront", new Vector2(1.5f, 5f),
                18f, 1, trees, bushes, ferns, null);
            BuildIsland(root.transform, terrain, "A2_DinosaurBack", new Vector2(-3.5f, 28f),
                -24f, 1, trees, bushes, ferns, null);

            // The existing central terrain tree stays in TerrainData. Only its
            // new understory and companion sapling belong to this child group.
            Vector2 centralTree = FindCentralTree(terrain, new Vector2(-14f, -1f));
            BuildIsland(root.transform, terrain, "B_CentralTree", centralTree,
                155f, 1, trees, bushes, ferns, deadwood.Length > 0 ? deadwood[0] : null);

            // Rock_Large_00 is the right-hand central rock at roughly (16,48).
            // Keep it under Environment Props: spawn avoidance reads that parent.
            GameObject rock = GameObject.Find("Rock_Large_00");
            if (rock == null)
                throw new InvalidOperationException("[VegetationIslands] Missing Rock_Large_00 anchor.");
            Vector2 rockXZ = new Vector2(rock.transform.position.x, rock.transform.position.z);
            BuildIsland(root.transform, terrain, "C_RightRock", rockXZ + new Vector2(4.5f, 0.5f),
                35f, 1, trees, bushes, ferns, null);

            BuildIsland(root.transform, terrain, "D_OppositePath", new Vector2(28f, 21f),
                -55f, 2, trees, bushes, ferns, null);

            // Broken sightlines across the whole central clearing. These sit at
            // different depths and alternate sides of the dinosaur routes.
            BuildIsland(root.transform, terrain, "E_WestApproach", new Vector2(-13f, -24f),
                62f, 1, trees, bushes, ferns, deadwood.Length > 0 ? deadwood[0] : null);
            BuildIsland(root.transform, terrain, "F_EastApproach", new Vector2(13f, -13f),
                -18f, 1, trees, bushes, ferns, null);
            BuildIsland(root.transform, terrain, "G_WestMiddle", new Vector2(-19f, 18f),
                119f, 1, trees, bushes, ferns, deadwood.Length > 0 ? deadwood[0] : null);
            BuildIsland(root.transform, terrain, "H_EastMiddle", new Vector2(18f, 34f),
                -42f, 1, trees, bushes, ferns, null);
            BuildIsland(root.transform, terrain, "I_NorthMiddle", new Vector2(-9f, 48f),
                27f, 1, trees, bushes, ferns, deadwood.Length > 0 ? deadwood[0] : null);

            Debug.Log($"[VegetationIslands] Ten islands baked; B tree={centralTree}, C rock={rockXZ}.");
        }

        private static Vector2 FindCentralTree(Terrain terrain, Vector2 target)
        {
            TerrainData data = terrain.terrainData;
            TreeInstance[] instances = data.treeInstances;
            float best = float.PositiveInfinity;
            Vector2 winner = target;
            foreach (TreeInstance tree in instances)
            {
                Vector2 xz = new Vector2(
                    terrain.transform.position.x + tree.position.x * data.size.x,
                    terrain.transform.position.z + tree.position.z * data.size.z);
                if (xz.x < -30f || xz.x > -5f || xz.y < -12f || xz.y > 12f)
                    continue;
                float score = Vector2.SqrMagnitude(xz - target) - tree.heightScale * 2f;
                if (score >= best) continue;
                best = score;
                winner = xz;
            }
            if (float.IsPositiveInfinity(best))
                throw new InvalidOperationException("[VegetationIslands] No existing central terrain tree near the clearing.");
            return winner;
        }

        private static void BuildIsland(Transform root, Terrain terrain, string name,
            Vector2 center, float yaw, int saplingCount, GameObject[] trees,
            GameObject[] bushes, GameObject[] ferns, GameObject accent)
        {
            var group = new GameObject(name);
            group.transform.SetParent(root);
            group.transform.position = new Vector3(center.x, GroundY(terrain, center), center.y);

            for (int i = 0; i < saplingCount; i++)
            {
                Vector2 offset = i == 0 ? new Vector2(-0.2f, 1.2f) : new Vector2(2.5f, 1.1f);
                Place(trees[(i + (name[0] - 'A')) % trees.Length], group.transform,
                    terrain, center + Rotate(offset, yaw), yaw + 35f + i * 83f,
                    4.5f + i * 0.7f, true, 4.4f, "Sapling_" + (i + 1));
            }

            for (int i = 0; i < FernOffsets.Length; i++)
                Place(ferns[(i + (name[0] - 'A')) % ferns.Length], group.transform,
                    terrain, center + Rotate(FernOffsets[i], yaw), yaw + 37f * i,
                    1.9f + 0.16f * (i % 3), false, 2.2f, "Fern_" + (i + 1));

            for (int i = 0; i < BushOffsets.Length; i++)
                Place(bushes[(i + (name[0] - 'A')) % bushes.Length], group.transform,
                    terrain, center + Rotate(BushOffsets[i], yaw), yaw - 29f * i,
                    2.9f + 0.22f * i, false, 2.2f, "Broadleaf_" + (i + 1));

            if (accent != null)
                Place(accent, group.transform, terrain,
                    center + Rotate(new Vector2(1.2f, 1.5f), yaw), yaw + 70f,
                    2.2f, false, 1.9f, "Deadwood");
        }

        private static void Place(GameObject prefab, Transform parent, Terrain terrain,
            Vector2 desired, float yaw, float targetSize, bool heightBased,
            float routeClearance, string name)
        {
            Vector2 xz = FindSafeSpot(desired, routeClearance);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;
            instance.transform.position = new Vector3(xz.x, GroundY(terrain, xz), xz.y);
            // Some saplings lean into the gap; the trunk is grounded again below.
            float lean = name.StartsWith("Sapling_", StringComparison.Ordinal)
                ? (parent.GetSiblingIndex() % 2 == 0 ? 13f : -11f) : 0f;
            instance.transform.rotation = Quaternion.Euler(lean, yaw, 0f);

            Bounds bounds = RenderBounds(instance);
            float currentSize = heightBased ? bounds.size.y : Mathf.Max(bounds.size.x, bounds.size.z);
            if (currentSize < 0.01f)
                throw new InvalidOperationException("[VegetationIslands] Empty renderer bounds: " + prefab.name);
            float scale = targetSize / currentSize;
            instance.transform.localScale *= scale;
            bounds = RenderBounds(instance);
            instance.transform.position += Vector3.up * (GroundY(terrain, xz) - bounds.min.y - 0.04f);

            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            GameObjectUtility.SetStaticEditorFlags(instance, StaticEditorFlags.BatchingStatic);
        }

        private static Vector2 FindSafeSpot(Vector2 desired, float clearance)
        {
            if (IsClear(desired, clearance)) return desired;
            // Keep all adjustments local, so each island remains a single mass.
            for (float radius = 0.6f; radius <= 8.4f; radius += 0.6f)
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI * 2f / 16f;
                    Vector2 candidate = desired + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    if (IsClear(candidate, clearance)) return candidate;
                }
            throw new InvalidOperationException($"[VegetationIslands] No route-safe spot near {desired} ({clearance}m).");
        }

        private static bool IsClear(Vector2 xz, float clearance)
        {
            // Avoid the player's initial camera and dinosaur spawn centers.
            if (Vector2.Distance(xz, new Vector2(0f, -16f)) < 4f) return false;
            Vector2[] spawns =
            {
                new Vector2(0f, 11f), new Vector2(10f, 27f),
                new Vector2(-10f, 19f), new Vector2(8f, 15f),
                new Vector2(-13f, 35f)
            };
            foreach (Vector2 spawn in spawns)
                if (Vector2.Distance(xz, spawn) < 3f) return false;
            for (int route = 0; route < MovementCorridor.RouteCount; route++)
                if (MovementCorridor.MinDistanceToRoute(xz, route) < clearance)
                    return false;
            return true;
        }

        private static float GroundY(Terrain terrain, Vector2 xz) =>
            terrain.SampleHeight(new Vector3(xz.x, 0f, xz.y)) + terrain.transform.position.y;

        private static Vector2 Rotate(Vector2 point, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians), cos = Mathf.Cos(radians);
            return new Vector2(point.x * cos - point.y * sin, point.x * sin + point.y * cos);
        }

        private static Bounds RenderBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException("[VegetationIslands] Prefab has no renderer: " + go.name);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
