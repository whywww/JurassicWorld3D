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
                    var profileEvent = typeof(BCITraining).GetField("InformLoadProfileDone", BindingFlags.NonPublic | BindingFlags.Instance);
                    var profileCallbacks = ((Delegate)profileEvent.GetValue(BCITraining.Instance)).GetInvocationList();
                    int profileListeners = 0;
                    foreach (var callback in profileCallbacks)
                        if (callback.Target == input)
                        {
                            profileListeners++;
                            callback.DynamicInvoke(BCITraining.Instance, "OtherProfile");
                            Require(!(bool)typeof(EmotivPlayerInput).GetField("sensitivityRefreshRequested",
                                BindingFlags.NonPublic | BindingFlags.Instance).GetValue(input), "Other profiles cannot refresh sensitivity");
                            callback.DynamicInvoke(BCITraining.Instance, "Acceptance");
                            Require((bool)typeof(EmotivPlayerInput).GetField("sensitivityRefreshRequested",
                                BindingFlags.NonPublic | BindingFlags.Instance).GetValue(input), "Reloaded target profile refreshes sensitivity");
                        }
                    Require(profileListeners == 1, "Profile reload listener registered once");
                    Equal(input.ReadMovement(), Vector2.zero, "No input before subscription");
                    // Feed the SDK's real JSON acknowledgement parser, not the component's callback.
                    var success = new JArray(new JObject { ["streamName"] = "com", ["cols"] = new JArray("act", "pow") });
                    Invoke(process, "OnSubscribeDataDone", null, new MultipleResultEventArgs(success, new JArray()));
                    Require(legacyAcknowledged == !buffering, "Legacy acknowledgement suppressed only in buffered mode");
                    Require((bool)typeof(EmotivPlayerInput).GetField("subscribed", BindingFlags.NonPublic | BindingFlags.Instance)
                        .GetValue(input), "Real Cortex acknowledgement must mark control ready in both modes");
                    Equal(input.ReadMovement(), new Vector2(0, 0.5f), "Ready stream without samples uses half-speed fallback");
                    var commandEvent = (Delegate)typeof(DataStreamProcess).GetField("MentalCommandReceived",
                        BindingFlags.NonPublic | BindingFlags.Instance).GetValue(process);
                    commandEvent.DynamicInvoke(process, new ArrayList { 1.0, "push", 0.9 });
                    Equal(input.ReadMovement(), new Vector2(0, 0.9f), "SDK push 0.9 must reach player input");
                    Set(manager, "_isSessActivated", false);
                    Equal(input.ReadMovement(), Vector2.zero, "Session loss blocks received commands");
                    Set(manager, "_isSessActivated", true);
                    Set(input, "receivedAt", System.Diagnostics.Stopwatch.GetTimestamp() - 2L * System.Diagnostics.Stopwatch.Frequency);
                    Equal(input.ReadMovement(), new Vector2(0, 0.5f), "Stale sample uses half-speed fallback");
                    commandEvent.DynamicInvoke(process, new ArrayList { 2.0, "push", 0.9 });
                    Equal(input.ReadMovement(), new Vector2(0, 0.9f), "Identical fresh SDK push resumes movement");
                    commandEvent.DynamicInvoke(process, new ArrayList { 3.0, "neutral", 0.9 });
                    Equal(input.ReadMovement(), new Vector2(0, 0.5f), "SDK neutral uses half-speed fallback");
                    commandEvent.DynamicInvoke(process, new ArrayList { 4.0, "push", 0.9 });
                    Invoke(manager, "OnStreamStopNotify", null, new List<string> { "com" });
                    Equal(input.ReadMovement(), Vector2.zero, "SDK stream stop clears readiness");
                    Invoke(input, "OnSubscribed", null, new Dictionary<string, JArray> { ["com"] = new JArray() });
                    Equal(input.ReadMovement(), new Vector2(0, 0.5f), "Resubscribed stream resumes fallback");
                    Set(emotiv, "_isAuthorizedOK", false);
                    Equal(input.ReadMovement(), Vector2.zero, "Authorization loss blocks fallback");
                    Set(emotiv, "_isAuthorizedOK", true);
                    Set(input, "running", false);
                    input.enabled = false;
                    Set(input, "running", true);
                    Equal(input.ReadMovement(), Vector2.zero, "Disabled component cannot auto-advance");
                    Invoke(input, "UnregisterStreamEvents");
                    var callbacks = (Delegate)typeof(DataStreamProcess).GetField("SubscribedOK",
                        BindingFlags.NonPublic | BindingFlags.Instance).GetValue(process);
                    foreach (var callback in callbacks.GetInvocationList())
                        Require(callback.Target != input, "Unregister detaches subscription callback");
                    foreach (var callback in ((Delegate)profileEvent.GetValue(BCITraining.Instance)).GetInvocationList())
                        Require(callback.Target != input, "Unregister detaches profile reload callback");
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
        {
            var settings = ScriptableObject.CreateInstance<EmotivControlSettings>();
            try
            {
                settings.threshold = threshold;
                return settings.ResolveCommand(action, power, age);
            }
            finally { UnityEngine.Object.DestroyImmediate(settings); }
        }

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
                foreach (string field in new[] { "clientId", "clientSecret", "appName", "profileName" })
                {
                    object actual = config.GetType().GetField(field).GetValue(config);
                    Require(JToken.FromObject(actual).ToString() == json[field].ToString(), "JSON setting loaded: " + field);
                }
                // Old control values must not override or invalidate the shared asset.
                json["threshold"] = -1f;
                json["commandTimeout"] = "obsolete-value";
                System.IO.File.WriteAllText(path, json.ToString());
                Require(read.Invoke(null, new object[] { path }) != null, "Legacy control fields ignored");
                json.Remove("threshold");
                json.Remove("commandTimeout");
                System.IO.File.WriteAllText(path, json.ToString());
                Require(read.Invoke(null, new object[] { path }) != null, "Connection-only JSON accepted");
                foreach (string field in new[] { "clientId", "clientSecret", "appName", "profileName" })
                {
                    var invalid = (JObject)json.DeepClone();
                    invalid[field] = "";
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
            checks += EmotivSensitivityAcceptance.Verify();
            VerifyControlSettings();
            Equal(Command("push", 0.5), new Vector2(0, 0.5f), "Push forward at half speed");
            Equal(Command("pull", 0.5), new Vector2(0, -0.5f), "Pull backward at half speed");
            Equal(Command("left", 0.5), new Vector2(-0.5f, 0.5f), "Left turns AND advances at half speed");
            Equal(Command("right", 0.5), new Vector2(0.5f, 0.5f), "Right turns AND advances at half speed");
            Equal(Command("right", 1), Vector2.one, "Full power");
            Equal(Command("push", 0.3), new Vector2(0, 0.3f), "Threshold inclusive");
            Equal(Command("push", 0.299), new Vector2(0, 0.5f), "Below threshold");
            Equal(Command("push", -1), new Vector2(0, 0.5f), "Negative power");
            Equal(Command("push", 5), Vector2.up, "Power capped at one");
            Equal(Command("push", double.NaN), new Vector2(0, 0.5f), "NaN ignored");
            Equal(Command("push", double.PositiveInfinity), new Vector2(0, 0.5f), "Infinity ignored");
            Equal(Command("neutral", 1), new Vector2(0, 0.5f), "Neutral uses half-speed fallback");
            Equal(Command("unknown", 1), new Vector2(0, 0.5f), "Unknown action ignored");
            Equal(Command(null, 1), new Vector2(0, 0.5f), "No action ignored");
            Equal(Command("push", 1, 0.999), Vector2.up, "Fresh sample usable");
            Equal(Command("push", 1, 1), new Vector2(0, 0.5f), "Timeout inclusive");
            Equal(Command("push", 1, double.PositiveInfinity), new Vector2(0, 0.5f), "No sample uses half-speed fallback");
            Equal(Command("push", 0.5, 0, 0.6f), new Vector2(0, 0.5f), "Configured threshold respected");
            Equal(Command("push", 1, 2), new Vector2(0, 0.5f), "Same action cannot survive a stale timestamp");
            Equal(Command("push", 1, 0), Vector2.up, "Same action with refreshed timestamp resumes");
        }

        private static void VerifyControlSettings()
        {
            var settings = ScriptableObject.CreateInstance<EmotivControlSettings>();
            var holder = new GameObject("Emotiv settings regression");
            holder.SetActive(false);
            var input = holder.AddComponent<EmotivPlayerInput>();
            var explorer = holder.AddComponent<SmoothExplorerCamera>();
            Set(input, "controlSettings", settings);
            Set(explorer, "emotivInput", input);
            string path = "Assets/BCI/Emotiv/Settings/EmotivAcceptance-" + Guid.NewGuid().ToString("N") + ".asset";
            try
            {
                settings.push = EmotivMovementAction.ForwardLeft;
                Equal(settings.ResolveCommand("push", 0.8, 0), new Vector2(-0.8f, 0.8f), "Push remaps to advancing left");
                settings.push = EmotivMovementAction.Stop;
                Equal(settings.ResolveCommand("push", 1, 0), Vector2.zero, "Explicit stop overrides fallback");
                settings.fallbackForward = 0;
                Equal(settings.ResolveCommand("neutral", 1, 0), Vector2.zero, "Fallback can stop");
                settings.fallbackForward = 0.25f;
                settings.threshold = 0.7f;
                settings.commandTimeout = 2f;
                Equal(settings.ResolveCommand("right", 0.6, 0), new Vector2(0, 0.25f), "Configured threshold and fallback");
                Equal(settings.ResolveCommand("right", 0.8, 1.5), new Vector2(0.8f, 0.8f), "Configured longer timeout");
                Equal(settings.ResolveCommand("right", 0.8, 2), new Vector2(0, 0.25f), "Configured timeout boundary");
                settings.moveSpeed = float.NaN;
                settings.turnSpeed = float.PositiveInfinity;
                settings.threshold = -1;
                settings.commandTimeout = -1;
                settings.fallbackForward = 2;
                settings.Validate();
                Equal(new Vector2(settings.moveSpeed, settings.turnSpeed), new Vector2(4, 60), "Non-finite speeds reset safely");
                Require(settings.threshold == 0 && settings.commandTimeout == 0.1f && settings.fallbackForward == 1,
                    "Settings ranges validated");
                settings.moveSpeed = -1;
                settings.turnSpeed = -1;
                settings.threshold = float.NaN;
                settings.commandTimeout = float.PositiveInfinity;
                settings.fallbackForward = float.NaN;
                settings.Validate();
                Require(settings.moveSpeed == 0 && settings.turnSpeed == 0 && settings.threshold == 0.3f
                    && settings.commandTimeout == 1 && settings.fallbackForward == 0.5f, "Negative and non-finite values sanitized");
                settings.moveSpeed = 8;
                settings.turnSpeed = 90;
                Invoke(input, "CreateRuntimeSettings");
                var copy = (EmotivControlSettings)typeof(EmotivPlayerInput).GetField("runtimeSettings",
                    BindingFlags.NonPublic | BindingFlags.Instance).GetValue(input);
                Require(copy != settings && !EditorUtility.IsPersistent(copy), "Runtime copy is not a project asset");
                copy.moveSpeed = 12;
                copy.push = EmotivMovementAction.ForwardRight;
                copy.pushSensitivity.value = 1;
                Require(settings.moveSpeed == 8 && settings.push == EmotivMovementAction.Stop
                    && settings.pushSensitivity.value == 5, "Runtime snapshot cannot modify saved source");
                Invoke(input, "ReleaseSettings");
                Require(input.ControlSettings == settings && settings.moveSpeed == 8, "Leaving runtime copy restores saved source");

                AssetDatabase.CreateAsset(settings, path);
                var serialized = new SerializedObject(settings);
                serialized.FindProperty("moveSpeed").floatValue = 9;
                serialized.FindProperty("pushSensitivity.useProjectValue").boolValue = true;
                serialized.FindProperty("pushSensitivity.value").intValue = 8;
                serialized.ApplyModifiedProperties();
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Require(settings.moveSpeed == 8 && !settings.pushSensitivity.useProjectValue, "Inspector asset edit supports Undo");
                Undo.PerformRedo();
                Require(settings.moveSpeed == 9 && settings.pushSensitivity.value == 8
                    && settings.pushSensitivity.useProjectValue, "Inspector asset edit supports Redo");
                AssetDatabase.SaveAssetIfDirty(settings);
                Set(input, "controlSettings", null);
                Resources.UnloadAsset(settings);
                settings = AssetDatabase.LoadAssetAtPath<EmotivControlSettings>(path);
                Require(settings.moveSpeed == 9 && settings.turnSpeed == 90
                    && settings.push == EmotivMovementAction.Stop && settings.pushSensitivity.useProjectValue
                    && settings.pushSensitivity.value == 8, "Saved asset and sensitivity survive unload/reload");

                // Source selection is exercised by the actual camera speed resolver.
                Set(input, "controlSettings", settings);
                Set(explorer, "moveSpeed", 3f);
                Set(explorer, "turnSpeed", 40f);
                var speedMethod = typeof(SmoothExplorerCamera).GetMethod("ResolveMovementSpeeds",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Func<bool, Vector2> speeds = keyboard => (Vector2)speedMethod.Invoke(explorer, new object[] { keyboard });
                Equal(speeds(true), new Vector2(3, 40), "Keyboard keeps its original speeds");
                // isActiveAndEnabled requires an active holder. Guard activation with a registered provider.
                JurassicWorld.PlayerInputs.PlayerMovementInput.Register(holder, () => Vector2.up);
                try
                {
                    holder.SetActive(true);
                    Equal(speeds(false), new Vector2(3, 40), "Optional input provider keeps original speeds");
                }
                finally { JurassicWorld.PlayerInputs.PlayerMovementInput.Unregister(holder); }
                Equal(speeds(false), new Vector2(9, 90), "Emotiv alone uses configured speeds");
                settings.turnSpeed = 60;
                settings.right = EmotivMovementAction.ForwardRight;
                foreach (int fps in new[] { 30, 60, 144 })
                    foreach (float power in new[] { 0.5f, 1f })
                    {
                        float angle = 0;
                        for (int frame = 0; frame < fps; frame++)
                            angle += settings.ResolveCommand("right", power, 0).x * speeds(false).y / fps;
                        Require(Mathf.Abs(angle - power * 60) < 0.001f, "Continuous turn independent of FPS: " + fps);
                    }
                input.enabled = false;
                Equal(speeds(false), new Vector2(3, 40), "Disabled Emotiv retains default speed selection");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(holder);
                Undo.ClearUndo(settings);
                if (AssetDatabase.LoadAssetAtPath<EmotivControlSettings>(path) != null) AssetDatabase.DeleteAsset(path);
                else if (settings != null) UnityEngine.Object.DestroyImmediate(settings);
            }
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
            float radius = SmoothExplorerCamera.PlayerRadius;
            var settings = UnityEngine.Object.FindObjectOfType<DinosaurInteractionSettings>();
            Require(settings != null, "Dinosaur behavior settings missing");
            Rect[] rocks = PlayerSolidObstacleSolver.BuildBlockingXZ(settings.solidObstacles);
            Require(rocks.Length > 0, "No blocking rocks baked in scene");
            try
            {
                TerrainTreeObstacleRegistry.EnsureInitialized();
                Require(TerrainTreeObstacleRegistry.ObstacleCount > 0, "Dinosaur obstacles missing");

                Vector2 spawn = new Vector2(originalPosition.x, originalPosition.z);
                Require(!TerrainTreeObstacleRegistry.IsBlocked(spawn, radius), "Camera spawns inside a tree trunk");
                Require(!PlayerSolidObstacleSolver.Overlaps(spawn, radius, rocks), "Camera spawns inside a solid rock");

                VerifyExplorationBounds(camera, terrain, explorer, eyeHeight, radius, rocks);
                VerifyTreeSliding(camera, terrain, explorer, eyeHeight, radius, rocks);
                VerifyRockSliding(camera, terrain, explorer, eyeHeight, radius, rocks);
            }
            finally { camera.transform.position = originalPosition; }
        }

        private static void VerifyExplorationBounds(Camera camera, Terrain terrain, SmoothExplorerCamera explorer,
            float eyeHeight, float radius, Rect[] rocks)
        {
            Rect bounds = WorldBounds.PlayerExplorationXZ;
            Vector2 center = bounds.center;
            var edges = new[] {
                new Vector2(bounds.xMin - 1f, center.y), new Vector2(bounds.xMax + 1f, center.y),
                new Vector2(center.x, bounds.yMin - 1f), new Vector2(center.x, bounds.yMax + 1f) };
            int tested = 0;
            foreach (Vector2 desired in edges)
            {
                Vector2 expected = WorldBounds.ClampToPlayerExploration(desired);
                Vector2 start = expected + (center - expected).normalized * 2f;
                if (!IsClear(start, radius, rocks)) continue;
                PlaceCamera(camera, terrain, start, eyeHeight);
                Invoke(explorer, "MoveTo", desired);
                Vector2 now = PlayerXZ(camera);
                Require(WorldBounds.ContainsPlayer(now), "Player remains inside exploration bounds");
                Require(!TerrainTreeObstacleRegistry.IsBlocked(now, radius), "Boundary move enters a tree trunk");
                Require(!PlayerSolidObstacleSolver.Overlaps(now, radius, rocks), "Boundary move enters a solid rock");
                Require((now - start).magnitude > 0.5f, "Boundary move should advance into the clamp");
                float ground = terrain.SampleHeight(camera.transform.position) + terrain.transform.position.y;
                Require(Mathf.Abs(camera.transform.position.y - ground - eyeHeight) < 0.001f,
                    "Player retains terrain-relative eye height");
                tested++;
            }
            Require(tested > 0, "No clear boundary lane tested");
        }

        private static void VerifyTreeSliding(Camera camera, Terrain terrain, SmoothExplorerCamera explorer,
            float eyeHeight, float radius, Rect[] rocks)
        {
            var candidates = new List<Vector2>();
            foreach (SceneTreeObstacle marker in UnityEngine.Object.FindObjectsOfType<SceneTreeObstacle>())
            {
                if (!marker.isActiveAndEnabled || marker.Radius <= 0f) continue;
                AddTreeCandidate(candidates, new Vector2(marker.transform.position.x, marker.transform.position.z), radius);
                if (candidates.Count >= 12) break;
            }
            // Unity marks erased Terrain trees with a negative width scale; they are not obstacles.
            TreeInstance[] instances = terrain.terrainData.treeInstances;
            for (int i = 0; i < instances.Length && candidates.Count < 24; i++)
            {
                if (instances[i].widthScale <= 0f) continue;
                Vector3 point = terrain.transform.position + Vector3.Scale(instances[i].position, terrain.terrainData.size);
                AddTreeCandidate(candidates, new Vector2(point.x, point.z), radius);
            }
            Require(candidates.Count > 0, "No explorable tree tested");

            int headOnTested = 0;
            foreach (Vector2 center in candidates)
            {
                Vector2 start = center + Vector2.left * 2f;
                if (!IsClear(start, radius, rocks)) continue;
                PlaceCamera(camera, terrain, start, eyeHeight);
                for (int frame = 0; frame <= 60; frame++)
                {
                    Invoke(explorer, "MoveTo", start + Vector2.right * (frame * 4f / 60f));
                    Vector2 now = PlayerXZ(camera);
                    Require(!TerrainTreeObstacleRegistry.IsBlocked(now, radius), "Player entered a tree trunk");
                    Require(!PlayerSolidObstacleSolver.Overlaps(now, radius, rocks), "Player entered a solid rock");
                    Require(WorldBounds.ContainsPlayer(now), "Player left exploration bounds at a tree");
                }
                Vector2 end = PlayerXZ(camera);
                // 允许贴树滑过去，但任何一帧都不得进入树干（上面的逐帧断言）。
                Require((end - start).magnitude > 0.05f, "Tree traversal should advance instead of freezing");
                headOnTested++;
            }
            Require(headOnTested > 0, "No head-on tree candidate tested");

            int slid = 0;
            foreach (Vector2 center in candidates)
            {
                if (slid >= 2) break;
                // Offset lane must actually intersect the expanded trunk, otherwise there is nothing to slide around.
                if (!TerrainTreeObstacleRegistry.IsBlocked(center + Vector2.up * 0.5f, radius)) continue;
                Vector2 start = center + new Vector2(-2f, 0.5f);
                if (!IsClear(start, radius, rocks)) continue;
                PlaceCamera(camera, terrain, start, eyeHeight);
                for (int frame = 0; frame <= 60; frame++)
                {
                    Invoke(explorer, "MoveTo", start + Vector2.right * (frame * 4f / 60f));
                    Vector2 now = PlayerXZ(camera);
                    Require(!TerrainTreeObstacleRegistry.IsBlocked(now, radius), "Player entered a tree trunk while sliding");
                    Require(!PlayerSolidObstacleSolver.Overlaps(now, radius, rocks), "Player entered a solid rock while sliding");
                    Require(WorldBounds.ContainsPlayer(now), "Player left exploration bounds while sliding");
                }
                if (PlayerXZ(camera).x > center.x + 0.3f) slid++;
            }
            Require(slid > 0, "Player could not slide around any tree");
        }

        private static void VerifyRockSliding(Camera camera, Terrain terrain, SmoothExplorerCamera explorer,
            float eyeHeight, float radius, Rect[] rocks)
        {
            Rect bounds = WorldBounds.PlayerExplorationXZ;
            Vector2 direction = new Vector2(1f, 1f).normalized;
            int chosen = -1;
            Vector2 headOnStart = Vector2.zero, diagonalStart = Vector2.zero;
            for (int i = 0; i < rocks.Length && chosen < 0; i++)
            {
                Rect rock = rocks[i];
                if (rock.xMin - radius - 2f < bounds.xMin || rock.xMax + radius + 2f > bounds.xMax) continue;
                if (rock.yMin - radius - 2f < bounds.yMin || rock.yMax + radius + 2f > bounds.yMax) continue;
                headOnStart = new Vector2(rock.xMin - radius - 2f, rock.center.y);
                diagonalStart = new Vector2(rock.xMin - radius - 1.5f, rock.yMin - radius - 1.5f);
                if (!IsClear(headOnStart, radius, rocks) || !IsClear(diagonalStart, radius, rocks)) continue;
                if (!DiagonalLaneClear(rock, diagonalStart, direction, radius, rocks, i)) continue;
                chosen = i;
            }
            Require(chosen >= 0, "No isolated blocking rock available for sliding");
            Rect block = rocks[chosen];

            PlaceCamera(camera, terrain, headOnStart, eyeHeight);
            for (int frame = 0; frame <= 60; frame++)
            {
                Invoke(explorer, "MoveTo", headOnStart + Vector2.right * (frame * 4f / 60f));
                Require(!PlayerSolidObstacleSolver.Overlaps(PlayerXZ(camera), radius, rocks), "Player entered a solid rock");
            }
            Require(PlayerXZ(camera).x < block.xMin, "Head-on rock traversal must not pass through");

            PlaceCamera(camera, terrain, diagonalStart, eyeHeight);
            for (int frame = 0; frame <= 240; frame++)
            {
                Invoke(explorer, "MoveTo", diagonalStart + direction * (frame * 4f / 60f));
                Vector2 now = PlayerXZ(camera);
                Require(!PlayerSolidObstacleSolver.Overlaps(now, radius, rocks), "Player entered a solid rock while sliding");
                Require(WorldBounds.ContainsPlayer(now), "Player left exploration bounds while sliding around a rock");
            }
            Vector2 end = PlayerXZ(camera);
            Require(end.x > block.xMax + radius - 0.5f || end.y > block.yMax + radius - 0.5f,
                "Diagonal rock traversal must slide around a side");
        }

        private static void AddTreeCandidate(List<Vector2> candidates, Vector2 center, float radius)
        {
            if (!WorldBounds.ContainsPlayer(center) || !TerrainTreeObstacleRegistry.IsBlocked(center, radius)) return;
            foreach (Vector2 existing in candidates)
                if ((existing - center).sqrMagnitude < 0.25f) return;
            candidates.Add(center);
        }

        private static bool IsClear(Vector2 xz, float radius, Rect[] rocks)
            => WorldBounds.ContainsPlayer(xz)
               && !TerrainTreeObstacleRegistry.IsBlocked(xz, radius)
               && !PlayerSolidObstacleSolver.Overlaps(xz, radius, rocks);

        // The diagonal lane is expected to cross the chosen rock; only other obstacles may reject the sample.
        private static bool DiagonalLaneClear(Rect block, Vector2 start, Vector2 direction, float radius,
            Rect[] rocks, int ignored)
        {
            for (int i = 0; i <= 24; i++)
            {
                Vector2 point = start + direction * (i * 0.8f);
                if (!WorldBounds.ContainsPlayer(point)) return false;
                if (point.x > block.xMin - radius && point.x < block.xMax + radius
                    && point.y > block.yMin - radius && point.y < block.yMax + radius) continue;
                if (TerrainTreeObstacleRegistry.IsBlocked(point, radius)) return false;
                for (int j = 0; j < rocks.Length; j++)
                {
                    if (j == ignored) continue;
                    Rect other = rocks[j];
                    if (point.x > other.xMin - radius && point.x < other.xMax + radius
                        && point.y > other.yMin - radius && point.y < other.yMax + radius) return false;
                }
            }
            return true;
        }

        private static void PlaceCamera(Camera camera, Terrain terrain, Vector2 xz, float eyeHeight)
        {
            float y = terrain.SampleHeight(new Vector3(xz.x, 0f, xz.y)) + terrain.transform.position.y + eyeHeight;
            camera.transform.position = new Vector3(xz.x, y, xz.y);
        }

        private static Vector2 PlayerXZ(Camera camera)
            => new Vector2(camera.transform.position.x, camera.transform.position.z);

        private static void VerifyScene()
        {
            // Refuse to discard the user's unsaved scene changes from the menu command.
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new InvalidOperationException("Acceptance canceled; scene unchanged.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var camera = Camera.main;
            Require(camera != null, "Main camera missing");
            var input = camera.GetComponent<EmotivPlayerInput>();
            Require(input != null, "Emotiv component missing");
            Require(UnityEngine.Object.FindObjectsOfType<EmotivPlayerInput>().Length == 1, "Duplicate input component");
            var explorer = camera.GetComponent<SmoothExplorerCamera>();
            Require(explorer != null, "Movement controller missing");
            var serialized = new SerializedObject(explorer);
            Require(serialized.FindProperty("emotivInput").objectReferenceValue == input, "Input reference missing");
            var settings = new SerializedObject(input);
            Require(settings.FindProperty("controlSettings").objectReferenceValue
                == AssetDatabase.LoadAssetAtPath<EmotivControlSettings>(EmotivControlSettingsEditor.DefaultAssetPath),
                "Main scene references shared control asset (also included in builds)");
            Require(Array.Exists(AssetDatabase.GetDependencies("Assets/Scenes/Main.unity", true),
                path => path == EmotivControlSettingsEditor.DefaultAssetPath), "Build dependency contains control settings");
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
