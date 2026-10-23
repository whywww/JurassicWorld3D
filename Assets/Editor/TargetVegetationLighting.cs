using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class TargetVegetationLighting
    {
        internal const string Output = "Reports/Atmosphere/TargetVegetation";
        internal const string Folder = "Assets/Environment/Atmosphere/Prepared/VegetationLighting";
        internal const string ProfilePath = Folder + "/TargetLighting.asset";

        public static void AuditBatch()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Audit();
        }

        public static void TrunkDiagnosticBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Object.FindObjectOfType<RainforestSkyFill>().Apply();
            Camera camera = Camera.main;
            Renderer r = Object.FindObjectsOfType<MeshRenderer>().Where(x => Kind(x) == "NSK" && x.sharedMaterials.Any(m =>
                AssetDatabase.GetAssetPath(m.GetTexture("_MainTex")).Contains("tree02_Textures")))
                .OrderBy(x => (x.transform.position - new Vector3(14.7f, 1f, 11.5f)).sqrMagnitude).First();
            var view = new Dictionary<string, Renderer> { { "TrunkDiagnostic", r } };
            var original = r.sharedMaterials;
            var copies = original.Select(m => new Material(m)).ToArray();
            try
            {
                r.sharedMaterials = copies;
                CaptureViews(camera, view, "Original");
                foreach (Material m in copies.Where(m => m.shader.name.EndsWith("Bark")))
                { m.SetFloat("_OcclusionStrength", 0f); m.SetFloat("_NormalStrength", 0f); m.SetTexture("_MainTex", Texture2D.whiteTexture); }
                CaptureViews(camera, view, "White");
                Debug.Log("TRUNK_PROBE_USAGE: " + r.lightProbeUsage + " lightmap=" + r.lightmapIndex);
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
                CaptureViews(camera, view, "BlendProbes");
                foreach (Material m in copies.Where(m => m.shader.name.EndsWith("Bark")))
                { m.shader = Shader.Find("Standard"); m.SetColor("_Color", Color.white); m.SetFloat("_Glossiness", .1f); }
                CaptureViews(camera, view, "StandardWhite");
            }
            finally { r.sharedMaterials = original; foreach (var m in copies) Object.DestroyImmediate(m); }
        }

        internal static string Kind(Renderer r)
        {
            string hierarchy = r.name;
            for (Transform t = r.transform.parent; t != null; t = t.parent) hierarchy += "/" + t.name;
            hierarchy = hierarchy.ToLowerInvariant();
            if (hierarchy.Contains("banana")) return "Banana";
            if (hierarchy.Contains("bush0")) return "Bush";
            if (r.sharedMaterials.Any(m => m != null && m.HasProperty("_MainTex") &&
                AssetDatabase.GetAssetPath(m.GetTexture("_MainTex")).Contains("NatureStarterKit2/Nature/bush0"))) return "Bush";
            if (r.sharedMaterials.Any(m => m != null && m.HasProperty("_MainTex") &&
                AssetDatabase.GetAssetPath(m.GetTexture("_MainTex")).Contains("NatureStarterKit2/Nature/tree0"))) return "NSK";
            return null;
        }

        public static void Audit()
        {
            Directory.CreateDirectory(Output);
            var report = new StringBuilder();
            foreach (var group in Object.FindObjectsOfType<MeshRenderer>().Where(r => Kind(r) != null)
                .GroupBy(r => Kind(r) + " " + string.Join(";", r.sharedMaterials.Select(m => AssetDatabase.GetAssetPath(m)))))
            {
                MeshRenderer r = group.First();
                Mesh mesh = r.GetComponent<MeshFilter>().sharedMesh;
                report.AppendLine(group.Key + " count=" + group.Count() + " example=" + r.name + " pos=" + r.bounds.center);
                report.AppendLine("mesh=" + AssetDatabase.GetAssetPath(mesh) + " vertices=" + mesh.vertexCount + " readable=" + mesh.isReadable);
                if (mesh.isReadable)
                {
                    Color[] colors = mesh.colors;
                    report.AppendLine("vertex alpha " + (colors.Length == 0 ? "absent" : "min=" + colors.Min(c => c.a) + " mean=" + colors.Average(c => c.a)) +
                        " normals=" + mesh.normals.Length + " tangents=" + mesh.tangents.Length);
                    for (int i = 0; i < mesh.subMeshCount; i++)
                    {
                        int[] ids = mesh.GetIndices(i); Vector3[] v = mesh.vertices;
                        report.AppendLine("submesh=" + i + " y=" + ids.Min(n => v[n].y) + ".." + ids.Max(n => v[n].y));
                    }
                }
                var block = new MaterialPropertyBlock(); r.GetPropertyBlock(block);
                var foliage = r.GetComponentInParent<InteractiveFoliage>();
                report.AppendLine("block TreeInstanceColor=" + block.GetColor("_TreeInstanceColor") + " foliageColor=" + (foliage == null ? "none" : foliage.instanceColor.ToString()));
                foreach (Material m in r.sharedMaterials)
                {
                    report.AppendLine("material=" + AssetDatabase.GetAssetPath(m) + " shader=" + m.shader.name);
                    foreach (string p in new[] { "_Color", "_TreeInstanceColor" }) if (m.HasProperty(p)) report.AppendLine(p + "=" + m.GetColor(p));
                    foreach (string p in new[] { "_OcclusionStrength", "_AlbedoGain", "_BumpScale" }) if (m.HasProperty(p)) report.AppendLine(p + "=" + m.GetFloat(p));
                    foreach (string p in new[] { "_MainTex", "_BumpSpecMap", "_BumpMap" }) if (m.HasProperty(p))
                    {
                        string path = AssetDatabase.GetAssetPath(m.GetTexture(p));
                        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                        report.AppendLine(p + "=" + path + (importer == null ? "" : " sRGB=" + importer.sRGBTexture + " type=" + importer.textureType));
                    }
                }
            }
            foreach (TreePrototype p in Terrain.activeTerrain.terrainData.treePrototypes)
                report.AppendLine("Terrain prototype=" + AssetDatabase.GetAssetPath(p.prefab));
            File.WriteAllText(Output + "/Audit.txt", report.ToString());
            Debug.Log("TARGET_VEGETATION_AUDIT_DONE");
        }

        internal static void ApplySaved()
        {
            var profile = AssetDatabase.LoadAssetAtPath<TargetVegetationLightingProfile>(ProfilePath);
            if (profile == null) return;
            foreach (var entry in profile.entries)
            {
                Material m = entry.material;
                if (entry.shader != null) m.shader = entry.shader;
                m.SetFloat("_AlbedoGain", entry.albedoGain);
                m.SetFloat("_InstanceTintStrength", entry.instanceTintStrength);
                if (m.HasProperty("_OcclusionStrength")) m.SetFloat("_OcclusionStrength", entry.occlusionStrength);
                if (entry.packedNormal != null) m.SetTexture("_BumpSpecMap", entry.packedNormal);
                EditorUtility.SetDirty(m);
            }
        }

        internal static void VerifyRuntime()
        {
            var profile = AssetDatabase.LoadAssetAtPath<TargetVegetationLightingProfile>(ProfilePath);
            if (profile == null) return;
            var affected = new HashSet<Material>(profile.entries.Select(e => e.material));
            var camera = Object.FindObjectOfType<RainforestAtmosphereController>().viewCamera;
            Vector3 position = camera.transform.position; Quaternion rotation = camera.transform.rotation;
            int quality = QualitySettings.GetQualityLevel();
            var selected = new Dictionary<string, InteractiveFoliage>();
            foreach (InteractiveFoliage f in Object.FindObjectsOfType<InteractiveFoliage>())
            {
                foreach (var original in f.originals.Where(o => o.materials.Any(affected.Contains)))
                {
                    Material m = original.materials.First(affected.Contains);
                    string texture = AssetDatabase.GetAssetPath(m.GetTexture("_MainTex"));
                    string key = texture.Contains("tiglajzdy") ? "Banana" : Path.GetFileName(Path.GetDirectoryName(texture));
                    if (!selected.ContainsKey(key)) selected.Add(key, f);
                }
            }
            var expected = new HashSet<string>(Enumerable.Range(1, 4).Select(i => "tree0" + i + "_Textures")
                .Concat(Enumerable.Range(1, 6).Select(i => "bush0" + i + "_Textures")).Concat(new[] { "Banana" }));
            if (!expected.SetEquals(selected.Keys)) throw new InvalidOperationException("Missing target interaction type: " + string.Join(",", expected.Except(selected.Keys)));
            var views = selected.ToDictionary(p => "Runtime-" + p.Key, p => p.Value.originals.First(o => o.materials.Any(affected.Contains)).renderer);
            var poses = new Dictionary<string, Pose>();
            try
            {
                foreach (InteractiveFoliage f in selected.Values)
                {
                    typeof(InteractiveFoliage).GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f, null);
                    f.ResetField();
                    var active = (Material[][])typeof(InteractiveFoliage).GetField("interactionMaterials", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f);
                    for (int r = 0; r < f.originals.Length; r++) for (int n = 0; n < f.originals[r].materials.Length; n++)
                    {
                        Material idle = f.originals[r].materials[n];
                        if (!affected.Contains(idle)) continue;
                        Material contact = active[r][n];
                        if (idle.shader != contact.shader) throw new InvalidOperationException("Target rest/contact shader differs: " + f.name);
                        foreach (string prop in new[] { "_AlbedoGain", "_OcclusionStrength", "_InstanceTintStrength", "_NormalStrength", "_Cutoff" })
                            if (idle.HasProperty(prop) && idle.GetFloat(prop) != contact.GetFloat(prop)) throw new InvalidOperationException("Target rest/contact parameter differs: " + prop);
                        foreach (string prop in new[] { "_MainTex", "_BumpSpecMap", "_BumpMap", "_AlphaTex" })
                            if (idle.HasProperty(prop) && idle.GetTexture(prop) != contact.GetTexture(prop)) throw new InvalidOperationException("Target rest/contact texture differs: " + prop);
                    }
                }
                CaptureViews(camera, views, "Idle", poses);
                foreach (InteractiveFoliage f in selected.Values) typeof(InteractiveFoliage).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f, null);
                CaptureViews(camera, views, "Contact", poses);
                foreach (InteractiveFoliage f in selected.Values) f.ResetField();
                var atmosphere = Object.FindObjectOfType<RainforestAtmosphereController>();
                for (int tier = 0; tier < 3; tier++)
                {
                    QualitySettings.SetQualityLevel(tier, true); atmosphere.ApplyQuality(tier);
                    CaptureViews(camera, views, new[] { "Low", "Medium", "High" }[tier], poses);
                }
                File.AppendAllText(Output + "/Checks.txt", "PASS: four NSK types, six bush types and banana rest/contact shaders, textures and local parameters match; three quality tiers captured.\n");
                Debug.Log("TARGET_VEGETATION_RUNTIME_PASS");
            }
            finally
            {
                foreach (InteractiveFoliage f in selected.Values) f.ResetField();
                QualitySettings.SetQualityLevel(quality, true);
                Object.FindObjectOfType<RainforestAtmosphereController>().ApplyQuality(quality);
                camera.transform.SetPositionAndRotation(position, rotation);
            }
        }

        internal static void CaptureTerrainTransition()
        {
            if (AssetDatabase.LoadAssetAtPath<TargetVegetationLightingProfile>(ProfilePath) == null) return;
            Terrain terrain = Terrain.activeTerrain;
            var trees = terrain.terrainData.treeInstances;
            Vector3[] positions = trees.Select(t => terrain.transform.position + Vector3.Scale(t.position, terrain.terrainData.size)).ToArray();
            int index = Enumerable.Range(0, trees.Length).Where(i => trees[i].prototypeIndex == 0)
                .OrderByDescending(i => Enumerable.Range(0, trees.Length).Where(j => j != i).Min(j => (positions[i] - positions[j]).sqrMagnitude)).First();
            Camera camera = Camera.main;
            Vector3 oldPosition = camera.transform.position; Quaternion oldRotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            Vector3 direction = RenderSettings.sun.transform.forward; direction.y = 0f; direction.Normalize();
            // A temporary, single-tree terrain preview isolates the billboard transition.
            // Source TerrainData and all source renderer settings are restored untouched.
            bool drawTrees = terrain.drawTreesAndFoliage;
            Renderer[] hidden = Object.FindObjectsOfType<Renderer>().Where(r => r.enabled && r.GetComponentInParent<RainforestAtmosphereController>() == null).ToArray();
            TerrainData previewData = Object.Instantiate(terrain.terrainData);
            previewData.SetTreeInstances(new[] { trees[index] }, false);
            var previewObject = new GameObject("Temporary Billboard Review", typeof(Terrain));
            var preview = previewObject.GetComponent<Terrain>();
            preview.transform.position = terrain.transform.position; preview.terrainData = previewData;
            preview.drawHeightmap = false; preview.treeDistance = terrain.treeDistance;
            preview.treeBillboardDistance = terrain.treeBillboardDistance; preview.treeCrossFadeLength = terrain.treeCrossFadeLength;
            try
            {
                terrain.drawTreesAndFoliage = false;
                foreach (Renderer r in hidden) r.enabled = false;
                Object.FindObjectOfType<RainforestSkyFill>().Apply();
                camera.fieldOfView = 15f;
                foreach (float distance in new[] { 55f, 58f, 62f, 65f })
                {
                    Vector3 target = positions[index] + Vector3.up * 4f;
                    camera.transform.position = target + direction * distance;
                    camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position);
                    Capture(camera, "Terrain-" + distance.ToString("F0") + "m");
                }
                File.AppendAllText(Output + "/Checks.txt", "Terrain 60m billboard threshold: 55/58/62/65m static captures saved with 15-degree review zoom.\n");
            }
            finally
            {
                foreach (Renderer r in hidden) r.enabled = true;
                terrain.drawTreesAndFoliage = drawTrees;
                Object.DestroyImmediate(previewObject); Object.DestroyImmediate(previewData);
                camera.fieldOfView = fov; camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
            }
        }

        public static void RunBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Run();
        }

        public static void Run()
        {
            Directory.CreateDirectory(Output);
            Audit();
            var atmosphere = Object.FindObjectOfType<RainforestAtmosphereController>();
            if (!Mathf.Approximately(atmosphere.profile.skyFillStrength, .03f)) throw new InvalidOperationException("Keep sky fill at 3%.");
            Object.FindObjectOfType<RainforestSkyFill>().Apply();
            Renderer[] targets = Object.FindObjectsOfType<MeshRenderer>().Where(r => Kind(r) != null).Cast<Renderer>().ToArray();
            Material[] all = targets.SelectMany(r => r.sharedMaterials).Distinct().Where(m => m != null).ToArray();
            bool IsTarget(Material m) => m.HasProperty("_MainTex") && (
                (AssetDatabase.GetAssetPath(m.GetTexture("_MainTex")).Contains("NatureStarterKit2/Nature/tree0") && m.shader.name.EndsWith("Bark")) ||
                AssetDatabase.GetAssetPath(m.GetTexture("_MainTex")).Contains("NatureStarterKit2/Nature/bush0") ||
                AssetDatabase.GetAssetPath(m.GetTexture("_MainTex")).Contains("tiglajzdy_2K_Albedo"));
            Material[] affected = all.Where(IsTarget).ToArray();
            var protectedMaterials = Object.FindObjectsOfType<Renderer>().SelectMany(r => r.sharedMaterials).Where(m => m != null && !affected.Contains(m)).Distinct()
                .ToDictionary(m => m, m => EditorJsonUtility.ToJson(m));
            string environmentBefore = EditorJsonUtility.ToJson(atmosphere.profile);
            string layoutBefore = FoliageInteractionPreparation.TreeSignature(Terrain.activeTerrain.terrainData.treeInstances, Terrain.activeTerrain.terrainData.treePrototypes);
            if (!File.Exists(Output + "/Main.Before.unity"))
            {
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), Output + "/Main.Before.unity", true);
                foreach (Material m in affected)
                {
                    string path = AssetDatabase.GetAssetPath(m);
                    File.Copy(path, Output + "/" + AssetDatabase.AssetPathToGUID(path) + ".Before.mat", true);
                }
            }
            var views = new Dictionary<string, Renderer>();
            Vector3 review = new Vector3(14.7f, 1f, 11.5f);
            for (int i = 1; i <= 4; i++)
            {
                string key = "tree0" + i + "_Textures";
                views.Add("NSK-tree0" + i, targets.Where(r => r.sharedMaterials.Any(m => m.HasProperty("_MainTex") && AssetDatabase.GetAssetPath(m.GetTexture("_MainTex")).Contains(key)))
                    .OrderBy(r => (r.transform.position - review).sqrMagnitude).First());
            }
            foreach (string kind in new[] { "Banana", "Bush" })
                views.Add(kind, targets.Where(r => Kind(r) == kind).OrderBy(r => (r.transform.position - review).sqrMagnitude).First());
            Camera camera = atmosphere.viewCamera;
            Vector3 oldPosition = camera.transform.position; Quaternion oldRotation = camera.transform.rotation;
            int quality = QualitySettings.GetQualityLevel();
            // Recreate the prior material settings transiently when recapturing comparisons.
            foreach (Material m in affected)
            {
                string texturePath = AssetDatabase.GetAssetPath(m.GetTexture("_MainTex"));
                bool banana = texturePath.Contains("tiglajzdy");
                if (m.HasProperty("_AlbedoGain")) m.SetFloat("_AlbedoGain", banana ? 1f : m.shader.name.EndsWith("Leaves") ? 1.45f : 2f);
                if (!banana)
                {
                    if (m.shader.name.Contains("Physical Bark")) m.shader = Shader.Find("Jurassic World/Interactive Tree Bark");
                    m.SetFloat("_OcclusionStrength", m.shader.name.EndsWith("Leaves") ? .7f : .65f);
                    m.SetTexture("_BumpSpecMap", AssetDatabase.LoadAssetAtPath<Texture>(Path.GetDirectoryName(texturePath).Replace('\\', '/') + "/normal_specular.png"));
                }
            }
            var saved = affected.ToDictionary(m => m, m => new Material(m));
            try
            {
                QualitySettings.SetQualityLevel(1, true); atmosphere.ApplyQuality(1);
                CaptureViews(camera, views, "Before");
                foreach (Material m in affected.Where(m => m.HasProperty("_OcclusionStrength") && m.shader.name.Contains("Tree"))) m.SetFloat("_OcclusionStrength", 0f);
                CaptureViews(camera, views, "NoVertexAO");
                foreach (var pair in saved) pair.Key.CopyPropertiesFromMaterial(pair.Value);
                foreach (Material m in affected.Where(m => m.HasProperty("_InstanceTintStrength"))) m.SetFloat("_InstanceTintStrength", 0f);
                CaptureViews(camera, views, "NoInstanceTint");
                foreach (var pair in saved) pair.Key.CopyPropertiesFromMaterial(pair.Value);
                foreach (Material m in affected.Where(m => m.HasProperty("_NormalStrength"))) m.SetFloat("_NormalStrength", 0f);
                CaptureViews(camera, views, "FlatNormal");
                foreach (var pair in saved) pair.Key.CopyPropertiesFromMaterial(pair.Value);
                Configure(affected, targets);
                CaptureViews(camera, views, "After");
                // Material application must preserve the saved category values.
                VegetationLightingRepair.ApplyConfiguredToCurrentScene(atmosphere.profile);
                foreach (var entry in AssetDatabase.LoadAssetAtPath<TargetVegetationLightingProfile>(ProfilePath).entries)
                    if (!Mathf.Approximately(entry.material.GetFloat("_AlbedoGain"), entry.albedoGain)) throw new InvalidOperationException("Category gain overwritten.");
                if (EditorJsonUtility.ToJson(atmosphere.profile) != environmentBefore ||
                    FoliageInteractionPreparation.TreeSignature(Terrain.activeTerrain.terrainData.treeInstances, Terrain.activeTerrain.terrainData.treePrototypes) != layoutBefore)
                    throw new InvalidOperationException("Environment or tree layout changed.");
                foreach (var pair in protectedMaterials)
                    if (EditorJsonUtility.ToJson(pair.Key) != pair.Value) throw new InvalidOperationException("Unrelated material changed: " + pair.Key.name);
                RainforestAtmosphereStageOne.ValidateCurrentSceneIfConfigured();
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                QualitySettings.SetQualityLevel(quality, true); atmosphere.ApplyQuality(quality);
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                AssetDatabase.SaveAssets();
                File.WriteAllText(Output + "/Checks.txt", "PASS: environment profile, protected materials, tree layout unchanged; category parameters survive repeat material apply. Manual Play pending.\n");
                Debug.Log("TARGET_VEGETATION_READY: local materials only; 3% sky fill preserved; manual Play pending.");
            }
            finally
            {
                foreach (Material m in saved.Values) Object.DestroyImmediate(m);
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                QualitySettings.SetQualityLevel(quality, true); atmosphere.ApplyQuality(quality);
            }
        }

        private static void Configure(Material[] affected, Renderer[] targets)
        {
            Directory.CreateDirectory(Folder + "/PackedNormals"); AssetDatabase.Refresh();
            var profile = AssetDatabase.LoadAssetAtPath<TargetVegetationLightingProfile>(ProfilePath);
            if (profile == null) { profile = ScriptableObject.CreateInstance<TargetVegetationLightingProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
            var entries = new List<TargetVegetationLightingProfile.Entry>();
            var bananaMap = new Dictionary<Material, Material>();
            foreach (Material source in affected)
            {
                bool banana = AssetDatabase.GetAssetPath(source.GetTexture("_MainTex")).Contains("tiglajzdy");
                bool bush = AssetDatabase.GetAssetPath(source.GetTexture("_MainTex")).Contains("bush0");
                Material m = source;
                if (banana)
                {
                    string path = Folder + "/Materials/BananaTrunk.mat";
                    m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (m == null) { m = new Material(source); AssetDatabase.CreateAsset(m, path); }
                    m.shader = Shader.Find("Jurassic World/Interactive Opaque");
                    m.shaderKeywords = Array.Empty<string>(); m.SetFloat("_FoliageActive", 0f);
                    m.SetFloat("_HasMetallicGlossMap", 0f);
                    bananaMap[source] = m;
                }
                Texture packedNormal = null;
                if (!banana)
                {
                    string sourcePath = AssetDatabase.GetAssetPath(m.GetTexture("_BumpSpecMap"));
                    string path = Folder + "/PackedNormals/" + AssetDatabase.AssetPathToGUID(sourcePath) + ".png";
                    if (!File.Exists(path)) { File.Copy(sourcePath, path); AssetDatabase.ImportAsset(path); }
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    importer.textureType = TextureImporterType.Default; importer.sRGBTexture = false;
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.SaveAndReimport();
                    packedNormal = AssetDatabase.LoadAssetAtPath<Texture>(path);
                }
                var existing = profile.entries.FirstOrDefault(e => e.material == m);
                var entry = existing ?? new TargetVegetationLightingProfile.Entry { category = banana ? "Banana trunk" : bush ? "Bush" : "NSK trunk", material = m,
                    packedNormal = packedNormal, occlusionStrength = bush ? .35f : .30f,
                    albedoGain = banana ? 1.25f : bush && m.shader.name.EndsWith("Leaves") ? 1.6f : 2f,
                    instanceTintStrength = 1f };
                entry.shader = banana ? Shader.Find("Jurassic World/Interactive Opaque") :
                    m.shader.name.EndsWith("Leaves") ? m.shader : Shader.Find("Jurassic World/Interactive Tree Physical Bark");
                entries.Add(entry);
            }
            foreach (Renderer r in targets)
            {
                Material[] next = r.sharedMaterials.Select(m => bananaMap.TryGetValue(m, out var replacement) ? replacement : m).ToArray();
                if (r.sharedMaterials.SequenceEqual(next)) continue;
                r.sharedMaterials = next; PrefabUtility.RecordPrefabInstancePropertyModifications(r); EditorUtility.SetDirty(r);
            }
            foreach (InteractiveFoliage f in Object.FindObjectsOfType<InteractiveFoliage>())
            {
                for (int i = 0; i < f.originals.Length; i++)
                {
                    var original = f.originals[i];
                    original.materials = original.materials.Select(m => bananaMap.TryGetValue(m, out var replacement) ? replacement : m).ToArray();
                    f.originals[i] = original;
                }
                EditorUtility.SetDirty(f);
            }
            profile.entries = entries.ToArray(); EditorUtility.SetDirty(profile); ApplySaved();
        }

        private static void CaptureViews(Camera camera, Dictionary<string, Renderer> views, string suffix, Dictionary<string, Pose> poses = null)
        {
            Vector3 direction = RenderSettings.sun.transform.forward; direction.y = 0f; direction.Normalize();
            foreach (var pair in views)
            {
                Bounds b = pair.Value.bounds;
                bool bush = pair.Key.IndexOf("bush", StringComparison.OrdinalIgnoreCase) >= 0;
                Vector3 target = bush ? b.center : new Vector3(pair.Value.transform.position.x, b.min.y + Mathf.Min(.85f, b.size.y * .2f), pair.Value.transform.position.z);
                float distance = bush ? Mathf.Max(2f, b.extents.magnitude * 1.5f) : 1.5f;
                camera.transform.position = target + direction * distance + Vector3.up * .2f;
                camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position);
                if (poses != null)
                {
                    if (!poses.TryGetValue(pair.Key, out Pose pose)) poses[pair.Key] = pose = new Pose(camera.transform.position, camera.transform.rotation);
                    camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
                }
                Capture(camera, pair.Key + "-" + suffix);
                if (suffix == "After" || suffix == "Before")
                {
                    camera.transform.position = target + direction * distance * 4f + Vector3.up * .2f;
                    camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position);
                    Capture(camera, pair.Key + "-Far-" + suffix);
                }
            }
            Terrain t = Terrain.activeTerrain;
            camera.transform.position = new Vector3(14.7f, t.SampleHeight(new Vector3(14.7f, 0f, 11.5f)) + t.transform.position.y + 1.7f, 11.5f);
            camera.transform.rotation = Quaternion.Euler(3f, 175f, 0f);
            Capture(camera, "Overview-" + suffix);
        }

        private static void Capture(Camera camera, string name)
        {
            VegetationLightingRepair.Capture(camera, "Target-" + name);
            File.Copy(VegetationLightingRepair.Output + "/Target-" + name + ".png", Output + "/" + name + ".png", true);
        }
    }
}
