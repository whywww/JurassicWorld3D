using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace JurassicWorld.Editor
{
    // Only the old generated ground meshes are owned by this migration.
    [InitializeOnLoad]
    internal static class NaturalGroundDecoration
    {
        private const string Folder = "Assets/Terrain/GroundEcology/Natural";
        private const string Atlas = Folder + "/ForestFloorAtlas.png";
        private const string Trigger = "Library/NaturalGroundRepair.request";
        private const string Output = "Logs/GroundDecoration";
        private static readonly string[] Sources = { "Leaves", "DarkGrass", "Moss", "Roots", "AutumnMoss" };
        static NaturalGroundDecoration() { EditorApplication.update += Poll; }
        private static void Poll()
        {
            if (!File.Exists(Trigger) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.isPlaying = false; return; }
            File.Delete(Trigger);
            try { RepairAndVerify(); }
            catch (Exception e) { Directory.CreateDirectory(Output); File.WriteAllText(Output + "/error.txt", e.ToString()); Debug.LogException(e); }
        }
        [MenuItem("Jurassic World/Environment/Ground/Repair Natural Ground Decorations")]
        private static void Menu() => JurassicMenuGuard.Run(RepairAndVerify, true);
        [MenuItem("Jurassic World/Environment/Ground/Repair Natural Ground Decorations", true)]
        private static bool Enabled() => JurassicMenuGuard.CanRun;

        public static void RepairAndVerify()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the scene and exit Play Mode before repairing ground decorations.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            EnsureAssets();
            Directory.CreateDirectory(Output);
            var renderers = AllRenderers();
            string untouched = Layout(renderers.Where(r => Kind(r) == null && !IsNatural(r)));
            int changed = 0;
            foreach (var renderer in renderers) if (Upgrade(renderer)) changed++;
            if (untouched != Layout(AllRenderers().Where(r => Kind(r) == null && !IsNatural(r))))
                throw new InvalidOperationException("Repair changed unrelated renderer transforms.");
            string first = Layout(AllRenderers());
            foreach (var renderer in AllRenderers()) Upgrade(renderer);
            if (first != Layout(AllRenderers())) throw new InvalidOperationException("Repeated repair changed scene layout.");
            VerifyGeometry();
            Audit();
            Capture();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            File.WriteAllText(Output + "/result.txt", "Repaired renderers: " + changed + "\nRepeated repair stable; unrelated renderer transforms preserved.\nAll scene renderers including inactive objects and LODs audited.\n");
            Debug.Log("NATURAL_GROUND_REPAIR_COMPLETE " + changed);
        }
        private static MeshRenderer[] AllRenderers() => Resources.FindObjectsOfTypeAll<MeshRenderer>()
            .Where(r => r.gameObject.scene == EditorSceneManager.GetActiveScene()).ToArray();
        private static string Layout(IEnumerable<MeshRenderer> renderers) => string.Join("\n", renderers.OrderBy(r => r.GetInstanceID())
            .Select(r => r.GetInstanceID() + " " + EditorJsonUtility.ToJson(r.transform)));
        private static bool IsNatural(MeshRenderer r) => r.GetComponent<MeshFilter>() != null &&
            AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh).StartsWith(Folder + "/");
        private static string Kind(MeshRenderer renderer)
        {
            MeshFilter f = renderer.GetComponent<MeshFilter>();
            if (f == null || f.sharedMesh == null) return null;
            string path = AssetDatabase.GetAssetPath(f.sharedMesh);
            foreach (string kind in Sources)
            {
                string expected = kind == "AutumnMoss" ? "Assets/Terrain/DeadVegetation/AutumnMoss.asset" : "Assets/Terrain/GroundEcology/" + kind + ".asset";
                if (path == expected) return kind;
            }
            return null;
        }
        internal static bool Upgrade(MeshRenderer renderer)
        {
            string kind = Kind(renderer);
            if (kind == null) return false;
            EnsureAssets();
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Mesh replacement = Build(filter.sharedMesh, renderer.transform, kind);
            string path = Folder + "/" + kind + ".asset";
            Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved == null) { AssetDatabase.CreateAsset(replacement, path); saved = replacement; }
            else { EditorUtility.CopySerialized(replacement, saved); UnityEngine.Object.DestroyImmediate(replacement); EditorUtility.SetDirty(saved); }
            filter.sharedMesh = saved;
            renderer.sharedMaterial = Material(kind);
            renderer.shadowCastingMode = kind.Contains("Moss") ? ShadowCastingMode.Off : ShadowCastingMode.TwoSided;
            renderer.gameObject.SetActive(true);
            EditorUtility.SetDirty(filter); EditorUtility.SetDirty(renderer);
            return true;
        }
        private static void EnsureAssets()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Terrain/GroundEcology", "Natural");
            if (!File.Exists(Atlas)) throw new InvalidOperationException("Missing natural forest floor atlas: " + Atlas);
            var importer = (TextureImporter)AssetImporter.GetAtPath(Atlas);
            if (importer == null) { AssetDatabase.ImportAsset(Atlas); importer = (TextureImporter)AssetImporter.GetAtPath(Atlas); }
            if (!importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp || importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true; importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
        }
        private static Material Material(string kind)
        {
            string path = Folder + "/" + kind + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Jurassic World/Foliage Cutout Double Sided");
            if (shader == null) throw new InvalidOperationException("Missing foliage cutout shader.");
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            string texture = kind == "DarkGrass" ? "Assets/NatureStarterKit2/Textures/grass01.tga" :
                kind == "Roots" ? "Assets/NatureStarterKit2/Textures/bark01.tga" : Atlas;
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texture);
            if (material.mainTexture == null) throw new InvalidOperationException("Missing ground decoration texture: " + texture);
            material.color = Color.white; material.SetFloat("_Cutoff", .4f); material.SetFloat("_Glossiness", 0f);
            material.enableInstancing = true; EditorUtility.SetDirty(material);
            return material;
        }
        private static Mesh Build(Mesh source, Transform transform, string kind)
        {
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            Vector3[] old = source.vertices;
            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null) throw new InvalidOperationException("Missing terrain.");
            Action<Vector3, Vector2, float> add = (point, tex, lift) =>
            {
                Vector3 world = transform.TransformPoint(point);
                world.y = terrain.SampleHeight(world) + terrain.transform.position.y + lift;
                vertices.Add(transform.InverseTransformPoint(world)); uv.Add(tex);
            };
            Action<int, int> gridTriangles = (first, steps) =>
            {
                for (int z = 0; z < steps; z++) for (int x = 0; x < steps; x++)
                { int a = first + z * (steps + 1) + x; triangles.AddRange(new[] { a, a + 1, a + steps + 2, a, a + steps + 2, a + steps + 1 }); }
            };
            if (kind == "DarkGrass")
            {
                for (int i = 0; i < old.Length; i += 6)
                {
                    Vector3 center = (old[i] + old[i + 1]) * .5f;
                    Vector3 ground = transform.TransformPoint(center); ground.y = terrain.SampleHeight(ground) + terrain.transform.position.y + .008f;
                    float height = Mathf.Clamp(old[i + 2].y - center.y, .16f, .4f);
                    for (int side = 0; side < 2; side++)
                    {
                        Vector3 across = side == 0 ? Vector3.right : Vector3.forward;
                        int first = vertices.Count;
                        foreach (Vector2 tex in new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up })
                        { vertices.Add(transform.InverseTransformPoint(ground + across * ((tex.x - .5f) * .32f) + Vector3.up * tex.y * height)); uv.Add(tex); }
                        triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
                    }
                }
            }
            else for (int i = 0; i < old.Length; i += 4)
            {
                Vector3 a = old[i], b = old[i + 1], c = old[i + 2], d = old[i + 3];
                bool roots = kind == "Roots", leaves = kind == "Leaves";
                Vector3 center = (a + b + c + d) * .25f;
                if (leaves) { a = center + (a - center) * .55f; b = center + (b - center) * .55f; c = center + (c - center) * .55f; d = center + (d - center) * .55f; }
                int steps = roots ? 16 : leaves ? 2 : 8;
                int first = vertices.Count;
                // Atlas regions: top-left moss, other three quadrants leaves.
                int variant = (i / 4) % 3;
                Vector2 tile = leaves ? (variant == 0 ? new Vector2(.5f, .5f) : variant == 1 ? Vector2.zero : new Vector2(.5f, 0)) : new Vector2(0, .5f);
                for (int z = 0; z <= steps; z++) for (int x = 0; x <= steps; x++)
                {
                    float u = x / (float)steps, v = z / (float)steps;
                    Vector3 point = Vector3.Lerp(Vector3.Lerp(a, b, u), Vector3.Lerp(d, c, u), v);
                    float lift = .012f;
                    if (roots) { lift = .012f + Mathf.Sin(u * Mathf.PI) * .035f; }
                    Vector2 tex = roots ? new Vector2(u, v * 3f) : tile + new Vector2(.012f + u * .476f, .012f + v * .476f);
                    add(point, tex, lift);
                }
                gridTriangles(first, steps);
            }
            var mesh = new Mesh { name = "Natural " + kind, indexFormat = IndexFormat.UInt32 };
            // Keep all ground-facing triangles oriented upward on sloping terrain.
            if (kind != "DarkGrass") for (int i = 0; i < triangles.Count; i += 3)
            {
                Vector3 normal = Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]], vertices[triangles[i + 2]] - vertices[triangles[i]]);
                if (transform.TransformDirection(normal).y < 0) { int swap = triangles[i + 1]; triangles[i + 1] = triangles[i + 2]; triangles[i + 2] = swap; }
            }
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private static void VerifyGeometry()
        {
            foreach (var renderer in AllRenderers().Where(IsNatural))
            {
                Mesh actual = renderer.GetComponent<MeshFilter>().sharedMesh;
                string kind = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(actual));
                string original = kind == "AutumnMoss" ? "Assets/Terrain/DeadVegetation/AutumnMoss.asset" : "Assets/Terrain/GroundEcology/" + kind + ".asset";
                Mesh expected = Build(AssetDatabase.LoadAssetAtPath<Mesh>(original), renderer.transform, kind);
                try
                {
                    if (!actual.vertices.SequenceEqual(expected.vertices) || !actual.uv.SequenceEqual(expected.uv) || !actual.triangles.SequenceEqual(expected.triangles))
                        throw new InvalidOperationException("Regeneration differs for " + kind);
                }
                finally { UnityEngine.Object.DestroyImmediate(expected); }
            }
        }
        private static void Audit()
        {
            var lines = new List<string>();
            var inventory = new HashSet<string>();
            foreach (var renderer in Resources.FindObjectsOfTypeAll<Renderer>().Where(r => r.gameObject.scene == EditorSceneManager.GetActiveScene()))
            {
                if (renderer is MeshRenderer legacy && Kind(legacy) != null) throw new InvalidOperationException("Legacy ground mesh remains: " + renderer.name);
                var filter = renderer.GetComponent<MeshFilter>();
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null) { lines.Add("Missing material: " + renderer.name); continue; }
                    string materialPath = AssetDatabase.GetAssetPath(mat);
                    inventory.Add(materialPath + " | " + (mat.shader != null ? mat.shader.name : "MISSING SHADER") + " | " + AssetDatabase.GetAssetPath(mat.mainTexture));
                    if (Sources.Where(k => k != "AutumnMoss").Any(k => materialPath == "Assets/Terrain/GroundEcology/" + k + ".mat"))
                        throw new InvalidOperationException("Legacy ground material remains: " + renderer.name);
                    bool foliage = mat.shader != null && (mat.shader.name.Contains("Cutout") || mat.IsKeywordEnabled("_ALPHATEST_ON"));
                    if (foliage && mat.mainTexture == null) lines.Add("Cutout missing texture: " + renderer.name + " / " + AssetDatabase.GetAssetPath(mat));
                }
                Mesh mesh = filter != null ? filter.sharedMesh : (renderer as SkinnedMeshRenderer)?.sharedMesh;
                if (mesh == null) { lines.Add("Missing mesh: " + renderer.name); continue; }
                if (renderer is MeshRenderer natural && IsNatural(natural) && (mesh.uv.Length != mesh.vertexCount || renderer.sharedMaterial.mainTexture == null))
                    throw new InvalidOperationException("Invalid natural decoration: " + renderer.name);
            }
            File.WriteAllLines(Output + "/materials.txt", inventory.OrderBy(p => p));
            File.WriteAllLines(Output + "/audit.txt", lines.Count == 0 ? new[] { "No missing cutout textures or materials. Legacy ground meshes absent." } : lines.ToArray());
        }
        private static void Capture()
        {
            Terrain terrain = Terrain.activeTerrain;
            var go = new GameObject("Ground repair capture"); var camera = go.AddComponent<Camera>();
            camera.fieldOfView = 65; camera.nearClipPlane = .05f; camera.farClipPlane = 250;
            var rt = new RenderTexture(1280, 720, 24); var prior = RenderTexture.active;
            Vector2[] sites = { new Vector2(31.3f,15.9f), Vector2.zero, new Vector2(-42,20), new Vector2(12,-38), new Vector2(55,35) };
            try
            {
                camera.targetTexture = rt;
                for (int site = 0; site < sites.Length; site++) for (int angle = 0; angle < 3; angle++)
                {
                    Vector2 p = sites[site]; float eye = angle == 1 ? .45f : angle == 2 ? 5f : 1.7f;
                    camera.transform.position = new Vector3(p.x, terrain.SampleHeight(new Vector3(p.x,0,p.y)) + terrain.transform.position.y + eye, p.y);
                    camera.transform.rotation = Quaternion.Euler(angle == 2 ? 38 : 18, site == 0 ? 282 : 45 + site * 70, 0);
                    camera.Render(); RenderTexture.active = rt;
                    var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); image.Apply();
                    File.WriteAllBytes(Output + "/site-" + site + "-view-" + angle + ".png", image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
                }
            }
            finally { RenderTexture.active = prior; camera.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
