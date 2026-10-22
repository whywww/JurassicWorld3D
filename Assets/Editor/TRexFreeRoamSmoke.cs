using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static partial class TRexFreeRoamSmoke
    {
        private const string Key = "JurassicWorld.FreeRoamSmoke";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static DinosaurAgent rex, raptor;
        private static Vector3 previous, goal;
        private static float started, travelled, maximumDeparture, walkingStarted;
        private static int frame, goals, turns, stage;
        private static bool wasTurning;
        private static bool disabled;
        private static DinosaurInteractionSettings settings;
        private static Bounds[] originalObstacles;
        private static Vector3 blockedPosition;
        private static float blockedStarted;
        private static bool turnsOnly;
        private static float turnStarted, turnHeading;
        private static int turnCase;
        static TRexFreeRoamSmoke()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) EditorApplication.delayCall += Begin;
            };
        }
        [MenuItem("Jurassic World/Acceptance/Verify T-Rex Free Roaming (Short)")]
        public static void Run() => StartRun(false);
        public static void RunDisabled() => StartRun(true);
        public static void RunTurns() => StartRun(false, true);
        private static void StartRun(bool off, bool turnTest = false)
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            if (off) UnityEngine.Object.FindObjectOfType<DinosaurInteractionSettings>().allowOffRouteInteraction = false;
            foreach (var behaviour in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                if (behaviour is EmotivPlayerInput || behaviour.GetType().Namespace == "Crest") behaviour.enabled = false;
            SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".Off", off);
            SessionState.SetBool(Key + ".Turns", turnTest);
            EditorApplication.isPlaying = true;
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static object Get(string field) => typeof(DinosaurAgent).GetField(field, Private).GetValue(rex);
        private static void Set(string field, object value) => typeof(DinosaurAgent).GetField(field, Private).SetValue(rex, value);
        private static void Begin()
        {
            try
            {
                rex = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex");
                raptor = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.Species == DinosaurAgent.DinosaurSpecies.Velociraptor);
                Require(rex.IsFreeRoaming && !rex.UsesRouteScheduling && raptor.IsFreeRoaming && !raptor.UsesRouteScheduling,
                    "Roaming modes/scheduling are incorrect.");
                rex.ViewCamera.GetComponent<SmoothExplorerCamera>().enabled = false;
                rex.ViewCamera.transform.position = new Vector3(200f, 2f, 200f);
                disabled = SessionState.GetBool(Key + ".Off", false);
                turnsOnly = SessionState.GetBool(Key + ".Turns", false);
                Require((bool)Get("interactionReady") != disabled, "Startup interaction switch mismatch.");
                settings = UnityEngine.Object.FindObjectOfType<DinosaurInteractionSettings>();
                originalObstacles = settings.solidObstacles;
                var serialized = new SerializedObject(rex);
                serialized.FindProperty("roamingMode").enumValueIndex = 0;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                // A Play edit cannot change the captured mode.
                Require(rex.IsFreeRoaming, "Startup roaming mode changed during Play.");
                previous = rex.transform.position; goal = Vector3.zero;
                travelled = maximumDeparture = 0f; goals = turns = stage = frame = 0; wasTurning = false;
                started = Time.time;
                walkingStarted = (float)Get("freeRoamWalkTravel");
                Time.captureDeltaTime = 1f / 30f;
                if (turnsOnly)
                {
                    rex.enabled = raptor.enabled = false;
                    raptor.transform.position = new Vector3(200f, 0f, 200f);
                    VerifyFreeRecoveryContracts();
                    turnCase = 0; BeginTurnCase();
                }
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void BeginTurnCase()
        {
            ConfigureTurnObstacles();
            bool found = false;
            foreach (var point in new[] { turnCase / 2 == 3 ? new Vector3(300f, 0f, 300f) : new Vector3(-22f, 0f, 8f) })
            {
                rex.transform.SetPositionAndRotation(point, Quaternion.Euler(0f, -rex.VisualYaw, 0f));
                rex.GetComponent<DinosaurFootLock>().ResetSupportAnchors();
                Set("freeGoal", point + Vector3.forward * 15f);
                var use = Enum.Parse(typeof(DinosaurAgent).GetNestedType("TurnUse", BindingFlags.NonPublic), "Wander");
                found = (bool)typeof(DinosaurAgent).GetMethod("TryBeginSteppingTurn", Private).Invoke(rex, new[] { (object)(turnCase % 2 == 0 ? -90f : 90f), use });
                if (found) break;
            }
            Require(found, "No safe full " + (turnCase == 0 ? "left" : "right") + " turn fixture.");
            Require(rex.ApplyFootRootCorrection(Vector3.right * .001f), "Static space blocked wander foot correction.");
            turnStarted = Time.time;
            turnHeading = rex.transform.eulerAngles.y;
        }
        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying || rex == null || frame == Time.frameCount) return;
                frame = Time.frameCount;
                if (turnsOnly)
                {
                    Vector3 before = rex.transform.position;
                    typeof(DinosaurAgent).GetMethod("TickSteppingTurn", Private).Invoke(rex, null);
                    Require(Vector3.Distance(before, rex.transform.position) < 1f, "Wander turn teleported.");
                    Require(Time.time - turnStarted < 15f, "Stepping turn did not finish.");
                    if (!rex.IsSteppingTurn)
                    {
                        float yaw = Mathf.DeltaAngle(turnHeading, rex.transform.eulerAngles.y);
                        Require(Mathf.Abs(yaw - (turnCase % 2 == 0 ? -90f : 90f)) < 1f, "Turn did not reach authored 90 degree heading.");
                        turns++;
                        if (++turnCase == 8) { Finish(null); return; }
                        BeginTurnCase();
                    }
                    return;
                }
                Vector3 position = rex.transform.position;
                float step = Vector3.Distance(previous, position);
                Require(step < 1f, "Root teleported during roaming.");
                travelled += step; previous = position;
                maximumDeparture = Mathf.Max(maximumDeparture, MovementCorridor.MinDistanceToRoute(new Vector2(position.x, position.z), rex.RouteIndex));
                Vector3 currentGoal = (Vector3)Get("freeGoal");
                if (Vector3.Distance(goal, currentGoal) > 1f) { goal = currentGoal; goals++; }
                if (rex.IsSteppingTurn && !wasTurning) turns++;
                wasTurning = rex.IsSteppingTurn;
                if (stage != 3 && !rex.IsSteppingTurn && !(bool)Get("freeRecoveryActive"))
                {
                    Vector3 forward = rex.transform.rotation * Quaternion.Euler(0f, rex.VisualYaw, 0f) * Vector3.forward;
                    Require(!settings.PoseBlocked(new Vector2(position.x, position.z), new Vector2(forward.x, forward.z), (DinosaurCoreBody)Get("interactionBody")),
                        "Free roaming entered a tree/rock/body boundary.");
                }
                Require(raptor.IsFreeRoaming && !raptor.UsesRouteScheduling,
                    "Raptor returned to route scheduling.");
                float elapsed = Time.time - started;
                if (stage == 0 && elapsed >= 35f)
                {
                    Require((float)Get("freeRoamWalkTravel") - walkingStarted > 5f && goals > 0 && maximumDeparture > 2f,
                        $"Free roaming did not make off-route walking progress. seed={DinosaurSpawnRandomizer.LastSpawnSeed}, position={position}, travelled={travelled:F3}, walking={(float)Get("freeRoamWalkTravel") - walkingStarted:F3}, goals={goals}, turn={rex.IsSteppingTurn}, turnTime={rex.StepTurnClipTime:F5}, clip={Get("activeClip")}, blocked={Get("curiosityBlockedTime")}");
                    if (disabled) { Finish(null); return; }
                    if (rex.IsSteppingTurn) return;
                    typeof(DinosaurAgent).GetMethod("FinishFreeInteraction", Private).Invoke(rex, null);
                    Set("actualInteractionSpeed", 0f);
                    stage = 1;
                }
                if (stage == 1)
                {
                    Require(rex.InteractionPhase != DinosaurAgent.PlayerInteractionPhase.Returning, "Free mode tried to return to a route.");
                    if (rex.InteractionPhase == DinosaurAgent.PlayerInteractionPhase.Inactive) { stage = 2; started = Time.time; return; }
                }
                if (stage == 2 && elapsed >= 12f)
                {
                    Require(goals >= 2, "Free roaming did not resume after interaction exit.");
                    if (rex.IsSteppingTurn || rex.CurrentState == DinosaurAgent.DinosaurState.Idle) return;
                    blockedPosition = rex.transform.position; blockedStarted = Time.time;
                    // A synthetic unexpected solid tests pausing/replanning without modifying the scene.
                    settings.solidObstacles = originalObstacles.Concat(new[] { new Bounds(blockedPosition + Vector3.up, new Vector3(2f, 3f, 2f)) }).ToArray();
                    stage = 3;
                }
                if (stage == 3 && Time.time - blockedStarted >= 2.2f)
                {
                    Require(Vector3.Distance(blockedPosition, rex.transform.position) < .01f, "Blocked agent kept moving.");
                    Require((string)Get("activeClip") != "walk slow loop", "Blocked agent kept stepping in place.");
                    settings.solidObstacles = originalObstacles;
                    Finish(null);
                }
                Require(elapsed < 90f, "Smoke scenario timed out.");
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            CleanupTurnObstacles();
            if (settings != null && originalObstacles != null) settings.solidObstacles = originalObstacles;
            string result = error == null ? turnsOnly ? "PASS 8 left/right turns through solids, trees, boundary and off-NavMesh; peer gate and recovery contracts. Short smoke; visual Play feedback required." : $"PASS free roaming {(disabled ? "interaction OFF" : "ON + local interaction exit + blocked stop")}: travelled={travelled:F2}m walking={(float)Get("freeRoamWalkTravel") - walkingStarted:F2}m goals={goals} turns={turns} departure={maximumDeparture:F2}m; raptor uses free roaming. Short smoke; visual Play feedback required." : error.ToString();
            Directory.CreateDirectory("Logs"); File.WriteAllText(turnsOnly ? "Logs/trex-free-turn-result.txt" : disabled ? "Logs/trex-free-roam-off-result.txt" : "Logs/trex-free-roam-result.txt", result);
            if (error == null) Debug.Log(result); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
