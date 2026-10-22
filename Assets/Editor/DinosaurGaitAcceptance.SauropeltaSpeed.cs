using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static partial class DinosaurGaitAcceptance
    {
        [MenuItem("Jurassic World/Acceptance/Verify Sauropelta Double Walking Speed")]
        public static void RunSauropeltaSpeed()
        {
            const string output = "Reports/SauropeltaSpeed";
            Directory.CreateDirectory(output);
            try
            {
                DinosaurGaitCalibration.Require(!EditorApplication.isPlayingOrWillChangePlaymode
                    && !EditorSceneManager.GetActiveScene().isDirty, "Stop Play and save scene edits first.");
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                baseline = JsonUtility.FromJson<BaselineData>(File.ReadAllText(Output + "/Baseline.json"));
                var agent = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Sauropelta");
                var others = Object.FindObjectsOfType<DinosaurAgent>().Where(a => a != agent).ToArray();
                var preserved = others.Select(a => EditorJsonUtility.ToJson(a)).ToArray();
                float height = agent.GetComponent<DinosaurFbxLoader>().ModelRoot.transform.localPosition.y;
                float nativeSpeed = agent.MoveAnimationGroundSpeed, approach = agent.ApproachSpeed;
                DinosaurGaitCalibration.Require(Mathf.Abs(agent.WalkSpeed - .44013974f) < .00001f
                    && Mathf.Abs(agent.WalkSpeed / nativeSpeed - 2f) < .00001f, "Saved walking speed must be doubled.");
                Set(agent, "legacyAnimation", agent.GetComponentInChildren<Animation>(true));
                typeof(DinosaurAgent).GetMethod("PlayMovementClip", Flags).Invoke(agent, new object[] { agent.WalkSpeed });
                DinosaurGaitCalibration.Require(agent.GetComponentInChildren<Animation>(true)["Walk"].speed == 2f,
                    "Runtime movement must select 2x playback.");
                Report.Clear();
                Report.AppendLine($"# Sauropelta double walking speed\n\nWalk={agent.WalkSpeed:F8} m/s; native={nativeSpeed:F8} m/s; playback=2x; approach={approach:F8} m/s.\n");
                Report.AppendLine("|Animal|Version|FPS|Case|Max stance drift m|Max reach error m|Max release jump m|Landings|\n|---|---|---:|---|---:|---:|---:|---:|");
                foreach (int rate in new[] { 30, 60, 144 })
                    foreach (string scenario in new[] { "straight", "turn", "turn-in-place", "stop-start", "blocked", "short-step", "reverse" })
                        Simulate(agent, agent.GaitProfile, true, rate, scenario, rate == 60 && scenario == "straight", output);
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    DinosaurGaitCalibration.Apply(agent);
                    DinosaurGaitCalibration.Require(Mathf.Abs(agent.WalkSpeed - nativeSpeed * 2f) < .00001f
                        && Mathf.Abs(agent.MoveAnimationGroundSpeed - nativeSpeed) < .00001f
                        && Mathf.Abs(agent.ApproachSpeed - approach) < .00001f
                        && Mathf.Abs(agent.GetComponent<DinosaurFbxLoader>().ModelRoot.transform.localPosition.y - height) < .00001f,
                        "Recalibration changed native speed, approach speed, height or double walking speed.");
                }
                for (int i = 0; i < others.Length; i++)
                    DinosaurGaitCalibration.Require(EditorJsonUtility.ToJson(others[i]) == preserved[i], "Another animal changed.");
                Report.AppendLine("\nPASS: 21 motion cases; saved speed and runtime 2x playback; two calibrations preserve speed, native speed, approach and height; other animals unchanged.\n");
                File.WriteAllText(output + "/Acceptance.md", Report.ToString());
                Debug.Log("PASS SAUROPELTA_DOUBLE_SPEED");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                File.WriteAllText(output + "/Acceptance.md", Report + "\nFAILED: " + error);
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }
    }
}
