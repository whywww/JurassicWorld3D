using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace JurassicWorld.Editor
{
    internal static class NorthwestAutumnForest
    {
        internal const string GroupName = "Northwest Autumn Region";
        internal static float SignedDistance(Vector2 p)
        {
            float eastEdge = 6f * Mathf.Sin(p.y * .055f) + 2f * Mathf.Sin(p.y * .13f);
            float southEdge = 6f * Mathf.Sin(p.x * .06f) + 2f * Mathf.Sin(p.x * .11f);
            return Mathf.Min(eastEdge - 10.6f - p.x, p.y - southEdge - 10.6f);
        }
        internal static float Probability(Vector2 p)
        {
            float d = SignedDistance(p);
            float cluster = Mathf.Lerp(.88f, 1f, Mathf.PerlinNoise((p.x + 171f) * .075f, (p.y + 261f) * .075f));
            if (d >= 0f) return .88f * cluster;
            if (d >= -18f) return .88f * cluster * Mathf.SmoothStep(0f, 1f, 1f + d / 18f);
            return d > -32f ? .025f * (1f + (d + 18f) / 14f) : 0f;
        }
        private static float Hash(Vector2 p, int salt)
        {
            uint hash = unchecked((uint)(Mathf.RoundToInt(p.x * 100f) * 73856093 ^ Mathf.RoundToInt(p.y * 100f) * 19349663 ^ salt * 83492791));
            hash ^= hash >> 13; hash *= 1274126177u; hash ^= hash >> 16;
            return (hash & 0xffffffu) / 16777216f;
        }
        internal static float MeasureArea(Terrain terrain)
        {
            Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
            int total = 0, inside = 0;
            for (float x = origin.x + 1f; x < origin.x + size.x; x += 2f)
            for (float z = origin.z + 1f; z < origin.z + size.z; z += 2f)
            { total++; if (SignedDistance(new Vector2(x, z)) >= 0f) inside++; }
            return inside / (float)total;
        }
        internal static void Apply(GameObject ownedRoot)
        {
            if (ownedRoot.transform.Find(GroupName) != null) return;
            Terrain terrain = Terrain.activeTerrain;
            var group = new GameObject(GroupName); group.transform.SetParent(ownedRoot.transform, false);
            var backup = group.AddComponent<ForestRegionBackup>(); backup.terrain = terrain;
            backup.northwestAreaFraction = MeasureArea(terrain); backup.layoutVersion = 4;
            backup.autumnBaseline = CentralForestDensification.AutumnBaseline;
            if (Mathf.Abs(backup.northwestAreaFraction - .20f) > .01f) throw new InvalidOperationException("Northwest region is not one fifth of the terrain.");
            var hidden = new GameObject("Original Scene Trees"); hidden.transform.SetParent(group.transform, false); hidden.SetActive(false);
            var treeSafe = RouteObstacleClearance.CreateTreeCandidateProbe(terrain);
            var trunkSafe = RouteObstacleClearance.CreateTrunkCandidateProbe(terrain);
            var removed = new List<ForestRegionBackup.TerrainTreeRecord>();
            var sceneRecords = new List<ForestRegionBackup.SceneTreeRecord>();
            var kept = new List<TreeInstance>();
            TreeInstance[] originals = terrain.terrainData.treeInstances;
            backup.originalTerrainCount = originals.Length;
            int rejected = 0;
            var rocks = UnityEngine.Object.FindObjectsOfType<Renderer>().Where(r => r.name.IndexOf("rock", StringComparison.OrdinalIgnoreCase) >= 0
                && r.GetComponentInParent<SceneTreeObstacle>() == null).Select(r => r.bounds).ToArray();
            bool Site(Vector2 p, float radius) => !MainCreekIntegration.NearCreek(p.x, p.y, radius + 1.5f) &&
                !rocks.Any(b => p.x > b.min.x - radius && p.x < b.max.x + radius && p.y > b.min.z - radius && p.y < b.max.z + radius);
            GameObject Replacement(Vector2 p, float oldHeight, bool autumn, int salt)
            {
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    string name = autumn ? new[] { "FabTree_1", "FabTree_5", "FabTree_10" }[(salt + attempt) % 3] : "tree0" + (1 + (salt + attempt) % 3);
                    string path = autumn ? "Assets/VegetationDiversity/Prefabs/" + name + ".prefab" : "Assets/NatureStarterKit2/Nature/" + name + ".prefab";
                    Vector2 point = p;
                    if (attempt >= 3)
                    { float a = (salt * 137.5f + attempt * 71f) * Mathf.Deg2Rad; point += new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (attempt - 2) * .5f; }
                    // A replacement stays within the same habitat class and near its original stem.
                    if ((SignedDistance(p) >= 0f) != (SignedDistance(point) >= 0f)) continue;
                    float height = Mathf.Clamp(oldHeight, autumn ? 8f : 6f, autumn ? 14f : 12f);
                    float radius = autumn ? .7f : .75f;
                    if (!Site(point, radius) || !trunkSafe(point, radius)) continue;
                    GameObject candidate = CentralForestDensification.Instantiate(path, group.transform);
                    candidate.name = name + "_Regional_" + salt.ToString("0000");
                    candidate.transform.localScale *= height / CentralForestDensification.BoundsOf(candidate).size.y;
                    candidate.transform.rotation = Quaternion.Euler(0f, Hash(p, salt + 17) * 360f, 0f);
                    foreach (Collider collider in candidate.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
                    var marker = candidate.GetComponent<SceneTreeObstacle>() ?? candidate.AddComponent<SceneTreeObstacle>(); marker.SetRadius(radius);
                    CentralForestDensification.Ground(candidate, terrain, point);
                    if (treeSafe(candidate.transform)) return candidate;
                    UnityEngine.Object.DestroyImmediate(candidate);
                }
                return null;
            }
            try
            {
                for (int i = 0; i < originals.Length; i++)
                {
                    TreeInstance tree = originals[i];
                    Vector3 world = terrain.transform.TransformPoint(Vector3.Scale(tree.position, terrain.terrainData.size));
                    Vector2 p = new Vector2(world.x, world.z);
                    if (tree.widthScale <= 0f || Hash(p, 41) >= Probability(p)) { kept.Add(tree); continue; }
                    GameObject source = terrain.terrainData.treePrototypes[tree.prototypeIndex].prefab;
                    float height = CentralForestDensification.BoundsOf(source).size.y * tree.heightScale;
                    GameObject replacement = Replacement(p, height, true, i);
                    if (replacement == null) { kept.Add(tree); rejected++; continue; }
                    removed.Add(new ForestRegionBackup.TerrainTreeRecord(i, tree));
                }
                // Capture before mutation; the hidden originals and their parents remain recoverable.
                var sceneTrees = UnityEngine.Object.FindObjectsOfType<SceneTreeObstacle>()
                    .Where(t => !t.transform.IsChildOf(ownedRoot.transform)).ToArray();
                var sceneBefore = sceneTrees.ToDictionary(t => t, t => new ForestRegionBackup.SceneTreeRecord {
                    original = t.gameObject, parent = t.transform.parent, sibling = t.transform.GetSiblingIndex(),
                    localPosition = t.transform.localPosition, localRotation = t.transform.localRotation, localScale = t.transform.localScale });
                int ordinal = originals.Length;
                foreach (var tree in sceneTrees)
                {
                    Vector2 p = new Vector2(tree.transform.position.x, tree.transform.position.z);
                    bool autumn = tree.name.StartsWith("FabTree", StringComparison.Ordinal);
                    bool desired = Hash(p, 41) < Probability(p);
                    if (autumn == desired) { ordinal++; continue; }
                    GameObject replacement = Replacement(p, CentralForestDensification.BoundsOf(tree.gameObject).size.y, desired, ordinal++);
                    if (replacement == null) { rejected++; continue; }
                    sceneRecords.Add(sceneBefore[tree]);
                    tree.transform.SetParent(hidden.transform, true);
                }
                backup.removedTrees = removed.ToArray(); backup.originalSceneTrees = sceneRecords.ToArray();
                terrain.terrainData.SetTreeInstances(kept.ToArray(), false); backup.applied = true;
                EditorUtility.SetDirty(terrain.terrainData);
                TerrainTreeObstacleRegistry.Invalidate();
                Debug.Log($"NORTHWEST_REGION_APPLIED: area={backup.northwestAreaFraction:P2}, terrainReplacements={removed.Count}, sceneReplacements={sceneRecords.Count}, retainedUnsafe={rejected}, regionalTotalPreserved=True.");
            }
            catch
            {
                backup.originalSceneTrees = sceneRecords.ToArray();
                foreach (var record in sceneRecords) RestoreSceneTree(record);
                terrain.terrainData.SetTreeInstances(originals, false);
                UnityEngine.Object.DestroyImmediate(group); throw;
            }
        }
        private static void RestoreSceneTree(ForestRegionBackup.SceneTreeRecord record)
        {
            Transform t = record.original.transform; t.SetParent(record.parent, false);
            t.localPosition = record.localPosition; t.localRotation = record.localRotation; t.localScale = record.localScale;
            t.SetSiblingIndex(record.sibling);
        }
        internal static void Restore(GameObject ownedRoot)
        {
            ForestRegionBackup backup = ownedRoot.GetComponentInChildren<ForestRegionBackup>(true);
            if (backup == null || !backup.applied) return;
            backup.terrain.terrainData.SetTreeInstances(backup.RestoreTreeArray(backup.terrain.terrainData.treeInstances), false);
            foreach (var record in backup.originalSceneTrees.OrderBy(r => r.sibling)) RestoreSceneTree(record);
            backup.applied = false;
            EditorUtility.SetDirty(backup.terrain.terrainData);
            TerrainTreeObstacleRegistry.Invalidate();
        }
        internal static void HalveAutumn(GameObject root)
        {
            var backup = root.GetComponentInChildren<ForestRegionBackup>();
            int target = Mathf.FloorToInt(backup.autumnBaseline * .5f + .5f);
            var inside = UnityEngine.Object.FindObjectsOfType<SceneTreeObstacle>()
                .Where(t => t.name.StartsWith("FabTree") && SignedDistance(XZ(t.transform.position)) >= 0f)
                .OrderByDescending(t => Mathf.PerlinNoise((t.transform.position.x + 313f) * .035f, (t.transform.position.z + 193f) * .035f))
                .ThenBy(t => t.transform.position.x).ThenBy(t => t.transform.position.z).ToList();
            if (inside.Count < target) throw new InvalidOperationException("Insufficient autumn candidates for the persisted half-count: " + inside.Count + " < " + target);
            var probe = RouteObstacleClearance.CreateTreeCandidateProbe(Terrain.activeTerrain);
            var externalBefore = inside.Where(t => !t.transform.IsChildOf(root.transform)).ToDictionary(t => t,
                t => new ForestRegionBackup.SceneTreeRecord { original = t.gameObject, parent = t.transform.parent,
                    sibling = t.transform.GetSiblingIndex(), localPosition = t.transform.localPosition,
                    localRotation = t.transform.localRotation, localScale = t.transform.localScale });
            foreach (var tree in inside.Skip(target))
            {
                bool owned = tree.transform.IsChildOf(root.transform);
                Transform parent = owned ? tree.transform.parent : backup.transform;
                Vector2 p = XZ(tree.transform.position); Bounds before = CentralForestDensification.BoundsOf(tree.gameObject);
                int species = 1 + (int)(Hash(p, 311) * 3f);
                var green = CentralForestDensification.Instantiate("Assets/NatureStarterKit2/Nature/tree0" + species + ".prefab", parent);
                green.name = "tree0" + species + "_AutumnHalf_" + tree.name;
                green.transform.rotation = tree.transform.rotation;
                green.transform.localScale *= before.size.y / CentralForestDensification.BoundsOf(green).size.y;
                foreach (var collider in green.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
                var marker = green.GetComponent<SceneTreeObstacle>() ?? green.AddComponent<SceneTreeObstacle>();
                marker.SetRadius(tree.Radius);
                CentralForestDensification.Ground(green, Terrain.activeTerrain, p);
                if (!probe(green.transform)) { UnityEngine.Object.DestroyImmediate(green); throw new InvalidOperationException("Same-position green replacement failed core clearance: " + tree.name); }
                if (owned) UnityEngine.Object.DestroyImmediate(tree.gameObject);
                else
                {
                    backup.originalSceneTrees = backup.originalSceneTrees.Concat(new[] { externalBefore[tree] }).ToArray();
                    tree.transform.SetParent(backup.transform.Find("Original Scene Trees"), true);
                }
            }
            backup.autumnRetained = target;
            Debug.Log($"AUTUMN_HALF_OK: baselineInNewRegion={backup.autumnBaseline}, retained={target}, same-location green replacements={inside.Count - target}.");
        }
        internal static void ValidateAndReport(GameObject root)
        {
            var backup = root.GetComponentInChildren<ForestRegionBackup>();
            if (backup == null || !backup.applied) throw new InvalidOperationException("Regional conversion backup missing.");
            float fraction = MeasureArea(backup.terrain);
            if (Mathf.Abs(fraction - .20f) > .01f) throw new InvalidOperationException("Autumn forest area out of range.");
            var conversion = backup.GetComponentsInChildren<SceneTreeObstacle>();
            if (conversion.Length != backup.removedTrees.Length + backup.originalSceneTrees.Length)
                throw new InvalidOperationException("Regional replacement tree count changed.");
            var all = UnityEngine.Object.FindObjectsOfType<SceneTreeObstacle>();
            int autumnInside = all.Count(t => t.name.StartsWith("FabTree") && SignedDistance(XZ(t.transform.position)) >= 0f);
            if (autumnInside != backup.autumnRetained || autumnInside != Mathf.FloorToInt(backup.autumnBaseline * .5f + .5f)) throw new InvalidOperationException("Autumn half-count changed.");
            int autumnTransition = all.Count(t => t.name.StartsWith("FabTree") && SignedDistance(XZ(t.transform.position)) < 0f && SignedDistance(XZ(t.transform.position)) >= -32f);
            int autumnFar = all.Count(t => t.name.StartsWith("FabTree") && SignedDistance(XZ(t.transform.position)) < -32f);
            if (autumnFar > 0) throw new InvalidOperationException("Autumn trees remain outside the northwest/transition region.");
            var text = new StringBuilder("# 西北秋叶林分区\n\n");
            text.AppendLine($"全地图地形投影面积的 **{fraction:P2}** 为西北秋叶林主体（2m网格）；边界为平滑不规则曲线。区域是面积指标，主体面积目标20%；秋叶数量按修正前新主体区内数量的一半保留，其余等量换成绿树。");
            text.AppendLine($"\n秋叶树：修正前落在新主体区内 {backup.autumnBaseline}，保留一半 {backup.autumnRetained}；主体区 {autumnInside}；18m渐变及额外14m零星过渡 {autumnTransition}；远离过渡区 {autumnFar}。");
            text.AppendLine($"\n外围及原有树转换：地形实例 {backup.removedTrees.Length}，场景树 {backup.originalSceneTrees.Length}；转换前后当地棵数相同。");
            text.AppendLine("\n被替换地形树的完整实例字段及原索引保存在 Northwest Autumn Region / ForestRegionBackup；原场景树保存在不激活的 Original Scene Trees 中，并记录原父对象和局部变换。Remove 恢复这些记录。地形高度、地表图层、溪流、原型数组和未涉及树实例不改动。");
            text.AppendLine("\n修改前检查点：Tools/VegetationDiversity/Checkpoints/Main_BeforeCoreRouteRevision.unity 及 MainCreekTerrain_BeforeCoreRouteRevision.asset。阶段仍为1，无截图。\n");
            Directory.CreateDirectory(CentralForestDensification.Output);
            File.WriteAllText(CentralForestDensification.Output + "/NorthwestRegion.md", text.ToString());
            Debug.Log($"NORTHWEST_REGION_VALIDATION_OK: area={fraction:P2}, autumnInside={autumnInside}, transition={autumnTransition}, far={autumnFar}; replacement counts and restore data present.");
        }
        private static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
    }
}
