using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    // Untimed, in-memory investigation. Never edits the scene or authored terrain.
    public static class LowQualityTerrainProbe
    {
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save before probing terrain.");
            var report = new StringBuilder("terrain,resolution,patch,layer,prototype,render_mode,instancing,map_total,low_instances\n");
            foreach (var terrain in Terrain.activeTerrains)
            {
                var original = terrain.terrainData;
                int n = original.detailResolution;
                if (n == 0) continue;
                for (int patch = original.detailResolutionPerPatch; patch <= 32; patch *= 2)
                {
                    TerrainData data = original;
                    try
                    {
                        if (patch != original.detailResolutionPerPatch)
                        {
                            data = UnityEngine.Object.Instantiate(original);
                            data.hideFlags = HideFlags.DontSave;
                            data.SetDetailResolution(n, patch);
                            for (int layer = 0; layer < original.detailPrototypes.Length; layer++)
                                data.SetDetailLayer(0, 0, layer, original.GetDetailLayer(0, 0, n, n, layer));
                        }
                        for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
                        {
                            long mapTotal = 0, instances = 0;
                            foreach (int value in data.GetDetailLayer(0, 0, n, n, layer)) mapTotal += value;
                            for (int y = 0; y < data.detailPatchCount; y++)
                            for (int x = 0; x < data.detailPatchCount; x++)
                                instances += data.ComputeDetailInstanceTransforms(x, y, layer, terrain.detailObjectDensity * .4f, out _).Length;
                            var prototype = data.detailPrototypes[layer];
                            report.AppendLine($"{terrain.name},{n},{patch},{layer},{prototype.prototype?.name ?? prototype.prototypeTexture?.name},{prototype.renderMode},{prototype.useInstancing},{mapTotal},{instances}");
                        }
                    }
                    finally { if (data != original) UnityEngine.Object.DestroyImmediate(data); }
                }
            }
            Directory.CreateDirectory("Logs/RuntimePerformance");
            File.WriteAllText("Logs/RuntimePerformance/terrain-detail-probe.csv", report.ToString());
            Debug.Log("JW_TERRAIN_DETAIL_PROBE_COMPLETE\n" + report);
        }
    }
}
