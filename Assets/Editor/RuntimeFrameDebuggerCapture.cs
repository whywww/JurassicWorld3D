using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    // An opt-in, untimed diagnostic. Reflection is confined to the installed Editor's internal API.
    [InitializeOnLoad]
    public static class RuntimeFrameDebuggerCapture
    {
        private const string Key = "JurassicWorld.FrameDebuggerCapture";
        private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static int phase;
        private static double ready;
        private static Type utility;
        private static RenderTexture target;
        static RuntimeFrameDebuggerCapture()
        { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }

        [MenuItem("Jurassic World/Performance/Capture Frame Debugger")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save before Frame Debugger capture.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            phase = 0;
            if (!Application.isBatchMode)
            {
                // The native debugger needs a visible Game view to collect frame events.
                var gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView", true));
                gameView.Show(); gameView.Focus();
            }
            SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".Exit", false);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        private static object Call(string method, params object[] args) => utility.GetMethod(method, Static).Invoke(null, args);
        private static object Field(object data, string name) => data.GetType().GetField(name, Fields).GetValue(data);
        private static string Csv(object value) => "\"" + Convert.ToString(value).Replace("\"", "\"\"") + "\"";

        private static void Tick()
        {
            if (SessionState.GetBool(Key + ".Exit", false))
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                SessionState.SetBool(Key, false); SessionState.SetBool(Key + ".Exit", false);
                EditorApplication.update -= Tick;
                if (Application.isBatchMode || Array.IndexOf(Environment.GetCommandLineArgs(), "-jwFrameDebuggerExit") >= 0)
                    EditorApplication.Exit(SessionState.GetInt(Key + ".Code", 1));
                return;
            }
            if (!EditorApplication.isPlaying) return;
            try
            {
                if (phase == 0)
                {
                    foreach (var input in UnityEngine.Object.FindObjectsOfType<SmoothExplorerCamera>()) input.enabled = false;
                    UnityEngine.Object.FindObjectOfType<GraphicsQualityController>().ApplyTier(1);
                    var camera = Camera.main;
                    target = new RenderTexture(1920, 1080, 24); target.Create(); camera.targetTexture = target;
                    camera.transform.SetPositionAndRotation(new Vector3(-14, 1.7f, 5), Quaternion.Euler(4.8321f, 0, 0));
                    utility = typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility", true);
                    if (!(bool)Call("get_locallySupported")) throw new InvalidOperationException("Local Frame Debugger unsupported.");
                    ready = EditorApplication.timeSinceStartup + 5; phase = 1; return;
                }
                if (EditorApplication.timeSinceStartup < ready) return;
                if (phase == 1) { Call("SetEnabled", true, 0); ready = EditorApplication.timeSinceStartup + 3; phase = 2; return; }
                var events = (Array)Call("GetFrameEvents");
                if (events.Length == 0)
                {
                    Directory.CreateDirectory("Logs/RuntimePerformance");
                    File.WriteAllText("Logs/RuntimePerformance/frame-debugger.md", $"# Frame Debugger snapshot\n\nUnavailable: no frame events returned (batchmode={Application.isBatchMode}). Use the menu entry with a visible Game view for a graphical capture. No batching conclusions were inferred.\n");
                    Debug.Log("JW_FRAME_DEBUGGER_UNAVAILABLE"); Finish(0); return;
                }
                var indices = new List<int>();
                for (int i = 0; i < events.Length; i++)
                    if (Field(events.GetValue(i), "m_Type").ToString().StartsWith("Draw", StringComparison.Ordinal)) indices.Add(i);
                var causes = (string[])Call("GetBatchBreakCauseStrings");
                var type = utility.Assembly.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEventData", true);
                var report = new StringBuilder("event,type,shader,pass,instances,draws,batch_break,keywords\n");
                var totals = new Dictionary<string, int>();
                int sampled = 0, instanced = 0;
                int stride = Mathf.Max(1, indices.Count / 512);
                for (int n = 0; n < indices.Count; n += stride)
                {
                    int i = indices[n]; object data = Activator.CreateInstance(type, true);
                    if (!(bool)Call("GetFrameEventData", i, data)) continue;
                    int cause = (int)Field(data, "m_BatchBreakCause"), instances = (int)Field(data, "m_InstanceCount");
                    string name = cause >= 0 && cause < causes.Length ? causes[cause] : cause.ToString();
                    if (!totals.ContainsKey(name)) totals.Add(name, 0); totals[name]++;
                    sampled++; if (instances > 1) instanced++;
                    report.AppendLine(string.Join(",", new[] { Csv(i), Csv(Field(events.GetValue(i), "m_Type")), Csv(Field(data, "m_RealShaderName")),
                        Csv(Field(data, "m_PassName")), Csv(instances), Csv(Field(data, "m_DrawCallCount")), Csv(name), Csv(Field(data, "shaderKeywords")) }));
                }
                Directory.CreateDirectory("Logs/RuntimePerformance");
                File.WriteAllText("Logs/RuntimePerformance/frame-debugger.csv", report.ToString());
                File.WriteAllText("Logs/RuntimePerformance/frame-debugger.md", $"# Frame Debugger snapshot\n\nEvents: {events.Length}; draw events: {indices.Count}; sampled: {sampled}; sampled instanced draws: {instanced}.\n\n" +
                    string.Join("\n", totals.OrderByDescending(t => t.Value).Select(t => $"- {t.Value}: {t.Key}")));
                Debug.Log("JW_FRAME_DEBUGGER_CAPTURE_COMPLETE"); Finish(0);
            }
            catch (Exception error) { Debug.LogException(error); Finish(1); }
        }

        private static void Finish(int code)
        {
            if (utility != null) Call("SetEnabled", false, 0);
            if (Camera.main != null) Camera.main.targetTexture = null;
            if (target != null) UnityEngine.Object.Destroy(target);
            SessionState.SetInt(Key + ".Code", code); SessionState.SetBool(Key + ".Exit", true);
            EditorApplication.isPlaying = false;
        }
    }
}
