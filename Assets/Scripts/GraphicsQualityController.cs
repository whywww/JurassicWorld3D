using System.Collections.Generic;
using UnityEngine;
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

        private readonly Dictionary<Terrain, TerrainBaseline> terrainBaselines = new Dictionary<Terrain, TerrainBaseline>();
        private int selectedTier = Medium;
        private bool menuOpen;

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
        }

        private void SelectTier(int tier)
        {
            selectedTier = tier;
            QualitySettings.SetQualityLevel(tier, true);
            ApplyTerrainTier();
            PlayerPrefs.SetInt(PreferenceKey, tier);
            PlayerPrefs.Save();
            menuOpen = false;
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
                return;
            GUI.Box(new Rect(x, y + rowHeight + 4f, width, rowHeight * 3f + 16f), "选择画质");
            for (int tier = Low; tier <= High; tier++)
            {
                string label = TierLabels[tier] + (tier == selectedTier ? "  ✓" : "");
                if (GUI.Button(new Rect(x + 8f, y + rowHeight + 26f + tier * rowHeight, width - 16f, rowHeight - 4f), label))
                    SelectTier(tier);
            }
        }
    }
}
