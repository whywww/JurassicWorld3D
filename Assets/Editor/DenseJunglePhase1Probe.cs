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
        private const string CrossingsKey = "JW_DenseJunglePhase1Probe_Crossings";
        private const string AssignmentsKey = "JW_DenseJunglePhase1Probe_Assignments";
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
        private static float movingYawDegrees;
        private static int scenarioIndex;
        private static double scenarioStarted;
        private static bool scenariosPassed;
        private static bool markersPassed;
        private static int crossingWaitSamples;
        private static bool[] seenRoutes;
        private static int assignmentTrials;
        private static bool assignmentsSafe;
        private static double nextAssignment;

        static DenseJunglePhase1Probe()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += Update;
            Application.logMessageReceived += OnLog;
        }

        public static void Run()
        {
            SessionState.SetBool(CrossingsKey, false);
            SessionState.SetBool(AssignmentsKey, false);
            SessionState.SetBool(RunningKey, true);
            SessionState.SetBool(PassedKey, false);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        public static void RunCrossings()
        {
            SessionState.SetBool(CrossingsKey, true);
            SessionState.SetBool(AssignmentsKey, false);
            SessionState.SetBool(RunningKey, true);
            SessionState.SetBool(PassedKey, false);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        public static void RunAssignments()
        {
            SessionState.SetBool(CrossingsKey, false);
            SessionState.SetBool(AssignmentsKey, true);
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
                movingYawDegrees = 0f;
                started = EditorApplication.timeSinceStartup;
                if (SessionState.GetBool(AssignmentsKey, false))
                {
                    Time.timeScale = 0f;
                    seenRoutes = new bool[MovementCorridor.RouteCount];
                    assignmentTrials = 0;
                    assignmentsSafe = agents.Length == 2;
                    nextAssignment = started;
                    return;
                }
                if (SessionState.GetBool(CrossingsKey, false))
                {
                    scenarioIndex = 0;
                    scenariosPassed = agents.Length == 2;
                    crossingWaitSamples = 0;
                    var marker = Object.FindObjectOfType<MovementCorridorDebug>();
                    markersPassed = marker != null;
                    if (marker != null)
                    {
                        marker.SetShowMarkers(false);
                        foreach (LineRenderer line in marker.GetComponentsInChildren<LineRenderer>())
                            markersPassed &= !line.enabled;
                        marker.SetShowMarkers(true);
                        LineRenderer[] lines = marker.GetComponentsInChildren<LineRenderer>();
                        markersPassed &= lines.Length == MovementCorridor.RouteCount;
                        foreach (LineRenderer line in lines) markersPassed &= line.enabled;
                    }
                    BeginCrossingScenario();
                }
                return;
            }

            if (SessionState.GetBool(AssignmentsKey, false))
            {
                UpdateAssignments();
                return;
            }

            for (int i = 0; i < agents.Length; i++)
            {
                DinosaurAgent agent = agents[i];
                if (agent == null) continue;
                Vector3 world = agent.transform.position;
                Vector2 xz = new Vector2(world.x, world.z);
                float moved = Vector2.Distance(xz, new Vector2(previous[i].x, previous[i].z));
                float turned = Mathf.Abs(Mathf.DeltaAngle(
                    previousRotation[i].eulerAngles.y, agent.transform.eulerAngles.y));
                travelled[i] += moved;
                if (moved < 0.001f && turned > 0.1f) stationaryTurns++;
                if (moved > 0.001f)
                {
                    movingYawDegrees += turned;
                    if (turned > 0.1f) movingTurns++;
                }
                previous[i] = world;
                previousRotation[i] = agent.transform.rotation;
                float arc = agent.RouteArc;
                Vector2 lane = MovementCorridor.PointAt(agent.RouteIndex, arc);
                maxLaneError = Mathf.Max(maxLaneError, Vector2.Distance(xz, lane));
                if (TerrainTreeObstacleRegistry.IsBlocked(xz, agent.TreeAvoidRadius)) blockedSamples++;
                if (SessionState.GetBool(CrossingsKey, false))
                {
                    var field = typeof(DinosaurAgent).GetField("waitingAtCrossing",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if ((bool)field.GetValue(agent)) crossingWaitSamples++;
                }
            }

            for (int i = 0; i < agents.Length; i++)
                for (int j = i + 1; j < agents.Length; j++)
                    minPeerClearance = Mathf.Min(minPeerClearance,
                        Vector3.Distance(agents[i].transform.position, agents[j].transform.position)
                        - agents[i].CollisionRadius - agents[j].CollisionRadius);

            if (SessionState.GetBool(CrossingsKey, false))
            {
                UpdateCrossingScenarios();
                return;
            }
            if (EditorApplication.timeSinceStartup - started < ProbeSeconds) return;

            bool passed = agents.Length == 2 && assignedRoutes[0] != assignedRoutes[1]
                && maxLaneError < 0.05f && minPeerClearance > 0f
                && blockedSamples == 0 && stuckWarnings == 0
                && stationaryTurns == 0 && movingYawDegrees > 5f;
            for (int i = 0; i < travelled.Length; i++)
                passed &= travelled[i] > 12f;

            Debug.Log($"DENSE_JUNGLE_PHASE1_PLAY_{(passed ? "OK" : "FAIL")}: " +
                $"routes={string.Join(",", assignedRoutes)}, travelled={string.Join(",", travelled)}, " +
                $"maxLaneError={maxLaneError:F2}m, minPeerClearance={minPeerClearance:F2}m, " +
                $"treeBlockedSamples={blockedSamples}, stuckWarnings={stuckWarnings}, " +
                $"stationaryTurns={stationaryTurns}, movingTurns={movingTurns}, " +
                $"movingYawDegrees={movingYawDegrees:F1}");
            SessionState.SetBool(PassedKey, passed);
            Time.timeScale = 1f;
            EditorApplication.isPlaying = false;
        }

        private static void UpdateAssignments()
        {
            if (EditorApplication.timeSinceStartup < nextAssignment) return;
            var method = typeof(DinosaurSpawnRandomizer).GetMethod("RandomizeAll",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            method.Invoke(null, null);
            assignmentTrials++;
            assignmentsSafe &= agents[0].RouteIndex != agents[1].RouteIndex;
            for (int i = 0; i < agents.Length; i++)
            {
                DinosaurAgent agent = agents[i];
                seenRoutes[agent.RouteIndex] = true;
                Vector2 position = new Vector2(agent.transform.position.x, agent.transform.position.z);
                assignmentsSafe &= Vector2.Distance(position,
                    MovementCorridor.PointAt(agent.RouteIndex, agent.RouteArc)) < 0.05f;
                assignmentsSafe &= !TerrainTreeObstacleRegistry.IsBlocked(position, agent.TreeAvoidRadius);
            }
            bool allSeen = true;
            foreach (bool seen in seenRoutes) allSeen &= seen;
            if (allSeen && assignmentTrials >= 10 || assignmentTrials >= 30)
            {
                bool passed = allSeen && assignmentsSafe;
                Debug.Log($"DENSE_JUNGLE_ASSIGNMENTS_{(passed ? "OK" : "FAIL")}: trials={assignmentTrials}, " +
                    $"seen={string.Join(",", seenRoutes)}, safe={assignmentsSafe}");
                SessionState.SetBool(PassedKey, passed);
                Time.timeScale = 1f;
                EditorApplication.isPlaying = false;
                return;
            }
            nextAssignment = EditorApplication.timeSinceStartup + 0.5;
        }

        private static void BeginCrossingScenario()
        {
            if (scenarioIndex >= MovementCorridorCrossings.ZoneCount) return;
            MovementCorridorCrossings.GetZone(scenarioIndex, out int a, out int b,
                out Vector2 center, out float radius);
            int[] routes = { a, b };
            for (int i = 0; i < 2; i++)
            {
                MovementCorridor.ProjectToCorridor(routes[i], center, out float arc, out _);
                Vector2 start = MovementCorridor.PointAt(routes[i], arc - radius - 10f);
                agents[i].ApplySpawnPlacement(start, routes[i]);
                agents[i].SendMessage("BeginRandomWalk");
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(DinosaurAgent).GetField("stateTimer", flags).SetValue(agents[i], 45f);
                typeof(DinosaurAgent).GetField("walkRemaining", flags).SetValue(agents[i], 90f);
                previous[i] = agents[i].transform.position;
                previousRotation[i] = agents[i].transform.rotation;
                travelled[i] = 0f;
            }
            scenarioStarted = EditorApplication.timeSinceStartup;
            Debug.Log($"DENSE_JUNGLE_CROSSING_START_{scenarioIndex}: routes={a},{b}, " +
                $"center={center}, radius={radius:F1}m");
        }

        private static void UpdateCrossingScenarios()
        {
            if (EditorApplication.timeSinceStartup - scenarioStarted < 12.0) return;
            bool scenarioPassed = travelled.Length == 2 && travelled[0] > 16f && travelled[1] > 16f;
            scenariosPassed &= scenarioPassed;
            Debug.Log($"DENSE_JUNGLE_CROSSING_ZONE_{scenarioIndex}: routes={agents[0].RouteIndex},{agents[1].RouteIndex}, " +
                $"travelled={travelled[0]:F1},{travelled[1]:F1}, passed={scenarioPassed}");
            scenarioIndex++;
            if (scenarioIndex < MovementCorridorCrossings.ZoneCount)
            {
                BeginCrossingScenario();
                return;
            }
            bool passed = scenariosPassed && markersPassed && maxLaneError < 0.05f &&
                minPeerClearance > 0f && blockedSamples == 0 && stuckWarnings == 0 && stationaryTurns == 0;
            Debug.Log($"DENSE_JUNGLE_CROSSINGS_{(passed ? "OK" : "FAIL")}: zones={scenarioIndex}, " +
                $"waitSamples={crossingWaitSamples}, markers={markersPassed}, maxLineError={maxLaneError:F2}, " +
                $"minPeerClearance={minPeerClearance:F2}, treeBlocked={blockedSamples}, " +
                $"stuckWarnings={stuckWarnings}, stationaryTurns={stationaryTurns}");
            SessionState.SetBool(PassedKey, passed);
            Time.timeScale = 1f;
            EditorApplication.isPlaying = false;
        }
    }
}
