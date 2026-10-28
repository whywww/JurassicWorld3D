using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using JurassicWorld.PlayerInputs;
using JurassicWorld.PlayerInputs.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace JurassicWorld.BCI.BrainLink.Tests
{
    public static class BrainLinkAcceptance
    {
        private static int checks;
        private static void Require(bool condition, string name)
        { checks++; if (!condition) throw new Exception("BrainLink acceptance: " + name); }
        private static void Equal(float actual, float expected, string name)
            => Require(Mathf.Abs(actual - expected) < .0001f, name + " actual=" + actual);
        private static string Packet(int attention = 70, int signal = 0)
            => "{\"attention\":" + attention + ",\"signal\":" + signal + ",\"mode\":\"simulate\"}\n";
        private static void Feed(BrainLinkStream stream, string packet, double now)
        { var bytes = Encoding.ASCII.GetBytes(packet); stream.Feed(bytes, bytes.Length, now); }

        [MenuItem("Jurassic World/BCI/Tests/BrainLink Acceptance")]
        public static void Run()
        {
            try
            {
                checks = 0; VerifyStream(); VerifySocket(); VerifyInspector();
                Debug.Log("[BrainLinkAcceptance] PASS " + checks + " checks; hardware not tested.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            { Debug.LogException(error); if (Application.isBatchMode) EditorApplication.Exit(1); else throw; }
        }

        private static void VerifyStream()
        {
            var stream = new BrainLinkStream();
            Equal(stream.ReadMovement(0, 40, 3).y, 0, "No initial sample");
            foreach (int value in new[] { 0, 39, 40, 70, 100 })
            {
                Feed(stream, Packet(value), 1);
                Equal(stream.ReadMovement(1, 40, 3).y, Mathf.Clamp01((value - 40) / 60f), "Mapping " + value);
            }
            Equal(stream.ReadMovement(3.999, 40, 3).y, 1, "Fresh sample");
            Equal(stream.ReadMovement(4, 40, 3).y, 0, "Timeout inclusive");
            foreach (string invalid in new[] { "{}\n", "not-json\n", "[]\n", "{broken}\n",
                "{\"attention\":70,\"mode\":\"simulate\"}\n",
                "{\"signal\":0,\"mode\":\"simulate\"}\n",
                "{\"attention\":70.5,\"signal\":0,\"mode\":\"simulate\"}\n",
                "{\"attention\":70,\"signal\":0,\"mode\":\"unknown\"}\n", Packet(-1), Packet(101), Packet(70, 201) })
            {
                Feed(stream, Packet(), 5); Feed(stream, invalid, 6);
                Equal(stream.ReadMovement(6, 40, 3).y, 0, "Reject " + invalid.Trim());
            }
            Feed(stream, Packet(70, 200), 7);
            Equal(stream.ReadMovement(7, 40, 3).y, 0, "Poor signal");
            Feed(stream, "{\"signal\":0}\n", 8);
            Equal(stream.ReadMovement(8, 40, 3).y, 0, "Signal recovery alone is insufficient");
            Feed(stream, Packet(), 9); Equal(stream.ReadMovement(9, 40, 3).y, .5f, "Fresh recovery");
            Feed(stream, "{\"rawEeg\":123}\n", 12);
            Equal(stream.ReadMovement(12, 40, 3).y, 0, "Raw does not refresh sample");
            string complete = Packet();
            for (int split = 1; split < complete.Length; split++)
            {
                stream.Reset(); Feed(stream, complete.Substring(0, split), 20);
                Equal(stream.ReadMovement(20, 40, 3).y, 0, "Incomplete fragment");
                Feed(stream, complete.Substring(split), 20);
                Equal(stream.ReadMovement(20, 40, 3).y, .5f, "Split " + split);
            }
            Feed(stream, Packet(40) + Packet(100).Replace("\n", "\r\n"), 21);
            Equal(stream.ReadMovement(21, 40, 3).y, 1, "Coalesced CRLF");
            Feed(stream, new string('x', 16390) + "\n" + Packet(), 22);
            Equal(stream.ReadMovement(22, 40, 3).y, .5f, "Oversized frame resynchronizes");
            stream.Reset(); Equal(stream.ReadMovement(22, 40, 3).y, 0, "Disconnect clears sample");
        }

        private static void Until(Func<bool> condition, string name)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!condition() && watch.Elapsed.TotalSeconds < 6) Thread.Sleep(10);
            Require(condition(), name);
        }
        private static void Send(TcpClient peer, string packet)
        { var bytes = Encoding.ASCII.GetBytes(packet); peer.GetStream().Write(bytes, 0, bytes.Length); }

        private static void VerifySocket()
        {
            var server = new TcpListener(IPAddress.Loopback, 0); server.Start();
            var client = new BrainLinkClient(new BrainLinkSettings { port = ((IPEndPoint)server.LocalEndpoint).Port, sampleTimeout = 1 });
            try
            {
                client.Start(); Until(server.Pending, "Loopback connect");
                using (var peer = server.AcceptTcpClient())
                {
                    Equal(client.ReadMovement().y, 0, "Connection alone cannot move");
                    Send(peer, Packet().Substring(0, 8)); Thread.Sleep(40);
                    Equal(client.ReadMovement().y, 0, "Socket fragment incomplete");
                    Send(peer, Packet().Substring(8)); Until(() => client.ReadMovement().y == .5f, "Socket half speed");
                    Send(peer, Packet(100)); Until(() => client.ReadMovement().y == 1f, "Socket full speed");
                    Until(() => client.ReadMovement().y == 0, "Paused sender expires");
                    Send(peer, Packet()); Until(() => client.ReadMovement().y == .5f, "Resume");
                }
                Until(() => client.ReadMovement().y == 0, "Disconnect clears immediately");
                Until(server.Pending, "Reconnect");
                using (var peer = server.AcceptTcpClient())
                {
                    Equal(client.ReadMovement().y, 0, "Reconnect cannot reuse old sample");
                    Send(peer, Packet(100)); Until(() => client.ReadMovement().y == 1f, "Fresh reconnect sample");
                    client.Dispose(); Require(!client.IsWorkerAlive, "Dispose interrupts blocked read");
                }
                client.Dispose();
            }
            finally { client.Dispose(); server.Stop(); }
            var pending = new BrainLinkClient(new BrainLinkSettings { port = ((IPEndPoint)new IPEndPoint(IPAddress.Loopback, 1)).Port });
            pending.Start(); pending.Dispose(); Require(!pending.IsWorkerAlive, "Dispose during connect/retry");
        }

        public static void Select(GameObject player, string typeName)
        {
            foreach (var input in player.GetComponents<MonoBehaviour>())
                if (input is IBrainControlInput) input.enabled = input.GetType().Name == typeName;
        }

        private static void VerifyInspector()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var player = Camera.main.gameObject;
            var brain = player.GetComponent<BrainLinkPlayerInput>();
            Require(brain != null && !brain.enabled, "BrainLink attached, initially off");
            int selected = 0, sources = 0;
            foreach (var input in player.GetComponents<MonoBehaviour>())
            {
                if (!(input is IBrainControlInput)) continue;
                sources++;
                if (input.enabled) { selected++; Require(input.GetType().Name == "MindWavePlayerInput", "Preserved MindWave selection"); }
                using (var inspector = new InspectorScope(UnityEditor.Editor.CreateEditor(input)))
                    Require(inspector.Value is BrainInputEditor, "Shared lock/checkbox inspector for " + input.GetType().Name);
            }
            Require(sources == 3 && selected == 1, "Three sources, exactly one enabled");
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.RecordObject(brain, "Choose BrainLink"); brain.enabled = true;
            BrainInputEditor.SelectOnly(brain); Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
            selected = 0;
            foreach (var input in player.GetComponents<MonoBehaviour>()) if (input is IBrainControlInput && input.enabled) selected++;
            Require(selected == 1 && brain.enabled, "Checkbox selection disables both other sources");
            Undo.PerformUndo(); Require(!brain.enabled, "Undo restores BrainLink off");
            selected = 0;
            foreach (var input in player.GetComponents<MonoBehaviour>()) if (input is IBrainControlInput && input.enabled) selected++;
            Require(selected == 1, "Undo restores previous selected source");
            Select(player, "");
            foreach (var input in player.GetComponents<MonoBehaviour>()) if (input is IBrainControlInput) Require(!input.enabled, "All-off keyboard mode");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Require(!Camera.main.GetComponent<BrainLinkPlayerInput>().enabled, "Persisted scene selection unchanged");
        }

        private sealed class InspectorScope : IDisposable
        {
            public readonly UnityEditor.Editor Value;
            public InspectorScope(UnityEditor.Editor value) { Value = value; }
            public void Dispose() { Object.DestroyImmediate(Value); }
        }

        public static void BuildMain()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Select(Camera.main.gameObject, "BrainLinkPlayerInput");
            // Build a separate scene asset so Main keeps its original MindWave selection.
            Directory.CreateDirectory("Assets/BCI/BrainLink/Tests/Generated");
            const string scene = "Assets/BCI/BrainLink/Tests/Generated/BrainLinkValidation.unity";
            int code = 1;
            try
            {
                EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), scene, true);
                var report = BuildPipeline.BuildPlayer(new[] { scene }, "Builds/BrainLinkValidation/JurassicWorld.exe",
                    BuildTarget.StandaloneWindows64, BuildOptions.Development);
                code = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1;
            }
            finally { AssetDatabase.DeleteAsset(scene); AssetDatabase.DeleteAsset("Assets/BCI/BrainLink/Tests/Generated"); }
            EditorApplication.Exit(code);
        }

        // Exercise the same serialized m_Enabled changes used by Unity's native component titlebar.
        public static void RunInspectorEvents()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            AssetDatabase.DeleteAsset("Assets/BCI/BrainLink/Tests/Generated");
            var player = Camera.main.gameObject;
            Selection.activeGameObject = player;
            var window = EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.InspectorWindow"));
            ActiveEditorTracker.sharedTracker.ForceRebuild();
            var input = player.GetComponent<BrainLinkPlayerInput>();
            var inspector = UnityEditor.Editor.CreateEditor(input);
            double next = EditorApplication.timeSinceStartup + 2;
            int stage = 0;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                if (EditorApplication.timeSinceStartup < next) return;
                next = EditorApplication.timeSinceStartup + 1;
                try
                {
                    if (stage == 0)
                    {
                        var serialized = new SerializedObject(input);
                        serialized.FindProperty("m_Enabled").boolValue = true;
                        serialized.ApplyModifiedProperties();
                        stage++; return;
                    }
                    if (stage == 1)
                    {
                        int count = 0;
                        foreach (var other in player.GetComponents<MonoBehaviour>()) if (other is IBrainControlInput && other.enabled) count++;
                        Require(input.enabled && count == 1, "Native serialized checkbox automatically disables other sources");
                        Undo.PerformUndo(); stage++; return;
                    }
                    if (stage == 2)
                    {
                        Require(!input.enabled, "Native checkbox Undo restores BrainLink off");
                        int count = 0;
                        foreach (var other in player.GetComponents<MonoBehaviour>()) if (other is IBrainControlInput && other.enabled) count++;
                        Require(count == 1, "Native checkbox Undo restores previous source together");
                        EditorApplication.update -= tick; Object.DestroyImmediate(inspector);
                        Debug.Log("[BrainLinkInspectorEvents] PASS native checkbox publication, exclusivity and Undo");
                        EditorApplication.Exit(0);
                    }
                }
                catch (Exception error)
                {
                    EditorApplication.update -= tick; Object.DestroyImmediate(inspector);
                    Debug.LogException(error); EditorApplication.Exit(1);
                }
            };
            EditorApplication.update += tick;
        }

        public static void PlayMain()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Select(Camera.main.gameObject, "BrainLinkPlayerInput");
            EditorApplication.isPlaying = true;
        }

        public static void RunPlayLifecycle()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Select(Camera.main.gameObject, "BrainLinkPlayerInput");
            Selection.activeGameObject = Camera.main.gameObject;
            var inspectorWindow = EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.InspectorWindow"));
            ActiveEditorTracker.sharedTracker.ForceRebuild();
            bool previous = EditorSettings.enterPlayModeOptionsEnabled;
            var options = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            int cycles = 0;
            double leaveAt = double.PositiveInfinity, deadline = EditorApplication.timeSinceStartup + 100;
            BrainLinkClient captured = null;
            EditorApplication.CallbackFunction tick = null;
            Action<PlayModeStateChange> changed = null;
            Action<bool, string> finish = (passed, detail) =>
            {
                EditorApplication.update -= tick; EditorApplication.playModeStateChanged -= changed;
                EditorSettings.enterPlayModeOptionsEnabled = previous; EditorSettings.enterPlayModeOptions = options;
                Debug.Log("[BrainLinkLifecycle] " + (passed ? "PASS " : "FAIL ") + detail);
                EditorApplication.Exit(passed ? 0 : 1);
            };
            changed = state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    var input = Camera.main.GetComponent<BrainLinkPlayerInput>(); captured = input.Client;
                    if (!PlayerMovementInput.HasProvider || captured == null) { finish(false, "No provider/client"); return; }
                    if (BrainInputEditor.CanEdit) { finish(false, "Inspector selection not locked"); return; }
                    Selection.activeGameObject = input.gameObject;
                    ActiveEditorTracker.sharedTracker.ForceRebuild();
                    leaveAt = EditorApplication.timeSinceStartup + 2;
                }
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    if (PlayerMovementInput.HasProvider || captured == null || captured.IsWorkerAlive)
                    { finish(false, "Provider or worker survived Play"); return; }
                    if (++cycles == 2) finish(true, "Two Play cycles without domain reload; input locked, provider and worker released");
                    else EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
                }
            };
            tick = () =>
            {
                if (EditorApplication.timeSinceStartup > deadline) { finish(false, "Lifecycle timeout"); return; }
                if (EditorApplication.isPlaying && EditorApplication.timeSinceStartup >= leaveAt)
                {
                    var input = Camera.main.GetComponent<BrainLinkPlayerInput>();
                    string name = ObjectNames.GetInspectorTitle(input) + "Header";
                    var header = inspectorWindow.rootVisualElement.Q<IMGUIContainer>(name);
                    if (header == null || header.enabledSelf) { finish(false, "Native Inspector header not disabled during Play"); return; }
                    // A malformed scene must not start either candidate, even outside Inspector enforcement.
                    var conflict = new GameObject("Expected BCI conflict test"); conflict.SetActive(false);
                    var brain = conflict.AddComponent<BrainLinkPlayerInput>();
                    var emotiv = (MonoBehaviour)conflict.AddComponent(Type.GetType("JurassicWorld.EmotivPlayerInput, Assembly-CSharp", true));
                    conflict.SetActive(true);
                    bool otherRunning = (bool)emotiv.GetType().GetField("running", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(emotiv);
                    bool blocked = brain.Client == null && !otherRunning;
                    Object.DestroyImmediate(conflict);
                    if (!blocked) { finish(false, "Conflicting components opened a connection"); return; }
                    leaveAt = double.PositiveInfinity; EditorApplication.isPlaying = false;
                }
            };
            EditorApplication.playModeStateChanged += changed; EditorApplication.update += tick;
            EditorApplication.isPlaying = true;
        }
    }
}
