using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Media;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static partial class BaurusuchusContinuousAcceptance
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private const float Dt = 1f / 144f;
        internal const string Output = "Reports/BaurusuchusContinuousWalk";
        private static readonly StringBuilder Report = new StringBuilder();
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Set(DinosaurAgent agent, string field, object value) => typeof(DinosaurAgent).GetField(field, Flags).SetValue(agent, value);
        private static object Get(DinosaurAgent agent, string field) => typeof(DinosaurAgent).GetField(field, Flags).GetValue(agent);
        private static object Call(DinosaurAgent agent, string method, params object[] args) => typeof(DinosaurAgent).GetMethod(method, Flags).Invoke(agent, args);
        [MenuItem("Jurassic World/Acceptance/Repair Baurusuchus Continuous Walk (144 FPS)")]
        public static void Repair()
        {
            Directory.CreateDirectory(Output); Report.Clear();
            Report.AppendLine("# Baurusuchus pure walking loop / 144 FPS only\n");
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorSceneManager.GetActiveScene().isDirty,
                    "Stop Play and save existing scene edits first.");
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                var original = Object.FindObjectsOfType<DinosaurAgent>(true).Single(a => a.name == "Baurusuchus");
                var oldProfile = original.GaitProfile;
                float oldSpeed = original.WalkSpeed;
                float oldHeight = original.GetComponent<DinosaurFbxLoader>().ModelRoot.transform.localPosition.y;
                var source = BaurusuchusWalkingClip.Source;
                var clip = BaurusuchusWalkingClip.Crop(source);
                var profile = Object.Instantiate(oldProfile);
                profile.clip = clip;
                try
                {
                    VerifyCurves(source, clip);
                    BaurusuchusWalkingClip.CropContacts(profile);
                    // Select a turn rate without changing the accepted speed, height or leg curves.
                    float selected = 2f;
                    foreach (float rate in new[] { 2f, 4f, 6f, 8f, 12f, 16f, 24f, 30f })
                    {
                        var left = Simulate(original, profile, rate, false);
                        var right = Simulate(original, profile, -rate, false);
                        bool safe = left.error <= .01f && right.error <= .01f
                            && left.drift <= .01f && right.drift <= .01f && left.jump <= .08f && right.jump <= .08f;
                        Report.AppendLine($"Turn candidate {rate:F0} deg/s: left reach={left.error:F5}m right reach={right.error:F5}m {(safe ? "PASS" : "REJECTED")}.");
                        Debug.Log($"BAURUSUCHUS_TURN_144 {rate} left={left.error:F5} right={right.error:F5} safe={safe}");
                        File.WriteAllText(Output + "/Motion144.md", Report.ToString());
                        if (safe) selected = rate;
                    }
                    profile.turnSpeed = selected;
                    var straight = Simulate(original, profile, 0f, true);
                    var turning = Simulate(original, profile, selected, false);
                    Require(straight.error <= .02f && straight.drift <= .02f && straight.jump <= .08f
                        && turning.error <= .02f && turning.drift <= .02f && turning.jump <= .08f, "Motion acceptance exceeded limits.");
                    RecordBefore(original, source, oldProfile);
                    Report.AppendLine($"\nSelected turn rate {selected:F0} deg/s. Straight: drift={straight.drift:F5}m, reach={straight.error:F5}m, release={straight.jump:F5}m. Curved: drift={turning.drift:F5}m, reach={turning.error:F5}m.");
                    // Publish only after the cropped animation and all retained motion checks pass.
                    BaurusuchusSceneIntegration.EnsureAssets();
                    var saved = BaurusuchusWalkingClip.ApplyProfile(original);
                    saved.turnSpeed = selected; EditorUtility.SetDirty(saved);
                    Require(Mathf.Abs(original.WalkSpeed - oldSpeed) < .000001f, "Walking speed changed.");
                    Require(Mathf.Abs(original.GetComponent<DinosaurFbxLoader>().ModelRoot.transform.localPosition.y - oldHeight) < .000001f, "Model height changed.");
                    AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                    Report.AppendLine("\nPASS clip, pose, ten cycles, leg length, drift and saved scene. Main-scene runtime checks are recorded separately.");
                }
                finally { Object.DestroyImmediate(profile); Object.DestroyImmediate(clip); }
                File.WriteAllText(Output + "/Motion144.md", Report.ToString());
                Debug.Log("PASS BAURUSUCHUS_WALK_144");
            }
            catch (Exception error)
            {
                File.WriteAllText(Output + "/Motion144.md", Report + "\nFAILED " + error);
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }
        private static void VerifyCurves(AnimationClip source, AnimationClip clip)
        {
            Require(Mathf.Abs(clip.length - BaurusuchusWalkingClip.Duration) < .00001f, "Wrong trimmed length.");
            var bindings = AnimationUtility.GetCurveBindings(source);
            Require(bindings.Length == AnimationUtility.GetCurveBindings(clip).Length, "Animation channels were lost.");
            float maximum = 0f;
            foreach (var binding in bindings)
            {
                var a = AnimationUtility.GetEditorCurve(source, binding);
                var b = AnimationUtility.GetEditorCurve(clip, binding);
                Require(b != null, "Missing retained channel " + binding.path);
                for (int frame = 0; frame <= Mathf.CeilToInt(clip.length * 144f); frame++)
                {
                    float t = Mathf.Min(clip.length, frame * Dt);
                    maximum = Mathf.Max(maximum, Mathf.Abs(a.Evaluate(t + BaurusuchusWalkingClip.Start) - b.Evaluate(t)));
                }
            }
            Require(maximum <= .00001f, "Cropped curve differs from original: " + maximum);
            Report.AppendLine($"Source interval {BaurusuchusWalkingClip.Start:F3}..{BaurusuchusWalkingClip.End:F3}s; duration {clip.length:F3}s. All {bindings.Length} channels retained. Maximum component deviation {maximum:G5}.");
        }
        private struct Metrics { public float error, drift, jump; }
        private static Metrics Simulate(DinosaurAgent original, DinosaurGaitProfile profile, float yawRate, bool record, float seconds = -1f, string videoName = "after")
        {
            var root = Object.Instantiate(original.gameObject); root.name = "Baurusuchus";
            Camera camera = null; RenderTexture target = null; Texture2D pixels = null;
            GameObject floor = null, lightObject = null; Material material = null; MediaEncoder video = null;
            try
            {
                var agent = root.GetComponent<DinosaurAgent>(); agent.enabled = false;
                Set(agent, "gaitProfile", profile); Set(agent, "gaitMoving", true);
                Set(agent, "<CurrentState>k__BackingField", DinosaurAgent.DinosaurState.RandomWalk);
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var animation = root.GetComponentInChildren<Animation>(true);
                animation.Stop(); animation.AddClip(profile.clip, "Run"); animation.Play("Run");
                var state = animation["Run"]; state.speed = 0f; state.weight = 1f;
                var solver = root.GetComponent<DinosaurFootLock>() ?? root.AddComponent<DinosaurFootLock>();
                solver.enabled = false; solver.Bind(root.GetComponent<DinosaurFbxLoader>().ModelRoot.transform, agent, DinosaurGaitCalibration.FootNames("Baurusuchus"));
                var feet = profile.feet.Select(f => animation.transform.Find(f.path)).ToArray();
                var hips = profile.feet.Select(f => animation.transform.Find(f.hipPath)).ToArray();
                var knees = profile.feet.Select(f => animation.transform.Find(f.kneePath)).ToArray();
                var anchors = new Vector3[feet.Length]; var previous = new Vector3[feet.Length];
                var planted = new bool[feet.Length];
                var jaw = animation.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Bone.004");
                profile.clip.SampleAnimation(animation.gameObject, 0f); Quaternion closedJaw = jaw.localRotation;
                Metrics metrics = default;
                if (record)
                {
                    foreach (Transform node in root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 30;
                    floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.layer = 30; floor.transform.localScale = Vector3.one * 30f;
                    floor.transform.position = Vector3.down * .02f;
                    material = new Material(Shader.Find("Standard")); material.color = new Color(.25f, .3f, .32f);
                    floor.GetComponent<Renderer>().sharedMaterial = material;
                    lightObject = new GameObject("Walking light"); var light = lightObject.AddComponent<Light>();
                    light.type = LightType.Directional; light.cullingMask = 1 << 30; light.intensity = 1.1f; light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
                    camera = new GameObject("Walking camera").AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 1 << 30;
                    camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.18f, .22f, .27f); camera.fieldOfView = 38f;
                    target = new RenderTexture(640, 360, 24); camera.targetTexture = target; pixels = new Texture2D(640, 360, TextureFormat.RGBA32, false);
                    video = new MediaEncoder(Output + "/" + videoName + ".mp4", new VideoTrackAttributes { width = 640, height = 360, frameRate = new MediaRational(24), includeAlpha = false, bitRateMode = VideoBitrateMode.High });
                }
                int frames = Mathf.CeilToInt((seconds > 0f ? seconds : profile.clip.length * 10f) * 144f);
                for (int frame = 0; frame < frames; frame++)
                {
                    root.transform.rotation *= Quaternion.Euler(0f, yawRate * Dt, 0f);
                    root.transform.position += root.transform.forward * profile.groundSpeed * Dt;
                    state.time = Mathf.Repeat((frame + 1) * Dt, profile.clip.length); animation.Sample();
                    var lengths = feet.Select((foot, i) => new Vector2(Vector3.Distance(hips[i].position, knees[i].position), Vector3.Distance(knees[i].position, foot.position))).ToArray();
                    solver.UpdateGaitFeet(Dt);
                    if (profile.clip.length < 2f) Require(Quaternion.Angle(closedJaw, jaw.localRotation) < 4f, "Open-mouth gesture entered walking loop.");
                    for (int f = 0; f < feet.Length; f++)
                    {
                        Vector3 sole = feet[f].TransformPoint(profile.feet[f].soleOffset);
                        bool contact = solver.GaitPlanted(f);
                        if (contact && !planted[f]) anchors[f] = sole;
                        if (contact && planted[f]) metrics.drift = Mathf.Max(metrics.drift, Vector3.Distance(sole, anchors[f]));
                        if (!contact && planted[f]) metrics.jump = Mathf.Max(metrics.jump, Vector3.Distance(sole, previous[f]));
                        var solved = new Vector2(Vector3.Distance(hips[f].position, knees[f].position), Vector3.Distance(knees[f].position, feet[f].position));
                        Require(Vector2.Distance(lengths[f], solved) < .0001f, "IK stretched a retained leg segment.");
                        metrics.error = Mathf.Max(metrics.error, -sole.y);
                        planted[f] = contact; previous[f] = sole;
                    }
                    metrics.error = Mathf.Max(metrics.error, solver.GaitMaximumReachError);
                    if (video != null && frame % 6 == 0)
                    {
                        camera.transform.position = root.transform.position + new Vector3(4.8f, 1.9f, 4.8f); camera.transform.LookAt(root.transform.position + Vector3.up * .8f);
                        camera.Render(); var active = RenderTexture.active; RenderTexture.active = target;
                        pixels.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); pixels.Apply(); RenderTexture.active = active; video.AddFrame(pixels);
                        if (frame == 144) File.WriteAllBytes(Output + "/" + videoName + ".png", pixels.EncodeToPNG());
                    }
                }
                return metrics;
            }
            finally
            {
                video?.Dispose(); if (camera != null) Object.DestroyImmediate(camera.gameObject);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (pixels != null) Object.DestroyImmediate(pixels); if (floor != null) Object.DestroyImmediate(floor);
                if (material != null) Object.DestroyImmediate(material); if (lightObject != null) Object.DestroyImmediate(lightObject);
                Object.DestroyImmediate(root);
            }
        }
        private static void RecordBefore(DinosaurAgent original, AnimationClip source, DinosaurGaitProfile profile)
        {
            // Use the same accepted gait and camera; stop the capture at the same wall-clock duration.
            var sourceClip = Object.Instantiate(source); sourceClip.name = "Run";
            DinosaurGaitProfile before;
            if (profile.loopEnd > BaurusuchusWalkingClip.Duration + .01f) before = Object.Instantiate(profile);
            else
            {
                // Repeated acceptance must still capture the original complete gesture clip,
                // using its original-length contact map rather than stretching the cropped map.
                var probe = Object.Instantiate(original.gameObject); probe.name = "Baurusuchus";
                try
                {
                    var animation = probe.GetComponentInChildren<Animation>(true);
                    animation.Stop(); animation.RemoveClip("Run"); animation.AddClip(sourceClip, "Run");
                    before = DinosaurGaitCalibration.Bake(probe.GetComponent<DinosaurAgent>());
                }
                finally { Object.DestroyImmediate(probe); }
            }
            before.clip = sourceClip;
            try { Simulate(original, before, 0f, true, BaurusuchusWalkingClip.Duration * 10f, "before"); }
            finally { Object.DestroyImmediate(sourceClip); Object.DestroyImmediate(before); }
        }
    }
}
