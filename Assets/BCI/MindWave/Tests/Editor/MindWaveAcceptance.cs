using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using JurassicWorld.PlayerInputs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.BCI.MindWave.Tests
{
    public static class MindWaveAcceptance
    {
        private static int checks;
        private static void Require(bool value, string name)
        {
            checks++;
            if (!value) throw new Exception("MindWave acceptance: " + name);
        }
        private static void Equal(float actual, float expected, string name)
            => Require(Mathf.Abs(actual - expected) < 0.0001f, name + " (actual " + actual + ")");
        private static void Feed(ThinkGearStream stream, string json, double time)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(json);
            stream.Feed(bytes, bytes.Length, time);
        }

        [MenuItem("Jurassic World/BCI/Tests/MindWave Acceptance")]
        public static void Run()
        {
            try
            {
                checks = 0;
                VerifyStream(); VerifyRegistry(); VerifySocket(false); VerifySocket(true); VerifyDenied();
                Debug.Log("[MindWaveAcceptance] PASS " + checks + " checks. Connector/Bluetooth not tested.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        [MenuItem("Jurassic World/BCI/Tests/MindWave Acceptance", true)]
        private static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode;

        private static void VerifyStream()
        {
            var state = new ThinkGearStream();
            Equal(state.ReadMovement(0, 40, 3).y, 0, "No initial signal");
            foreach (int value in new[] { 0, 39, 40, 70, 100 })
            {
                Feed(state, "{\"poorSignalLevel\":0,\"eSense\":{\"attention\":" + value + "}}\r", 1);
                Equal(state.ReadMovement(1, 40, 3).y, Mathf.Clamp01((value - 40) / 60f), "Attention " + value);
            }
            Equal(state.ReadMovement(3.999, 40, 3).y, 1, "Fresh sample");
            Equal(state.ReadMovement(4, 40, 3).y, 0, "Timeout inclusive");
            Feed(state, "{\"rawEeg\":123}\r{\"blinkStrength\":50}\r{\"eegPower\":{\"delta\":1}}\r", 4);
            Equal(state.ReadMovement(4, 40, 3).y, 0, "Other packets do not refresh age");
            Feed(state, "{\"poorSignalLevel\":0,\"eSense\":{\"attention\":70}}\r", 5);
            Feed(state, "{\"poorSignalLevel\":200}\r", 6);
            Equal(state.ReadMovement(6, 40, 3).y, 0, "Poor signal stops");
            Feed(state, "{\"poorSignalLevel\":0}\r", 7);
            Equal(state.ReadMovement(7, 40, 3).y, 0, "Recovery needs new attention");
            Feed(state, "{\"eSense\":{\"attention\":70}}\r", 7);
            Equal(state.ReadMovement(7, 40, 3).y, .5f, "Separate packet resumes");
            Feed(state, "{\"eSense\":{\"attention\":100}}\r", 9.5);
            Equal(state.ReadMovement(10, 40, 3).y, 0, "Signal freshness independent");
            foreach (string invalid in new[] { "-1", "101", "70.5", "0" })
            {
                Feed(state, "{\"poorSignalLevel\":0,\"eSense\":{\"attention\":" + invalid + "}}\r", 11);
                Equal(state.ReadMovement(11, 40, 3).y, 0, "Invalid attention " + invalid);
            }
            state.Reset();
            Feed(state, "{\"eSense\":{\"attention\":100}}\r", 1);
            Equal(state.ReadMovement(1, 40, 3).y, 0, "Unknown signal cannot move");
            Feed(state, "{\"poorSignalLevel\":0,\"eSense\":{\"attention\":", 2);
            Equal(state.ReadMovement(2, 40, 3).y, 0, "Fragment incomplete");
            Feed(state, "70}}\r\n{\"rawEeg\":10}\r{\"eSense\":{}}\r", 2);
            Equal(state.ReadMovement(2, 40, 3).y, .5f, "Split/coalesced CRLF and missing attention");
            Feed(state, "not-json\r{broken}\r[]\r", 2);
            Require(state.InvalidPackets >= 3, "Malformed data counted");
            Feed(state, new string('x', 65538) + "\r{\"poorSignalLevel\":0,\"eSense\":{\"attention\":100}}\r", 3);
            Equal(state.ReadMovement(3, 40, 3).y, 1, "Oversized frame resynchronizes");
            Feed(state, "{\"isAuthorized\":false}\r", 3);
            Equal(state.ReadMovement(3, 40, 3).y, 0, "Authorization denied");
            state.Reset();
            Equal(state.ReadMovement(3, 40, 3).y, 0, "Reset invalidates cached samples");
            Feed(state, "{\"poorSignalLevel\":0,\"eSense\":{\"attention\":70}}\r", 0);
            Feed(state, "{\"eSense\":{\"attention\":100}}\r", 2);
            Equal(state.ReadMovement(3, 40, 3).y, 0, "Signal expires before attention");
            Feed(state, "{\"eSense\":{\"attention\":100}}\r", 3.1);
            Feed(state, "{\"poorSignalLevel\":0}\r", 4);
            Equal(state.ReadMovement(4, 40, 3).y, 0, "Signal recovery cannot reuse attention received while stale");
            Feed(state, "{\"eSense\":{\"attention\":70}}\r", 4.1);
            Equal(state.ReadMovement(4.1, 40, 3).y, .5f, "New attention after stale signal recovers");
        }

        private static void VerifyRegistry()
        {
            Require(!PlayerMovementInput.HasProvider, "Test starts in Original mode");
            var owner = ScriptableObject.CreateInstance<TestOwner>();
            var other = ScriptableObject.CreateInstance<TestOwner>();
            try
            {
                PlayerMovementInput.Register(owner, () => Vector2.up * .5f);
                Require(PlayerMovementInput.HasProvider, "Registration");
                Equal(PlayerMovementInput.ReadMovement().y, .5f, "Registry reads provider");
                PlayerMovementInput.Unregister(other);
                Require(PlayerMovementInput.HasProvider, "Non-owner cannot unregister");
                bool rejected = false;
                try { PlayerMovementInput.Register(other, () => Vector2.one); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "Multiple providers rejected");
                PlayerMovementInput.Register(owner, () => Vector2.zero);
                Equal(PlayerMovementInput.ReadMovement().y, 0, "Selected but idle stays zero");
            }
            finally
            {
                PlayerMovementInput.Unregister(owner);
                UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(other);
            }
            Require(!PlayerMovementInput.HasProvider, "Unregister restores default");
        }

        private static void VerifySocket(bool authorization)
        {
            var server = new TcpListener(IPAddress.Loopback, 0); server.Start();
            var config = new MindWaveSettings { source = "TcpSimulator", simulatorPort = ((IPEndPoint)server.LocalEndpoint).Port,
                requestAuthorization = authorization };
            var client = new ThinkGearClient(config);
            TcpClient peer = null;
            try
            {
                client.Start();
                Require(server.Pending() || SpinWait.SpinUntil(server.Pending, 5000), "TCP connected");
                peer = server.AcceptTcpClient(); peer.ReceiveTimeout = 5000;
                var reader = new StreamReader(peer.GetStream());
                string request = ReadFrame(reader);
                if (authorization)
                {
                    Require(request.Contains("appName") && request.Contains("appKey"), "Authorization request");
                    Write(peer, "{\"isAuthorized\":true}\r"); request = ReadFrame(reader);
                }
                Require(request.Contains("\"format\":\"Json\"") && request.Contains("\"enableRawOutput\":false"), "JSON raw disabled");
                Write(peer, "{\"poorSignalLevel\":0,\"eSense\":{\"attention\":70}}\r");
                Require(SpinWait.SpinUntil(() => client.ReadMovement().y == .5f, 3000), "Real TCP drives half speed");
                peer.Close(); peer = null;
                Require(SpinWait.SpinUntil(() => client.ReadMovement() == Vector2.zero, 3000), "Disconnect clears input");
                Require(SpinWait.SpinUntil(server.Pending, 5000), "Automatic reconnect");
                peer = server.AcceptTcpClient(); peer.ReceiveTimeout = 5000;
                reader = new StreamReader(peer.GetStream()); request = ReadFrame(reader);
                if (authorization) { Write(peer, "{\"isAuthorized\":true}\r"); request = ReadFrame(reader); }
                Equal(client.ReadMovement().y, 0, "Reconnect cannot reuse sample");
                Write(peer, "{\"poorSignalLevel\":0,\"eSense\":{\"attention\":100}}\r");
                Require(SpinWait.SpinUntil(() => client.ReadMovement().y == 1, 3000), "Reconnect resumes with fresh sample");
                client.Dispose(); client.Dispose();
                Equal(client.ReadMovement().y, 0, "Dispose is idempotent and clears output");
            }
            finally { client.Dispose(); peer?.Close(); server.Stop(); }
        }

        private static void VerifyDenied()
        {
            var server = new TcpListener(IPAddress.Loopback, 0); server.Start();
            using (var client = new ThinkGearClient(new MindWaveSettings { source = "TcpSimulator",
                simulatorPort = ((IPEndPoint)server.LocalEndpoint).Port, requestAuthorization = true }))
            {
                try
                {
                    client.Start(); Require(SpinWait.SpinUntil(server.Pending, 5000), "Denied server connected");
                    using (var peer = server.AcceptTcpClient())
                    {
                        peer.ReceiveTimeout = 5000; ReadFrame(new StreamReader(peer.GetStream()));
                        Write(peer, "{\"isAuthorized\":false}\r{\"poorSignalLevel\":0,\"eSense\":{\"attention\":100}}\r");
                        Require(SpinWait.SpinUntil(() => client.Describe().Contains("Authorization denied"), 3000), "Denied status visible");
                        Equal(client.ReadMovement().y, 0, "Denied server cannot move");
                    }
                }
                finally { server.Stop(); }
            }
        }

        private static string ReadFrame(StreamReader reader)
        {
            var text = new StringBuilder(); int c;
            while ((c = reader.Read()) >= 0 && c != '\r') text.Append((char)c);
            return text.ToString();
        }
        private static void Write(TcpClient peer, string text)
        { byte[] bytes = Encoding.ASCII.GetBytes(text); peer.GetStream().Write(bytes, 0, bytes.Length); }

        public static void RunProjectRegression()
        {
            try
            {
                // Reflection deliberately avoids an assembly dependency from the removable module to Assembly-CSharp.
                var regression = Type.GetType("JurassicWorld.Editor.EmotivInputAcceptance, Assembly-CSharp-Editor", true);
                regression.GetMethod("RunPlayerMovementRegression").Invoke(null, null);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        public static void BuildMain()
        {
            string destination = Environment.GetEnvironmentVariable("MINDWAVE_BUILD_PATH");
            if (string.IsNullOrEmpty(destination)) destination = "Builds/MindWaveValidation/JurassicWorld.exe";
            var report = BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/Main.unity" }, destination,
                BuildTarget.StandaloneWindows64, BuildOptions.Development);
            EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }

        public static void PlayMain()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            EditorApplication.isPlaying = true;
        }

        // Runs in an isolated batch project; disables domain reload to catch persistent static registrations.
        public static void RunPlayLifecycle()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            bool previousEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            EnterPlayModeOptions previousOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            int cycles = 0;
            double leaveAt = double.PositiveInfinity;
            ThinkGearClient capturedClient = null;
            EditorApplication.CallbackFunction update = null;
            Action<PlayModeStateChange> changed = null;
            Action<bool, string> finish = (passed, detail) =>
            {
                EditorApplication.update -= update;
                EditorApplication.playModeStateChanged -= changed;
                EditorSettings.enterPlayModeOptionsEnabled = previousEnabled;
                EditorSettings.enterPlayModeOptions = previousOptions;
                Debug.Log("[MindWaveLifecycle] " + (passed ? "PASS " : "FAIL ") + detail);
                EditorApplication.Exit(passed ? 0 : 1);
            };
            changed = state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    var roots = UnityEngine.Object.FindObjectsOfType<MindWavePlayerInput>();
                    if (roots.Length != 1 || !PlayerMovementInput.HasProvider) { finish(false, "Missing/duplicate provider"); return; }
                    capturedClient = (ThinkGearClient)typeof(MindWavePlayerInput).GetField("client", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(roots[0]);
                    leaveAt = EditorApplication.timeSinceStartup + 1;
                }
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    var worker = capturedClient == null ? null : (Thread)typeof(ThinkGearClient).GetField("worker", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(capturedClient);
                    if (PlayerMovementInput.HasProvider || worker == null || worker.IsAlive)
                    { finish(false, "Registration or TCP worker survived Play exit"); return; }
                    if (++cycles == 2) finish(true, "Two Play cycles without domain reload; providers and TCP workers released");
                    else EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
                }
            };
            update = () =>
            {
                if (EditorApplication.isPlaying && EditorApplication.timeSinceStartup >= leaveAt)
                { leaveAt = double.PositiveInfinity; EditorApplication.isPlaying = false; }
            };
            EditorApplication.playModeStateChanged += changed;
            EditorApplication.update += update;
            EditorApplication.isPlaying = true;
        }

        private sealed class TestOwner : ScriptableObject { }
    }
}
