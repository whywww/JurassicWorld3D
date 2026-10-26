using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    internal static class FernHeightAdjustment
    {
        internal const float MaximumHeight = 1.6f;
        private const string Backdrop = "Assets/Environment/Backdrop/Prepared/RainforestBackdrop.prefab";
        private const string BoundaryMeshes = "Assets/Environment/Backdrop/Prepared/Meshes/BoundaryUnderstory";

        internal static bool IsFern(GameObject root)
        {
            if (root == null) return false;
            string path = EditorUtility.IsPersistent(root) ? AssetDatabase.GetAssetPath(root)
                : PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            return path.StartsWith(UnderstoryAssetLocator.FernFolder + "/", StringComparison.Ordinal)
                || path == "Assets/Environment/Terrain/Prepared/ForestFloor/LowFern.prefab";
        }

        internal static bool IsBoundaryFern(GameObject root)
        {
            var filter = root.GetComponent<MeshFilter>();
            if (filter == null) return false;
            string path = AssetDatabase.GetAssetPath(filter.sharedMesh);
            return path == BoundaryMeshes + "2.asset" || path == BoundaryMeshes + "3.asset";
        }

        internal static GameObject[] SceneFerns() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Where(t => (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) && IsFern(t.gameObject)) || IsBoundaryFern(t.gameObject))
            .Select(t => t.gameObject).ToArray();

        internal static bool Clamp(GameObject root) => (IsFern(root) || IsBoundaryFern(root))
            && BushHeightAdjustment.ClampToHeight(root, MaximumHeight);

        internal static int ClampDetails(TerrainData data) => BushHeightAdjustment.ClampDetails(data, IsFern, MaximumHeight);

        internal static DetailPrototype LimitPrototype(DetailPrototype p)
        {
            float max = (MaximumHeight - .0001f) / BushHeightAdjustment.MeshBounds(p.prototype).size.y;
            if (p.maxHeight > max) { p.minHeight *= max / p.maxHeight; p.maxHeight = max; }
            return p;
        }

        private static int ClampBoundaryPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Backdrop) == null) return 0;
            GameObject root = PrefabUtility.LoadPrefabContents(Backdrop);
            try
            {
                int changed = 0;
                var groups = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == RainforestLightForest.UnderstoryName).ToArray();
                foreach (var group in groups)
                {
                    int count = group.Cast<Transform>().Count(t => Clamp(t.gameObject));
                    if (count == 0) continue;
                    RainforestLightForest.BatchGeometry(group.parent, group.gameObject.layer);
                    changed += count;
                }
                if (changed > 0) PrefabUtility.SaveAsPrefabAsset(root, Backdrop);
                return changed;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        internal static int ApplyCurrent()
        {
            int changed = ClampBoundaryPrefab();
            // Boundary originals are owned by the backdrop prefab and updated above.
            changed += SceneFerns().Where(f => !IsBoundaryFern(f)).Count(Clamp);
            foreach (Terrain terrain in UnityEngine.Object.FindObjectsOfType<Terrain>()) changed += ClampDetails(terrain.terrainData);
            if (changed > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return changed;
        }
    }
}
