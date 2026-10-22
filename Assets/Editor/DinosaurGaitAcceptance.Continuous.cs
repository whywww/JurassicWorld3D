using System;
using System.IO;
using UnityEngine;

namespace JurassicWorld.Editor
{
    public static partial class DinosaurGaitAcceptance
    {
        internal static void VerifyContinuousTurns(DinosaurAgent agent, string output)
        {
            baseline = JsonUtility.FromJson<BaselineData>(File.ReadAllText(BaselinePath));
            Report.Clear();
            Report.AppendLine("# Continuous turn gait calibration\n\nHighest accepted turn rate wins; rejected candidates retain their measured errors. Limits: stance drift/reach <=2cm, release <=8cm.\n\n|Animal|Version|FPS|Case|Max stance drift m|Max reach error m|Max release jump m|Landings|\n|---|---|---:|---|---:|---:|---:|---:|");
            float selected = 0f;
            try
            {
                foreach (float candidate in new[] { 10f, 8f, 6f, 4f, 2f })
                {
                    agent.GaitProfile.turnSpeed = candidate;
                    Report.AppendLine($"\nCandidate {candidate:F0} deg/s.\n");
                    try
                    {
                        foreach (int fps in new[] { 30, 60, 144 })
                        {
                            Simulate(agent, agent.GaitProfile, true, fps, "straight", false, output);
                            Simulate(agent, agent.GaitProfile, true, fps, "turn", false, output);
                            agent.GaitProfile.turnSpeed = -candidate;
                            Simulate(agent, agent.GaitProfile, true, fps, "turn", false, output);
                            agent.GaitProfile.turnSpeed = candidate;
                        }
                        selected = candidate; break;
                    }
                    catch (InvalidOperationException error)
                    {
                        if (!error.Message.Contains("Stance error") && !error.Message.Contains("Sole below ground")
                            && !error.Message.Contains("Abrupt foot release")) throw;
                        Report.AppendLine($"\nREJECTED {candidate:F0} deg/s: {error.Message}\n");
                    }
                }
                DinosaurGaitCalibration.Require(selected > 0f, "No safe continuous turn rate passed the gait limits.");
                agent.GaitProfile.turnSpeed = selected;
                Simulate(agent, agent.GaitProfile, true, 30, "turn", true, output);
                Report.AppendLine($"\nPASS selected {selected:F0} deg/s, both turn directions at 30/60/144 FPS, original walking speed and 2x playback retained.\n");
            }
            finally
            {
                agent.GaitProfile.turnSpeed = selected > 0f ? selected : 2f;
                File.WriteAllText(output + "/Gait.md", Report.ToString());
            }
        }
    }
}
