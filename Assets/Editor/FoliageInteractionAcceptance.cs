using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class FoliageInteractionAcceptance
    {
        private const string Running = "JurassicWorld.FoliageAcceptance.Running";
        private const string Output = "Logs/FoliageInteraction";
        private const int Frames = 240, Warmup = 60;
        private static Camera camera;
        private static RenderTexture target;
        private static Terrain terrain;
        private static TerrainData preparedData, baselineData;
        private static InteractiveFoliage[] trees;
        private static Mesh[][] preparedMeshes;
        private static Material[][][] preparedMaterials;
        private static FoliageInteractionSystem system;
        private static DinosaurFoliageInteractor source;
        private static Transform capsuleBone;
        private static Vector3 capsuleMiddle, basePosition, direction, leafPoint;
        private static readonly List<double>[] Costs = { new List<double>(), new List<double>(), new List<double>() };
        private static readonly List<double> UpdateCosts = new List<double>();
        private static Recorder updateRecorder;
        private static int phase, frame, lastFrame;
        private static double deadline;
        private static bool contacted;
        private static readonly List<string> Report = new List<string>();

        static FoliageInteractionAcceptance() { EditorApplication.playModeStateChanged += StateChanged; }
        [MenuItem("Jurassic World/Acceptance/Foliage Interaction — Verify and Record")]
        private static void MenuRun() => JurassicMenuGuard.Run(VerifyAndRun, true);
        [MenuItem("Jurassic World/Acceptance/Foliage Interaction — Verify and Record", true)]
        private static bool MenuEnabled() => JurassicMenuGuard.CanRun;

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Foliage acceptance: " + message); }

        public static void VerifyAndRun()
        {
            Directory.CreateDirectory(Output + "/Frames"); Report.Clear();
            RegressionChecks();
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            FoliageInteractionPreparation.RestoreCurrent();
            FoliageInteractionPreparation.PrepareCurrent();
            FoliageInteractionPreparation.ValidateCurrent();
            var ledger = Object.FindObjectOfType<FoliageTerrainConversion>();
            string retained = ledger.retainedSignature;
            int total = ledger.originalTreeCount, converted = ledger.removed.Length;
            TerrainTreeObstacleRegistry.Invalidate();
            int obstacleCount = TerrainTreeObstacleRegistry.ObstacleCount;
            bool[] before = ObstacleSamples();
            FoliageInteractionPreparation.RestoreCurrent();
            Require(Terrain.activeTerrain.terrainData.treeInstanceCount == total, "restoration tree count");
            string original = FoliageInteractionPreparation.TreeSignature(Terrain.activeTerrain.terrainData.treeInstances, Terrain.activeTerrain.terrainData.treePrototypes);
            Require(TerrainTreeObstacleRegistry.ObstacleCount == obstacleCount && before.SequenceEqual(ObstacleSamples()), "restoration changed trunk blocking");
            VerifyTerrainConversion();
            FoliageInteractionPreparation.PrepareCurrent();
            ledger = Object.FindObjectOfType<FoliageTerrainConversion>();
            Require(ledger.retainedSignature == retained && ledger.removed.Length == converted, "conversion repeat changed layout");
            Require(TerrainTreeObstacleRegistry.ObstacleCount == obstacleCount && before.SequenceEqual(ObstacleSamples()), "conversion changed trunk blocking");
            int interactiveCount = Object.FindObjectsOfType<InteractiveFoliage>().Length;
            FoliageInteractionPreparation.PrepareCurrent();
            Require(Object.FindObjectsOfType<InteractiveFoliage>().Length == interactiveCount, "duplicate preparation");
            Require(FoliageInteractionPreparation.TreeSignature(Reconstruct(ledger), ledger.terrain.terrainData.treePrototypes) == original,
                "tree order, pose, scale, prototype or colour drifted");
            foreach (var tree in Object.FindObjectsOfType<InteractiveFoliage>())
                foreach (Renderer renderer in tree.renderers)
                {
                    Require((GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & StaticEditorFlags.BatchingStatic) == 0, "static batching still enabled");
                    var mask = new List<Vector2>(); ((MeshRenderer)renderer).additionalVertexStreams.GetUVs(3, mask);
                    Require(mask.Count == renderer.GetComponent<MeshFilter>().sharedMesh.vertexCount, "missing attachment mask");
                    Mesh stream = ((MeshRenderer)renderer).additionalVertexStreams;
                    Require(!stream.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal) &&
                        !stream.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent) &&
                        !stream.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color), "flexibility stream overrides original shading attributes");
                }
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
            Report.Add($"Conversion: {total} Terrain trees before; {converted} converted; {interactiveCount} interactive trees total.");
            Report.Add("PASS: sweep middle/endpoints, height rejection, teleport history, spring limits/settling, repeat preparation, exact tree restoration and trunk query equivalence.");
            File.WriteAllLines(Output + "/EditVerification.txt", Report);
            SessionState.SetBool(Running, true);
            EditorApplication.isPlaying = true;
        }

        private static TreeInstance[] Reconstruct(FoliageTerrainConversion ledger)
        {
            var result = new TreeInstance[ledger.originalTreeCount];
            var removed = ledger.removed.ToDictionary(t => t.originalIndex);
            TreeInstance[] retained = ledger.terrain.terrainData.treeInstances; int next = 0;
            for (int i = 0; i < result.Length; i++) result[i] = removed.TryGetValue(i, out var tree) ? tree.instance : retained[next++];
            return result;
        }
        private static bool[] ObstacleSamples()
        {
            var values = new List<bool>(); var bodies = Object.FindObjectsOfType<DinosaurCoreBody>();
            for (int x = -75; x <= 75; x += 3) for (int z = -75; z <= 90; z += 3)
            {
                var point = new Vector2(x, z); values.Add(TerrainTreeObstacleRegistry.IsBlocked(point, .5f));
                foreach (var body in bodies) values.Add(TerrainTreeObstacleRegistry.IsCoreBlocked(point, Vector2.up, body));
            }
            return values.ToArray();
        }

        private static void VerifyTerrainConversion()
        {
            Terrain terrain = Terrain.activeTerrain; TerrainData original = terrain.terrainData;
            var temporary = Object.Instantiate(original);
            TreeInstance[] all = temporary.treeInstances;
            var route = MovementCorridor.GetSample(0, 10);
            var probe = new TreeInstance
            {
                prototypeIndex = 0, widthScale = .7f, heightScale = .7f, rotation = .73f,
                color = new Color32(93, 172, 128, 255), lightmapColor = new Color32(211, 195, 171, 255),
                position = new Vector3((route.Position.x - terrain.transform.position.x) / temporary.size.x,
                    -terrain.transform.position.y / temporary.size.y, (route.Position.y - terrain.transform.position.z) / temporary.size.z)
            };
            var expanded = all.Concat(new[] { probe }).ToArray(); temporary.SetTreeInstances(expanded, false);
            string signature = FoliageInteractionPreparation.TreeSignature(expanded, temporary.treePrototypes);
            terrain.terrainData = temporary; TerrainTreeObstacleRegistry.Invalidate();
            int count = TerrainTreeObstacleRegistry.ObstacleCount; bool[] blocking = ObstacleSamples();
            try
            {
                FoliageInteractionPreparation.PrepareCurrent();
                var ledger = Object.FindObjectOfType<FoliageTerrainConversion>();
                Require(ledger.removed.Any(t => t.originalIndex == all.Length), "reachable Terrain tree was not converted");
                Require(count == TerrainTreeObstacleRegistry.ObstacleCount && blocking.SequenceEqual(ObstacleSamples()), "converted Terrain trunk differs");
                FoliageInteractionPreparation.RestoreCurrent();
                Require(signature == FoliageInteractionPreparation.TreeSignature(temporary.treeInstances, temporary.treePrototypes),
                    "Terrain tree prototype/rotation/scale/colour/order restoration");
            }
            finally
            {
                if (Object.FindObjectOfType<FoliageTerrainConversion>() != null) FoliageInteractionPreparation.RestoreCurrent();
                terrain.terrainData = original; Object.DestroyImmediate(temporary); TerrainTreeObstacleRegistry.Invalidate();
            }
        }

        internal static void RegressionChecks()
        {
            var sweep = new FoliageContact { previousStart = new Vector3(-1, 0, -2), previousEnd = new Vector3(1, 0, -2),
                start = new Vector3(-1, 0, 2), end = new Vector3(1, 0, 2), radius = .15f, speed = 2f, valid = true };
            Require(sweep.Push(new Vector3(0, .05f, 0), 0).sqrMagnitude > 0f, "missed middle of swept capsule");
            Require(sweep.Push(new Vector3(0, 5f, 0), 0) == Vector3.zero, "height-separated foliage touched");
            var degenerate = new FoliageContact { start = Vector3.zero, end = Vector3.zero, previousStart = Vector3.zero,
                previousEnd = Vector3.zero, radius = .5f, valid = true };
            Require(degenerate.Push(Vector3.zero, 0).sqrMagnitude > 0f, "stationary/zero-length capsule");
            var holder = new GameObject("Foliage regression objects") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var interactor = holder.AddComponent<DinosaurFoliageInteractor>();
                interactor.capsules = new[] { new DinosaurFoliageInteractor.Capsule { bone = holder.transform, radius = 1f } };
                interactor.SamplePose(1f / 60f); Require(!interactor.contacts[0].valid, "birth sweep");
                holder.transform.position = Vector3.right; interactor.SamplePose(1f / 60f); Require(interactor.contacts[0].valid, "normal motion not sampled");
                holder.transform.position = Vector3.right * 20f; interactor.SamplePose(1f / 60f); Require(!interactor.contacts[0].valid, "teleport sweep");
                holder.transform.position = Vector3.zero;
                var settings = holder.AddComponent<FoliageInteractionSystem>();
                var boundsObject = new GameObject("Rotated tree bounds") { hideFlags = HideFlags.HideAndDontSave };
                boundsObject.transform.SetParent(holder.transform, false);
                boundsObject.transform.SetPositionAndRotation(new Vector3(17, 2, -11), Quaternion.Euler(12, 63, -8));
                boundsObject.transform.localScale = new Vector3(2f, .7f, 3f);
                var boundsTree = boundsObject.AddComponent<InteractiveFoliage>();
                boundsTree.localBounds = new Bounds(new Vector3(3, 4, 1), new Vector3(4, 12, 6));
                boundsTree.PrepareRuntime();
                for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                {
                    Vector3 corner = new Vector3(x == 0 ? boundsTree.localBounds.min.x : boundsTree.localBounds.max.x,
                        y == 0 ? boundsTree.localBounds.min.y : boundsTree.localBounds.max.y,
                        z == 0 ? boundsTree.localBounds.min.z : boundsTree.localBounds.max.z);
                    Require(boundsTree.WorldBounds.Contains(boundsTree.transform.TransformPoint(corner)), "spatial index lost tree crown bounds");
                }
                var stemObject = new GameObject("Trunk contact regression") { hideFlags = HideFlags.HideAndDontSave };
                stemObject.transform.SetParent(holder.transform, false);
                var stem = stemObject.AddComponent<SceneTreeObstacle>();
                stem.trunkSections = new[] { new TreeTrunkSection { center = Vector2.zero, bottom = 0f, top = 3f, radius = .5f } };
                var crown = stemObject.AddComponent<InteractiveFoliage>();
                crown.localBounds = new Bounds(new Vector3(0, 6, 0), Vector3.one * 2f);
                crown.PrepareRuntime();
                settings.Register(crown);
                Require(crown.WorldBounds.Contains(Vector3.up), "trunk excluded from crown spatial index");
                var stemContact = new FoliageContact { start = new Vector3(.8f, 1f, 0), end = new Vector3(.8f, 2f, 0),
                    previousStart = new Vector3(1f, 1f, 0), previousEnd = new Vector3(1f, 2f, 0), radius = .4f, speed = 2f, valid = true };
                for (int i = 0; i < 60; i++) { crown.ClearForces(); Require(crown.Accumulate(stemContact), "stem contact missed"); crown.Simulate(1f / 60f, settings); }
                Require(Peak(crown) > .01f && Peak(crown) <= .4501f, "stem did not transfer force to separated crown");
                for (int i = 0; i < 120; i++) { crown.ClearForces(); crown.Simulate(1f / 60f, settings); }
                Require(Peak(crown) < .005f, "stem-driven crown did not settle");
                stemContact.start += Vector3.right * 4f; stemContact.end += Vector3.right * 4f;
                stemContact.previousStart += Vector3.right * 4f; stemContact.previousEnd += Vector3.right * 4f;
                crown.ClearForces(); Require(!crown.Accumulate(stemContact), "distant body moved crown");
                foreach (int rate in new[] { 30, 60, 144 })
                {
                    var treeObject = new GameObject("Spring " + rate) { hideFlags = HideFlags.HideAndDontSave };
                    treeObject.transform.SetParent(holder.transform, false);
                    var tree = treeObject.AddComponent<InteractiveFoliage>(); tree.localBounds = new Bounds(Vector3.zero, Vector3.one * 4f);
                    for (int i = 0; i < rate; i++) { tree.ClearForces(); tree.Accumulate(degenerate); tree.Simulate(1f / rate, settings); }
                    Require(Peak(tree) > .01f && Peak(tree) <= .4501f, "spring displacement at " + rate + " FPS");
                    for (int i = 0; i < rate * 2; i++) { tree.ClearForces(); tree.Simulate(1f / rate, settings); }
                    Require(Peak(tree) < .005f, "spring did not settle in two seconds at " + rate + " FPS");
                    tree.Accumulate(degenerate); tree.Accumulate(degenerate); tree.Simulate(.5f, settings);
                    Require(Peak(tree) <= .4501f, "multi-source/stalled frame overshoot");
                    tree.ResetField(); Require(Peak(tree) == 0f, "disable reset");
                    // Warm first-use allocations before checking persistent simulation allocations.
                    for (int i = 0; i < 5; i++) { tree.ClearForces(); tree.Accumulate(degenerate); tree.Simulate(1f / rate, settings); }
                    long allocated = GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 0; i < 120; i++) { tree.ClearForces(); tree.Accumulate(degenerate); tree.Simulate(1f / rate, settings); }
                    allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                    Require(allocated == 0, "persistent tree simulation allocated " + allocated + " bytes");
                }
            }
            finally { Object.DestroyImmediate(holder); }
        }
        private static float Peak(InteractiveFoliage tree)
        {
            float maximum = 0f;
            if (tree.displacement != null) foreach (Vector3 d in tree.displacement) maximum = Mathf.Max(maximum, d.magnitude);
            return maximum;
        }

        private static void StateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Running, false) || state != PlayModeStateChange.EnteredPlayMode) return;
            try
            {
                Report.Clear(); Report.AddRange(File.ReadAllLines(Output + "/EditVerification.txt"));
                foreach (var controller in Object.FindObjectsOfType<SmoothExplorerCamera>()) controller.enabled = false;
                foreach (var agent in Object.FindObjectsOfType<DinosaurAgent>()) agent.enabled = false;
                foreach (var foot in Object.FindObjectsOfType<DinosaurFootLock>()) foot.enabled = false;
                foreach (var animation in Object.FindObjectsOfType<Animation>()) animation.Stop();
                source = Object.FindObjectsOfType<DinosaurFoliageInteractor>().First(s => s.name.Contains("Velociraptor"));
                foreach (var interactor in Object.FindObjectsOfType<DinosaurFoliageInteractor>()) interactor.enabled = false;
                source.enabled = true;
                var capsule = source.capsules.Last(c => c.bone.name.IndexOf("tail", StringComparison.OrdinalIgnoreCase) >= 0);
                capsuleBone = capsule.bone; capsuleMiddle = (capsule.start + capsule.end) * .5f;
                trees = Object.FindObjectsOfType<InteractiveFoliage>();
                preparedMeshes = trees.Select(t => t.renderers.Select(r => r.GetComponent<MeshFilter>().sharedMesh).ToArray()).ToArray();
                preparedMaterials = trees.Select(t => t.renderers.Select(r => r.sharedMaterials).ToArray()).ToArray();
                float best = float.PositiveInfinity;
                float height = capsuleBone.TransformPoint(capsuleMiddle).y;
                foreach (var tree in trees)
                {
                    foreach (Renderer renderer in tree.renderers)
                    {
                        Mesh mesh = tree.InteractionMeshes[Array.IndexOf(tree.renderers, renderer)];
                        var mask = new List<Vector2>(); mesh.GetUVs(3, mask); Vector3[] vertices = mesh.vertices;
                        for (int i = 0; i < vertices.Length; i++)
                        {
                            if (mask[i].x < .8f || mask[i].y > .5f) continue;
                            Vector3 point = renderer.transform.TransformPoint(vertices[i]);
                            if (point.y < 1f || point.y > 3.5f) continue;
                            if (new Vector2(point.x - tree.transform.position.x, point.z - tree.transform.position.z).magnitude < 1.8f) continue;
                            float routeGap = MovementCorridor.MinDistanceToRoute(new Vector2(point.x, point.z), source.GetComponent<DinosaurAgent>().RouteIndex);
                            float score = Mathf.Abs(point.y - height) * 4f + routeGap * 5f + Vector3.Distance(point, source.transform.position) * .025f;
                            if (score < best) { best = score; leafPoint = point; }
                        }
                    }
                }
                Require(!float.IsPositiveInfinity(best), "no reachable leaf for tail preview");
                direction = source.transform.rotation * Quaternion.Euler(0f, source.GetComponent<DinosaurAgent>().VisualYaw, 0f) * Vector3.forward;
                Vector3 delta = leafPoint - capsuleBone.TransformPoint(capsuleMiddle); delta.y = 0f;
                basePosition = source.transform.position + delta;
                system = Object.FindObjectOfType<FoliageInteractionSystem>();
                terrain = Terrain.activeTerrain; preparedData = terrain.terrainData;
                baselineData = Object.Instantiate(preparedData);
                baselineData.SetTreeInstances(Reconstruct(Object.FindObjectOfType<FoliageTerrainConversion>()), false);
                camera = Camera.main; camera.enabled = false;
                camera.transform.position = PreviewEye(leafPoint, direction);
                camera.transform.LookAt(leafPoint + Vector3.up * .3f);
                target = new RenderTexture(1280, 720, 24); target.Create(); camera.targetTexture = target;
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60; Time.captureFramerate = 60;
                updateRecorder = Recorder.Get("JurassicWorld.FoliageInteraction"); updateRecorder.enabled = true;
                deadline = EditorApplication.timeSinceStartup + 240; phase = -1; lastFrame = -1; contacted = false;
                foreach (var costs in Costs) costs.Clear(); UpdateCosts.Clear();
                NextPhase(); EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }

        private static Vector3 PreviewEye(Vector3 point, Vector3 forward)
        {
            Vector3 fallback = point - forward * 5f + Vector3.Cross(Vector3.up, forward) * 4f + Vector3.up * 1.2f;
            for (int i = 0; i < 24; i++)
            {
                Vector3 offset = Quaternion.Euler(0, i * 15f, 0) * (fallback - point);
                Vector3 eye = point + offset;
                bool clear = true;
                for (int sample = 0; sample < 20; sample++)
                {
                    Vector3 p = Vector3.Lerp(eye, point, sample / 20f);
                    if (TerrainTreeObstacleRegistry.IsBlocked(new Vector2(p.x, p.z), .1f)) { clear = false; break; }
                }
                if (clear) return eye;
            }
            return fallback;
        }

        private static void NextPhase()
        {
            phase++; frame = 0; system.ClearInteraction(); source.ResetHistory();
            if (phase == 4) { Finish(null); return; }
            bool baseline = phase == 0;
            terrain.terrainData = baseline ? baselineData : preparedData;
            for (int i = 0; i < trees.Length; i++)
            {
                InteractiveFoliage tree = trees[i];
                if (tree.convertedTerrainTree) { tree.gameObject.SetActive(!baseline); continue; }
                for (int j = 0; j < tree.renderers.Length; j++)
                {
                    Renderer renderer = tree.renderers[j];
                    renderer.sharedMaterials = baseline ? tree.originals[j].materials : preparedMaterials[i][j];
                    renderer.GetComponent<MeshFilter>().sharedMesh = baseline ? tree.originals[j].mesh : preparedMeshes[i][j];
                }
            }
            system.enabled = !baseline; system.interactionsEnabled = phase >= 2;
            source.transform.position = basePosition - direction * 3f;
            TerrainTreeObstacleRegistry.Invalidate();
        }

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup > deadline) { Finish(new TimeoutException("Foliage preview timed out.")); return; }
            if (!EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            try
            {
                // Same trajectory, animation phase and camera in all three comparisons.
                float distance = frame < 150 ? Mathf.Lerp(-3f, 5f, Mathf.Clamp01((frame - 30f) / 120f)) : 5f;
                source.transform.position = basePosition + direction * distance;
                Animation animation = source.GetComponent<DinosaurFbxLoader>().ModelRoot.GetComponentInChildren<Animation>();
                if (frame == 0)
                {
                    animation.Stop();
                    foreach (var foot in Object.FindObjectsOfType<DinosaurFootLock>()) foot.enabled = false;
                }
                AnimationState walk = animation.Cast<AnimationState>().First(s => !s.name.EndsWith("_RM") &&
                    (s.name.IndexOf("walk", StringComparison.OrdinalIgnoreCase) >= 0 || s.name.IndexOf("run", StringComparison.OrdinalIgnoreCase) >= 0));
                walk.clip.SampleAnimation(animation.gameObject, frame / 60f % walk.length);
                Shader.SetGlobalVector("_Time", new Vector4(frame / 1200f, frame / 60f, frame / 30f, frame / 20f));
                var stopwatch = Stopwatch.StartNew(); camera.Render(); stopwatch.Stop();
                if (frame >= Warmup && phase < 3)
                {
                    double update = phase == 0 ? 0f : updateRecorder.elapsedNanoseconds / 1000000.0;
                    Costs[phase].Add(stopwatch.Elapsed.TotalMilliseconds + update);
                    if (phase == 2) UpdateCosts.Add(update);
                }
                if (phase >= 2)
                {
                    contacted |= system.ActiveTreeCount > 0;
                    if (phase == 3 && frame % 2 == 0) Capture(Output + "/Frames/contact_" + (frame / 2).ToString("D4") + ".png");
                }
                if (frame == 105 && phase < 3) Capture(Output + "/" + new[] { "disabled", "idle", "contact" }[phase] + ".png");
                frame++;
                if (frame == Frames) NextPhase();
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Capture(string path)
        {
            RenderTexture saved = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try { image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
            finally { RenderTexture.active = saved; Object.Destroy(image); }
        }

        private static void Finish(Exception error)
        {
            SessionState.SetBool(Running, false); EditorApplication.update -= Tick;
            Time.captureFramerate = 0;
            if (terrain != null && preparedData != null) terrain.terrainData = preparedData;
            if (camera != null) camera.targetTexture = null;
            if (target != null) { target.Release(); Object.Destroy(target); }
            if (baselineData != null) Object.Destroy(baselineData);
            if (error == null)
            {
                try
                {
                    Require(contacted, "tail preview never triggered a tree");
                    for (int i = 0; i < 3; i++) Report.Add(new[] { "Disabled", "Prepared idle", "Contact" }[i] +
                        ": mean CPU render + interaction = " + Costs[i].Average().ToString("F3") + " ms.");
                    double idle = Costs[1].Average() / Costs[0].Average() - 1, contact = Costs[2].Average() / Costs[0].Average() - 1;
                    Report.Add($"Overhead: idle {idle:P1}, contact {contact:P1}; targets 5% / 10%.");
                    Report.Add($"Interaction update: mean {UpdateCosts.Average():F3} ms; max {UpdateCosts.Max():F3} ms.");
                    Report.Add("Actual Play Mode, 1280x720, identical trajectory/camera. CPU render submission + interaction only; GPU/full player frame budgets require a player profiler capture.");
                    Report.Add("PASS: live bone-driven contact and rendered preview; 120 PNG frames at 30 FPS.");
                    foreach (string shaderPath in AssetDatabase.FindAssets("t:Shader", new[] { "Assets/Shaders" }).Select(AssetDatabase.GUIDToAssetPath).Where(p => p.Contains("FoliageInteraction")))
                    {
                        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
                        Require(!ShaderUtil.ShaderHasError(shader), "shader errors in " + shaderPath);
                    }
                    Report.Add("PASS: all four interaction shaders compiled without errors.");
                }
                catch (Exception failure) { error = failure; }
            }
            if (error != null) { Report.Add("FAILED: " + error); Debug.LogException(error); }
            File.WriteAllLines(Output + "/Verification.txt", Report);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}
