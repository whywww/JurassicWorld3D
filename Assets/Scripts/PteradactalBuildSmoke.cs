#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace JurassicWorld
{
    // Opt-in development-build acceptance. Normal launches never create this component.
    public sealed class PteradactalBuildSmoke : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
            if (Environment.GetCommandLineArgs().Contains("-pteradactal-smoke"))
                new GameObject("Pteradactal Build Smoke").AddComponent<PteradactalBuildSmoke>();
        }

        private IEnumerator Start()
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../Tools/Pteradactal/BuildSmoke.txt"));
            Time.timeScale = 10f;
            yield return null;
            var flyers = FindObjectsOfType<PteradactalFlightController>();
            if (flyers.Length != 1) { Finish(output, "FAIL: expected one flyer", 1); yield break; }
            var flyer = flyers[0];
            var camera = Camera.main;
            var explorer = camera.GetComponent<SmoothExplorerCamera>();
            if (explorer != null) explorer.enabled = false;
            Vector3 originalEye = camera.transform.position;
            Quaternion originalLook = camera.transform.rotation;
            var animation = flyer.GetComponentInChildren<Animation>();
            if (animation == null || !animation.isPlaying) { Finish(output, "FAIL: flight animation is not playing", 1); yield break; }
            float start = Time.time, distance = 0f, maxXZ = 0f, lastClipTime = animation[animation.clip.name].time;
            int wraps = 0; bool captured = false;
            float minHeight = float.MaxValue, maxHeight = float.MinValue;
            var previous = flyer.transform.position;
            while (Time.time - start < 300f)
            {
                float elapsed = Time.time - start;
                if (elapsed > 20f && elapsed < 80f)
                    camera.transform.SetPositionAndRotation(new Vector3(7f, 1.7f, -16f), Quaternion.Euler(0, 270f, 0));
                else if (elapsed >= 80f && elapsed < 110f) camera.transform.rotation = Quaternion.Euler(0, 90f, 0);
                else if (elapsed >= 110f && elapsed < 200f)
                    camera.transform.SetPositionAndRotation(new Vector3(-20f, 1.7f, 23f), Quaternion.Euler(0, 180f, 0));
                else if (elapsed >= 200f) camera.transform.SetPositionAndRotation(originalEye, originalLook);
                var p = flyer.transform.position;
                distance += Vector3.Distance(previous, p); previous = p;
                maxXZ = Mathf.Max(maxXZ, Mathf.Abs(p.x), Mathf.Abs(p.z));
                float clipTime = animation[animation.clip.name].time;
                if (clipTime < lastClipTime) wraps++;
                else wraps += Mathf.FloorToInt(clipTime / animation.clip.length) - Mathf.FloorToInt(lastClipTime / animation.clip.length);
                lastClipTime = clipTime;
                float height = flyer.HeightAboveGround;
                minHeight = Mathf.Min(minHeight, height); maxHeight = Mathf.Max(maxHeight, height);
                if (!animation.isPlaying || maxXZ > PteradactalFlightController.HorizontalLimit ||
                    height < flyer.MinimumHeight - .01f || height > flyer.MaximumHeight + .01f ||
                    !flyer.Airspace.IsClear(p, flyer.Airspace.bodyRadius + PteradactalAirspace.Margin))
                { Finish(output, "FAIL: animation or bounds at " + p, 1); yield break; }
                if (!captured && flyer.PassCount > 0)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(output), "low-runtime-pass.png"));
                    captured = true;
                }
                yield return null;
            }
            if (distance < 1400f || wraps < 20 || flyer.PassCount < 1) { Finish(output, "FAIL: insufficient movement, flybys or animation cycles", 1); yield break; }
            Finish(output, $"PASS: standalone build, 300 game seconds; distance={distance:F2}m; maxXZ={maxXZ:F2}m; AGL={minHeight:F2}..{maxHeight:F2}m; local geometry clearance passed; front passes={flyer.PassCount}; animation playing; observed wraps={wraps}", 0);
        }

        private static void Finish(string output, string text, int code)
        {
            File.WriteAllText(output, text);
            Debug.Log("PTERADACTAL_BUILD_SMOKE: " + text);
            Application.Quit(code);
        }
    }
}
#endif
