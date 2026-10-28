using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class RuntimePerformanceTools
    {
        private const string Key = "JurassicWorld.RuntimePerformance";
        static RuntimePerformanceTools()
        {
            EditorApplication.update += () =>
            {
                if (!SessionState.GetBool(Key, false)) return;
                if (EditorApplication.isPlaying) SessionState.SetBool(Key + ".Started", true);
                if (!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetBool(Key + ".Started", false) &&
                    !SessionState.GetBool(Key + ".Exit", false))
                {
                    Debug.LogWarning("JW_PERF_CANCELLED: capture stopped before completion; partial samples cannot pass acceptance.");
                    SessionState.SetInt(Key + ".Code", 1);
                    SessionState.SetBool(Key + ".Exit", true);
                }
                if (EditorApplication.isPlaying && RuntimePerformanceBenchmark.Finished)
                {
                    SessionState.SetInt(Key + ".Code", RuntimePerformanceBenchmark.Failed ? 1 : 0);
                    SessionState.SetBool(Key + ".Exit", true);
                    EditorApplication.isPlaying = false;
                }
                else if (!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetBool(Key + ".Exit", false))
                {
                    SessionState.SetBool(Key, false); SessionState.SetBool(Key + ".Exit", false);
                    if (SessionState.GetBool("JurassicWorld.Performance.Interactive", false))
                    {
                        SessionState.SetBool("JurassicWorld.Performance.Interactive", false);
                        QualitySettings.SetQualityLevel(SessionState.GetInt(Key + ".PreviousQuality", 1), true);
                        var gameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
                        EditorWindow.GetWindow(gameViewType).maximized = SessionState.GetBool(Key + ".PreviousMaximized", false);
                    }
                    if (SessionState.GetInt(Key + ".Code", 1) == 0 && Array.IndexOf(Environment.GetCommandLineArgs(), "-jwPerfBuildAfter") >= 0)
                    {
                        try { Build(); }
                        catch (Exception error) { Debug.LogException(error); SessionState.SetInt(Key + ".Code", 1); }
                    }
                    if (Application.isBatchMode || Array.IndexOf(Environment.GetCommandLineArgs(), "-jwPerfExit") >= 0)
                        EditorApplication.Exit(SessionState.GetInt(Key + ".Code", 1));
                }
            };
        }

        [MenuItem("Jurassic World/Performance/Low/Baseline Diagnostic %&#1")]
        public static void LowBaselineDiagnostic() => RunInteractive("Baseline", false);
        [MenuItem("Jurassic World/Performance/Low/Contact Diagnostic %&#2")]
        public static void LowContactDiagnostic() => RunInteractive("Contact", false);
        [MenuItem("Jurassic World/Performance/Low/Reflection Diagnostic %&#3")]
        public static void LowReflectionDiagnostic() => RunInteractive("Reflection", false);
        [MenuItem("Jurassic World/Performance/Low/Baseline Acceptance %&#4")]
        public static void LowBaselineAcceptance() => RunInteractive("Baseline", true);
        [MenuItem("Jurassic World/Performance/Low/Reflection Acceptance %&#5")]
        public static void LowReflectionAcceptance() => RunInteractive("Reflection", true);
        [MenuItem("Jurassic World/Performance/Low/Water Diagnostic %&#7")]
        public static void LowWaterDiagnostic() => RunInteractive("Water", false);
        [MenuItem("Jurassic World/Performance/Low/Batching Diagnostic %&#8")]
        public static void LowBatchingDiagnostic() => RunInteractive("Batching", false);
        [MenuItem("Jurassic World/Performance/Low/Materials Diagnostic %&#9")]
        public static void LowMaterialsDiagnostic() => RunInteractive("Materials", false);
        [MenuItem("Jurassic World/Performance/Low/Materials Acceptance %&#0")]
        public static void LowMaterialsAcceptance() => RunInteractive("Materials", true);
        [MenuItem("Jurassic World/Performance/Low/Soft Plants 10 Diagnostic %&#F1")]
        public static void LowPlants10Diagnostic() => RunInteractive("Materials", false, 10);
        [MenuItem("Jurassic World/Performance/Low/Soft Plants 20 Diagnostic %&#F2")]
        public static void LowPlants20Diagnostic() => RunInteractive("Materials", false, 20);
        [MenuItem("Jurassic World/Performance/Low/Soft Plants 30 Diagnostic %&#F3")]
        public static void LowPlants30Diagnostic() => RunInteractive("Materials", false, 30);
        [MenuItem("Jurassic World/Performance/Low/Soft Plants 30 Acceptance %&#F5")]
        public static void LowPlants30Acceptance() => RunInteractive("Materials", true, 30);
        [MenuItem("Jurassic World/Performance/Low/Verify Optimized Budgets %&#F6")]
        public static void VerifyOptimizedBudgets() => RuntimePerformanceAcceptance.Run();
        [MenuItem("Jurassic World/Performance/Low/Probe Terrain Details %&#F7")]
        public static void ProbeTerrainDetails() => LowQualityTerrainProbe.Run();
        [MenuItem("Jurassic World/Performance/Low/Verify Existing Navigation %&#F9")]
        public static void VerifyLowNavigation() => NavigationAcceptance.VerifyExistingLow();
        [MenuItem("Jurassic World/Performance/Low/Verify Information Card %&#F10")]
        public static void VerifyInformationCard() => DinosaurInfoCardAcceptance.Run();
        [MenuItem("Jurassic World/Performance/Low/Isolate Terrain Details %&#F8")]
        public static void IsolateTerrainDetails() => RunInteractive("Materials", false, 0, "creek", "normal,no-terrain-details");

        private static void RunInteractive(string stage, bool acceptance, int reduction = 0,
            string views = "forest,creek,dinosaur-close,route", string cases = "normal")
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save before starting a performance capture.");
            SessionState.SetInt(Key + ".PreviousQuality", QualitySettings.GetQualityLevel());
            foreach (var pair in new Dictionary<string, string>
            {
                { "-jwPerfQuality", "Low" }, { "-jwLowStage", stage },
                { "-jwPerfLabel", "low-" + stage.ToLowerInvariant() + (reduction > 0 ? "-thin" + reduction : "") + (acceptance ? "-acceptance-" : "-diagnostic-") + DateTime.Now.ToString("yyyyMMdd-HHmmss") },
                { "-jwLowVegetationReduction", reduction.ToString() },
                { "-jwPerfViews", views }, { "-jwPerfCases", cases },
                { "-jwPerfWarmup", acceptance ? "15" : "3" }, { "-jwPerfSeconds", acceptance ? "60" : "10" },
                { "-jwPerfRepeats", acceptance ? "3" : "1" }
            }) SessionState.SetString("JurassicWorld.Performance.Argument." + pair.Key, pair.Value);
            SessionState.SetBool("JurassicWorld.Performance.Interactive", true);
            try { Run(); }
            catch { SessionState.SetBool("JurassicWorld.Performance.Interactive", false); throw; }
        }

        public static void Run()
        {
            if (!RuntimePerformanceBenchmark.Requested) throw new InvalidOperationException("Pass -jwPerf to opt into the benchmark.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            UnityEngine.Profiling.Profiler.enabled = false;
            UnityEngine.Profiling.Profiler.enableBinaryLog = false;
            UnityEditorInternal.ProfilerDriver.deepProfiling = false;
            if (!Application.isBatchMode)
            {
                var gameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
                var gameView = EditorWindow.GetWindow(gameViewType);
                SessionState.SetBool(Key + ".PreviousMaximized", gameView.maximized);
                gameView.maximized = true;
                var assembly = typeof(UnityEditor.Editor).Assembly;
                var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
                var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
                var sizes = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);
                var groupType = assembly.GetType("UnityEditor.GameViewSizeGroupType");
                var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new[] { Enum.Parse(groupType, "Standalone") });
                string[] args = Environment.GetCommandLineArgs();
                int widthIndex = Array.IndexOf(args, "-jwPerfWidth"), heightIndex = Array.IndexOf(args, "-jwPerfHeight");
                int width = widthIndex >= 0 ? int.Parse(args[widthIndex + 1]) : 1920;
                int height = heightIndex >= 0 ? int.Parse(args[heightIndex + 1]) : 1080;
                var sizeType = assembly.GetType("UnityEditor.GameViewSize");
                var fixedType = assembly.GetType("UnityEditor.GameViewSizeType");
                var size = Activator.CreateInstance(sizeType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { Enum.Parse(fixedType, "FixedResolution"), (object)width, height, "JW benchmark" }, null);
                group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
                int count = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null);
                gameViewType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(gameView, count - 1);
                gameView.Show(); gameView.Focus();
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-jwPerfNoSceneView") >= 0 || SessionState.GetBool("JurassicWorld.Performance.Interactive", false))
                foreach (var view in SceneView.sceneViews.Cast<SceneView>().ToArray()) view.Close();
            SessionState.SetBool(Key, true);
            SessionState.SetBool(Key + ".Started", false);
            SessionState.SetBool(Key + ".Exit", false);
            EditorApplication.isPlaying = true;
        }

        public static void Build()
        {
            string path = "Builds/Performance/JurassicWorld.exe";
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-jwPerfBuildPath");
            if (index >= 0 && index + 1 < args.Length) path = args[index + 1];
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Main.unity" }, locationPathName = path,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Performance build failed: " + report.summary.result);
        }

        [MenuItem("Jurassic World/Performance/Audit Main Rendering")]
        public static void Audit()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save before auditing Main.");
            string previous = EditorSceneManager.GetActiveScene().path;
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                foreach (var plant in UnityEngine.Object.FindObjectsOfType<InteractiveFoliage>()) plant.PrepareRuntime();
                var renderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
                var report = new StringBuilder("# Main rendering audit\n\nRestoration bindings applied in memory to inspect untouched runtime plants. No scene or mesh assets saved.\n\n");
                report.AppendLine($"Mesh renderers: {renderers.Length}; LOD groups: {UnityEngine.Object.FindObjectsOfType<LODGroup>().Length}; interactive plants: {UnityEngine.Object.FindObjectsOfType<InteractiveFoliage>().Length}\n");
                report.AppendLine("|Shader|Renderers using shader|Unique materials|Instancing enabled|Shadow casters|\n|---|---:|---:|---:|---:|");
                var entries = renderers.SelectMany(r => r.sharedMaterials.Where(m => m != null).Select(m => new { renderer = r, material = m }));
                foreach (var group in entries.GroupBy(e => e.material.shader.name).OrderByDescending(g => g.Count()))
                    report.AppendLine($"|{group.Key}|{group.Select(e => e.renderer).Distinct().Count()}|{group.Select(e => e.material).Distinct().Count()}|{group.Select(e => e.material).Distinct().Count(m => m.enableInstancing)}|{group.Select(e => e.renderer).Distinct().Count(r => r.shadowCastingMode != ShadowCastingMode.Off)}|");
                report.AppendLine("\n|Repeated mesh + material|Instances|Triangles per mesh|Without LOD|With property blocks|Instancing|Material asset|\n|---|---:|---:|---:|---:|---|---|");
                var draws = renderers.SelectMany(r =>
                {
                    var filter = r.GetComponent<MeshFilter>();
                    return r.sharedMaterials.Where(m => m != null).Select(m => new { renderer = r, mesh = filter != null ? filter.sharedMesh : null, material = m });
                });
                foreach (var group in draws.GroupBy(d => new { d.mesh, d.material }).Where(g => g.Count() > 1).OrderByDescending(g => g.Count()).Take(60))
                    report.AppendLine($"|{group.Key.mesh?.name}|{group.Count()}|{Triangles(group.Key.mesh)}|{group.Count(d => d.renderer.GetComponentInParent<LODGroup>() == null)}|{group.Count(d => d.renderer.HasPropertyBlock())}|{group.Key.material.enableInstancing}|{AssetDatabase.GetAssetPath(group.Key.material)}|");
                report.AppendLine("\n|Root|Mesh renderers|LOD groups|All authored mesh triangles (includes inactive LOD levels)|\n|---|---:|---:|---:|");
                foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects().OrderByDescending(r => r.GetComponentsInChildren<MeshRenderer>().Length))
                {
                    int count = root.GetComponentsInChildren<MeshRenderer>().Length;
                    if (count > 0) report.AppendLine($"|{root.name}|{count}|{root.GetComponentsInChildren<LODGroup>().Length}|{root.GetComponentsInChildren<MeshFilter>().Sum(f => (long)Triangles(f.sharedMesh))}|");
                }
                Directory.CreateDirectory("Logs/RuntimePerformance");
                File.WriteAllText("Logs/RuntimePerformance/rendering-audit.md", report.ToString());
                Debug.Log("JW_PERF_AUDIT_COMPLETE");
            }
            finally { if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous); }
        }

        private static ulong Triangles(Mesh mesh)
        {
            if (mesh == null) return 0;
            ulong total = 0;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++) total += mesh.GetIndexCount(submesh) / 3;
            return total;
        }

        // Explicit editor maintenance; never changes scene objects or batches deforming meshes.
        [MenuItem("Jurassic World/Performance/Enable Repeated Vegetation Material Instancing")]
        public static void OptimizeMaterials()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save before optimizing materials.");
            string previous = EditorSceneManager.GetActiveScene().path;
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                var materials = new HashSet<Material>();
                foreach (var renderer in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                    if (renderer.additionalVertexStreams == null)
                        foreach (var material in renderer.sharedMaterials) if (material != null) materials.Add(material);
                // InteractiveFoliage restores these shared materials before the first render.
                foreach (var plant in UnityEngine.Object.FindObjectsOfType<InteractiveFoliage>())
                    foreach (var original in plant.originals)
                        if (original.additionalStreams == null && original.materials != null)
                            foreach (var material in original.materials) if (material != null) materials.Add(material);
                var report = new StringBuilder("# Instancing material changes\n\n");
                foreach (var material in materials.OrderBy(AssetDatabase.GetAssetPath))
                {
                    string path = AssetDatabase.GetAssetPath(material);
                    string shader = material.shader.name;
                    bool supported = shader == "Standard" || shader == "Jurassic World/Foliage Cutout Double Sided" ||
                        shader == "Jurassic World/Vegetation Prototype Cutout" || shader.StartsWith("Jurassic World/Interactive ", StringComparison.Ordinal);
                    if (!supported || material.enableInstancing || !path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) continue;
                    material.enableInstancing = true; EditorUtility.SetDirty(material);
                    report.AppendLine("- " + path);
                }
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory("Logs/RuntimePerformance");
                File.WriteAllText("Logs/RuntimePerformance/instancing-materials.md", report.ToString());
                Debug.Log("JW_PERF_INSTANCING_COMPLETE\n" + report);
            }
            finally { if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous); }
        }
    }
}
