using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class PteradactalAcceptance
    {
        private const string Output = "Tools/Pteradactal";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static bool exitWhenComplete = true;
        private static bool buildRequested;
        private static readonly System.Collections.Generic.List<(Vector3 position, Quaternion rotation)> openViews =
            new System.Collections.Generic.List<(Vector3, Quaternion)>();

        static PteradactalAcceptance() { EditorApplication.update += PollRequest; }

        // Share an already-open editor only after its current test and unsaved edits finish.
        private static void PollRequest()
        {
            const string request = "Logs/pteradactal-request.txt";
            if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty) return;
            string command = File.ReadAllText(request).Trim();
            File.Delete(request);
            exitWhenComplete = false;
            buildRequested = command == "verify-and-build";
            try { IntegrateAndVerify(); File.WriteAllText("Logs/pteradactal-request-status.txt", "PASS"); }
            catch (Exception e) { File.WriteAllText("Logs/pteradactal-request-status.txt", "FAIL\n" + e); }
            finally { exitWhenComplete = true; buildRequested = false; }
        }

        public static void BuildStandalone()
        {
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/Scenes/Main.unity" },
                locationPathName = "Builds/PteradactalAcceptance/JurassicWorld.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development });
            if (result.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("Pteradactal verification build failed: " + result.summary.result);
            Debug.Log("PTERADACTAL_BUILD_PASS");
        }

        public static void IntegrateAndVerify()
        {
            try
            {
                VerifyConnections();
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                var roots = EditorSceneManager.GetActiveScene().GetRootGameObjects()
                    .Where(r => r.GetComponent<PteradactalFlightController>() == null)
                    .Select(r => (root: r, position: r.transform.position, rotation: r.transform.rotation, scale: r.transform.localScale)).ToArray();
                int groundCount = Object.FindObjectsOfType<DinosaurAgent>().Length;
                PteradactalSceneIntegration.AddToMainScene();
                PteradactalSceneIntegration.AddToMainScene();
                Require(roots.All(r => r.root != null && r.root.transform.position == r.position && r.root.transform.rotation == r.rotation && r.root.transform.localScale == r.scale), "Existing roots removed or moved.");
                Require(Object.FindObjectsOfType<DinosaurAgent>().Length == groundCount, "Ground dinosaur count changed.");
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                var controller = Object.FindObjectsOfType<PteradactalFlightController>().Single();
                Require(controller.GetComponent<DinosaurAgent>() == null, "Airborne dinosaur uses ground agent.");
                var animation = controller.GetComponentInChildren<Animation>();
                Require(animation.clip != null && animation.clip.legacy && animation.clip.length > 10f, "Flight animation missing/truncated.");
                Require(animation.clip.wrapMode == WrapMode.Loop, "Flight is not looping.");
                foreach (var renderer in controller.GetComponentsInChildren<Renderer>())
                    Require(renderer.sharedMaterial != null && renderer.sharedMaterial.shader.isSupported && renderer.sharedMaterial.mainTexture != null, "Missing material/texture or unsupported shader.");
                Require(!ShaderUtil.ShaderHasError(controller.GetComponentInChildren<Renderer>().sharedMaterial.shader), "Shader compile error.");
                Bounds b = PteradactalSceneIntegration.SampleBounds(animation.gameObject, animation.clip);
                Require(Mathf.Abs(b.size.x - 5f) < .15f, "Wingspan is not five meters: " + b.size);
                float seam = CheckAnimation(animation);
                CaptureModel(controller, 0f, "model-start.png");
                CaptureModel(controller, animation.clip.length * .25f, "model-quarter.png");

                string viewReport = VerifyLevelViews(controller);
                controller.PrepareFlight(controller.Airspace, Camera.main);
                bool initialPassAvailable = controller.Mode == PteradactalFlightController.FlightMode.FrontPass;
                float distance = 0f, maxXZ = 0f, minY = float.MaxValue, maxY = float.MinValue;
                var advance = typeof(PteradactalFlightController).GetMethod("AdvanceFlight", Private);
                var skin = controller.GetComponentInChildren<SkinnedMeshRenderer>();
                var savedEye = Camera.main.transform.position;
                var savedLook = Camera.main.transform.rotation;
                var baked = new Mesh();
                float lowest = float.MaxValue;
                try
                {
                    for (int step = 0; step < 15000; step++)
                    {
                        if (step == 1000 || step == 5000 || step == 12000)
                            Camera.main.transform.SetPositionAndRotation(openViews[0].position, openViews[0].rotation);
                        if (step == 3000) Camera.main.transform.rotation *= Quaternion.Euler(0, 180, 0);
                        if (step == 7000) Camera.main.transform.SetPositionAndRotation(openViews[1].position, openViews[1].rotation);
                        if (step == 10000) Camera.main.transform.SetPositionAndRotation(savedEye, savedLook);
                        Vector3 before = controller.transform.position;
                        Quaternion rotation = controller.transform.rotation;
                        advance.Invoke(controller, new object[] { .02f });
                        Vector3 p = controller.transform.position;
                        Require(float.IsNaN(p.x) == false, "Non-finite movement.");
                        Require(Quaternion.Angle(rotation, controller.transform.rotation) <= .91f, "Sudden turn.");
                        Require(Mathf.Abs(p.y - before.y) <= .041f, "Vertical speed exceeded.");
                        distance += Vector3.Distance(before, p);
                        maxXZ = Mathf.Max(maxXZ, Mathf.Abs(p.x), Mathf.Abs(p.z));
                        minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                        Require(maxXZ <= PteradactalFlightController.HorizontalLimit, "Flight escaped map bounds.");
                        Require(controller.HeightAboveGround >= 6f && controller.HeightAboveGround <= 10f, "Flight is not low AGL.");
                        Require(controller.Airspace.IsClear(p, controller.Airspace.bodyRadius + PteradactalAirspace.Margin), "Local mesh clearance violated.");
                        if (step == 299) CapturePlayerView(controller);
                        if (step % 50 == 0)
                        {
                            animation.clip.SampleAnimation(animation.gameObject, (step * .02f) % animation.clip.length);
                            skin.BakeMesh(baked, true);
                            foreach (var vertex in baked.vertices) lowest = Mathf.Min(lowest, skin.transform.TransformPoint(vertex).y);
                        }
                    }
                }
                finally { Object.DestroyImmediate(baked); }
                Require(distance > 1400f, "Flight lacks continuous movement.");
                Require(!initialPassAvailable || controller.PassCount >= 1, "No front pass completed.");
                Require(controller.PassCount >= 2, "Flybys did not resume after the player entered open areas.");
                string report = $"# Pteradactal acceptance\n\nPASS\n\n- Import and scene integration repeated twice; reload contains exactly one flyer.\n- Ground dinosaurs preserved: {groundCount}.\n- Clip: {animation.clip.length:F4}s, Legacy, loop.\n- Animated bounds: {b.size}; start/end maximum vertex difference: {seam:F4}m.\n- Five simulated minutes, 50Hz: travelled {distance:F2}m; maximum absolute X/Z {maxXZ:F2}m.\n- Height: {minY:F2}..{maxY:F2}m; local mesh clearance passed; lowest sampled wing/body vertex {lowest:F2}m; front passes {controller.PassCount}.\n- Shader compiles and textures resolve.\n- Captures: model-start.png, model-quarter.png, player-view.png.\n";
                File.WriteAllText(Output + "/Acceptance.md", report);
                File.AppendAllText(Output + "/Acceptance.md", "\n## Level camera flybys\n\n" + viewReport);
                // Reload discards simulation and temporary animation sampling; saved spawn stays intact.
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                Debug.Log("PTERADACTAL_ACCEPTANCE_PASS\n" + report);
                if (buildRequested || Environment.GetCommandLineArgs().Contains("-pteradactal-build")) BuildStandalone();
                if (Application.isBatchMode && exitWhenComplete) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                File.WriteAllText(Output + "/Acceptance.md", "FAIL\n" + e);
                Debug.LogException(e);
                if (Application.isBatchMode && exitWhenComplete) EditorApplication.Exit(1);
                else throw;
            }
        }

        private static void VerifyConnections()
        {
            var random = new System.Random(611);
            for (int i = 0; i < 100; i++)
            {
                Vector2 start = new Vector2((float)random.NextDouble() * 50, (float)random.NextDouble() * 50);
                Vector2 end = new Vector2((float)random.NextDouble() * 50, (float)random.NextDouble() * 50);
                float a = (float)random.NextDouble() * 6.28f, b = (float)random.NextDouble() * 6.28f;
                foreach (var path in PteradactalFlightPath.Connections(start, a, 7f, end, b, 7f, 7f))
                {
                    path.Sample(path.Length, out var p, out float heading, out _);
                    Require(Vector2.Distance(p, end) < .002f && Mathf.Abs(Mathf.DeltaAngle(heading * Mathf.Rad2Deg, b * Mathf.Rad2Deg)) < .01f,
                        "Curved connection does not meet its endpoint/tangent.");
                }
            }
        }

        private static string VerifyLevelViews(PteradactalFlightController controller)
        {
            Camera camera = Camera.main;
            Vector3 originalPosition = camera.transform.position;
            Quaternion originalRotation = camera.transform.rotation;
            string report = ""; int found = 0; openViews.Clear();
            var accepted = new System.Collections.Generic.List<Vector3>();
            try
            {
                camera.transform.rotation = Quaternion.Euler(0f, originalRotation.eulerAngles.y, 0f);
                controller.PrepareFlight(controller.Airspace, camera);
                bool spawnSafe = controller.Mode == PteradactalFlightController.FlightMode.FrontPass;
                report += spawnSafe ? "- Saved spawn has a safe low front pass.\n" : "- Saved camera faces dense forest: no safe 30–45m low flyby fits; defer instead of flying through trees.\n";
                foreach (var orbit in controller.Airspace.orbits)
                {
                    foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
                    {
                        Vector3 forward = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                        Vector3 eye = new Vector3(orbit.center.x, 0, orbit.center.y) - forward * 35f;
                        if (Mathf.Abs(eye.x) > 70f || Mathf.Abs(eye.z) > 70f || accepted.Any(p => Vector3.Distance(p, eye) < 20f)) continue;
                        eye.y = controller.Airspace.Ground(eye.x, eye.z) + 1.7f;
                        if (controller.Airspace.Clearance(eye) < .3f) continue;
                        camera.transform.SetPositionAndRotation(eye, Quaternion.Euler(0, yaw, 0));
                        controller.PrepareFlight(controller.Airspace, camera);
                        if (controller.Mode != PteradactalFlightController.FlightMode.FrontPass) continue;
                        float consecutive = 0f, longest = 0f, first = -1f;
                        var advance = typeof(PteradactalFlightController).GetMethod("AdvanceFlight", Private);
                        for (int step = 0; step < 750; step++)
                        {
                            advance.Invoke(controller, new object[] { .02f });
                            Vector3 view = camera.WorldToViewportPoint(controller.transform.position);
                            bool visible = view.z > 0f && view.x > .05f && view.x < .95f && view.y > .05f && view.y < .95f &&
                                controller.Airspace.Sightline(camera.transform.position, controller.transform.position);
                            consecutive = visible ? consecutive + .02f : 0f;
                            if (visible && first < 0f) first = (step + 1) * .02f;
                            if (consecutive > longest) longest = consecutive;
                            if (consecutive >= 3f) { Capture(camera, $"low-pass-{found + 1}.png"); break; }
                        }
                        if (longest < 3f || first > 15f) continue;
                        accepted.Add(eye); found++;
                        openViews.Add((eye, camera.transform.rotation));
                        report += $"- Open view {found}: camera={eye}, yaw={yaw:F0}°, pitch=0°; first visible={first:F2}s; continuous clear visibility={longest:F2}s.\n";
                        if (found == 2) break;
                    }
                    if (found == 2) break;
                }
                Require(found == 2, "Could not demonstrate two unobstructed level-camera flybys.");
            }
            finally { camera.transform.SetPositionAndRotation(originalPosition, originalRotation); }
            return report;
        }

        private static float CheckAnimation(Animation animation)
        {
            var skin = animation.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh = new Mesh();
            try
            {
                animation.clip.SampleAnimation(animation.gameObject, 0f); skin.BakeMesh(mesh, true);
                Vector3[] first = mesh.vertices;
                // Sample just before the end: sampling exactly length on a looping clip wraps to zero.
                animation.clip.SampleAnimation(animation.gameObject, animation.clip.length - 1f / 120f); skin.BakeMesh(mesh, true);
                Vector3[] last = mesh.vertices;
                float seam = 0f;
                for (int i = 0; i < first.Length; i++) seam = Mathf.Max(seam, skin.transform.TransformVector(first[i] - last[i]).magnitude);
                animation.clip.SampleAnimation(animation.gameObject, animation.clip.length * .25f); skin.BakeMesh(mesh, true);
                float change = 0f; var quarter = mesh.vertices;
                for (int i = 0; i < first.Length; i++) change = Mathf.Max(change, skin.transform.TransformVector(first[i] - quarter[i]).magnitude);
                Require(change > .1f, "Animation does not deform wings.");
                Require(seam < .15f, "Animation loop has a visible jump: " + seam);
                return seam;
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        private static void CaptureModel(PteradactalFlightController controller, float time, string file)
        {
            var animation = controller.GetComponentInChildren<Animation>();
            animation.clip.SampleAnimation(animation.gameObject, time);
            var go = new GameObject("Pteradactal QA Camera");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.23f, .36f, .47f);
                camera.transform.position = controller.transform.position + controller.transform.rotation * new Vector3(5f, 2f, 6f);
                camera.transform.LookAt(controller.transform.position);
                camera.fieldOfView = 45f;
                Capture(camera, file);
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static void CapturePlayerView(PteradactalFlightController controller)
        {
            var camera = Camera.main;
            Quaternion original = camera.transform.rotation;
            try { camera.transform.rotation = Quaternion.Euler(0f, original.eulerAngles.y, 0f); Capture(camera, "player-view.png"); }
            finally { camera.transform.rotation = original; }
        }

        private static void Capture(Camera camera, string name)
        {
            var rt = new RenderTexture(1280, 720, 24);
            var previous = RenderTexture.active; var previousTarget = camera.targetTexture;
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
                File.WriteAllBytes(Output + "/" + name, texture.EncodeToPNG());
            }
            finally { camera.targetTexture = previousTarget; RenderTexture.active = previous; Object.DestroyImmediate(rt); Object.DestroyImmediate(texture); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
