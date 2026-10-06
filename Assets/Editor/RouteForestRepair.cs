using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    public static class RouteForestRepair
    {
        // One-time recovery reads exported Git assets; only obstacle positions are copied.
        // Terrain height/paint, model configuration and routes remain in the active scene.
        public static void Restore()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the current scene and exit Play Mode before restoring forest positions.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Terrain terrain = Terrain.activeTerrain;
            TerrainData baseline = AssetDatabase.LoadAssetAtPath<TerrainData>("Assets/Editor/RouteRepairBaseline/OriginalTerrain.asset");
            if (baseline == null) throw new InvalidOperationException("Missing exported baseline terrain.");
            TreeInstance[] before = terrain.terrainData.treeInstances, original = baseline.treeInstances;
            if (before.Length != original.Length) throw new InvalidOperationException("Tree instance counts do not match baseline; nothing saved.");
            if (terrain.terrainData.treePrototypes.Length != baseline.treePrototypes.Length)
                throw new InvalidOperationException("Tree prototypes do not match baseline.");
            for (int i = 0; i < baseline.treePrototypes.Length; i++)
                if (baseline.treePrototypes[i].prefab != terrain.terrainData.treePrototypes[i].prefab)
                    throw new InvalidOperationException("Tree prototype identity mismatch at " + i);
            for (int i = 0; i < before.Length; i++)
                if (before[i].prototypeIndex != original[i].prototypeIndex || before[i].widthScale != original[i].widthScale
                    || before[i].heightScale != original[i].heightScale || before[i].rotation != original[i].rotation
                    || !before[i].color.Equals(original[i].color) || !before[i].lightmapColor.Equals(original[i].lightmapColor))
                    throw new InvalidOperationException("Tree instance identity mismatch at " + i + "; nothing saved.");

            var targets = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).Where(IsObstaclePosition).ToArray();
            var sourceScene = EditorSceneManager.OpenScene("Assets/Editor/RouteRepairBaseline/OriginalMain.unity", OpenSceneMode.Additive);
            var positions = new Dictionary<Transform, Vector3>();
            var oldPositions = new Dictionary<Transform, Vector3>();
            var originalWorld = new Dictionary<Transform, Vector3>();
            try
            {
                var sources = sourceScene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                    .ToDictionary(Identity);
                foreach (Transform target in targets)
                {
                    if (!sources.TryGetValue(Identity(target), out Transform source) || source.name != target.name
                        || source.localScale != target.localScale || Quaternion.Angle(source.localRotation, target.localRotation) > .001f)
                        throw new InvalidOperationException("Cannot reliably match original obstacle " + target.name + "; nothing saved.");
                    positions.Add(target, source.localPosition);
                    originalWorld.Add(target, source.position);
                    oldPositions.Add(target, target.localPosition);
                }
            }
            finally { EditorSceneManager.CloseScene(sourceScene, true); EditorSceneManager.SetActiveScene(scene); }

            var restored = (TreeInstance[])before.Clone();
            int restoredTrees = 0, restoredProps = 0;
            for (int i = 0; i < restored.Length; i++)
            {
                if (restored[i].position != original[i].position) restoredTrees++;
                restored[i].position = original[i].position;
            }
            try
            {
                foreach (var pair in positions)
                {
                    if (pair.Key.localPosition != pair.Value) restoredProps++;
                    pair.Key.localPosition = pair.Value;
                }
                terrain.terrainData.SetTreeInstances(restored, false);
                TerrainTreeObstacleRegistry.Invalidate();
                RouteObstacleClearance.Rebuild(terrain);
                TreeInstance[] final = terrain.terrainData.treeInstances;
                int keptTrees = 0, keptProps = 0;
                for (int i = 0; i < final.Length; i++)
                {
                    Vector3 offset = Vector3.Scale(final[i].position - original[i].position, terrain.terrainData.size);
                    if (new Vector2(offset.x, offset.z).magnitude > RouteObstacleClearance.MaxRelocation + .001f)
                        throw new InvalidOperationException("Tree relocated beyond local limit: " + i);
                    if (final[i].position == original[i].position) keptTrees++;
                }
                foreach (var pair in originalWorld)
                {
                    Vector3 offset = pair.Key.position - pair.Value;
                    if (new Vector2(offset.x, offset.z).magnitude > RouteObstacleClearance.MaxRelocation + .001f)
                        throw new InvalidOperationException("Scene obstacle relocated beyond local limit: " + pair.Key.name);
                    if (pair.Key.position == pair.Value) keptProps++;
                }
                GroundEcologyPass.RepaintRoutes(terrain);
                EditorUtility.SetDirty(terrain.terrainData);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log($"FOREST_RESTORED_OK: restoredTrees={restoredTrees} restoredTransforms={restoredProps}; originalTreesKept={keptTrees}/{final.Length}, originalTransformsKept={keptProps}/{targets.Length}; all moves <=12m.");
            }
            catch
            {
                terrain.terrainData.SetTreeInstances(before, false);
                foreach (var pair in oldPositions) pair.Key.localPosition = pair.Value;
                TerrainTreeObstacleRegistry.Invalidate(); throw;
            }
        }

        private static string Identity(Transform transform)
        {
            GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(transform);
            return id.targetObjectId + ":" + id.targetPrefabId;
        }

        private static bool IsObstaclePosition(Transform transform)
        {
            if (transform.GetComponent<SceneTreeObstacle>() != null) return true;
            string parent = transform.parent == null ? "" : transform.parent.name;
            return parent == "Environment Props" || parent == "Rocks" && transform.parent.parent?.name == "Understory Pass"
                || parent == "Ground Pass Rocks" || parent == "Ground Ecology" && transform.name == "Small moss rock"
                || parent == VegetationIslandSeeder.RootName && transform.name == "C_RightRock";
        }

        public static void Inspect()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            foreach (var prototype in Terrain.activeTerrain.terrainData.treePrototypes)
                Describe(prototype.prefab);
            foreach (var tree in UnityEngine.Object.FindObjectsOfType<SceneTreeObstacle>().Take(5))
                Describe(tree.gameObject);
            foreach (var agent in UnityEngine.Object.FindObjectsOfType<DinosaurAgent>())
            {
                var model = agent.GetComponent<DinosaurFbxLoader>().ModelRoot;
                var bounds = new Bounds(); bool first = true;
                foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var mesh = new Mesh(); skin.BakeMesh(mesh);
                    foreach (var vertex in mesh.vertices)
                    {
                        var point = agent.transform.InverseTransformPoint(skin.transform.TransformPoint(vertex));
                        if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
                    }
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
                Debug.Log($"FOREST_MODEL {agent.name}: {bounds} scale={agent.transform.lossyScale}");
            }
        }

        private static void Describe(GameObject root)
        {
            Debug.Log("FOREST_TREE " + root.name);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                Debug.Log($"FOREST_MESH {renderer.name}: {mesh?.name} submeshes={mesh?.subMeshCount} readable={mesh?.isReadable} materials={string.Join(",", renderer.sharedMaterials.Select(m => m == null ? "null" : m.name))}");
            }
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                Debug.Log($"FOREST_COLLIDER {collider.name}: {collider.GetType().Name} {collider.bounds}");
        }
    }
}
