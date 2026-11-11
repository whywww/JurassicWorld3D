using System;
using System.Collections.Generic;
using System.IO;
using EmotivUnityPlugin;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace JurassicWorld
{
    [DisallowMultipleComponent]
    public sealed class EmotivPlayerInput : MonoBehaviour, JurassicWorld.PlayerInputs.IBrainControlInput
    {
        private string appName;
        private string profileName;
        [SerializeField] private EmotivControlSettings controlSettings;
        [NonSerialized] private EmotivControlSettings runtimeSettings;
        [NonSerialized] private EmotivControlSettings defaultSettings;
        [NonSerialized] private int runtimeSession;
        private static int playSession;
        private readonly EmotivSensitivityController sensitivity = new EmotivSensitivityController();
        public EmotivSensitivityController Sensitivity => sensitivity;

        // Also runs when Enter Play Mode Options disables domain/scene reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void BeginPlaySession() => playSession++;

        // Never expose the saved asset for editing in Play mode, even before connection succeeds.
        public EmotivControlSettings ControlSettings
        {
            get
            {
                if (Application.isPlaying)
                {
                    if (runtimeSettings == null || runtimeSession != playSession) CreateRuntimeSettings();
                    return runtimeSettings;
                }
                return SavedSettings;
            }
        }

        private EmotivControlSettings SavedSettings
        {
            get
            {
                if (controlSettings != null) return controlSettings;
                if (defaultSettings == null)
                {
                    defaultSettings = ScriptableObject.CreateInstance<EmotivControlSettings>();
                    defaultSettings.hideFlags = HideFlags.HideAndDontSave;
                    Debug.LogWarning("[EMOTIV] 未关联控制配置，使用默认参数。请在 Inspector 关联配置资源以保存修改。", this);
                }
                return defaultSettings;
            }
        }

        private void CreateRuntimeSettings()
        {
            if (runtimeSettings != null) DestroyImmediate(runtimeSettings);
            runtimeSettings = Instantiate(SavedSettings);
            runtimeSession = playSession;
            runtimeSettings.name = "Emotiv Control Settings (运行配置)";
            runtimeSettings.hideFlags = HideFlags.HideAndDontSave;
            runtimeSettings.Validate();
        }

        private static EmotivPlayerInput owner;
        private readonly object commandLock = new object();
        private EmotivUnityItf emotiv;
        private bool running;
        private bool authorizationHandled;
        private bool sessionRequested;
        private bool profileRequested;
        private bool subscriptionRequested;
        private bool subscriptionLogged;
        private volatile bool subscribed;
        private volatile bool sensitivityRefreshRequested;
        private float nextHeadsetQuery;
        private ConnectToCortexStates? lastConnectionState;
        private string action;
        private double power;
        private long receivedAt;

        private void OnEnable()
        {
            if (!JurassicWorld.PlayerInputs.BrainInputSelection.CanStart(this)) return;
            if (Application.isPlaying && (runtimeSettings == null || runtimeSession != playSession)) CreateRuntimeSettings();
            if (JurassicWorld.PlayerInputs.PlayerMovementInput.HasProvider) return;
#if USE_EMBEDDED_LIB || (!UNITY_EDITOR_WIN && !UNITY_STANDALONE_WIN)
            Debug.LogWarning("[EMOTIV] 此组件需要 Windows Cortex Service / Option 1，且不能定义 USE_EMBEDDED_LIB。键盘仍可用。", this);
            return;
#else
            if (owner != null && owner != this)
            {
                Debug.LogWarning("[EMOTIV] 已有连接组件，忽略重复连接。", this);
                return;
            }
            LocalConfiguration configuration;
            try
            {
                // Editor: project root. Windows Player: beside the executable.
                string configPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "emotiv.local.json");
                configuration = ReadConfiguration(configPath);
            }
            catch (Exception error)
            {
                // Exception messages from JSON parsers may contain credential values.
                Debug.LogWarning("[EMOTIV] 无法读取 emotiv.local.json：" + error.GetType().Name
                    + "。请检查本地配置文件；键盘仍可用。", this);
                return;
            }
            appName = configuration.appName;
            profileName = configuration.profileName;
            owner = this;
            authorizationHandled = sessionRequested = profileRequested = subscriptionRequested = subscriptionLogged = false;
            sensitivityRefreshRequested = false;
            lastConnectionState = null;
            ClearCommand();
            emotiv = EmotivUnityItf.Instance;
            running = true;
            try
            {
                emotiv.Init(configuration.clientId, configuration.clientSecret, appName, allowSaveLogToFile: false,
                    isDataBufferUsing: true, appUrl: "wss://localhost:6868");
                RegisterStreamEvents();
                emotiv.Start();
                Debug.Log("[EMOTIV] Connecting to wss://localhost:6868; waiting for Launcher authorization.", this);
            }
            catch (Exception error)
            {
                Debug.LogWarning("[EMOTIV] 连接初始化失败：" + error.GetType().Name + "。键盘仍可用。", this);
                StopConnection();
            }
#endif
        }

        [Serializable]
        private sealed class LocalConfiguration
        {
            public string clientId;
            public string clientSecret;
            public string appName;
            public string profileName;
        }

        private static LocalConfiguration ReadConfiguration(string path)
        {
            var configuration = JsonUtility.FromJson<LocalConfiguration>(File.ReadAllText(path));
            if (configuration == null || string.IsNullOrWhiteSpace(configuration.clientId)
                || string.IsNullOrWhiteSpace(configuration.clientSecret)
                || string.IsNullOrWhiteSpace(configuration.appName)
                || string.IsNullOrWhiteSpace(configuration.profileName))
                throw new InvalidDataException("Invalid EMOTIV local configuration.");
            return configuration;
        }

        private void Update()
        {
            if (!running) return;
            try { AdvanceConnection(); }
            catch (Exception error)
            {
                Debug.LogWarning("[EMOTIV] 连接流程失败：" + error.GetType().Name + "。请检查 Launcher 和插件错误，重新进入 Play；键盘仍可用。", this);
                StopConnection();
            }
        }

        private void AdvanceConnection()
        {
            var state = emotiv.GetConnectToCortexState();
            if (lastConnectionState != state)
            {
                lastConnectionState = state;
                Debug.Log("[EMOTIV] Cortex state: " + state, this);
            }
            if (!emotiv.IsAuthorizedOK || state != ConnectToCortexStates.Authorized)
            {
                ClearCommand();
                sensitivity.Reset();
                if (authorizationHandled)
                {
                    authorizationHandled = sessionRequested = profileRequested = subscriptionRequested = subscriptionLogged = false;
                    emotiv.ResetProfileAndTrainingData();
                }
                return;
            }
            if (!authorizationHandled)
            {
                authorizationHandled = true;
                emotiv.QueryHeadsets();
                nextHeadsetQuery = Time.unscaledTime + 2f;
                Debug.Log("[EMOTIV] Authorized; searching for headset / Virtual Brainwear.", this);
            }
            if (!sessionRequested)
            {
                var headsets = emotiv.GetDetectedHeadsets();
                if (headsets == null || headsets.Count == 0)
                {
                    if (Time.unscaledTime >= nextHeadsetQuery)
                    {
                        emotiv.QueryHeadsets();
                        nextHeadsetQuery = Time.unscaledTime + 2f;
                    }
                    return;
                }
                sessionRequested = true;
                Debug.Log("[EMOTIV] Headset found: " + headsets[0].HeadsetID + "; creating session.", this);
                emotiv.CreateSessionWithHeadset(headsets[0].HeadsetID);
                return;
            }
            if (!emotiv.IsSessionCreated)
            {
                ClearCommand();
                sensitivity.Reset();
                if (profileRequested)
                {
                    sessionRequested = profileRequested = subscriptionRequested = subscriptionLogged = false;
                    emotiv.ResetProfileAndTrainingData();
                    Debug.LogWarning("[EMOTIV] Session lost; input cleared, searching again.", this);
                }
                return;
            }
            if (!profileRequested)
            {
                profileRequested = true;
                Debug.Log("[EMOTIV] Session created; loading profile: " + profileName, this);
                emotiv.LoadProfile(profileName);
                return;
            }
            if (!emotiv.IsProfileLoaded || emotiv.LoadedProfileName != profileName)
            {
                sensitivity.Reset();
                return;
            }
            string sessionId = SessionHandler.Instance.SessionId;
            if (sensitivityRefreshRequested)
            {
                sensitivityRefreshRequested = false;
                sensitivity.Reset();
            }
            if (sensitivity.SessionId != sessionId)
                sensitivity.Begin(CortexClient.Instance, Authorizer.Instance.CortexToken, sessionId,
                    ControlSettings, Time.realtimeSinceStartupAsDouble);
            sensitivity.Tick(Time.realtimeSinceStartupAsDouble);
            if (!subscriptionRequested)
            {
                subscriptionRequested = true;
                Debug.Log("[EMOTIV] Profile loaded: " + profileName + "; subscribing to com/sys/dev.", this);
                emotiv.SubscribeData(new List<string> { "com", "sys", "dev" });
            }
            if (subscribed && !subscriptionLogged)
            {
                subscriptionLogged = true;
                Debug.Log("[EMOTIV] Mental Command stream subscribed; control ready.", this);
            }
        }

        // WebSocket events run off the Unity thread. Store a coherent snapshot and a monotonic timestamp.
        private void OnCommand(object sender, MentalCommandEventArgs data)
        {
            lock (commandLock)
            {
                action = data.Act;
                power = data.Pow;
                receivedAt = Stopwatch.GetTimestamp();
            }
        }

        private void RegisterStreamEvents()
        {
            sensitivity.Failed += OnSensitivityFailed;
            BCITraining.Instance.InformLoadProfileDone += OnSensitivityProfileLoaded;
            DataStreamManager.Instance.MentalCommandReceived += OnCommand;
            // The manager's InformSuccessSubscribedData is suppressed when buffering is enabled.
            // The process-level acknowledgement reports Cortex success in both buffering modes.
            DataStreamProcess.Instance.SubscribedOK += OnSubscribed;
            DataStreamManager.Instance.StreamStopNotify += OnStreamsStopped;
        }

        private void UnregisterStreamEvents()
        {
            sensitivity.Failed -= OnSensitivityFailed;
            BCITraining.Instance.InformLoadProfileDone -= OnSensitivityProfileLoaded;
            DataStreamManager.Instance.MentalCommandReceived -= OnCommand;
            DataStreamProcess.Instance.SubscribedOK -= OnSubscribed;
            DataStreamManager.Instance.StreamStopNotify -= OnStreamsStopped;
        }

        private void OnSubscribed(object sender, Dictionary<string, JArray> headers)
        {
            if (headers.ContainsKey("com")) subscribed = true;
        }

        private void OnSensitivityFailed(string message) => Debug.LogWarning("[EMOTIV] " + message, this);

        private void OnSensitivityProfileLoaded(object sender, string loadedProfile)
        {
            if (loadedProfile == profileName) sensitivityRefreshRequested = true;
        }

        private void OnStreamsStopped(object sender, List<string> streams)
        {
            if (streams.Contains("com")) ClearCommand();
        }

        private void ClearCommand()
        {
            subscribed = false;
            lock (commandLock) { action = null; power = 0; receivedAt = 0; }
        }

        public Vector2 ReadMovement()
        {
            if (!running || !isActiveAndEnabled || !subscribed || !emotiv.IsAuthorizedOK
                || emotiv.GetConnectToCortexState() != ConnectToCortexStates.Authorized
                || !emotiv.IsSessionCreated || !emotiv.IsProfileLoaded
                || emotiv.LoadedProfileName != profileName) return Vector2.zero;
            lock (commandLock)
            {
                double age = receivedAt == 0 ? double.PositiveInfinity
                    : (Stopwatch.GetTimestamp() - receivedAt) / (double)Stopwatch.Frequency;
                return ControlSettings.ResolveCommand(action, power, age);
            }
        }

        private void OnDisable() => StopConnection();
        private void OnApplicationQuit()
        {
            StopConnection();
            ReleaseSettings();
        }

        private void OnDestroy() => ReleaseSettings();

        private void ReleaseSettings()
        {
            if (runtimeSettings != null) DestroyImmediate(runtimeSettings);
            if (defaultSettings != null) DestroyImmediate(defaultSettings);
            runtimeSettings = defaultSettings = null;
        }

        private void StopConnection()
        {
            sensitivity.Reset();
            if (!running) return;
            running = false;
            UnregisterStreamEvents();
            ClearCommand();
            try { emotiv.Stop(); }
            catch (Exception error) { Debug.LogWarning("[EMOTIV] 停止连接：" + error.GetType().Name, this); }
            finally { if (owner == this) owner = null; }
        }
    }
}
