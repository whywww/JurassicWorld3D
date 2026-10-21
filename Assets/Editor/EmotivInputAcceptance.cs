using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using EmotivUnityPlugin;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    public static class EmotivInputAcceptance
    {
        private static int checks;

        [MenuItem("Jurassic World/Tests/Emotiv Input Acceptance")]
        public static void Run()
        {
            checks = 0;
            try
            {
                VerifyCommands();
                VerifyLocalConfiguration();
                VerifyKeyboard();
                VerifyScene();
                VerifyPluginLifecycle();
                VerifyStreamDelivery();
                Debug.Log("[EmotivAcceptance] PASS: " + checks + " checks. Hardware/authorization not tested.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        [MenuItem("Jurassic World/Tests/Emotiv Input Acceptance", true)]
        private static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode;

        // Batch entry point independent of whether hardware input is enabled in Main.
        public static void RunPlayerMovementRegression()
        {
            checks = 0;
            try
            {
                VerifyCommands();
                VerifyKeyboard();
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                Require(Camera.main != null && Camera.main.GetComponent<SmoothExplorerCamera>() != null,
                    "Player camera missing");
                Require(Terrain.activeTerrain != null, "Terrain missing");
                VerifyPlayerMovement(Camera.main, Terrain.activeTerrain);
                VerifyStreamDelivery();
                Debug.Log("[PlayerMovementRegression] PASS: " + checks + " checks. Hardware/authorization not tested.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        // Can also run in a minimal temporary Unity project while the user's Editor remains open.
        public static void RunStreamRegression()
        {
            checks = 0;
            try
            {
                VerifyCommands();
                VerifyLocalConfiguration();
                VerifyKeyboard();
                VerifyStreamDelivery();
                Debug.Log("[EmotivStreamRegression] PASS: " + checks + " checks. No Cortex connection opened.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        private static void Set(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private static void Invoke(object target, string name, params object[] arguments)
            => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, arguments);

        private static void VerifyStreamDelivery()
        {
            var emotiv = EmotivUnityItf.Instance;
            emotiv.Init("acceptance-test", "dummy-not-a-credential", "Acceptance", false, true, "wss://localhost:6868");
            var manager = DataStreamManager.Instance;
            var process = DataStreamProcess.Instance;
            foreach (bool buffering in new[] { true, false })
            {
                manager.IsDataBufferUsing = buffering;
                var holder = new GameObject("Emotiv subscription regression");
                holder.SetActive(false);
                var input = holder.AddComponent<EmotivPlayerInput>();
                // Block OnEnable from opening a real Cortex connection with local credentials.
                var blocker = new GameObject("Emotiv test connection guard");
                blocker.SetActive(false);
                var guard = blocker.AddComponent<EmotivPlayerInput>();
                var ownerField = typeof(EmotivPlayerInput).GetField("owner", BindingFlags.Static | BindingFlags.NonPublic);
                object previousOwner = ownerField.GetValue(null);
                try
                {
                    ownerField.SetValue(null, guard);
                    holder.SetActive(true);
                }
                finally
                {
                    ownerField.SetValue(null, previousOwner);
                    UnityEngine.Object.DestroyImmediate(blocker);
                }
                bool legacyAcknowledged = false;
                EventHandler<List<string>> legacy = (sender, streams) => legacyAcknowledged = streams.Contains("com");
                manager.InformSuccessSubscribedData += legacy;
                try
                {
                    Set(input, "emotiv", emotiv);
                    Set(input, "running", true);
                    Set(input, "profileName", "Acceptance");
                    Set(emotiv, "_isAuthorizedOK", true);
                    Set(emotiv, "_isProfileLoaded", true);
                    Set(emotiv, "_loadedProfileName", "Acceptance");
                    Set(manager, "_isSessActivated", true);
                    Set(process, "_connectCortexState", ConnectToCortexStates.Authorized);
                    Invoke(input, "RegisterStreamEvents");
                    Equal(input.ReadMovement(), Vector2.zero, "No input before subscription");
                    // Feed the SDK's real JSON acknowledgement parser, not the component's callback.
                    var success = new JArray(new JObject { ["streamName"] = "com", ["cols"] = new JArray("act", "pow") });
                    Invoke(process, "OnSubscribeDataDone", null, new MultipleResultEventArgs(success, new JArray()));
                    Require(legacyAcknowledged == !buffering, "Legacy acknowledgement suppressed only in buffered mode");
                    Require((bool)typeof(EmotivPlayerInput).GetField("subscribed", BindingFlags.NonPublic | BindingFlags.Instance)
                        .GetValue(input), "Real Cortex acknowledgement must mark control ready in both modes");
                    var commandEvent = (Delegate)typeof(DataStreamProcess).GetField("MentalCommandReceived",
                        BindingFlags.NonPublic | BindingFlags.Instance).GetValue(process);
                    commandEvent.DynamicInvoke(process, new ArrayList { 1.0, "push", 0.9 });
                    Equal(input.ReadMovement(), new Vector2(0, 0.9f), "SDK push 0.9 must reach player input");
                    Set(manager, "_isSessActivated", false);
                    Equal(input.ReadMovement(), Vector2.zero, "Session loss blocks received commands");
                    Set(manager, "_isSessActivated", true);
                    Set(input, "receivedAt", System.Diagnostics.Stopwatch.GetTimestamp() - 2L * System.Diagnostics.Stopwatch.Frequency);
                    Equal(input.ReadMovement(), Vector2.zero, "Stale received sample blocks movement");
                    commandEvent.DynamicInvoke(process, new ArrayList { 2.0, "push", 0.9 });
                    Equal(input.ReadMovement(), new Vector2(0, 0.9f), "Identical fresh SDK push resumes movement");
                    commandEvent.DynamicInvoke(process, new ArrayList { 3.0, "neutral", 0.9 });
                    Equal(input.ReadMovement(), Vector2.zero, "SDK neutral stops movement");
                    commandEvent.DynamicInvoke(process, new ArrayList { 4.0, "push", 0.9 });
                    Invoke(manager, "OnStreamStopNotify", null, new List<string> { "com" });
                    Equal(input.ReadMovement(), Vector2.zero, "SDK stream stop clears readiness");
                    Invoke(input, "UnregisterStreamEvents");
                    var callbacks = (Delegate)typeof(DataStreamProcess).GetField("SubscribedOK",
                        BindingFlags.NonPublic | BindingFlags.Instance).GetValue(process);
                    foreach (var callback in callbacks.GetInvocationList())
                        Require(callback.Target != input, "Unregister detaches subscription callback");
                }
                finally
                {
                    Invoke(input, "UnregisterStreamEvents");
                    Set(input, "running", false);
                    Set(manager, "_isSessActivated", false);
                    manager.InformSuccessSubscribedData -= legacy;
                    UnityEngine.Object.DestroyImmediate(holder);
                    emotiv.Stop();
                }
            }
            manager.IsDataBufferUsing = true;
        }

        private static MethodInfo Method(Type type, string name)
            => type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);

        private static Vector2 Command(string action, double power, double age = 0, float threshold = 0.3f)
            => (Vector2)Method(typeof(EmotivPlayerInput), "ResolveCommand").Invoke(null,
                new object[] { action, power, threshold, age, 1f });

        private static void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Equal(Vector2 actual, Vector2 expected, string message)
            => Require((actual - expected).sqrMagnitude < 0.000001f, message + ": " + actual);

        private static void VerifyLocalConfiguration()
        {
            string path = System.IO.Path.GetTempFileName();
            var read = typeof(EmotivPlayerInput).GetMethod("ReadConfiguration", BindingFlags.NonPublic | BindingFlags.Static);
            var json = new JObject
            {
                ["clientId"] = "test-id", ["clientSecret"] = "dummy-not-a-credential",
                ["appName"] = "TestApp", ["profileName"] = "TestProfile",
                ["threshold"] = 0.6f, ["commandTimeout"] = 2.5f
            };
            try
            {
                System.IO.File.WriteAllText(path, json.ToString());
                object config = read.Invoke(null, new object[] { path });
                foreach (string field in new[] { "clientId", "clientSecret", "appName", "profileName", "threshold", "commandTimeout" })
                {
                    object actual = config.GetType().GetField(field).GetValue(config);
                    Require(JToken.FromObject(actual).ToString() == json[field].ToString(), "JSON setting loaded: " + field);
                }
                foreach (string field in new[] { "clientId", "clientSecret", "appName", "profileName", "threshold", "commandTimeout" })
                {
                    var invalid = (JObject)json.DeepClone();
                    invalid[field] = field == "threshold" || field == "commandTimeout" ? (JToken)(-1f) : "";
                    System.IO.File.WriteAllText(path, invalid.ToString());
                    bool rejected = false;
                    try { read.Invoke(null, new object[] { path }); }
                    catch (TargetInvocationException error) { rejected = error.InnerException is System.IO.InvalidDataException; }
                    Require(rejected, "Invalid JSON setting rejected: " + field);
                }
                System.IO.File.WriteAllText(path, "{invalid json");
                bool malformedRejected = false;
                try { read.Invoke(null, new object[] { path }); }
                catch (TargetInvocationException) { malformedRejected = true; }
                Require(malformedRejected, "Malformed JSON rejected");
                System.IO.File.Delete(path);
                bool missingRejected = false;
                try { read.Invoke(null, new object[] { path }); }
                catch (TargetInvocationException error) { missingRejected = error.InnerException is System.IO.FileNotFoundException; }
                Require(missingRejected, "Missing config rejected");
            }
            finally { System.IO.File.Delete(path); }
        }

        private static void VerifyCommands()
        {
            Equal(Command("push", 0.5), new Vector2(0, 0.5f), "Push forward at half speed");
            Equal(Command("pull", 0.5), new Vector2(0, -0.5f), "Pull backward at half speed");
            Equal(Command("left", 0.5), new Vector2(-0.5f, 0.5f), "Left turns AND advances at half speed");
            Equal(Command("right", 0.5), new Vector2(0.5f, 0.5f), "Right turns AND advances at half speed");
            Equal(Command("right", 1), Vector2.one, "Full power");
            Equal(Command("push", 0.3), new Vector2(0, 0.3f), "Threshold inclusive");
            Equal(Command("push", 0.299), Vector2.zero, "Below threshold");
            Equal(Command("push", -1), Vector2.zero, "Negative power");
            Equal(Command("push", 5), Vector2.up, "Power capped at one");
            Equal(Command("push", double.NaN), Vector2.zero, "NaN ignored");
            Equal(Command("push", double.PositiveInfinity), Vector2.zero, "Infinity ignored");
            Equal(Command("neutral", 1), Vector2.zero, "Neutral stops input");
            Equal(Command("unknown", 1), Vector2.zero, "Unknown action ignored");
            Equal(Command(null, 1), Vector2.zero, "No action ignored");
            Equal(Command("push", 1, 0.999), Vector2.up, "Fresh sample usable");
            Equal(Command("push", 1, 1), Vector2.zero, "Timeout inclusive");
            Equal(Command("push", 1, double.PositiveInfinity), Vector2.zero, "No sample stops input");
            Equal(Command("push", 0.5, 0, 0.6f), Vector2.zero, "Configured threshold respected");
            Equal(Command("push", 1, 2), Vector2.zero, "Same action cannot survive a stale timestamp");
            Equal(Command("push", 1, 0), Vector2.up, "Same action with refreshed timestamp resumes");
        }

        private static void VerifyKeyboard()
        {
            MethodInfo resolve = Method(typeof(SmoothExplorerCamera), "ResolveMovementInput");
            Func<bool, float, float, Vector2, Vector2> input = (active, turn, forward, mental)
                => (Vector2)resolve.Invoke(null, new object[] { active, turn, forward, mental });
            Equal(input(true, 0, -1, Vector2.up), Vector2.down, "Keyboard overrides opposite mental input");
            Equal(input(true, -1, 0, Vector2.one), new Vector2(-1, 1), "A keeps turning while advancing");
            Equal(input(true, 1, -1, Vector2.zero), Vector2.one, "D/S combination preserves original behavior");
            Equal(input(true, 0, 0, Vector2.one), Vector2.zero, "Held opposing keys suppress mental input");
            Equal(input(false, 0, 0, new Vector2(0.4f, 0.4f)), new Vector2(0.4f, 0.4f), "Release resumes mental power");
            Equal(input(false, 0, 0, Vector2.zero), Vector2.zero, "No source produces no input");
        }

        private static void VerifyPluginLifecycle()
        {
            // Init alone never connects to Cortex. Check the singleton handlers with dummy credentials.
            var emotiv = EmotivUnityItf.Instance;
            emotiv.Init("acceptance-test", "dummy-not-a-credential", "Acceptance", false, true, "wss://localhost:6868");
            var eventField = typeof(DataStreamManager).GetField("MentalCommandReceived", BindingFlags.Instance | BindingFlags.NonPublic);
            int first = ((Delegate)eventField.GetValue(DataStreamManager.Instance)).GetInvocationList().Length;
            emotiv.Stop();
            emotiv.Init("acceptance-test", "dummy-not-a-credential", "Acceptance", false, true, "wss://localhost:6868");
            int second = ((Delegate)eventField.GetValue(DataStreamManager.Instance)).GetInvocationList().Length;
            Require(first == second, "Repeated Init/Stop must not duplicate command subscriptions");
            emotiv.Stop();
            Require(!emotiv.IsAuthorizedOK && !emotiv.IsSessionCreated && !emotiv.IsProfileLoaded, "Stop clears readiness");
        }

        private static void VerifyPlayerMovement(Camera camera, Terrain terrain)
        {
            var explorer = camera.GetComponent<SmoothExplorerCamera>();
            Vector3 originalPosition = camera.transform.position;
            Set(explorer, "groundTerrain", terrain);
            float eyeHeight = (float)typeof(SmoothExplorerCamera).GetField("eyeHeight",
                BindingFlags.NonPublic | BindingFlags.Instance).GetValue(explorer);
            try
            {
                TerrainTreeObstacleRegistry.EnsureInitialized();
                Require(TerrainTreeObstacleRegistry.ObstacleCount > 0, "Dinosaur obstacles missing");
                Rect bounds = WorldBounds.PlayerExplorationXZ;
                foreach (Vector2 desired in new[] {
                    new Vector2(bounds.xMin - 1, 0), new Vector2(bounds.xMax + 1, 0),
                    new Vector2(0, bounds.yMin - 1), new Vector2(0, bounds.yMax + 1) })
                {
                    Invoke(explorer, "MoveTo", desired);
                    Vector2 expected = WorldBounds.ClampToPlayerExploration(desired);
                    Equal(new Vector2(camera.transform.position.x, camera.transform.position.z), expected,
                        "Player remains inside exploration bounds");
                    float ground = terrain.SampleHeight(camera.transform.position) + terrain.transform.position.y;
                    Require(Mathf.Abs(camera.transform.position.y - ground - eyeHeight) < 0.001f,
                        "Player retains terrain-relative eye height");
                }

                int crossedTrees = 0;
                Vector3 origin = terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                foreach (var tree in terrain.terrainData.treeInstances)
                {
                    Vector3 point = origin + Vector3.Scale(tree.position, size);
                    Vector2 center = new Vector2(point.x, point.z);
                    if (!WorldBounds.ContainsPlayer(center)
                        || !TerrainTreeObstacleRegistry.IsBlocked(center, 0.45f)) continue;
                    // Traverse in both directions with normal frame-sized steps, including through the tree center.
                    foreach (float direction in new[] { 1f, -1f })
                        for (int frame = -30; frame <= 30; frame++)
                        {
                            Vector2 desired = center + Vector2.right * (direction * frame * 4f / 60f);
                            Invoke(explorer, "MoveTo", desired);
                            Equal(new Vector2(camera.transform.position.x, camera.transform.position.z),
                                WorldBounds.ClampToPlayerExploration(desired), "Player passes through trees without displacement");
                        }
                    Require(TerrainTreeObstacleRegistry.IsBlocked(center, 0.45f),
                        "Player movement must preserve shared tree obstacles for dinosaurs");
                    crossedTrees++;
                }
                Require(crossedTrees > 0, "No explorable tree tested");
            }
            finally { camera.transform.position = originalPosition; }
        }

        private static void VerifyScene()
        {
            // Refuse to discard the user's unsaved scene changes from the menu command.
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new InvalidOperationException("Acceptance canceled; scene unchanged.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var camera = Camera.main;
            Require(camera != null, "Main camera missing");
            var input = camera.GetComponent<EmotivPlayerInput>();
            Require(input != null && input.enabled, "Emotiv component missing or disabled");
            Require(UnityEngine.Object.FindObjectsOfType<EmotivPlayerInput>().Length == 1, "Duplicate input component");
            var explorer = camera.GetComponent<SmoothExplorerCamera>();
            Require(explorer != null, "Movement controller missing");
            var serialized = new SerializedObject(explorer);
            Require(serialized.FindProperty("emotivInput").objectReferenceValue == input, "Input reference missing");
            var settings = new SerializedObject(input);
            foreach (string field in new[] { "clientId", "clientSecret", "appName", "profileName", "threshold", "commandTimeout" })
                Require(settings.FindProperty(field) == null, "Config must not be serialized in scene: " + field);
            Equal(input.ReadMovement(), Vector2.zero, "Unconnected component yields zero");
            Require(Terrain.activeTerrain != null, "Existing terrain missing");
            VerifyPlayerMovement(camera, Terrain.activeTerrain);
            Require(WorldBounds.ContainsPlayer(new Vector2(camera.transform.position.x, camera.transform.position.z)), "Player starts outside bounds");

            // Same action and power must still refresh arrival time; exercise the actual event callback.
            var callback = typeof(EmotivPlayerInput).GetMethod("OnCommand", BindingFlags.NonPublic | BindingFlags.Instance);
            var timestamp = typeof(EmotivPlayerInput).GetField("receivedAt", BindingFlags.NonPublic | BindingFlags.Instance);
            callback.Invoke(input, new object[] { null, new MentalCommandEventArgs(1, "push", 0.5) });
            long first = (long)timestamp.GetValue(input);
            System.Threading.Thread.Sleep(2);
            callback.Invoke(input, new object[] { null, new MentalCommandEventArgs(2, "push", 0.5) });
            Require((long)timestamp.GetValue(input) > first, "Identical commands must refresh timestamp");
            Equal(input.ReadMovement(), Vector2.zero, "Receiving a sample without an authorized session cannot move");
        }
    }
}
