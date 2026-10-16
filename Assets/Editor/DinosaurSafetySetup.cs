using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    public static class DinosaurSafetySetup
    {
        [MenuItem("Jurassic World/Interaction/Bake Raptor Free Roaming and Safety")]
        public static void BakeMain()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode
                || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save the current scene before baking.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            ConfigureCurrentScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            File.WriteAllText("Logs/dinosaur-safety-status.txt", "BAKED: raptor free roaming, safety 15m / release 20m.");
        }

        public static void BakeAndVerify()
        {
            BakeMain();
            DinosaurSafetyAcceptance.Run();
        }

        internal static void ConfigureCurrentScene()
        {
            foreach (var agent in UnityEngine.Object.FindObjectsOfType<DinosaurAgent>())
            {
                bool rex = agent.name == "Tyrannosaurus Rex", raptor = agent.name == "Velociraptor";
                if (!rex && !raptor) continue;
                var data = new SerializedObject(agent);
                data.FindProperty("species").enumValueIndex = (int)(rex
                    ? DinosaurAgent.DinosaurSpecies.Tyrannosaurus : DinosaurAgent.DinosaurSpecies.Velociraptor);
                if (raptor)
                {
                    var animation = agent.GetComponentInChildren<Animation>(true);
                    var loader = new SerializedObject(agent.GetComponent<DinosaurFbxLoader>());
                    var clips = new[] {
                        FindClip(animation, loader.FindProperty("idleAnimationHint").stringValue, "idle", "stand", "wait"),
                        FindClip(animation, loader.FindProperty("moveAnimationHint").stringValue, "run", "walk", "fly", "animation")
                    }.Distinct().ToArray();
                    // Match runtime clip selection; unused root-motion clips must not inflate the envelope.
                    var body = RouteObstacleClearance.BakeInteractionBody(agent, clips);
                    data.FindProperty("interactionBody").objectReferenceValue = body;
                    data.FindProperty("roamingMode").enumValueIndex = (int)DinosaurAgent.RoamingMode.Free;
                    data.FindProperty("playerInteractionEnabled").boolValue = false;
                    data.FindProperty("safetyDistance").floatValue = 15f;
                    data.FindProperty("safetyReleaseDistance").floatValue = 20f;
                    EditorUtility.SetDirty(body);
                }
                data.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(agent);
            }
        }

        private static AnimationClip FindClip(Animation animation, params string[] hints)
        {
            var states = animation.Cast<AnimationState>().ToArray();
            var matching = states.Where(s => hints.Any(h => !string.IsNullOrEmpty(h)
                && s.name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            return matching.FirstOrDefault(s => s.name.IndexOf("_RM", StringComparison.OrdinalIgnoreCase) < 0)?.clip
                ?? matching.FirstOrDefault()?.clip ?? states.First().clip;
        }
    }
}
