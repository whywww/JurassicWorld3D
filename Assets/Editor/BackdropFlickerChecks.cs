using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class BackdropFlickerChecks
    {
        const string Key = "BackdropFlickerChecks";
        const string Output = "Reports/Backdrop/Flicker";
        static Camera camera;
        static RenderTexture target;
        static Texture2D pixels;
        static double next;
        static int sample;
        static string label;
        static BackdropFlickerChecks()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Key, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    try { Begin(); } catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
                }
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    SessionState.SetBool(Key, false);
                    if (Application.isBatchMode) EditorApplication.Exit(0);
                }
            };
        }
        public static void Before() => Run("Before");
        public static void After() => Run("After");
        static void Run(string stage)
        {
            Directory.CreateDirectory(Output + "/" + stage);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            SessionState.SetString(Key + "Stage", stage);
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }
        static void Begin()
        {
            label = SessionState.GetString(Key + "Stage", "Before");
            camera = Camera.main;
            foreach (MonoBehaviour b in camera.GetComponents<MonoBehaviour>())
                if (!(b is RainforestImageFinish)) b.enabled = false;
            target = new RenderTexture(1146, 650, 24);
            pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            camera.targetTexture = target; camera.aspect = (float)target.width / target.height;
            sample = 0; next = EditorApplication.timeSinceStartup + 3;
            EditorApplication.update += Tick;
        }
        static void Pose()
        {
            Vector3 eye = new Vector3(49.5f, 0, 39.5f);
            eye.y = Terrain.activeTerrain.SampleHeight(eye) + Terrain.activeTerrain.transform.position.y + 1.7f;
            camera.transform.SetPositionAndRotation(eye, Quaternion.Euler(-12, 336, 0));
            var atmosphere = Object.FindObjectOfType<RainforestAtmosphereController>();
            if (atmosphere.cloudRenderer != null) atmosphere.cloudRenderer.transform.position = eye;
        }
        static void Capture(string name, bool resetPose = true)
        {
            if (resetPose) Pose();
            camera.Render();
            var old = RenderTexture.active;
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
            File.WriteAllBytes(Output + "/" + label + "/" + name + ".png", pixels.EncodeToPNG());
            RenderTexture.active = old;
        }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 1;
            try
            {
                int tier = sample / 11;
                if (tier < 3)
                {
                    Object.FindObjectOfType<GraphicsQualityController>().ApplyTier(tier);
                    Capture("Tier" + tier + "-" + (sample % 11).ToString("00"));
                    sample++; return;
                }
                var root = Object.FindObjectOfType<RainforestBackdrop>().transform;
                foreach (string group in new[] { "Outer Forest Canopy", "Baked Forest Groups", "Distant Cliff Ridges" })
                {
                    var child = root.Find(group);
                    child.gameObject.SetActive(false);
                    try { Capture("Without-" + group); }
                    finally { child.gameObject.SetActive(true); }
                }
                if (label == "After")
                {
                    RainforestBackdropStageThree.Validate();
                    int bit = 1 << root.GetComponent<RainforestBackdrop>().backdropLayer;
                    foreach (Camera other in Object.FindObjectsOfType<Camera>())
                        if (other.name.Contains("Landscape") && (other.cullingMask & bit) != 0)
                            throw new InvalidOperationException("Minimap includes backdrop.");
                    foreach (Crest.OceanPlanarReflection reflection in Object.FindObjectsOfType<Crest.OceanPlanarReflection>())
                        if ((new SerializedObject(reflection).FindProperty("_reflectionLayers").intValue & bit) != 0)
                            throw new InvalidOperationException("Reflection includes backdrop.");
                    for (int tierIndex = 0; tierIndex < 3; tierIndex++)
                    {
                        Object.FindObjectOfType<GraphicsQualityController>().ApplyTier(tierIndex);
                        for (int heading = 0; heading < 8; heading++)
                        {
                            Pose(); camera.transform.rotation = Quaternion.Euler(-12, heading * 45, 0);
                            Capture("Heading-" + tierIndex + "-" + heading, false);
                        }
                        for (int step = 0; step < 12; step++)
                        {
                            Pose(); camera.transform.position += new Vector3(step * .15f, 0, step * .1f);
                            camera.transform.rotation = Quaternion.Euler(-12, 332 + step, 0);
                            Capture("Walk-" + tierIndex + "-" + step.ToString("00"), false);
                        }
                    }
                    File.WriteAllText(Output + "/After/Checks.txt", "PASS: three tiers, eleven samples per tier at intervals >= 1 second; 8 headings and 12 walking/turning poses per tier; backdrop validation and minimap/reflection exclusion.\n");
                }
                Debug.Log("BACKDROP_FLICKER_CAPTURE_COMPLETE " + label);
                Finish();
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.update -= Tick; EditorApplication.Exit(1); }
        }
        static void Finish()
        {
            EditorApplication.update -= Tick;
            camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            EditorApplication.isPlaying = false;
        }
    }
}
