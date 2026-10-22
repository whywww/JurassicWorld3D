using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static partial class DinosaurGaitAcceptance
    {
        private const string MainKey = "JurassicWorld.GaitMain";
        private static DinosaurAgent[] mainAgents;
        private static Vector3[] mainPrevious;
        private static float[] mainTravel, mainError;
        private static float mainBegan;
        private static int mainFrame, mainRate;
        private static double mainDeadline;
        private static readonly StringBuilder MainReport = new StringBuilder();
        static DinosaurGaitAcceptance()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(MainKey, false))
                    EditorApplication.delayCall += BeginMain;
            };
        }
        [MenuItem("Jurassic World/Acceptance/Verify Three Animal Gaits in Main Play")]
        public static void RunMain()
        {
            DinosaurGaitCalibration.Require(!EditorApplication.isPlayingOrWillChangePlaymode
                && !EditorSceneManager.GetActiveScene().isDirty, "Save scene edits and stop Play first.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            SessionState.SetBool(MainKey, true);
            EditorApplication.isPlaying = true;
        }
        private static void BeginMain()
        {
            try
            {
                mainAgents = Object.FindObjectsOfType<DinosaurAgent>();
                mainPrevious = mainAgents.Select(a => a.transform.position).ToArray();
                mainTravel = new float[mainAgents.Length]; mainError = new float[mainAgents.Length];
                foreach (var behaviour in Object.FindObjectsOfType<MonoBehaviour>())
                    if (behaviour is EmotivPlayerInput || behaviour is SmoothExplorerCamera) behaviour.enabled = false;
                foreach (var camera in Object.FindObjectsOfType<Camera>()) camera.enabled = false;
                foreach (var agent in mainAgents)
                {
                    bool targeted = DinosaurGaitCalibration.Species.Contains(agent.name);
                    var feet = agent.GetComponent<DinosaurFootLock>();
                    DinosaurGaitCalibration.Require(targeted == (agent.GaitProfile != null), "Unexpected gait profile on " + agent.name);
                    if (targeted) DinosaurGaitCalibration.Require(feet != null && feet.GaitFootCount == agent.GaitProfile.feet.Length,
                        "Gait did not bind at runtime: " + agent.name);
                    else DinosaurGaitCalibration.Require(feet == null || feet.GaitFootCount == 0,
                        "Other species entered the new foot solver: " + agent.name);
                }
                MainReport.Clear(); MainReport.AppendLine("# Main scene runtime gait regression\n\nNormal spawn, goals, navigation and peer avoidance remain enabled.\n\n|FPS|Animal|Cumulative travel m|Max planted error m|\n|---:|---|---:|---:|");
                mainRate = 0; mainFrame = -1; mainBegan = Time.time;
                Time.captureDeltaTime = 1f / 30f;
                mainDeadline = EditorApplication.timeSinceStartup + 900;
                EditorApplication.update += TickMain;
            }
            catch (Exception error) { FinishMain(error); }
        }
        private static void TickMain()
        {
            if (!EditorApplication.isPlaying || mainFrame == Time.frameCount) return;
            mainFrame = Time.frameCount;
            try
            {
                DinosaurGaitCalibration.Require(EditorApplication.timeSinceStartup < mainDeadline, "Main play test timeout");
                for (int i = 0; i < mainAgents.Length; i++)
                {
                    var agent = mainAgents[i];
                    mainTravel[i] += Vector3.Distance(agent.transform.position, mainPrevious[i]);
                    mainPrevious[i] = agent.transform.position;
                    var feet = agent.GetComponent<DinosaurFootLock>();
                    if (feet != null && agent.GaitProfile != null)
                    {
                        mainError[i] = Mathf.Max(mainError[i], feet.GaitMaximumReachError);
                        DinosaurGaitCalibration.Require(feet.GaitMaximumReachError <= .02f,
                            $"Main play stance error: {agent.name} {feet.GaitMaximumReachError:F4} state={agent.CurrentState} traffic={agent.GroundTrafficStatus}");
                    }
                }
                if (Time.time - mainBegan < 45f) return;
                int[] rates = { 30, 60, 144 };
                for (int i = 0; i < mainAgents.Length; i++)
                    MainReport.AppendLine($"|{rates[mainRate]}|{mainAgents[i].name}|{mainTravel[i]:F3}|{mainError[i]:F5}|");
                File.WriteAllText(Output + "/MainPlay.md", MainReport.ToString());
                Debug.Log("GAIT_MAIN_RATE " + rates[mainRate]);
                if (++mainRate == rates.Length)
                {
                    for (int i = 0; i < mainAgents.Length; i++)
                        if (mainAgents[i].GaitProfile != null) DinosaurGaitCalibration.Require(mainTravel[i] > .1f,
                            "No roaming progress during Main test: " + mainAgents[i].name);
                    VerifyIdleRules(); FinishMain(null); return;
                }
                Time.captureDeltaTime = 1f / rates[mainRate]; mainBegan = Time.time;
            }
            catch (Exception error) { FinishMain(error); }
        }
        private static void VerifyIdleRules()
        {
            foreach (var agent in mainAgents.Where(a => a.GaitProfile != null))
            {
                Set(agent, "gaitMoving", false);
                var idle = (string)typeof(DinosaurAgent).GetField("idleClip", Flags).GetValue(agent);
                typeof(DinosaurAgent).GetMethod("PlayClip", Flags).Invoke(agent, new object[] { idle, 1f, false });
                var state = agent.GetComponentInChildren<Animation>()[idle];
                DinosaurGaitCalibration.Require(agent.name == "Sauropelta" ? state.speed == 0f : state.speed == 1f,
                    "Idle animation rule changed: " + agent.name);
            }
            MainReport.AppendLine("\nPASS idle rules: Sauropelta frozen; Pachycephalosaurus Idle; Baurusuchus Run continues.\n");
        }
        private static void FinishMain(Exception error)
        {
            EditorApplication.update -= TickMain;
            SessionState.SetBool(MainKey, false); Time.captureDeltaTime = 0f;
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/MainPlay.md", MainReport + (error == null ? "\nPASS MAIN_GAITS\n" : "\nFAILED " + error));
            if (error == null) Debug.Log("PASS MAIN_GAITS"); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}
