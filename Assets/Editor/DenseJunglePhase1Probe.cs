using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    // 批处理入口：进入 Play 后采样两只恐龙约 100 秒游戏时间，验证路径与互斥。
    [InitializeOnLoad]
    public static class DenseJunglePhase1Probe
    {
        private const string RunningKey = "JW_DenseJunglePhase1Probe_Running";
        private const string PassedKey = "JW_DenseJunglePhase1Probe_Passed";
        private const double ProbeSeconds = 25.0;
        private static DinosaurAgent[] agents;
        private static Vector3[] previous;
        private static Quaternion[] previousRotation;
        private static float[] travelled;
        private static int[] assignedRoutes;
        private static double started;
        private static float maxLaneError;
        private static float minPeerClearance = float.MaxValue;
        private static int blockedSamples;
        private static int stuckWarnings;
        private static int stationaryTurns;
        private static int movingTurns;

        static DenseJunglePhase1Probe()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += Update;
            Application.logMessageReceived += OnLog;
        }

        public static void Run()
        {
            SessionState.SetBool(RunningKey, true);
            SessionState.SetBool(PassedKey, false);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                // 进入 Play 后再等两秒取基线，排除 RuntimeInitializeOnLoadMethod 的开局传送。
                agents = null;
                started = EditorApplication.timeSinceStartup;
                Time.timeScale = 4f;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                bool passed = SessionState.GetBool(PassedKey, false);
                SessionState.SetBool(RunningKey, false);
                EditorApplication.Exit(passed ? 0 : 1);
            }
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            if (SessionState.GetBool(RunningKey, false) && message.Contains("卡死看门狗"))
                stuckWarnings++;
        }

        private static void Update()
        {
            if (!SessionState.GetBool(RunningKey, false) || !EditorApplication.isPlaying)
                return;
            if (agents == null)
            {
                if (EditorApplication.timeSinceStartup - started < 2.0) return;
                agents = Object.FindObjectsOfType<DinosaurAgent>();
                previous = new Vector3[agents.Length];
                previousRotation = new Quaternion[agents.Length];
                travelled = new float[agents.Length];
                assignedRoutes = new int[agents.Length];
                for (int i = 0; i < agents.Length; i++)
                {
                    previous[i] = agents[i].transform.position;
                    previousRotation[i] = agents[i].transform.rotation;
                    assignedRoutes[i] = agents[i].RouteIndex;
                }
                maxLaneError = 0f;
                minPeerClearance = float.MaxValue;
                blockedSamples = 0;
                stuckWarnings = 0;
                stationaryTurns = 0;
                movingTurns = 0;
                started = EditorApplication.timeSinceStartup;
                return;
            }

            for (int i = 0; i < agents.Length; i++)
            {
                DinosaurAgent agent = agents[i];
                if (agent == null) continue;
                Vector3 world = agent.transform.position;
                Vector2 xz = new Vector2(world.x, world.z);
                float moved = Vector2.Distance(xz, new Vector2(previous[i].x, previous[i].z));
                float turned = Quaternion.Angle(previousRotation[i], agent.transform.rotation);
                travelled[i] += moved;
                if (moved < 0.001f && turned > 0.1f) stationaryTurns++;
                if (moved > 0.001f && turned > 0.1f) movingTurns++;
                previous[i] = world;
                previousRotation[i] = agent.transform.rotation;
                float arc = agent.RouteArc;
                Vector2 lane = MovementCorridor.PointAt(agent.RouteIndex, arc);
                maxLaneError = Mathf.Max(maxLaneError, Vector2.Distance(xz, lane));
                if (TerrainTreeObstacleRegistry.IsBlocked(xz, agent.TreeAvoidRadius)) blockedSamples++;
            }

            for (int i = 0; i < agents.Length; i++)
                for (int j = i + 1; j < agents.Length; j++)
                    minPeerClearance = Mathf.Min(minPeerClearance,
                        Vector3.Distance(agents[i].transform.position, agents[j].transform.position)
                        - agents[i].CollisionRadius - agents[j].CollisionRadius);

            if (EditorApplication.timeSinceStartup - started < ProbeSeconds) return;

            bool passed = agents.Length == 2 && assignedRoutes[0] != assignedRoutes[1]
                && maxLaneError < 0.05f && minPeerClearance > 0f
                && blockedSamples == 0 && stuckWarnings == 0
                && stationaryTurns == 0 && movingTurns > 0;
            for (int i = 0; i < travelled.Length; i++)
                passed &= travelled[i] > 12f;

            Debug.Log($"DENSE_JUNGLE_PHASE1_PLAY_{(passed ? "OK" : "FAIL")}: " +
                $"routes={string.Join(",", assignedRoutes)}, travelled={string.Join(",", travelled)}, " +
                $"maxLaneError={maxLaneError:F2}m, minPeerClearance={minPeerClearance:F2}m, " +
                $"treeBlockedSamples={blockedSamples}, stuckWarnings={stuckWarnings}, " +
                $"stationaryTurns={stationaryTurns}, movingTurns={movingTurns}");
            SessionState.SetBool(PassedKey, passed);
            Time.timeScale = 1f;
            EditorApplication.isPlaying = false;
        }
    }
}
