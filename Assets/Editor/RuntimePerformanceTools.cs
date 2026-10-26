using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                if (EditorApplication.isPlaying && RuntimePerformanceBenchmark.Finished)
                {
                    SessionState.SetInt(Key + ".Code", RuntimePerformanceBenchmark.Failed ? 1 : 0);
                    SessionState.SetBool(Key + ".Exit", true);
                    EditorApplication.isPlaying = false;
                }
                else if (!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetBool(Key + ".Exit", false))
                {
                    SessionState.SetBool(Key, false); SessionState.SetBool(Key + ".Exit", false);
                    if (SessionState.GetInt(Key + ".Code", 1) == 0 && Array.IndexOf(Environment.GetCommandLineArgs(), "-jwPerfBuildAfter") >= 0)
                    {
                        try { Build(); }
                        catch (Exception error) { Debug.LogException(error); SessionState.SetInt(Key + ".Code", 1); }
                    }
                    if (Application.isBatchMode) EditorApplication.Exit(SessionState.GetInt(Key + ".Code", 1));
                }
            };
        }

        public static void Run()
        {
            if (!RuntimePerformanceBenchmark.Requested) throw new InvalidOperationException("Pass -jwPerf to opt into the benchmark.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-jwPerfNoSceneView") >= 0)
                foreach (var view in SceneView.sceneViews.Cast<SceneView>().ToArray()) view.Close();
            SessionState.SetBool(Key, true);
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
