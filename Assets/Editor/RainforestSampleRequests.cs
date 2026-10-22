using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    // Development-only IPC for running the same acceptance entry points in an already-open
    // editor. Never closes the user's editor, starts a second instance or saves a dirty scene.
    [InitializeOnLoad]
    internal static class RainforestSampleRequests
    {
        private static string previous;
        static RainforestSampleRequests() { EditorApplication.update += Poll; }
        private static void Poll()
        {
            const string request = "Logs/rainforest-request.txt", statusPath = "Logs/rainforest-status.txt";
            if (!File.Exists(request)) return;
            string state = EditorApplication.isCompiling || EditorApplication.isUpdating ? "WAITING_FOR_IMPORT"
                : EditorApplication.isPlayingOrWillChangePlaymode ? "WAITING_FOR_PLAY_TO_STOP"
                : EditorSceneManager.GetActiveScene().isDirty ? "WAITING_FOR_SCENE_SAVE" : "STARTING";
            if (state != previous) { File.WriteAllText(statusPath, state); previous = state; }
            if (state != "STARTING") return;
            string command = File.ReadAllText(request).Trim(); File.Delete(request);
            try
            {
                File.WriteAllText(statusPath, "RUNNING " + command);
                switch (command)
                {
                    case "tree-heights": TreeHeightAdjustment.ApplyAndVerify(); break;
                    case "tree-height-audit": TreeHeightAdjustment.AuditCurrent(); break;
                    case "tree-height-undo": TreeHeightAdjustment.Undo(); break;
                    case "expansion-build": RainforestExpansion.BuildAndVerify(); break;
                    case "expansion-performance": RainforestSampleAcceptance.RunExpansionPerformance(); break;
                    case "expansion-movement": RainforestSampleAcceptance.RunExpansionMovement(); break;
                    case "expansion-play": ForestPlayAcceptance.RunExpansion(); break;
                    case "exit-left-only": TRexExitPlannerAcceptance.RunLeftOnly(); break;
                    case "exit-planner": TRexExitPlannerAcceptance.Run(); break;
                    case "exit-cost": TRexExitPlannerPerformance.Run(); break;
                    case "open-follow": TRexOpenFollowAcceptance.Run(); break;
                    case "body-envelope": TRexBodyEnvelopeAcceptance.Run(); break;
                    case "timing": DinosaurAnimationTimingAcceptance.Run(); break;
                    case "movement": RainforestSampleAcceptance.RunMovementOnly(); break;
                    case "normal-play": ForestPlayAcceptance.Run(); break;
                    case "performance": RainforestSampleAcceptance.RunPerformanceOnly(); break;
                    case "recording": RainforestSampleAcceptance.RunRecordingOnly(); break;
                    case "routes": DinosaurRouteAcceptance.RunWithoutImages(); break;
                    case "safety": DinosaurSafetyMainAcceptance.Run(); break;
                    default: throw new InvalidOperationException("Unknown rainforest acceptance request: " + command);
                }
            }
            catch (Exception error) { File.WriteAllText(statusPath, "FAILED " + error); Debug.LogException(error); }
        }
    }
}
