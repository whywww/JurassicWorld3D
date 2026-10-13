using System;
using System.Reflection;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld.Editor
{
    // Reference workload from the former full-sequence predictor, retained only for acceptance comparison.
    internal static class TRexPredictionBenchmark
    {
        private delegate bool Step(Vector3 from, Vector3 to, Quaternion fromRotation, Quaternion toRotation,
            DinosaurCoreBody body, DinosaurCoreBody.Band[] playerBands);
        private delegate bool Refresh(bool force);
        internal static string Measure(DinosaurAgent rex)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(DinosaurAgent);
            var profile = (DinosaurInteractionProfile)type.GetField("interactionProfile", flags).GetValue(rex);
            var step = (Step)Delegate.CreateDelegate(typeof(Step), rex, type.GetMethod("FrontStepSafe", flags));
            var refresh = (Refresh)Delegate.CreateDelegate(typeof(Refresh), rex, type.GetMethod("RefreshFrontReservation", flags));
            var holder = new GameObject("Prediction benchmark probe") { hideFlags = HideFlags.HideAndDontSave };
            var body = holder.AddComponent<DinosaurCoreBody>();
            var timer = new Stopwatch();
            try
            {
                refresh(true); // Warm the six cached poses before measuring recurring work.
                long probeBefore = GC.GetAllocatedBytesForCurrentThread();
                var probe = new byte[4096]; GC.KeepAlive(probe);
                bool allocationCounterAvailable = GC.GetAllocatedBytesForCurrentThread() - probeBefore >= 4096;
                long allocation = GC.GetAllocatedBytesForCurrentThread(); timer.Start();
                const int trials = 30;
                for (int i = 0; i < trials; i++) refresh(true);
                timer.Stop(); double after = timer.Elapsed.TotalMilliseconds / trials;
                long afterAllocation = (GC.GetAllocatedBytesForCurrentThread() - allocation) / trials;
                timer.Reset(); allocation = GC.GetAllocatedBytesForCurrentThread(); timer.Start();
                for (int point = 0; point <= 10; point++) foreach (var bite in new[] { InteractionMotionRole.BiteCloseLeft, InteractionMotionRole.BiteCloseRight })
                {
                    Vector3 position = rex.transform.position + Vector3.forward * (point * .5f);
                    Quaternion rotation = rex.transform.rotation;
                    foreach (var role in new[] { InteractionMotionRole.Roar, InteractionMotionRole.LookForwardTwo, bite, InteractionMotionRole.LookForwardTwo, InteractionMotionRole.Turn180Look })
                    {
                        var motion = profile.Find(role); body.bands = motion.bodyBands;
                        Vector3 origin = position; Quaternion heading = rotation;
                        float duration = role == InteractionMotionRole.LookForwardTwo ? 2f : motion.clip.length;
                        foreach (var recorded in motion.footFrames)
                        {
                            float time = Mathf.Min(duration, recorded.time);
                            var sample = DinosaurAgent.SampleFootFrame(motion, time);
                            Vector3 next = origin + heading * Quaternion.Euler(0f, rex.VisualYaw, 0f) * new Vector3(sample.rootOffset.x, 0f, sample.rootOffset.y);
                            Quaternion q = Quaternion.Euler(0f, sample.yaw, 0f) * heading;
                            step(position, next, rotation, q, body, sample.playerBodyBands);
                            position = next; rotation = q;
                            if (time >= duration) break;
                        }
                    }
                    var path = new NavMeshPath(); NavMesh.CalculatePath(position, rex.transform.position, NavMesh.AllAreas, path);
                }
                timer.Stop(); double before = timer.Elapsed.TotalMilliseconds;
                long beforeAllocation = GC.GetAllocatedBytesForCurrentThread() - allocation;
                string allocationReport = allocationCounterAvailable ? $"{beforeAllocation}B -> {afterAllocation}B" : "allocation counter unavailable on this Unity runtime";
                return $"Reference full-sequence batch: {before:F2}ms; lightweight refresh: {after:F3}ms; {allocationReport}; reference comparison, not recorded old-frame profiling.";
            }
            finally { UnityEngine.Object.DestroyImmediate(holder); }
        }
    }
}
