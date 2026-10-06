using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace JurassicWorld
{
    [DisallowMultipleComponent]
    public sealed class WorldNavigationHud : MonoBehaviour
    {
        [SerializeField] private Transform player;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private bool automaticWorldBounds = true;
        [SerializeField] private Bounds worldBounds = new Bounds(Vector3.zero, new Vector3(200f, 10f, 200f));
        [SerializeField, Min(80f)] private float mapSize = 220f;
        [SerializeField] private Vector2 screenMargin = new Vector2(20f, 20f);
        [SerializeField, Range(0f, 0.25f)] private float landscapeMargin = 0.03f;
        [SerializeField] private LayerMask landscapeLayers = ~0;
        [SerializeField] private RectTransform panel;
        [SerializeField] private RawImage mapImage;
        [SerializeField] private RectTransform playerArrow;
        [SerializeField] private Text compassText;
        [SerializeField] private Transform creekFlow;
        [SerializeField] private MinimapCreekGraphic creekLine;

        private Camera mapCamera;
        private RenderTexture mapTexture;
        private SmoothExplorerCamera explorer;
        private Coroutine captureRoutine;
        private bool refreshRequested = true;
        private bool mapReady;
        private float nextTerrainCheck;
        private int terrainSignature;
        private float lastHeading;
        private static readonly string[] Directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        public bool MapReady => mapReady;
        public Camera MapCamera => mapCamera;
        public RectTransform PlayerArrow => playerArrow;
        public Text CompassText => compassText;
        public MinimapCreekGraphic CreekLine => creekLine;

        // Called by the scene builder too, so the UI remains visible/editable outside Play Mode.
        public void BuildUi(Font font)
        {
            if (panel != null) { ApplyLayout(); EnsureCreekOverlay(); return; }
            panel = MakeRect(transform, "World Navigation", new Vector2(mapSize + 8f, mapSize + 54f));
            panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.one;
            panel.anchoredPosition = -screenMargin;
            Image background = panel.gameObject.AddComponent<Image>();
            background.color = new Color(0.025f, 0.055f, 0.045f, 0.88f);
            background.raycastTarget = false;

            compassText = MakeText(panel, "Compass", font, 18, new Vector2(mapSize, 34f));
            compassText.rectTransform.anchoredPosition = new Vector2(0f, mapSize * 0.5f + 8f);
            compassText.text = "N   000°";
            RectTransform mapRect = MakeRect(panel, "Landscape", new Vector2(mapSize, mapSize));
            mapRect.anchoredPosition = new Vector2(0f, -19f);
            mapImage = mapRect.gameObject.AddComponent<RawImage>();
            mapImage.color = Color.white;
            mapImage.raycastTarget = false;
            var border = mapRect.gameObject.AddComponent<Outline>();
            border.effectColor = new Color(0.45f, 0.65f, 0.5f, 0.8f);
            border.effectDistance = Vector2.one;
            MakeText(mapRect, "North", font, 15, new Vector2(22f, 22f)).rectTransform.anchoredPosition = new Vector2(0f, mapSize * 0.5f - 13f);
            mapRect.Find("North").GetComponent<Text>().text = "N";
            MakeText(mapRect, "East", font, 15, new Vector2(22f, 22f)).rectTransform.anchoredPosition = new Vector2(mapSize * 0.5f - 13f, 0f);
            mapRect.Find("East").GetComponent<Text>().text = "E";
            MakeText(mapRect, "South", font, 15, new Vector2(22f, 22f)).rectTransform.anchoredPosition = new Vector2(0f, -mapSize * 0.5f + 13f);
            mapRect.Find("South").GetComponent<Text>().text = "S";
            MakeText(mapRect, "West", font, 15, new Vector2(22f, 22f)).rectTransform.anchoredPosition = new Vector2(-mapSize * 0.5f + 13f, 0f);
            mapRect.Find("West").GetComponent<Text>().text = "W";
            playerArrow = MakeRect(mapRect, "Player", new Vector2(18f, 22f));
            var arrow = playerArrow.gameObject.AddComponent<NavigationArrowGraphic>();
            arrow.color = new Color(1f, 0.86f, 0.25f);
            arrow.raycastTarget = false;
            var shadow = playerArrow.gameObject.AddComponent<Outline>();
            shadow.effectColor = Color.black;
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            playerArrow.gameObject.SetActive(false);
            EnsureCreekOverlay();
        }

        private void EnsureCreekOverlay()
        {
            if (creekFlow == null)
            {
                GameObject flow = GameObject.Find("Downstream Flow");
                if (flow != null) creekFlow = flow.transform;
            }
            if (creekLine == null)
            {
                Transform existing = mapImage.transform.Find("Creek Marker");
                RectTransform rect = existing != null ? (RectTransform)existing : MakeRect(mapImage.transform, "Creek Marker", new Vector2(mapSize, mapSize));
                creekLine = rect.GetComponent<MinimapCreekGraphic>();
                if (creekLine == null) creekLine = rect.gameObject.AddComponent<MinimapCreekGraphic>();
                creekLine.color = new Color(0.35f, 0.8f, 1f);
                creekLine.raycastTarget = false;
            }
            creekLine.rectTransform.sizeDelta = new Vector2(mapSize, mapSize);
            creekLine.transform.SetAsFirstSibling();
            creekLine.gameObject.SetActive(mapReady);
        }

        private void UpdateCreekMarker()
        {
            EnsureCreekOverlay();
            bool visible = mapReady && creekFlow != null && creekFlow.gameObject.activeInHierarchy && creekFlow.childCount >= 2;
            creekLine.gameObject.SetActive(visible);
            if (!visible) { creekLine.SetPoints(new Vector2[0]); return; }
            var points = new Vector2[creekFlow.childCount];
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 viewport = mapCamera.WorldToViewportPoint(creekFlow.GetChild(i).position);
                points[i] = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f) * mapSize;
            }
            creekLine.SetPoints(points);
        }
        private void ApplyLayout()
        {
            mapSize = Mathf.Max(80f, mapSize);
            panel.sizeDelta = new Vector2(mapSize + 8f, mapSize + 54f);
            panel.anchoredPosition = -screenMargin;
            mapImage.rectTransform.sizeDelta = new Vector2(mapSize, mapSize);
            compassText.rectTransform.sizeDelta = new Vector2(mapSize, 34f);
            compassText.rectTransform.anchoredPosition = new Vector2(0f, mapSize * 0.5f + 8f);
            Transform map = mapImage.transform;
            map.Find("North").GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, mapSize * 0.5f - 13f);
            map.Find("East").GetComponent<RectTransform>().anchoredPosition = new Vector2(mapSize * 0.5f - 13f, 0f);
            map.Find("South").GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -mapSize * 0.5f + 13f);
            map.Find("West").GetComponent<RectTransform>().anchoredPosition = new Vector2(-mapSize * 0.5f + 13f, 0f);
        }
        private static RectTransform MakeRect(Transform parent, string name, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            return rect;
        }

        private static Text MakeText(Transform parent, string name, Font font, int size, Vector2 rectSize)
        {
            Text text = MakeRect(parent, name, rectSize).gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.black;
            return text;
        }

        private void Awake()
        {
            BuildUi(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        }

        private void OnEnable()
        {
            if (panel != null) panel.gameObject.SetActive(true);
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            TerrainCallbacks.heightmapChanged += OnHeightmapChanged;
            TerrainCallbacks.textureChanged += OnTextureChanged;
            RefreshMap();
        }

        public void RefreshMap()
        {
            refreshRequested = true;
            nextTerrainCheck = 0f;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => RefreshMap();
        private void OnSceneUnloaded(Scene scene) => RefreshMap();
        private void OnHeightmapChanged(Terrain terrain, RectInt region, bool synced) => RefreshMap();
        private void OnTextureChanged(Terrain terrain, string texture, RectInt region, bool synced) => RefreshMap();

        private void LateUpdate()
        {
            if (viewCamera == null) viewCamera = Camera.main;
            if (player == null && viewCamera != null)
            {
                explorer = viewCamera.GetComponentInParent<SmoothExplorerCamera>();
                player = explorer != null ? explorer.transform : viewCamera.transform;
            }
            if (player != null && explorer == null) explorer = player.GetComponent<SmoothExplorerCamera>();
            if (viewCamera != null)
            {
                lastHeading = HorizontalHeading(viewCamera.transform.forward, lastHeading);
                int degrees = Mathf.RoundToInt(lastHeading) % 360;
                compassText.text = Directions[Mathf.RoundToInt(lastHeading / 45f) % 8] + "   " + degrees.ToString("000") + "°";
            }
            if (Time.unscaledTime >= nextTerrainCheck)
            {
                nextTerrainCheck = Time.unscaledTime + 1f;
                if (TryWorldBounds(out Bounds bounds, out int signature))
                {
                    if (signature != terrainSignature) refreshRequested = true;
                    if (refreshRequested && captureRoutine == null)
                        captureRoutine = StartCoroutine(CaptureLandscape(bounds, signature));
                }
                else
                {
                    mapReady = false;
                    mapImage.texture = null;
                    UpdateCreekMarker();
                }
            }
            playerArrow.gameObject.SetActive(player != null && mapReady);
            if (player != null && mapReady)
            {
                Vector3 viewport = mapCamera.WorldToViewportPoint(player.position);
                float inset = 12f / mapSize;
                playerArrow.anchoredPosition = MapPosition(viewport, mapSize, inset);
                float heading = explorer != null ? explorer.TravelHeading : HorizontalHeading(player.forward, 0f);
                playerArrow.localRotation = Quaternion.Euler(0f, 0f, -heading);
            }
        }

        public static float HorizontalHeading(Vector3 forward, float fallback)
        {
            return forward.x * forward.x + forward.z * forward.z < 0.000001f
                ? fallback : Mathf.Repeat(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 360f);
        }

        public static Vector2 MapPosition(Vector3 viewport, float size, float inset)
        {
            return new Vector2(Mathf.Clamp(viewport.x, inset, 1f - inset) - 0.5f,
                Mathf.Clamp(viewport.y, inset, 1f - inset) - 0.5f) * size;
        }

        private bool TryWorldBounds(out Bounds bounds, out int signature)
        {
            bounds = worldBounds;
            signature = worldBounds.GetHashCode();
            if (!automaticWorldBounds) return bounds.size.x > 0f && bounds.size.z > 0f;
            bool found = false;
            signature = 17;
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                if (terrain.terrainData == null) continue;
                Bounds current = new Bounds(terrain.transform.position + terrain.terrainData.size * 0.5f, terrain.terrainData.size);
                if (found) bounds.Encapsulate(current); else bounds = current;
                found = true;
                unchecked { signature = signature * 31 + terrain.terrainData.GetInstanceID(); signature = signature * 31 + current.GetHashCode(); }
            }
            return found;
        }

        private IEnumerator CaptureLandscape(Bounds bounds, int signature)
        {
            // Let scene Start methods and terrain initialization finish before taking the snapshot.
            refreshRequested = false;
            yield return null;
            yield return null;
            try
            {
                if (mapCamera == null)
                {
                    var cameraObject = new GameObject("Landscape Snapshot Camera", typeof(Camera));
                    SceneManager.MoveGameObjectToScene(cameraObject, gameObject.scene);
                    mapCamera = cameraObject.GetComponent<Camera>();
                    mapCamera.enabled = false;
                    mapCamera.orthographic = true;
                    mapCamera.clearFlags = CameraClearFlags.SolidColor;
                    mapCamera.backgroundColor = new Color(0.08f, 0.13f, 0.11f);
                    mapCamera.allowHDR = false;
                    mapCamera.allowMSAA = false;
                    mapCamera.useOcclusionCulling = false;
                }
                if (mapTexture == null)
                {
                    mapTexture = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
                    mapTexture.name = "Landscape Thumbnail";
                    mapTexture.Create();
                }
                float span = Mathf.Max(bounds.size.x, bounds.size.z);
                float altitude = Mathf.Max(50f, span);
                mapCamera.transform.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.max.y + altitude, bounds.center.z), Quaternion.Euler(90f, 0f, 0f));
                mapCamera.aspect = 1f;
                mapCamera.orthographicSize = span * (0.5f + landscapeMargin);
                mapCamera.nearClipPlane = 0.1f;
                mapCamera.farClipPlane = altitude + bounds.size.y + 100f;
                mapCamera.cullingMask = landscapeLayers.value & ~(1 << LayerMask.NameToLayer("UI"));
                mapCamera.targetTexture = mapTexture;
                bool fog = RenderSettings.fog;
                Terrain[] terrains = Terrain.activeTerrains;
                float[] billboardDistances = new float[terrains.Length];
                float[] treeDistances = new float[terrains.Length];
                var hiddenRenderers = new Dictionary<Renderer, bool>();
                try
                {
                    foreach (MovementCorridorDebug debug in FindObjectsOfType<MovementCorridorDebug>()) HideRenderers(debug.transform, hiddenRenderers);
                    foreach (DinosaurAgent dinosaur in FindObjectsOfType<DinosaurAgent>()) HideRenderers(dinosaur.transform, hiddenRenderers);
                    if (player != null) HideRenderers(player, hiddenRenderers);
                    // Eye-height fog and tree billboards obscure a high, orthographic landscape view.
                    RenderSettings.fog = false;
                    for (int i = 0; i < terrains.Length; i++)
                    {
                        billboardDistances[i] = terrains[i].treeBillboardDistance;
                        treeDistances[i] = terrains[i].treeDistance;
                        terrains[i].treeDistance = Mathf.Max(treeDistances[i], mapCamera.farClipPlane * 2f);
                        terrains[i].treeBillboardDistance = terrains[i].treeDistance;
                    }
                    mapCamera.Render();
                }
                finally
                {
                    RenderSettings.fog = fog;
                    foreach (var entry in hiddenRenderers)
                        if (entry.Key != null) entry.Key.forceRenderingOff = entry.Value;
                    for (int i = 0; i < terrains.Length; i++)
                    {
                        if (terrains[i] == null) continue;
                        terrains[i].treeBillboardDistance = billboardDistances[i];
                        terrains[i].treeDistance = treeDistances[i];
                    }
                }
                mapImage.texture = mapTexture;
                terrainSignature = signature;
                mapReady = true;
                UpdateCreekMarker();
            }
            finally { captureRoutine = null; }
        }

        private static void HideRenderers(Transform root, Dictionary<Renderer, bool> previous)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (previous.ContainsKey(renderer)) continue;
                previous.Add(renderer, renderer.forceRenderingOff);
                renderer.forceRenderingOff = true;
            }
        }
        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            TerrainCallbacks.heightmapChanged -= OnHeightmapChanged;
            TerrainCallbacks.textureChanged -= OnTextureChanged;
            if (captureRoutine != null) StopCoroutine(captureRoutine);
            captureRoutine = null;
            if (panel != null) panel.gameObject.SetActive(false);
            ReleaseResources();
        }



        private void OnDestroy() => ReleaseResources();

        private void ReleaseResources()
        {
            mapReady = false;
            if (mapImage != null) mapImage.texture = null;
            if (creekLine != null) creekLine.gameObject.SetActive(false);
            if (mapCamera != null)
            {
                mapCamera.targetTexture = null;
                Destroy(mapCamera.gameObject);
                mapCamera = null;
            }
            if (mapTexture != null)
            {
                mapTexture.Release();
                Destroy(mapTexture);
                mapTexture = null;
            }
        }
    }
}