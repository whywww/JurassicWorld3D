using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    public static class TRexAnimationAudit
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var rex = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex");
            var report = new StringBuilder();
            var body = rex.GetComponent<DinosaurCoreBody>();
            report.AppendLine("Core broad radius: " + body.BroadRadius);
            foreach (var band in body.bands) report.AppendLine("Body " + band.layer + ": " + band.bounds);
            var animation = rex.GetComponentInChildren<Animation>(true);
            foreach (AnimationState state in animation)
            {
                report.AppendLine(state.name + " | duration=" + state.length);
                foreach (var binding in AnimationUtility.GetCurveBindings(state.clip))
                    if (binding.path == "" || binding.path.Split('/').Length <= 2)
                    {
                        var curve = AnimationUtility.GetEditorCurve(state.clip, binding);
                        report.AppendLine("  " + binding.path + " " + binding.propertyName + " first=" + curve.Evaluate(0) + " last=" + curve.Evaluate(state.length));
                    }
                if (state.name.StartsWith("turn 90", StringComparison.Ordinal))
                {
                    foreach (var node in animation.GetComponentsInChildren<Transform>())
                        if (node.name == "Hips" || node.name == "jt_Cog_C" || node.name == "jt_Hips_C")
                        {
                            state.clip.SampleAnimation(animation.gameObject, 0f); var first = node.eulerAngles;
                            state.clip.SampleAnimation(animation.gameObject, state.length * .999f); var last = node.eulerAngles;
                            report.AppendLine("TURN_NODE " + node.name + " " + first + " -> " + last);
                        }
                }
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/trex-animation-audit.txt", report.ToString());
            Debug.Log(report.ToString());
        }
    }
}
