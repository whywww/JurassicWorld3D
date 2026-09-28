using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace JurassicWorld
{
    /// <summary>Applies the three player-facing Built-in Render Pipeline quality tiers.</summary>
    public sealed class GraphicsQualityController : MonoBehaviour
    {
        private const string PreferenceKey = "JurassicWorld.GraphicsQuality";
        private const int Low = 0;
        private const int Medium = 1;
        private const int High = 2;
        private static readonly string[] TierLabels = { "低", "中", "高" };
        private static readonly string[] TierNames = { "Low", "Medium", "High" };

        private readonly Dictionary<Terrain, TerrainBaseline> terrainBaselines = new Dictionary<Terrain, TerrainBaseline>();
        private int selectedTier = Medium;
        private bool menuOpen;
        private float benchmarkElapsed;
        private int benchmarkFrames;
        private double benchmarkTextureMiBSum;
        private double benchmarkAllocatedMiBSum;
        private string benchmarkResult;
        private const float BenchmarkSeconds = 15f;

        private struct TerrainBaseline
        {
            public float detailDistance;
            public float detailDensity;
            public float treeDistance;
            public float treeBillboardDistance;
            public int maxFullLodTrees;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            var gameObject = new GameObject("Graphics Quality Controller");
            DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<GraphicsQualityController>();
        }

        private void Awake()
        {
            selectedTier = Mathf.Clamp(PlayerPrefs.GetInt(PreferenceKey, Medium), Low, High);
            QualitySettings.SetQualityLevel(selectedTier, true);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var removed = new List<Terrain>();
            foreach (Terrain terrain in terrainBaselines.Keys)
                if (terrain == null) removed.Add(terrain);
            foreach (Terrain terrain in removed)
                terrainBaselines.Remove(terrain);

            foreach (Terrain terrain in FindObjectsOfType<Terrain>())
            {
                if (terrainBaselines.ContainsKey(terrain)) continue;
                terrainBaselines.Add(terrain, new TerrainBaseline
                {
                    detailDistance = terrain.detailObjectDistance,
                    detailDensity = terrain.detailObjectDensity,
                    treeDistance = terrain.treeDistance,
                    treeBillboardDistance = terrain.treeBillboardDistance,
                    maxFullLodTrees = terrain.treeMaximumFullLODCount
                });
            }
            ApplyTerrainTier();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F10))
                menuOpen = !menuOpen;

            if (benchmarkFrames == 0 && benchmarkElapsed <= 0f) return;
            benchmarkElapsed += Time.unscaledDeltaTime;
            benchmarkFrames++;
            benchmarkTextureMiBSum += Texture.currentTextureMemory / 1048576.0;
            benchmarkAllocatedMiBSum += Profiler.GetTotalAllocatedMemoryLong() / 1048576.0;
            if (benchmarkElapsed >= BenchmarkSeconds)
                FinishBenchmark();
        }

        private void SelectTier(int tier)
        {
            selectedTier = tier;
            QualitySettings.SetQualityLevel(tier, true);
            ApplyTerrainTier();
            PlayerPrefs.SetInt(PreferenceKey, tier);
            PlayerPrefs.Save();
            menuOpen = false;
            benchmarkElapsed = 0f;
            benchmarkFrames = 0;
        }

        private void StartBenchmark()
        {
            benchmarkElapsed = Mathf.Epsilon;
            benchmarkFrames = 0;
            benchmarkTextureMiBSum = 0;
            benchmarkAllocatedMiBSum = 0;
            benchmarkResult = null;
            menuOpen = false;
        }

        private void FinishBenchmark()
        {
            double fps = benchmarkFrames / benchmarkElapsed;
            double textureMiB = benchmarkTextureMiBSum / benchmarkFrames;
            double allocatedMiB = benchmarkAllocatedMiBSum / benchmarkFrames;
            string path = Path.Combine(Application.persistentDataPath, "quality-benchmark.csv");
            bool newFile = !File.Exists(path);
            var csv = new StringBuilder();
            if (newFile)
                csv.AppendLine("utc,tier,scene,width,height,seconds,average_fps,average_texture_mib,average_unity_allocated_mib");
            csv.Append(System.DateTime.UtcNow.ToString("o")).Append(',').Append(TierNames[selectedTier]).Append(',')
                .Append(SceneManager.GetActiveScene().name).Append(',').Append(Screen.width).Append(',')
                .Append(Screen.height).Append(',').Append(benchmarkElapsed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                .Append(',').Append(fps.ToString("F1", System.Globalization.CultureInfo.InvariantCulture))
                .Append(',').Append(textureMiB.ToString("F1", System.Globalization.CultureInfo.InvariantCulture))
                .Append(',').Append(allocatedMiB.ToString("F1", System.Globalization.CultureInfo.InvariantCulture))
                .AppendLine();
            benchmarkResult = string.Format("{0:F1} FPS / 纹理 {1:F1} MiB", fps, textureMiB);
            try
            {
                File.AppendAllText(path, csv.ToString());
                Debug.Log("[GraphicsQuality] " + benchmarkResult + "；记录：" + path);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[GraphicsQuality] 无法保存测量结果：" + exception.Message);
            }
            benchmarkElapsed = 0f;
            benchmarkFrames = 0;
        }

        private void ApplyTerrainTier()
        {
            float detailDistanceScale = selectedTier == High ? 1f : selectedTier == Medium ? 0.75f : 0.5f;
            float detailDensityScale = selectedTier == High ? 1f : selectedTier == Medium ? 0.75f : 0.4f;
            float treeDistanceScale = selectedTier == High ? 1f : selectedTier == Medium ? 0.8f : 0.55f;
            float fullLodScale = selectedTier == High ? 1f : selectedTier == Medium ? 0.75f : 0.5f;

            foreach (KeyValuePair<Terrain, TerrainBaseline> entry in terrainBaselines)
            {
                Terrain terrain = entry.Key;
                if (terrain == null) continue;
                TerrainBaseline baseline = entry.Value;
                terrain.detailObjectDistance = baseline.detailDistance * detailDistanceScale;
                terrain.detailObjectDensity = baseline.detailDensity * detailDensityScale;
                terrain.treeDistance = baseline.treeDistance * treeDistanceScale;
                terrain.treeBillboardDistance = baseline.treeBillboardDistance * treeDistanceScale;
                terrain.treeMaximumFullLODCount = Mathf.RoundToInt(baseline.maxFullLodTrees * fullLodScale);
            }
        }

        private void OnGUI()
        {
            const float x = 16f;
            const float y = 16f;
            const float width = 172f;
            const float rowHeight = 38f;
            if (GUI.Button(new Rect(x, y, width, rowHeight), "画质: " + TierLabels[selectedTier] + "  (F10)"))
                menuOpen = !menuOpen;

            if (!menuOpen)
            {
                if (benchmarkFrames > 0)
                    GUI.Label(new Rect(x, y + rowHeight, 250f, 28f), "测量中 " + Mathf.CeilToInt(BenchmarkSeconds - benchmarkElapsed) + " 秒");
                else if (!string.IsNullOrEmpty(benchmarkResult))
                    GUI.Label(new Rect(x, y + rowHeight, 280f, 28f), benchmarkResult);
                return;
            }
            GUI.Box(new Rect(x, y + rowHeight + 4f, width, rowHeight * 4f + 16f), "选择画质");
            for (int tier = Low; tier <= High; tier++)
            {
                string label = TierLabels[tier] + (tier == selectedTier ? "  ✓" : "");
                if (GUI.Button(new Rect(x + 8f, y + rowHeight + 26f + tier * rowHeight, width - 16f, rowHeight - 4f), label))
                    SelectTier(tier);
            }
            if (GUI.Button(new Rect(x + 8f, y + rowHeight + 26f + 3f * rowHeight, width - 16f, rowHeight - 4f), "测量 15 秒"))
                StartBenchmark();
        }
    }
}
