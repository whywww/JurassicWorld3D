using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class NavigationAcceptance
    {
        private const string PendingKey = "JurassicWorld.NavigationAcceptance.Pending";
        private static int frames;
        private static double deadline;
        private static bool checkedMap;
        private static bool reloaded;
        private static string ShotPath => Path.GetFullPath("Logs/navigation/navigation-hud.png");

        static NavigationAcceptance()
        {
            if (SessionState.GetBool(PendingKey, false))
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("Jurassic World/Navigation/Install in Main Scene")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before installing navigation.");
            if (EditorSceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the current scene before installing navigation.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            HudController hud = UnityEngine.Object.FindObjectOfType<HudController>();
            if (hud == null) throw new InvalidOperationException("Main scene HUD missing.");
            WorldNavigationHud nav = hud.GetComponent<WorldNavigationHud>();
            if (nav == null) nav = hud.gameObject.AddComponent<WorldNavigationHud>();
            nav.BuildUi(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            if (nav.PlayerArrow.GetComponent<CanvasRenderer>() == null) nav.PlayerArrow.gameObject.AddComponent<CanvasRenderer>();
            var status = hud.transform.Find("Status").GetComponent<RectTransform>();
            status.anchoredPosition = new Vector2(-268f, -30f);
            EditorUtility.SetDirty(nav);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("NAVIGATION_INSTALLED");
        }

        // Batch entry point; omit -quit so the Play Mode check can finish asynchronously.
        public static void InstallAndVerify()
        {
            try
            {
                Install();
                SessionState.SetBool(PendingKey, true);
                EditorApplication.playModeStateChanged -= OnPlayModeChanged;
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            deadline = EditorApplication.timeSinceStartup + 90;
            EditorApplication.update += Verify;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Verify()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Navigation capture timed out.");
                frames++;
                WorldNavigationHud nav = UnityEngine.Object.FindObjectOfType<WorldNavigationHud>();
                if (nav == null || !nav.MapReady || frames < 20) return;
                if (!checkedMap)
                {
                    Require(UnityEngine.Object.FindObjectsOfType<WorldNavigationHud>().Length == 1, "Duplicate navigation HUD.");
                    Require(!nav.MapCamera.enabled, "Snapshot camera must not render every frame.");
                    Require(nav.MapCamera.targetTexture.width == 512 && nav.MapCamera.targetTexture.height == 512, "Wrong thumbnail resolution.");
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 forward = Quaternion.Euler(35f, i * 90f, 0f) * Vector3.forward;
                        Require(Mathf.Abs(Mathf.DeltaAngle(WorldNavigationHud.HorizontalHeading(forward, 0f), i * 90f)) < 0.01f, "Heading/pitch mismatch.");
                    }
                    Require(WorldNavigationHud.HorizontalHeading(Vector3.up, 123f) == 123f, "Vertical heading fallback failed.");
                    Require(WorldNavigationHud.HorizontalHeading(new Vector3(-0.001f, 0f, 1f), 0f) > 359f, "North wrap failed.");
                    Require(WorldNavigationHud.MapPosition(new Vector3(0.5f, 0.5f), 220f, 12f / 220f).sqrMagnitude < 0.001f, "Map center mismatch.");
                    Require(WorldNavigationHud.MapPosition(new Vector3(2f, -1f), 220f, 12f / 220f) == new Vector2(98f, -98f), "Map edge clamp failed.");
                    Terrain terrain = Terrain.activeTerrain;
                    Vector3 origin = terrain.transform.position;
                    Vector3 size = terrain.terrainData.size;
                    foreach (Vector3 point in new[] { origin, origin + new Vector3(size.x, 0f, 0f), origin + new Vector3(0f, 0f, size.z), origin + size, origin + size * 0.5f })
                    {
                        Vector3 uv = nav.MapCamera.WorldToViewportPoint(point);
                        Require(uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f, "Terrain does not fit map.");
                    }
                    Camera view = Camera.main;
                    SmoothExplorerCamera explorer = view.GetComponent<SmoothExplorerCamera>();
                    bool enabled = explorer.enabled;
                    Quaternion rotation = view.transform.rotation;
                    Vector3 position = view.transform.position;
                    explorer.enabled = false;
                    view.transform.position = origin + size * 0.5f;
                    view.transform.rotation = Quaternion.Euler(35f, 90f, 0f);
                    nav.SendMessage("LateUpdate");
                    Require(nav.CompassText.text.StartsWith("E"), "Compass is not tracking camera.");
                    Require(nav.PlayerArrow.anchoredPosition.sqrMagnitude < 0.01f, "Player marker center mismatch.");
                    float arrowHeading = Mathf.Repeat(-nav.PlayerArrow.localEulerAngles.z, 360f);
                    Require(Mathf.Abs(Mathf.DeltaAngle(arrowHeading, explorer.TravelHeading)) < 0.01f, "Arrow is not tracking travel heading.");
                    view.transform.position = position;
                    view.transform.rotation = rotation;
                    explorer.enabled = enabled;
                    nav.SendMessage("LateUpdate");
                    CheckCreek(nav);
                    CheckThumbnail(nav.MapCamera.targetTexture);
                    nav.enabled = false;
                    Require(nav.MapCamera == null && !nav.MapReady, "Navigation resources were not released.");
                    nav.enabled = true;
                    checkedMap = true;
                    return;
                }
                if (!reloaded)
                {
                    reloaded = true;
                    UnityEngine.SceneManagement.SceneManager.LoadScene("Main");
                    return;
                }
                nav.SendMessage("LateUpdate");
                Require(nav.PlayerArrow.gameObject.activeInHierarchy, "Player arrow invisible after reload.");
                Require(nav.PlayerArrow.GetComponent<CanvasRenderer>() != null, "Player arrow CanvasRenderer missing.");
                Require(UnityEngine.Object.FindObjectsOfType<WorldNavigationHud>().Length == 1, "Duplicate navigation after reload.");
                CheckCreek(nav);
                CaptureHud(1920, 1080, "navigation-hud.png");
                CaptureHud(960, 540, "navigation-small.png");
                Debug.Log("NAVIGATION_VERIFIED: heading, pitch, north wrap, terrain coverage, marker position, independent travel direction, thumbnail, enable/disable cleanup, scene reload, 1920x1080 and 960x540 HUD captures, creek node projection/order, marker layering, missing creek fallback, scene reload.");
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        private static void CheckCreek(WorldNavigationHud nav)
        {
            Transform flow = GameObject.Find("Downstream Flow").transform;
            Require(nav.CreekLine.gameObject.activeInHierarchy, "Creek marker hidden.");
            Require(nav.CreekLine.Points.Count == flow.childCount, "Creek node count mismatch.");
            float size = nav.CreekLine.rectTransform.rect.width;
            for (int i = 0; i < flow.childCount; i++)
            {
                Vector3 uv = nav.MapCamera.WorldToViewportPoint(flow.GetChild(i).position);
                Vector2 expected = new Vector2(uv.x - 0.5f, uv.y - 0.5f) * size;
                Require((nav.CreekLine.Points[i] - expected).sqrMagnitude < 0.0001f, "Creek projection/order mismatch.");
            }
            Require(nav.CreekLine.transform.GetSiblingIndex() < nav.PlayerArrow.GetSiblingIndex(), "Creek obscures player arrow.");
            Require(nav.CreekLine.Points[0].x > 0f && nav.CreekLine.Points[nav.CreekLine.Points.Count - 1].y > nav.CreekLine.Points[0].y, "Creek should extend north on east side.");
            // Simulate a world without this creek, then restore it before taking the HUD shots.
            flow.gameObject.SetActive(false);
            var settings = new SerializedObject(nav);
            settings.FindProperty("creekFlow").objectReferenceValue = null;
            settings.ApplyModifiedPropertiesWithoutUndo();
            nav.SendMessage("UpdateCreekMarker");
            Require(!nav.CreekLine.gameObject.activeSelf && nav.CreekLine.Points.Count == 0, "Missing creek should hide marker.");
            flow.gameObject.SetActive(true);
            nav.SendMessage("UpdateCreekMarker");
            nav.BuildUi(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            nav.SendMessage("UpdateCreekMarker");
            Require(nav.CreekLine.gameObject.activeInHierarchy, "Creek marker did not recover.");
            Require(nav.CreekLine.transform.parent.GetComponentsInChildren<MinimapCreekGraphic>(true).Length == 1, "Duplicate creek marker.");
        }
        private static void CaptureHud(int width, int height, string filename)
        {
            Camera camera = Camera.main;
            Canvas[] canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
            var modes = new RenderMode[canvases.Length];
            var cameras = new Camera[canvases.Length];
            var distances = new float[canvases.Length];
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = camera.targetTexture;
            float aspect = camera.aspect;
            var output = new RenderTexture(width, height, 24);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                output.Create();
                camera.targetTexture = output;
                camera.aspect = (float)width / height;
                for (int i = 0; i < canvases.Length; i++)
                {
                    modes[i] = canvases[i].renderMode;
                    cameras[i] = canvases[i].worldCamera;
                    distances[i] = canvases[i].planeDistance;
                    if (modes[i] != RenderMode.ScreenSpaceOverlay) continue;
                    canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                    canvases[i].worldCamera = camera;
                    canvases[i].planeDistance = camera.nearClipPlane + 0.1f;
                }
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = output;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(ShotPath), filename), pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = target;
                camera.aspect = aspect;
                for (int i = 0; i < canvases.Length; i++)
                {
                    canvases[i].renderMode = modes[i];
                    canvases[i].worldCamera = cameras[i];
                    canvases[i].planeDistance = distances[i];
                }
                RenderTexture.active = previous;
                output.Release();
                UnityEngine.Object.Destroy(output);
                UnityEngine.Object.Destroy(pixels);
            }
        }
        private static void CheckThumbnail(RenderTexture renderTexture)
        {
            var previous = RenderTexture.active;
            var pixels = new Texture2D(512, 512, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = renderTexture;
                pixels.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                pixels.Apply();
                Color first = pixels.GetPixel(0, 0);
                bool varied = false;
                for (int y = 0; y < 512; y += 32)
                    for (int x = 0; x < 512; x += 32)
                        if ((pixels.GetPixel(x, y) - first).maxColorComponent > 0.05f) varied = true;
                Require(varied, "Landscape thumbnail is blank.");
                Directory.CreateDirectory(Path.GetDirectoryName(ShotPath));
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(ShotPath), "landscape.png"), pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.Destroy(pixels); }
        }

        private static void Finish(Exception error)
        {
            SessionState.SetBool(PendingKey, false);
            EditorApplication.update -= Verify;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            if (error != null) Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}