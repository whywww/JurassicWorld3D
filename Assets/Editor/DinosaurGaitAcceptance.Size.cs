using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static partial class DinosaurGaitAcceptance
    {
        static string RecordingFolder(string output, string scenario)
        {
            string path = output + "/" + scenario; Directory.CreateDirectory(path); return path;
        }
        public static void AdjustBaurusuchusAndVerifyMain()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var agent = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Baurusuchus");
            const float target = -.20823628f;
            float delta = target - agent.GaitProfile.heightCorrection;
            agent.GetComponent<DinosaurFbxLoader>().ModelRoot.transform.position += Vector3.up * delta;
            agent.GaitProfile.heightCorrection = target;
            EditorUtility.SetDirty(agent.GaitProfile); AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(agent.gameObject.scene); EditorSceneManager.SaveScene(agent.gameObject.scene);
            VerifyEnlarged();
            RunMain();
        }
        public static void AdjustAndVerifyEnlarged()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var agent = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Sauropelta");
            const float target = -.16743453f;
            float delta = target - agent.GaitProfile.heightCorrection;
            agent.GetComponent<DinosaurFbxLoader>().ModelRoot.transform.position += Vector3.up * delta;
            agent.GaitProfile.heightCorrection = target;
            EditorUtility.SetDirty(agent.GaitProfile); AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(agent.gameObject.scene); EditorSceneManager.SaveScene(agent.gameObject.scene);
            VerifyEnlarged();
        }
        public static void VerifyEnlarged()
        {
            const string output = "Reports/DinosaurSize";
            Directory.CreateDirectory(output);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            baseline = JsonUtility.FromJson<BaselineData>(File.ReadAllText(BaselinePath));
            Report.Clear();
            Report.AppendLine("# Enlarged dinosaur gait acceptance\n\n|Animal|Version|FPS|Case|Max stance drift m|Max reach error m|Max release jump m|Landings|\n|---|---|---:|---|---:|---:|---:|---:|");
            try
            {
                foreach (var agent in Object.FindObjectsOfType<DinosaurAgent>().Where(a => a.GaitProfile != null))
                    foreach (int fps in new[] { 30, 60, 144 })
                        foreach (string scenario in new[] { "straight", "turn", "stop-start", "blocked" })
                            Simulate(agent, agent.GaitProfile, true, fps, scenario, fps == 60 && (scenario == "straight" || scenario == "turn"), RecordingFolder(output, scenario));
                File.WriteAllText(output + "/Acceptance.md", Report + "\nPASS enlarged gait checks\n");
            }
            catch (System.Exception e)
            {
                File.WriteAllText(output + "/Acceptance.md", Report + "\nFAIL: " + e);
                throw;
            }
        }
    }
}
