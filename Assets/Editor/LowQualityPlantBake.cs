using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class LowQualityPlantBake
    {
        [MenuItem("Jurassic World/Performance/Low/Prepare Soft Plant List %&#F4")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save before baking the soft plant list.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var owners = new HashSet<GameObject>();
            var entries = new List<LowQualityPlantManifest.Entry>();
            var layout = Object.FindObjectOfType<RainforestSampleLayout>();
            Vector3[] route = layout != null ? layout.corridor : Array.Empty<Vector3>();
            foreach (var renderer in Object.FindObjectsOfType<MeshRenderer>())
            {
                if (!renderer.sharedMaterials.Any(m => m != null && (m.shader.name == "Jurassic World/Foliage Cutout Double Sided" ||
                    m.shader.name == "Jurassic World/Vegetation Prototype Cutout"))) continue;
                var marker = renderer.GetComponentInParent<CenterGapGroundPlant>();
                GameObject owner = marker != null ? marker.gameObject : PrefabUtility.GetOutermostPrefabInstanceRoot(renderer.gameObject);
                if (owner == null) owner = renderer.gameObject;
                if (!owners.Add(owner) || !owner.activeInHierarchy || owner.transform.root.name == "Environment Props") continue;
                if (owner.GetComponentInParent<DinosaurAgent>() != null || owner.GetComponentInParent<SceneTreeObstacle>() != null ||
                    owner.GetComponentsInChildren<SceneTreeObstacle>(true).Length > 0 || owner.GetComponentsInChildren<Collider>(true).Length > 0 ||
                    owner.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>(true).Length > 0) continue;
                Renderer[] plants = owner.GetComponentsInChildren<Renderer>(true);
                if (plants.Length == 0 || plants.Any(r => r is SkinnedMeshRenderer)) continue;
                Bounds bounds = plants[0].bounds;
                foreach (var plant in plants) bounds.Encapsulate(plant.bounds);
                if (bounds.size.y > 3.5f || Mathf.Max(bounds.size.x, bounds.size.z) > 6f) continue;
                string path = Path(owner.transform);
                float distance = RouteDistance(bounds.center, route);
                entries.Add(new LowQualityPlantManifest.Entry
                {
                    hierarchy = path, name = owner.name, position = owner.transform.position, renderers = plants.Length,
                    priority = !WorldBounds.DinosaurActivityXZ.Contains(new Vector2(bounds.center.x, bounds.center.z)) ? 0 : distance >= 15f ? 1 : 2,
                    routeDistance = distance, stableId = StableId(path)
                });
            }
            // Canonical ownership excludes nested entries; reducing a parent can never count its child twice.
            var ownership = entries.ToArray();
            entries.RemoveAll(e => ownership.Any(parent => parent.hierarchy != e.hierarchy && e.hierarchy.StartsWith(parent.hierarchy + "/", StringComparison.Ordinal)));
            entries.Sort((a,b) => a.priority != b.priority ? a.priority.CompareTo(b.priority) :
                a.routeDistance != b.routeDistance ? b.routeDistance.CompareTo(a.routeDistance) : a.stableId.CompareTo(b.stableId));
            const string assetPath = "Assets/Resources/LowQuality/SoftPlants.asset";
            var manifest = AssetDatabase.LoadAssetAtPath<LowQualityPlantManifest>(assetPath);
            if (manifest == null) { manifest = ScriptableObject.CreateInstance<LowQualityPlantManifest>(); AssetDatabase.CreateAsset(manifest, assetPath); }
            manifest.entries = entries.ToArray(); EditorUtility.SetDirty(manifest); AssetDatabase.SaveAssets();
            Debug.Log($"JW_LOW_SOFT_LIST: {entries.Count} complete soft plants, {entries.Count(e=>e.priority==0)} peripheral; scene and vegetation unchanged.");
        }

        private static string Path(Transform transform)
        {
            var parts = new List<int>();
            for (var current = transform; current != null; current = current.parent) parts.Add(current.GetSiblingIndex());
            parts.Reverse(); return string.Join("/", parts);
        }
        private static uint StableId(string value)
        { unchecked { uint hash=2166136261; foreach(char c in value) hash=(hash^c)*16777619; return hash; } }
        private static float RouteDistance(Vector3 position, Vector3[] route)
        {
            float result = float.MaxValue;
            var point = new Vector2(position.x, position.z);
            for (int i=1;i<route.Length;i++)
            {
                var a = new Vector2(route[i-1].x,route[i-1].z); var b = new Vector2(route[i].x,route[i].z);
                var d = b-a; float t = d.sqrMagnitude > .0001f ? Mathf.Clamp01(Vector2.Dot(point-a,d)/d.sqrMagnitude) : 0;
                result = Mathf.Min(result,Vector2.Distance(point,a+d*t));
            }
            return result == float.MaxValue ? 0 : result;
        }
    }
}
