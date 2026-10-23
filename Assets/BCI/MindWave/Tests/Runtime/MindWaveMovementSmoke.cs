using System;
using System.IO;
using System.Reflection;
using JurassicWorld.PlayerInputs;
using UnityEngine;

namespace JurassicWorld.BCI.MindWave.Tests
{
    // Inactive in normal play/builds. Exercises the real main-scene player with the external -Auto simulator.
    public sealed class MindWaveMovementSmoke : MonoBehaviour
    {
        private float began, zeroSince = -1;
        private Vector3 previousPosition;
        private bool half, full, moving, stopped, failed;
        private string report;
        private Camera player;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-mindwaveSmoke") < 0) return;
            var test = new GameObject("MindWave movement acceptance").AddComponent<MindWaveMovementSmoke>();
            test.began = Time.realtimeSinceStartup;
            int pathIndex = Array.IndexOf(args, "-mindwaveSmokeReport");
            test.report = pathIndex >= 0 && pathIndex + 1 < args.Length ? args[pathIndex + 1] : null;
            test.player = Camera.main;
            if (test.player != null) test.previousPosition = test.player.transform.position;
        }

        private void Update()
        {
            float elapsed = Time.realtimeSinceStartup - began;
            if (player == null || !PlayerMovementInput.HasProvider) { Finish(false, "Missing player/provider"); return; }
            foreach (var behaviour in player.GetComponents<MonoBehaviour>())
            {
                if (behaviour.GetType().Name != "EmotivPlayerInput") continue;
                var running = behaviour.GetType().GetField("running", BindingFlags.Instance | BindingFlags.NonPublic);
                if (running != null && (bool)running.GetValue(behaviour)) { Finish(false, "EMOTIV also started"); return; }
            }
            float input = PlayerMovementInput.ReadMovement().y;
            Vector3 position = player.transform.position;
            float speed = new Vector2(position.x - previousPosition.x, position.z - previousPosition.z).magnitude
                / Mathf.Max(Time.deltaTime, .00001f);
            previousPosition = position;
            half |= Mathf.Abs(input - .5f) < .001f;
            full |= Mathf.Abs(input - 1f) < .001f;
            moving |= input >= .5f && speed > .5f;
            if (input == 0)
            {
                if (zeroSince < 0) zeroSince = Time.realtimeSinceStartup;
                stopped |= moving && Time.realtimeSinceStartup - zeroSince > 1.2f && speed < .1f;
            }
            else zeroSince = -1;
            if (half && full && moving && stopped) Finish(true, "Half/full input, actual movement, smooth stop, EMOTIV suppressed");
            else if (elapsed > 45) Finish(false, "Timeout: half=" + half + " full=" + full + " moving=" + moving + " stopped=" + stopped);
        }

        private void Finish(bool passed, string detail)
        {
            if (failed) return;
            failed = true;
            string result = "[MindWaveMovementSmoke] " + (passed ? "PASS " : "FAIL ") + detail;
            if (report != null) File.WriteAllText(report, result + "\n");
            Debug.Log(result);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(passed ? 0 : 1);
#else
            Application.Quit(passed ? 0 : 1);
#endif
        }
    }
}
