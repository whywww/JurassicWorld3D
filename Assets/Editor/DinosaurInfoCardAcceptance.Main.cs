using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static partial class DinosaurInfoCardAcceptance
    {
        private const string MainKey = "DinosaurInfoCard.MainAcceptance";
        private static Camera mainCamera;
        private static DinosaurInfoCard mainCard;
        private static DinosaurVisibility mainDetector;
        private static RenderTexture mainOutput;
        private static int mainView, mainPhase, mainFrames, lastFrame;
        private static float savedTimeScale;
        private static Recorder maskRecorder;
        private static readonly List<double> frameTimes = new List<double>();
        private static readonly List<double> captureTimes = new List<double>();
        private static readonly List<double> gpuTimes = new List<double>();
        private static readonly StringBuilder mainReport = new StringBuilder();
        private static int visibleCards;
        private static double mainDeadline;
        private static float lastCaptureTime;
        private static MonoBehaviour focusOwner;
        private static Vector3 focusOffset;
        private static readonly string[] ViewNames = { "player-start", "ground-focus", "dense-forest", "flying-focus" };

        static DinosaurInfoCardAcceptance()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(MainKey, false))
                    EditorApplication.delayCall += BeginMain;
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(MainKey + ".Exit", false))
                {
                    SessionState.SetBool(MainKey + ".Exit", false);
                    if (Application.isBatchMode) EditorApplication.Exit(SessionState.GetInt(MainKey + ".Code", 1));
                }
            };
        }

        public static void RunMain()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("Main acceptance requires graphics.");
            if (!Application.isBatchMode && EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the current scene before running Main acceptance.");
            Directory.CreateDirectory(Output);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            SessionState.SetBool(MainKey, true);
            EditorApplication.isPlaying = true;
        }

        private static void BeginMain()
        {
            try
            {
                SessionState.SetBool(MainKey, false);
                mainCamera = Camera.main;
                mainCard = Object.FindObjectOfType<DinosaurInfoCard>();
                if (mainCamera == null || mainCard == null) throw new InvalidOperationException("Main HUD or camera missing.");
                mainDetector = mainCard.GetComponent<DinosaurVisibility>();
                foreach (var input in Object.FindObjectsOfType<SmoothExplorerCamera>()) input.enabled = false;
                foreach (var input in Object.FindObjectsOfType<EmotivPlayerInput>()) input.enabled = false;
                savedTimeScale = Time.timeScale;
                Time.timeScale = 1f;
                mainOutput = new RenderTexture(1280, 720, 24);
                mainOutput.Create(); mainCamera.targetTexture = mainOutput;
                mainCamera.aspect = 1280f / 720f;
                var canvas = mainCard.GetComponentInParent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = mainCamera;
                canvas.planeDistance = mainCamera.nearClipPlane + .1f;
                mainReport.Clear();
                mainReport.AppendLine("# Dinosaur information card graphical acceptance");
                mainReport.AppendLine("\nUnity Main scene in Play Mode, Direct3D 11, 1280x720. Each view: 20 warmup frames and 60 measured frames, first periodic sampling off then on. Frame time includes the Editor. Capture CPU time includes visibility rendering submission; GPU mask timing is reported only when the profiler supplies it.\n");
                mainReport.AppendLine("|View|Detection|Mean frame ms|Mean capture submission ms|Mask GPU ms|\n|---|---|---:|---:|---:|");
                mainView = mainPhase = mainFrames = visibleCards = 0; lastFrame = -1;
                mainDeadline = EditorApplication.timeSinceStartup + 300;
                PrepareMainView();
                EditorApplication.update += TickMain;
            }
            catch (Exception error) { FinishMain(error); }
        }

        private static float GroundHeight(Vector3 point)
        {
            var terrain = Terrain.activeTerrain;
            return terrain == null ? 0f : terrain.SampleHeight(point) + terrain.transform.position.y;
        }

        private static void PrepareMainView()
        {
            if (mainPhase == 0)
            {
                Invoke(mainDetector, "ResetCapture");
                focusOwner = null;
                if (mainView == 1)
                {
                    var agents = Object.FindObjectsOfType<DinosaurAgent>().Where(a => a.isActiveAndEnabled)
                        .OrderBy(a => Vector3.Distance(a.transform.position, mainCamera.transform.position)).ToArray();
                    bool found = false;
                    foreach (var agent in agents)
                    {
                        for (int angle = 0; angle < 8 && !found; angle++)
                        {
                            float radians = angle * Mathf.PI / 4f;
                            Vector3 position = agent.transform.position + new Vector3(Mathf.Sin(radians), 0, Mathf.Cos(radians)) * 12f;
                            position.y = GroundHeight(position) + 1.7f;
                            mainCamera.transform.position = position;
                            mainCamera.transform.LookAt(agent.transform.position + Vector3.up * agent.ViewFocusHeight);
                            mainDetector.CaptureNow(mainCamera);
                            if (angle == 0)
                                foreach (var observation in mainDetector.Observations)
                                    Debug.Log($"INFO_CARD_MAIN_PROBE: {observation.Target.DisplayName}, pixels {observation.VisiblePixels}, ratio {observation.VisibleRatio:F3}");
                            found = mainDetector.Observations.Any(o => o.Target.Owner == agent && o.VisibleRatio >= .5f && o.VisiblePixels >= 16);
                            if (found) { focusOwner = agent; focusOffset = position - agent.transform.position; }
                        }
                        if (found) break;
                    }
                    if (!found) throw new InvalidOperationException("No readable ground dinosaur found in Main viewpoints.");
                }
                else if (mainView == 2)
                {
                    Vector3 position = new Vector3(-62, 0, -55); position.y = GroundHeight(position) + 1.7f;
                    mainCamera.transform.position = position;
                    mainCamera.transform.LookAt(new Vector3(0, 3, 0));
                }
                else if (mainView == 3)
                {
                    var flyer = Object.FindObjectOfType<PteradactalFlightController>();
                    if (flyer == null) throw new InvalidOperationException("Main flyer missing.");
                    Vector3 position = flyer.transform.position + new Vector3(0, 0, -12);
                    position.y = GroundHeight(position) + 1.7f;
                    mainCamera.transform.position = position; mainCamera.transform.LookAt(flyer.transform.position);
                    focusOwner = flyer; focusOffset = new Vector3(0, 0, -12);
                }
                mainDetector.CaptureNow(mainCamera);
            }
            // Keep render trackers alive: disabling/re-enabling a component schedules deferred
            // destruction in Play Mode, which would invalidate same-frame vantage checks.
            Set(mainDetector, "nextSample", mainPhase == 1 ? 0f : float.PositiveInfinity);
            mainFrames = 0; frameTimes.Clear(); captureTimes.Clear(); gpuTimes.Clear();
            lastCaptureTime = float.NegativeInfinity;
            maskRecorder = Recorder.Get("DinosaurVisibility.Masks"); maskRecorder.enabled = true;
        }

        private static void TickMain()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > mainDeadline) throw new TimeoutException("Main graphical acceptance timeout.");
                if (Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                if (focusOwner != null)
                {
                    Vector3 position = focusOwner.transform.position + focusOffset;
                    position.y = GroundHeight(position) + 1.7f;
                    mainCamera.transform.position = position;
                    var agent = focusOwner as DinosaurAgent;
                    mainCamera.transform.LookAt(focusOwner.transform.position + (agent != null ? Vector3.up * agent.ViewFocusHeight : Vector3.zero));
                }
                mainFrames++;
                if (mainFrames > 20)
                {
                    frameTimes.Add(Time.unscaledDeltaTime * 1000.0);
                    if (mainPhase == 1 && mainDetector.LastResultTime != lastCaptureTime && !float.IsInfinity(mainDetector.LastResultTime))
                    {
                        lastCaptureTime = mainDetector.LastResultTime;
                        captureTimes.Add(mainDetector.LastCaptureMilliseconds);
                    }
                    if (maskRecorder.gpuElapsedNanoseconds > 0) gpuTimes.Add(maskRecorder.gpuElapsedNanoseconds / 1000000.0);
                }
                if (mainFrames < 80) return;
                mainReport.AppendLine($"|{ViewNames[mainView]}|{(mainPhase == 1 ? "on" : "off")}|{frameTimes.Average():F2}|{(captureTimes.Count > 0 ? captureTimes.Average().ToString("F2") : "—")}|{(gpuTimes.Count > 0 ? gpuTimes.Average().ToString("F2") : "unavailable") }|");
                if (mainPhase == 1)
                {
                    Invoke(mainCard, "LateUpdate");
                    var selected = (DinosaurVisibility.Target)Get(mainCard, "currentTarget");
                    if (selected != null)
                    {
                        var item = mainDetector.Observations.FirstOrDefault(o => o.Target.Owner == selected.Owner);
                        if (item.VisiblePixels < 16 || item.VisibleRatio < .5f) throw new InvalidOperationException("Main selected an ineligible dinosaur.");
                        visibleCards++;
                        mainReport.AppendLine($"\n{ViewNames[mainView]}: selected {selected.DisplayName}, visible {item.VisibleRatio:P1}, pixels {item.VisiblePixels}.\n");
                    }
                    var pixels = new Texture2D(mainOutput.width, mainOutput.height, TextureFormat.RGB24, false);
                    var old = RenderTexture.active; RenderTexture.active = mainOutput;
                    pixels.ReadPixels(new Rect(0, 0, mainOutput.width, mainOutput.height), 0, 0); pixels.Apply();
                    File.WriteAllBytes(Output + "/main-" + ViewNames[mainView] + ".png", pixels.EncodeToPNG());
                    RenderTexture.active = old; Object.Destroy(pixels);
                }
                if (mainPhase == 0) mainPhase = 1;
                else { mainPhase = 0; mainView++; }
                if (mainView == ViewNames.Length)
                {
                    if (visibleCards == 0) throw new InvalidOperationException("No Main card was displayed.");
                    FinishMain(null);
                }
                else PrepareMainView();
            }
            catch (Exception error) { FinishMain(error); }
        }

        private static void FinishMain(Exception error)
        {
            EditorApplication.update -= TickMain;
            if (maskRecorder != null) maskRecorder.enabled = false;
            if (mainDetector != null) mainDetector.enabled = false;
            if (mainCamera != null) mainCamera.targetTexture = null;
            if (mainOutput != null) Object.Destroy(mainOutput);
            Time.timeScale = savedTimeScale > 0 ? savedTimeScale : 1f;
            mainReport.AppendLine(error == null ? "\nMAIN_ACCEPTANCE_PASS" : "\nMAIN_ACCEPTANCE_FAIL: " + error);
            File.WriteAllText(Output + "/main-performance.md", mainReport.ToString());
            if (error != null) Debug.LogException(error); else Debug.Log("INFO_CARD_MAIN_ACCEPTANCE_PASS");
            SessionState.SetInt(MainKey + ".Code", error == null ? 0 : 1);
            SessionState.SetBool(MainKey + ".Exit", true);
            EditorApplication.isPlaying = false;
        }
    }
}
