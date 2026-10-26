using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    internal static class BushHeightAdjustment
    {
        internal const float MaximumHeight = 2f;
        private const float Tolerance = .0001f;
        private const string SourceFolder = "Assets/ThirdParty/NatureStarterKit2/Nature/";

        internal static bool IsBush(GameObject root)
        {
            if (root == null) return false;
            string path = EditorUtility.IsPersistent(root) ? AssetDatabase.GetAssetPath(root)
                : PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            return Enumerable.Range(1, 6).Any(i => path == SourceFolder + "bush0" + i + ".prefab");
        }

        internal static GameObject[] SceneBushes() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Where(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) && IsBush(t.gameObject))
            .Select(t => t.gameObject).ToArray();

        // Transform every mesh bound, including non-first LODs. Renderer bounds can include
        // shader displacement padding, so measure the underlying geometry instead.
        internal static Bounds MeshBounds(GameObject root, bool local = false)
        {
            bool first = true;
            Bounds result = default;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<Renderer>() == null) continue;
                Bounds b = filter.sharedMesh.bounds;
                for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                {
                    Vector3 p = filter.transform.TransformPoint(new Vector3(x == 0 ? b.min.x : b.max.x,
                        y == 0 ? b.min.y : b.max.y, z == 0 ? b.min.z : b.max.z));
                    if (local) p = root.transform.InverseTransformPoint(p);
                    if (first) { result = new Bounds(p, Vector3.zero); first = false; }
                    else result.Encapsulate(p);
                }
            }
            if (first) throw new InvalidOperationException("Bush mesh missing: " + root.name);
            return result;
        }

        internal static bool Clamp(GameObject root)
        {
            if (!IsBush(root)) return false;
            return ClampToHeight(root, MaximumHeight);
        }

        internal static bool ClampToHeight(GameObject root, float maximumHeight)
        {
            Bounds before = MeshBounds(root);
            if (before.size.y <= maximumHeight + Tolerance) return false;
            // Leave a tiny numerical margin so a second pass is a true no-op.
            float ratio = (maximumHeight - Tolerance) / before.size.y;
            root.transform.localScale *= ratio;
            root.transform.position += Vector3.up * (before.min.y - MeshBounds(root).min.y);
            foreach (LODGroup lod in root.GetComponentsInChildren<LODGroup>(true))
            {
                lod.RecalculateBounds();
                PrefabUtility.RecordPrefabInstancePropertyModifications(lod);
            }
            // InteractiveFoliage.localBounds is in root space: it scales automatically.
            // SceneTreeObstacle sections/radius, in contrast, are stored in world units.
            foreach (SceneTreeObstacle obstacle in root.GetComponentsInChildren<SceneTreeObstacle>(true))
            {
                obstacle.SetRadius(obstacle.Radius * ratio);
                var sections = obstacle.trunkSections;
                for (int i = 0; i < sections.Length; i++)
                {
                    var section = sections[i];
                    section.center *= ratio;
                    section.radius *= ratio;
                    section.bottom = before.min.y + (section.bottom - before.min.y) * ratio;
                    section.top = before.min.y + (section.top - before.min.y) * ratio;
                    sections[i] = section;
                }
                EditorUtility.SetDirty(obstacle);
                PrefabUtility.RecordPrefabInstancePropertyModifications(obstacle);
            }
            if (PrefabUtility.IsPartOfPrefabInstance(root))
                PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
            EditorUtility.SetDirty(root.transform);
            return true;
        }

        internal static int ClampDetails(TerrainData data) => ClampDetails(data, IsBush, MaximumHeight);

        internal static int ClampDetails(TerrainData data, Func<GameObject, bool> matches, float maximumHeight)
        {
            DetailPrototype[] prototypes = data.detailPrototypes;
            int changed = 0;
            foreach (DetailPrototype p in prototypes)
            {
                if (!p.usePrototypeMesh || !matches(p.prototype)) continue;
                float meshHeight = MeshBounds(p.prototype).size.y;
                float maximumScale = (maximumHeight - Tolerance) / meshHeight;
                if (p.maxHeight <= maximumScale) continue;
                float ratio = maximumScale / p.maxHeight;
                p.minHeight *= ratio;
                p.maxHeight = maximumScale;
                changed++;
            }
            if (changed > 0)
            {
                data.detailPrototypes = prototypes;
                EditorUtility.SetDirty(data);
            }
            return changed;
        }

        internal static int ApplyCurrent()
        {
            int changed = SceneBushes().Count(Clamp);
            foreach (Terrain terrain in UnityEngine.Object.FindObjectsOfType<Terrain>())
                changed += ClampDetails(terrain.terrainData);
            if (changed > 0)
            {
                TerrainTreeObstacleRegistry.Invalidate();
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }
            return changed;
        }

        [MenuItem("Jurassic World/Environment/Limit Bush Height to 2m")]
        private static void ApplyMenu() => JurassicMenuGuard.Run(() =>
        {
            ApplyCurrent();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }, true);
    }
}
