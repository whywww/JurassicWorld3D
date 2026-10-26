using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class FernHeightAcceptance
    {
        private const string Scene = "Assets/Scenes/Main.unity";
        private const string Output = "Logs/FernHeight";
        private const float Limit = FernHeightAdjustment.MaximumHeight;

        private static bool IsBatch(Transform t) => t.name == "Boundary Static Batches"
            || (t.parent != null && IsBatch(t.parent));

        public static void ApplyMain()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the current scene and exit Play first.");
            EditorSceneManager.OpenScene(Scene);
            Directory.CreateDirectory(Output);
            var ferns = FernHeightAdjustment.SceneFerns();
            Require(ferns.Length > 0, "No fern sources found.");
            var targets = ferns.Select(f => f.transform).ToHashSet();
            var other = EditorSceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Where(t => !targets.Contains(t) && !IsBatch(t))
                .Select(t => (t, t.localPosition, t.localRotation, t.localScale)).ToArray();
            var before = ferns.Select(f => (root: f, bounds: BushHeightAdjustment.MeshBounds(f),
                position: f.transform.position, rotation: f.transform.rotation, scale: f.transform.localScale)).ToArray();
            var terrains = Object.FindObjectsOfType<Terrain>();
            var densities = terrains.Select(t => t.terrainData.detailPrototypes.Select((p, i) => t.terrainData
                .GetDetailLayer(0, 0, t.terrainData.detailWidth, t.terrainData.detailHeight, i).Cast<int>().ToArray()).ToArray()).ToArray();
            var prototypes = terrains.Select(t => t.terrainData.detailPrototypes.Select(JsonUtility.ToJson).ToArray()).ToArray();
            var trees = terrains.Select(t => t.terrainData.treeInstances).ToArray();
            var heights = terrains.Select(t => t.terrainData.GetHeights(0, 0, t.terrainData.heightmapResolution,
                t.terrainData.heightmapResolution).Cast<float>().ToArray()).ToArray();
            var paint = terrains.Select(t => t.terrainData.GetAlphamaps(0, 0, t.terrainData.alphamapWidth,
                t.terrainData.alphamapHeight).Cast<float>().ToArray()).ToArray();
            int vertices = BatchVertexCount();
            int changed = FernHeightAdjustment.ApplyCurrent();
            foreach (var f in before)
            {
                Require(f.root != null, "Fern source object removed.");
                Bounds after = BushHeightAdjustment.MeshBounds(f.root);
                Require(after.size.y <= Limit + .0002f, "Fern height exceeded: " + f.root.name);
                Require(Mathf.Abs(after.min.y - f.bounds.min.y) < .0002f, "Fern base moved: " + f.root.name);
                Require(f.root.transform.position.x == f.position.x && f.root.transform.position.z == f.position.z
                    && f.root.transform.rotation == f.rotation, "Fern pose changed: " + f.root.name);
                if (f.bounds.size.y <= Limit + .0001f)
                    Require(f.root.transform.position == f.position && f.root.transform.localScale == f.scale, "Short fern changed.");
            }
            foreach (var t in other)
                Require(t.t != null && t.t.localPosition == t.localPosition && t.t.localRotation == t.localRotation
                    && t.t.localScale == t.localScale, "Unrelated transform changed.");
            Require(FernHeightAdjustment.SceneFerns().Length == ferns.Length && BatchVertexCount() == vertices, "Population/batch geometry count changed.");
            Require(FernHeightAdjustment.ApplyCurrent() == 0, "Second pass was not a no-op.");
            for (int ti = 0; ti < terrains.Length; ti++)
            {
                var data = terrains[ti].terrainData;
                Require(trees[ti].SequenceEqual(data.treeInstances), "Terrain trees changed.");
                Require(heights[ti].SequenceEqual(data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution).Cast<float>()), "Terrain heights changed.");
                Require(paint[ti].SequenceEqual(data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight).Cast<float>()), "Terrain paint changed.");
                for (int i = 0; i < data.detailPrototypes.Length; i++)
                {
                    var p = data.detailPrototypes[i];
                    Require(densities[ti][i].SequenceEqual(data.GetDetailLayer(0, 0, data.detailWidth, data.detailHeight, i).Cast<int>()), "Detail density changed.");
                    if (!FernHeightAdjustment.IsFern(p.prototype)) Require(prototypes[ti][i] == JsonUtility.ToJson(p), "Unrelated prototype changed.");
                    else Require(BushHeightAdjustment.MeshBounds(p.prototype).size.y * p.maxHeight <= Limit + .0001f, "Fern detail height exceeded.");
                }
            }
            TestParentAndLod();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(Scene);
            Require(FernHeightAdjustment.ApplyCurrent() == 0, "Saved fern height not stable.");
            CenterForestGapFill.ValidateBushReplay();
            var report = new StringBuilder("# Fern height acceptance\n\n");
            report.AppendLine($"Fern sources including inactive/boundary originals: {ferns.Length}; changed sources/layers: {changed}.");
            report.AppendLine($"Actual maximum: {before.Max(f => f.bounds.size.y):F4} m -> {FernHeightAdjustment.SceneFerns().Max(f => BushHeightAdjustment.MeshBounds(f).size.y):F4} m.");
            report.AppendLine("PASS: <=1.6m; base height/XZ/rotation; unchanged short ferns, bushes and other transforms; source population/batch vertex count; terrain trees/heights/paint/densities/unrelated prototypes; second pass; save/reload; scaled parent, LOD, interactive bounds.");
            File.WriteAllText(Output + "/Acceptance.md", report.ToString());
            Debug.Log("FERN_HEIGHT_ACCEPTANCE_OK: " + report);
        }

        private static int BatchVertexCount() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<MeshFilter>(true)).Where(f => IsBatch(f.transform))
            .Sum(f => f.sharedMesh == null ? 0 : f.sharedMesh.vertexCount);

        private static void TestParentAndLod()
        {
            var parent = new GameObject("Fern height scaled-parent probe");
            try
            {
                parent.transform.SetPositionAndRotation(new Vector3(12, 5, 12), Quaternion.Euler(0, 37, 0));
                parent.transform.localScale = new Vector3(2, 3, 2);
                var fern = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                    UnderstoryAssetLocator.FernFolder + "/Fern02_A.prefab"), parent.transform);
                fern.transform.localScale *= 10;
                var lod = fern.AddComponent<LODGroup>(); lod.SetLODs(new[] { new LOD(.02f, fern.GetComponentsInChildren<Renderer>()) });
                var foliage = fern.AddComponent<InteractiveFoliage>();
                foliage.localBounds = BushHeightAdjustment.MeshBounds(fern, true);
                Bounds before = BushHeightAdjustment.MeshBounds(fern), local = foliage.localBounds;
                Require(FernHeightAdjustment.Clamp(fern), "Oversized fern not reduced.");
                Bounds after = BushHeightAdjustment.MeshBounds(fern);
                Require(after.size.y <= Limit + .0001f && Mathf.Abs(before.min.y - after.min.y) < .0002f, "Scaled-parent height/base failed.");
                Require(foliage.localBounds == local && lod.size > 0, "Local interaction bounds/LOD invalid.");
                Require(!FernHeightAdjustment.Clamp(fern), "Scaled-parent second pass changed.");
            }
            finally { Object.DestroyImmediate(parent); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
