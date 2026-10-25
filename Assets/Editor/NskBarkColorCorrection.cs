using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class NskBarkColorCorrection
    {
        private const string Output = "Reports/Atmosphere/BarkContrast";
        private const string Main = "Assets/Scenes/Main.unity";
        private const int CaptureWidth = 1350;
        private const int CaptureHeight = 670;
        private const int PatchWidth = 100;
        private const int PatchHeight = 240;

        private readonly struct Candidate
        {
            public readonly string Name;
            public readonly float Direct;
            public readonly float ShadowLift;

            public Candidate(string name, float direct, float shadowLift)
            {
                Name = name;
                Direct = direct;
                ShadowLift = shadowLift;
            }
        }

        private sealed class View
        {
            public string Name;
            public Vector3 Position;
            public Vector3 Target;
        }

        private static readonly Candidate[] Candidates =
        {
            new Candidate("A", .90f, .04f),
            new Candidate("B", .88f, .06f),
            new Candidate("C", .86f, .08f),
        };

        public static void ApplyBatch()
        {
            Directory.CreateDirectory(Output);
            var report = new StringBuilder();
            bool success = false;
            try
            {
                Apply(report);
                success = true;
            }
            catch (Exception exception)
            {
                report.AppendLine("FAIL: " + exception);
                Debug.LogException(exception);
            }
            finally
            {
                report.AppendLine(success
                    ? "PASS: only the eight NSK rest/contact trunk materials and their profile entries changed; scene not saved."
                    : "FAILED: no new bark values were left saved.");
                report.AppendLine("Manual Play pending.");
                File.WriteAllText(Output + "/Checks.txt", report.ToString());
                if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
            }
        }

        private static void Apply(StringBuilder report)
        {
            EditorSceneManager.OpenScene(Main);
            Shader bark = Shader.Find("Jurassic World/Interactive Tree Physical Bark");
            if (bark == null || !bark.isSupported || ShaderUtil.ShaderHasError(bark))
                throw new InvalidOperationException("Bark shader is missing or failed to compile.");

            var profile = AssetDatabase.LoadAssetAtPath<TargetVegetationLightingProfile>(TargetVegetationLighting.ProfilePath);
            if (profile == null) throw new InvalidOperationException("Missing lighting profile.");
            var entries = profile.entries.Where(e => e.category == "NSK trunk").ToArray();
            if (entries.Length != 8) throw new InvalidOperationException("Expected eight rest/contact NSK bark materials.");
            if (entries.Any(e => e.material == null || e.material.shader != bark))
                throw new InvalidOperationException("An NSK trunk material is not on the physical bark shader.");

            var atmosphere = Object.FindObjectOfType<RainforestAtmosphereController>();
            if (atmosphere == null) throw new InvalidOperationException("Missing atmosphere controller.");
            if (!Mathf.Approximately(atmosphere.profile.skyFillStrength, .03f))
                throw new InvalidOperationException("Sky fill must stay at 3%.");

            var targets = new HashSet<Material>(entries.Select(e => e.material));
            // Compare on-disk content: Unity lazily materializes shader properties into in-memory
            // material instances during rendering, which is not a persistent asset change.
            var protectedMaterials = SceneMaterials().Concat(profile.entries.Where(e => e.material != null).Select(e => e.material))
                .Where(m => !targets.Contains(m)).Select(AssetDatabase.GetAssetPath)
                .Where(path => !string.IsNullOrEmpty(path)).Distinct()
                .ToDictionary(path => path, path => File.ReadAllText(path));
            var banana = profile.entries.FirstOrDefault(e => e.category == "Banana trunk");
            if (banana == null || banana.material == null) throw new InvalidOperationException("Missing banana trunk entry.");
            string bananaBefore = File.ReadAllText(AssetDatabase.GetAssetPath(banana.material));
            var bushBark = profile.entries.Where(e => e.category == "Bush" && e.material != null && e.material.HasProperty("_DirectLightScale")).ToArray();
            if (bushBark.Length != 12) throw new InvalidOperationException("Expected twelve bush branch materials on the bark shader, found " + bushBark.Length + ".");
            foreach (var entry in bushBark)
                if (!Mathf.Approximately(entry.material.GetFloat("_DirectLightScale"), 1f) ||
                    !Mathf.Approximately(entry.material.GetFloat("_ShadowLift"), 0f))
                    throw new InvalidOperationException("Bush branch light response must stay neutral: " + entry.material.name);

            string environmentBefore = EditorJsonUtility.ToJson(atmosphere.profile);

            Camera camera = atmosphere.viewCamera;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float cameraFov = camera.fieldOfView;
            int quality = QualitySettings.GetQualityLevel();
            var preRunScales = entries.ToDictionary(e => e, e => (direct: e.directLightScale, shadowLift: e.shadowLift));
            var preRunMaterials = entries.ToDictionary(e => e, e => new Material(e.material));
            bool saved = false;
            try
            {
                QualitySettings.SetQualityLevel(1, true);
                atmosphere.ApplyQuality(1);
                Object.FindObjectOfType<RainforestSkyFill>().Apply();
                camera.fieldOfView = 45f;

                var views = BuildViews();
                var medians = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
                void Stage(string stage)
                {
                    var values = new Dictionary<string, double>(StringComparer.Ordinal);
                    foreach (View view in views)
                    {
                        camera.transform.SetPositionAndRotation(view.Position, Quaternion.LookRotation(view.Target - view.Position));
                        values[view.Name] = Capture(camera, view.Name + "-" + stage);
                    }
                    medians[stage] = values;
                }

                ApplyStage(entries, 1f, 0f);
                Stage("Baseline");
                foreach (Candidate candidate in Candidates)
                {
                    ApplyStage(entries, candidate.Direct, candidate.ShadowLift);
                    Stage(candidate.Name);
                }

                Candidate chosen = Choose(medians, report);
                ApplyStage(entries, chosen.Direct, chosen.ShadowLift);
                foreach (var entry in entries)
                {
                    entry.directLightScale = chosen.Direct;
                    entry.shadowLift = chosen.ShadowLift;
                    EditorUtility.SetDirty(entry.material);
                }
                EditorUtility.SetDirty(profile);
                Stage("Final");
                WriteMeasurements(report, medians);
                report.AppendLine("Selected " + chosen.Name + ": direct=" + F3(chosen.Direct) + ", shadowLift=" + F3(chosen.ShadowLift) + ".");

                foreach (var entry in entries)
                {
                    Material material = entry.material;
                    Require(Mathf.Approximately(material.GetFloat("_AlbedoGain"), 2f), "Unexpected albedo gain: " + material.name);
                    Require(material.HasProperty("_DirectLightScale") &&
                        Mathf.Approximately(material.GetFloat("_DirectLightScale"), chosen.Direct), "Direct light scale mismatch: " + material.name);
                    Require(material.HasProperty("_ShadowLift") &&
                        Mathf.Approximately(material.GetFloat("_ShadowLift"), chosen.ShadowLift), "Shadow lift mismatch: " + material.name);
                    Require(Mathf.Approximately(material.GetFloat("_OcclusionStrength"), .15f), "Unexpected occlusion strength: " + material.name);
                    Require(Mathf.Approximately(material.GetFloat("_NormalStrength"), 1f), "Unexpected normal strength: " + material.name);
                    Require(Mathf.Approximately(material.GetFloat("_Smoothness"), .18f), "Unexpected smoothness: " + material.name);
                }
                foreach (var pair in protectedMaterials)
                {
                    string current = File.Exists(pair.Key) ? File.ReadAllText(pair.Key) : "<missing>";
                    if (current == pair.Value) continue;
                    string safe = Path.GetFileNameWithoutExtension(pair.Key);
                    File.WriteAllText(Output + "/Diff-" + safe + "-before.txt", pair.Value);
                    File.WriteAllText(Output + "/Diff-" + safe + "-after.txt", current);
                    throw new InvalidOperationException("Unrelated material changed: " + pair.Key);
                }
                Require(File.ReadAllText(AssetDatabase.GetAssetPath(banana.material)) == bananaBefore, "Banana trunk changed.");
                Require(EditorJsonUtility.ToJson(atmosphere.profile) == environmentBefore, "Environment profile changed.");
                Require(Mathf.Approximately(atmosphere.profile.skyFillStrength, .03f), "Sky fill changed.");
                foreach (var entry in bushBark)
                    Require(Mathf.Approximately(entry.material.GetFloat("_DirectLightScale"), 1f) &&
                        Mathf.Approximately(entry.material.GetFloat("_ShadowLift"), 0f), "Bush branch light response changed.");
                Require(!ShaderUtil.ShaderHasError(bark) && bark.isSupported, "Bark shader failed after rendering.");

                foreach (var entry in entries) AssetDatabase.SaveAssetIfDirty(entry.material);
                AssetDatabase.SaveAssetIfDirty(profile);
                saved = true;
                report.AppendLine("PASS: eight rest/contact trunks share the selected values; protected materials, banana, bush branches and environment unchanged.");
            }
            finally
            {
                camera.fieldOfView = cameraFov;
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                QualitySettings.SetQualityLevel(quality, true);
                atmosphere.ApplyQuality(quality);
                if (!saved)
                {
                    foreach (var pair in preRunMaterials)
                    {
                        pair.Key.material.CopyPropertiesFromMaterial(pair.Value);
                        EditorUtility.SetDirty(pair.Key.material);
                    }
                    foreach (var pair in preRunScales)
                    {
                        pair.Key.directLightScale = pair.Value.direct;
                        pair.Key.shadowLift = pair.Value.shadowLift;
                    }
                    EditorUtility.SetDirty(profile);
                    foreach (var entry in entries) AssetDatabase.SaveAssetIfDirty(entry.material);
                    AssetDatabase.SaveAssetIfDirty(profile);
                }
                foreach (var material in preRunMaterials.Values) Object.DestroyImmediate(material);
            }
        }

        private static IEnumerable<Material> SceneMaterials()
        {
            var seen = new HashSet<Material>();
            foreach (Renderer renderer in Object.FindObjectsOfType<Renderer>())
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null && seen.Add(material)) yield return material;
            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null || terrain.terrainData == null) yield break;
            foreach (TreePrototype prototype in terrain.terrainData.treePrototypes)
            {
                if (prototype.prefab == null) continue;
                foreach (Renderer renderer in prototype.prefab.GetComponentsInChildren<Renderer>())
                    foreach (Material material in renderer.sharedMaterials)
                        if (material != null && seen.Add(material)) yield return material;
            }
        }

        private static List<View> BuildViews()
        {
            Vector3 lightSide = -RenderSettings.sun.transform.forward;
            lightSide.y = 0f;
            lightSide.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, lightSide);
            var views = new List<View>();
            for (int i = 1; i <= 4; i++)
            {
                string name = "tree0" + i;
                Renderer renderer = Object.FindObjectsOfType<MeshRenderer>().Where(r => r.sharedMaterials.Any(m => m != null && m.HasProperty("_MainTex") &&
                        AssetDatabase.GetAssetPath(m.GetTexture("_MainTex")).Contains(name + "_Textures")))
                    .OrderBy(r => (r.transform.position - new Vector3(14.7f, 1f, 11.5f)).sqrMagnitude).First();
                Vector3 target = new Vector3(renderer.transform.position.x, renderer.bounds.min.y + .85f, renderer.transform.position.z);
                foreach (var view in new[] { (label: "Lit", direction: lightSide), (label: "Side", direction: side), (label: "Shade", direction: -lightSide) })
                    views.Add(new View { Name = name + "-" + view.label, Position = target + view.direction * 1.5f, Target = target });
            }
            return views;
        }

        private static void ApplyStage(IEnumerable<TargetVegetationLightingProfile.Entry> entries, float direct, float shadowLift)
        {
            foreach (var entry in entries)
            {
                Material material = entry.material;
                material.SetFloat("_AlbedoGain", 2f);
                if (material.HasProperty("_DirectLightScale")) material.SetFloat("_DirectLightScale", direct);
                if (material.HasProperty("_ShadowLift")) material.SetFloat("_ShadowLift", shadowLift);
                if (material.HasProperty("_OcclusionStrength")) material.SetFloat("_OcclusionStrength", .15f);
                if (material.HasProperty("_NormalStrength")) material.SetFloat("_NormalStrength", 1f);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .18f);
                EditorUtility.SetDirty(material);
            }
        }

        private static Candidate Choose(Dictionary<string, Dictionary<string, double>> medians, StringBuilder report)
        {
            double LitRatio(Candidate candidate) => Enumerable.Range(1, 3).Average(i =>
                medians[candidate.Name]["tree0" + i + "-Lit"] / Math.Max(.001, medians["Baseline"]["tree0" + i + "-Lit"]));
            double ShadeMin(Candidate candidate) => Enumerable.Range(1, 4).Min(i => medians[candidate.Name]["tree0" + i + "-Shade"]);
            double ShadeMax(Candidate candidate) => Enumerable.Range(1, 4).Max(i => medians[candidate.Name]["tree0" + i + "-Shade"]);

            foreach (Candidate candidate in Candidates)
                report.AppendLine(candidate.Name + " direct=" + F3(candidate.Direct) + " lift=" + F3(candidate.ShadowLift) +
                    ", lit ratio " + LitRatio(candidate).ToString("P1", CultureInfo.InvariantCulture) +
                    ", shade " + F(ShadeMin(candidate)) + "-" + F(ShadeMax(candidate)) + ".");

            foreach (Candidate candidate in Candidates)
                if (LitRatio(candidate) >= .90 && LitRatio(candidate) <= .97 &&
                    ShadeMin(candidate) >= 20 && ShadeMax(candidate) <= 24)
                    return candidate;

            Candidate[] reaching = Candidates.Where(c => ShadeMin(c) >= 20).ToArray();
            if (reaching.Length > 0)
            {
                report.AppendLine("Targets not met exactly; choosing the smallest lift that reaches shade 20.");
                return reaching[0];
            }
            report.AppendLine("No candidate reached shade 20; choosing the highest shade result.");
            return Candidates.OrderByDescending(ShadeMin).First();
        }

        private static void WriteMeasurements(StringBuilder report, Dictionary<string, Dictionary<string, double>> medians)
        {
            report.AppendLine("Central " + PatchWidth + "x" + PatchHeight + " patch sRGB median (0-255, mean of RGB bytes).");
            report.AppendLine("stage,tree,Lit,Side,Shade");
            foreach (string stage in new[] { "Baseline", "A", "B", "C", "Final" })
                for (int i = 1; i <= 4; i++)
                {
                    string tree = "tree0" + i;
                    report.AppendLine(stage + "," + tree + "," +
                        F(medians[stage][tree + "-Lit"]) + "," +
                        F(medians[stage][tree + "-Side"]) + "," +
                        F(medians[stage][tree + "-Shade"]));
                }
            report.AppendLine("tree04 Lit is occluded by leaves; reference only, not used for the lit target.");
        }

        private static double Capture(Camera camera, string name)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture oldTarget = camera.targetTexture;
            float aspect = camera.aspect;
            var target = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.aspect = CaptureWidth / (float)CaptureHeight;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Output + "/" + name + ".png", pixels.EncodeToPNG());
                return CenterMedian(pixels.GetPixels32());
            }
            finally
            {
                camera.targetTexture = oldTarget;
                camera.aspect = aspect;
                RenderTexture.active = previous;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(pixels);
            }
        }

        private static double CenterMedian(Color32[] pixels)
        {
            int x0 = (CaptureWidth - PatchWidth) / 2;
            int y0 = (CaptureHeight - PatchHeight) / 2;
            var values = new double[PatchWidth * PatchHeight];
            int index = 0;
            for (int y = y0; y < y0 + PatchHeight; y++)
                for (int x = x0; x < x0 + PatchWidth; x++)
                {
                    Color32 color = pixels[y * CaptureWidth + x];
                    values[index++] = (color.r + color.g + color.b) / 3.0;
                }
            Array.Sort(values);
            return values[values.Length / 2];
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static string F(double value) => value.ToString("F1", CultureInfo.InvariantCulture);
        private static string F3(double value) => value.ToString("F3", CultureInfo.InvariantCulture);
    }
}
