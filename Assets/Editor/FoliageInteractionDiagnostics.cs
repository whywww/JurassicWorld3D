using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class FoliageInteractionDiagnostics
    {
        private const string LiveKey = "JurassicWorld.Foliage.LiveProbe";
        private static int liveFrame = -1, liveStart;
        private static float liveTime, forcedTime;
        private static readonly StringBuilder LiveReport = new StringBuilder();
        private static DinosaurFoliageInteractor liveSource;
        private static InteractiveFoliage liveTree;
        private static Vector3 livePosition, liveLeaf;
        private static int naturalContacts, forcedContacts;
        private static bool heightAligned;
        private static readonly Dictionary<string, float> NaturalGreenPeaks = new Dictionary<string, float>();
        static FoliageInteractionDiagnostics()
        {
            EditorApplication.update += UpdateLive;
            EditorApplication.playModeStateChanged += OnPlayState;
        }
        private static void UpdateLive()
        {
            if (SessionState.GetBool(LiveKey, false) && Application.isPlaying) TraceLive();
        }

        private static void OnPlayState(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(LiveKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) BeginLive();
            if (state == PlayModeStateChange.ExitingPlayMode) SessionState.SetBool(LiveKey, false);
        }
        public static void RunLiveBatch()
        {
            Directory.CreateDirectory("Logs/FoliageInteraction");
            FoliageInteractionAcceptance.RegressionChecks();
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            SessionState.SetBool(LiveKey, true); EditorApplication.isPlaying = true;
        }
        private static void BeginLive()
        {
            liveStart = Time.frameCount; liveFrame = -1; naturalContacts = forcedContacts = 0;
            liveTime = Time.time; forcedTime = float.PositiveInfinity;
            liveSource = null; liveTree = null; LiveReport.Clear();
            NaturalGreenPeaks.Clear();
            heightAligned = false;
            LiveReport.AppendLine("Actual Play Mode: natural walk followed by controlled bone contact on FirTree01.");
        }
        private static void TraceLive()
        {
            if (liveFrame == Time.frameCount) return;
            liveFrame = Time.frameCount;
            int frame = liveFrame - liveStart;
            try
            {
                var system = UnityEngine.Object.FindObjectOfType<FoliageInteractionSystem>();
                if (liveSource == null) naturalContacts = Mathf.Max(naturalContacts, system.ActiveTreeCount);
                if (liveSource == null && frame % 60 == 0)
                    foreach (var tree in UnityEngine.Object.FindObjectsOfType<InteractiveFoliage>())
                    {
                        if (!(tree.name.StartsWith("tree0") || tree.name.StartsWith("FirTree") || tree.name.StartsWith("FabTree") || tree.name.StartsWith("IslandTree"))) continue;
                        if (tree.displacement == null || tree.displacement.Length == 0) continue;
                        float peak = tree.displacement.Max(d => d.magnitude);
                        if (peak > .001f) NaturalGreenPeaks[tree.name] = Mathf.Max(peak, NaturalGreenPeaks.TryGetValue(tree.name, out float previous) ? previous : 0f);
                    }
                if (frame == 1)
                    foreach (var source in UnityEngine.Object.FindObjectsOfType<DinosaurFoliageInteractor>())
                        LiveReport.AppendLine(source.name + ": capsules=" + source.capsules.Length + "; bound bones=" + source.capsules.Count(c => c.bone != null));
                if (Time.time - liveTime >= 20f && liveSource == null)
                {
                    forcedTime = Time.time;
                    liveSource = UnityEngine.Object.FindObjectsOfType<DinosaurFoliageInteractor>().First(s => s.name.Contains("Tyrannosaurus"));
                    liveTree = UnityEngine.Object.FindObjectsOfType<InteractiveFoliage>().First(t => t.name.StartsWith("FirTree01", StringComparison.Ordinal));
                    var capsule = liveSource.capsules.OrderByDescending(c => c.radius).First();
                    Vector3 middle = capsule.bone.TransformPoint((capsule.start + capsule.end) * .5f);
                    foreach (var actor in UnityEngine.Object.FindObjectsOfType<DinosaurFoliageInteractor>())
                    {
                        float top = actor.capsules.Max(c => Mathf.Max(c.bone.TransformPoint(c.start).y, c.bone.TransformPoint(c.end).y) + c.radius);
                        LiveReport.AppendLine(actor.name + " current capsule top=" + top);
                    }
                    Mesh stream = liveTree.InteractionMeshes[0]; var mask = new List<Vector2>(); stream.GetUVs(3, mask);
                    var vertices = stream.vertices; float best = float.PositiveInfinity;
                    float lowest = float.PositiveInfinity;
                    for (int i = 0; i < mask.Count; i++)
                    {
                        if (mask[i].x > .05f && mask[i].y < .5f) lowest = Mathf.Min(lowest, liveTree.renderers[0].transform.TransformPoint(vertices[i]).y);
                        if (mask[i].x < .8f || mask[i].y > .5f) continue;
                        Vector3 p = liveTree.renderers[0].transform.TransformPoint(vertices[i]);
                        float score = Mathf.Abs(p.y - middle.y);
                        if (score < best) { best = score; liveLeaf = p; }
                    }
                    Vector3 delta = liveLeaf - middle; delta.y = 0f;
                    livePosition = liveSource.transform.position + delta;
                    liveSource.GetComponent<DinosaurAgent>().enabled = false;
                    var foot = liveSource.GetComponent<DinosaurFootLock>(); if (foot != null) foot.enabled = false;
                    liveSource.ResetHistory(); system.ClearInteraction();
                    LiveReport.AppendLine("Fir target=" + liveTree.name + "; leaf=" + liveLeaf + "; body radius=" + capsule.radius + "; height gap=" + best);
                    LiveReport.AppendLine("Lowest flexible Fir leaf=" + lowest);
                }
                if (liveSource != null && Time.time - forcedTime < 2f)
                {
                    liveSource.transform.position = livePosition + Vector3.right * Mathf.Lerp(-.4f, .4f, (Time.time - forcedTime) / 2f);
                    forcedContacts = Mathf.Max(forcedContacts, system.ActiveTreeCount);
                }
                if (Time.time - forcedTime >= 2f && !heightAligned)
                {
                    heightAligned = true;
                    float peak = liveTree.displacement == null || liveTree.displacement.Length == 0 ? 0f : liveTree.displacement.Max(d => d.magnitude);
                    LiveReport.AppendLine("Ground-height body contact displacement=" + peak);
                    LiveReport.AppendLine("Ground-height actual leaf displacement=" + LeafPeak(liveTree));
                    var capsule = liveSource.capsules.OrderByDescending(c => c.radius).First();
                    var middle = capsule.bone.TransformPoint((capsule.start + capsule.end) * .5f);
                    livePosition.y += liveLeaf.y - middle.y;
                    liveSource.transform.position = livePosition; liveSource.ResetHistory();
                    LiveReport.AppendLine("Aligned body centre=" + capsule.bone.TransformPoint((capsule.start + capsule.end) * .5f));
                }
                if (Time.time - forcedTime >= 4f)
                {
                    var selected = liveSource.capsules.OrderByDescending(c => c.radius).First();
                    LiveReport.AppendLine("Final root=" + liveSource.transform.position + "; body centre=" + selected.bone.TransformPoint((selected.start + selected.end) * .5f));
                    LiveReport.AppendLine("Source registered=" + DinosaurFoliageInteractor.Sources.Contains(liveSource) + "; tree bounds=" + liveTree.WorldBounds);
                    float gap = liveSource.contacts.Min(c => Vector3.Distance(liveLeaf, FoliageContact.ClosestOnSegment(liveLeaf, c.start, c.end)) - c.radius);
                    LiveReport.AppendLine("Final capsule-to-leaf gap=" + gap);
                    LiveReport.AppendLine("Natural max active trees=" + naturalContacts + "; controlled max active=" + forcedContacts);
                    foreach (var source in UnityEngine.Object.FindObjectsOfType<DinosaurFoliageInteractor>())
                        LiveReport.AppendLine(source.name + ": valid swept capsules=" + source.contacts.Count(c => c.valid));
                    LiveReport.AppendLine("Target displacement=" + (liveTree.displacement == null || liveTree.displacement.Length == 0 ? 0f : liveTree.displacement.Max(d => d.magnitude)));
                    LiveReport.AppendLine("Height-aligned actual leaf displacement=" + LeafPeak(liveTree));
                    bool direct = false;
                    for (int step = 0; step < 60; step++)
                    {
                        liveTree.ClearForces();
                        foreach (var contact in liveSource.contacts) if (contact.valid) direct |= liveTree.Accumulate(contact);
                        liveTree.Simulate(1f / 60f, system);
                    }
                    LiveReport.AppendLine("Direct same-contact result=" + direct + "; leaf displacement=" + LeafPeak(liveTree));
                    foreach (var entry in NaturalGreenPeaks) LiveReport.AppendLine("Natural green contact: " + entry.Key + "; peak=" + entry.Value);
                    File.WriteAllText("Logs/FoliageInteraction/LiveProbe.txt", LiveReport.ToString());
                    if (Application.isBatchMode) { EditorApplication.Exit(0); return; }
                    SessionState.SetBool(LiveKey, false); EditorApplication.isPlaying = false;
                }
            }
            catch (Exception error)
            {
                LiveReport.AppendLine("FAIL: " + error); File.WriteAllText("Logs/FoliageInteraction/LiveProbe.txt", LiveReport.ToString());
                if (Application.isBatchMode) { EditorApplication.Exit(1); return; }
                SessionState.SetBool(LiveKey, false); EditorApplication.isPlaying = false; Debug.LogException(error);
            }
        }

        private static float LeafPeak(InteractiveFoliage tree)
        {
            if (tree.displacement == null || tree.displacement.Length == 0) return 0f;
            Vector3 size = tree.localBounds.size, scale = tree.transform.lossyScale;
            int nx = Mathf.Clamp(Mathf.CeilToInt(size.x * Mathf.Abs(scale.x) / tree.nodeSpacing) + 1, 2, tree.maximumAxisNodes);
            int ny = Mathf.Clamp(Mathf.CeilToInt(size.y * Mathf.Abs(scale.y) / tree.nodeSpacing) + 1, 2, tree.maximumAxisNodes);
            int nz = Mathf.Clamp(Mathf.CeilToInt(size.z * Mathf.Abs(scale.z) / tree.nodeSpacing) + 1, 2, tree.maximumAxisNodes);
            Mesh mesh = tree.InteractionMeshes[0]; var mask = new List<Vector2>(); mesh.GetUVs(3, mask); var vertices = mesh.vertices;
            Matrix4x4 matrix = tree.transform.worldToLocalMatrix * tree.renderers[0].transform.localToWorldMatrix;
            float peak = 0f;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (mask[i].y > .5f || mask[i].x <= .05f) continue;
                Vector3 p = matrix.MultiplyPoint3x4(vertices[i]) - tree.localBounds.min;
                float x = Mathf.Clamp01(p.x / size.x) * (nx - 1), y = Mathf.Clamp01(p.y / size.y) * (ny - 1), z = Mathf.Clamp01(p.z / size.z) * (nz - 1);
                int ix = Mathf.Min(Mathf.FloorToInt(x), nx - 2), iy = Mathf.Min(Mathf.FloorToInt(y), ny - 2), iz = Mathf.Min(Mathf.FloorToInt(z), nz - 2);
                Vector3 offset = Vector3.zero;
                for (int dz = 0; dz < 2; dz++) for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
                    offset += tree.displacement[ix + dx + nx * (iy + dy + ny * (iz + dz))] *
                        (dx == 0 ? 1 - x + ix : x - ix) * (dy == 0 ? 1 - y + iy : y - iy) * (dz == 0 ? 1 - z + iz : z - iz);
                peak = Mathf.Max(peak, offset.magnitude * mask[i].x);
            }
            return peak;
        }

        [MenuItem("Jurassic World/Acceptance/Diagnose Foliage in Current Scene")]
        public static void Diagnose()
        {
            var output = new StringBuilder("Foliage diagnostics; Play Mode=" + Application.isPlaying + "\n");
            var sources = UnityEngine.Object.FindObjectsOfType<DinosaurFoliageInteractor>();
            var manager = UnityEngine.Object.FindObjectOfType<FoliageInteractionSystem>();
            output.AppendLine("Manager=" + (manager == null ? "missing" : manager.enabled + "/" + manager.interactionsEnabled + "; active=" + manager.ActiveTreeCount));
            foreach (var source in sources)
                output.AppendLine("Source " + source.name + ": enabled=" + source.enabled + "; capsules=" + source.capsules.Length + "; valid=" + (source.contacts == null ? 0 : source.contacts.Count(c => c.valid)));
            foreach (var obstacle in UnityEngine.Object.FindObjectsOfType<SceneTreeObstacle>())
            {
                var tree = obstacle.GetComponent<InteractiveFoliage>();
                if (tree == null) { output.AppendLine(obstacle.name + " NOT PREPARED at " + obstacle.transform.position); continue; }
                float nearest = float.PositiveInfinity;
                foreach (var source in sources) foreach (var capsule in source.capsules)
                {
                    if (capsule.bone == null) continue;
                    var point = FoliageContact.ClosestOnSegment(tree.transform.TransformPoint(tree.localBounds.center), capsule.bone.TransformPoint(capsule.start), capsule.bone.TransformPoint(capsule.end));
                    nearest = Mathf.Min(nearest, Vector3.Distance(point, tree.transform.TransformPoint(tree.localBounds.center)) - capsule.radius);
                }
                float maximum = tree.displacement == null || tree.displacement.Length == 0 ? 0f : tree.displacement.Max(d => d.magnitude);
                output.AppendLine(tree.name + " at " + tree.transform.position + "; field=" + tree.localBounds + "; scale=" + tree.transform.lossyScale + "; centre gap=" + nearest.ToString("F2") + "; displacement=" + maximum.ToString("F4"));
                for (int i = 0; i < tree.renderers.Length; i++)
                {
                    var renderer = tree.renderers[i] as MeshRenderer;
                    Mesh stream = Application.isPlaying ? tree.InteractionMeshes[i] : renderer.additionalVertexStreams;
                    var weights = new List<Vector2>(); if (stream != null) stream.GetUVs(3, weights);
                    output.AppendLine("  " + renderer.name + ": mask=" + weights.Count + "; flexible=" + weights.Count(w => w.x > .5f) + "; shader=" + string.Join(",", renderer.sharedMaterials.Select(m => m == null ? "null" : m.shader.name)) + "; batched=" + renderer.isPartOfStaticBatch);
                }
            }
            Directory.CreateDirectory("Logs/FoliageInteraction");
            File.WriteAllText("Logs/FoliageInteraction/SceneDiagnostics.txt", output.ToString());
            Debug.Log("Foliage diagnostic report: Logs/FoliageInteraction/SceneDiagnostics.txt");
        }

        [MenuItem("Jurassic World/Acceptance/Verify Green Tree Rendering")]
        public static void VerifyGreenRendering()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before the isolated render check.");
            if (Application.isBatchMode) EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var scene = EditorSceneManager.NewPreviewScene();
            var output = new StringBuilder();
            RenderTexture target = null;
            Camera camera = null;
            try
            {
                var cameraObject = new GameObject("Foliage test camera"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
                camera = cameraObject.AddComponent<Camera>(); camera.scene = scene; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.gray; camera.nearClipPlane = .05f; camera.farClipPlane = 100f;
                target = new RenderTexture(512, 512, 24); target.Create(); camera.targetTexture = target;
                var lightObject = new GameObject("Foliage test light"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject, scene);
                var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(40, 20, 0);
                var settings = UnityEngine.Object.FindObjectOfType<FoliageInteractionSystem>();
                foreach (string name in new[] { "tree01", "tree02", "tree03", "tree04", "FirTree01", "FabTree_1", "FabTree_5", "IslandTree02" })
                {
                    var original = UnityEngine.Object.FindObjectsOfType<InteractiveFoliage>().First(t => t.name.StartsWith(name, StringComparison.Ordinal));
                    var clone = UnityEngine.Object.Instantiate(original.gameObject); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone, scene);
                    clone.transform.position = Vector3.zero;
                    var tree = clone.GetComponent<InteractiveFoliage>(); tree.PrepareRuntime();
                    var lod = clone.GetComponent<LODGroup>();
                    for (int level = 0; level < (lod == null ? 1 : lod.lodCount); level++)
                    {
                        if (lod != null) lod.ForceLOD(level);
                        int rendererIndex = lod == null ? 0 : Array.IndexOf(tree.renderers, lod.GetLODs()[level].renderers[0]);
                        Mesh stream = tree.InteractionMeshes[rendererIndex]; var mask = new List<Vector2>(); stream.GetUVs(3, mask);
                        Vector3[] vertices = stream.vertices; Vector3 leaf = Vector3.zero; float best = float.PositiveInfinity;
                        for (int i = 0; i < mask.Count; i++)
                        {
                            if (mask[i].x < .8f || mask[i].y > .5f) continue;
                            Vector3 p = tree.renderers[rendererIndex].transform.TransformPoint(vertices[i]);
                            float score = Mathf.Abs(p.y - 2.5f);
                            if (score < best) { best = score; leaf = p; }
                        }
                        if (float.IsPositiveInfinity(best)) throw new InvalidOperationException(name + " LOD " + level + " has no flexible leaf.");
                        camera.transform.position = leaf + new Vector3(3, 1, 3); camera.transform.LookAt(leaf);
                        tree.ResetField(); Color32[] before = Read(camera, target, name + "_LOD" + level + "_before");
                        var contact = new FoliageContact { start = leaf - Vector3.up * .5f, end = leaf + Vector3.up * .5f,
                            previousStart = leaf - Vector3.up * .5f - Vector3.right * .3f, previousEnd = leaf + Vector3.up * .5f - Vector3.right * .3f,
                            radius = .5f, speed = 2f, valid = true };
                        for (int step = 0; step < 60; step++) { tree.ClearForces(); tree.Accumulate(contact); tree.Simulate(1f / 60f, settings); }
                        Color32[] after = Read(camera, target, name + "_LOD" + level + "_contact");
                        int changed = 0;
                        for (int i = 0; i < before.Length; i++)
                            if (Math.Abs(before[i].r - after[i].r) + Math.Abs(before[i].g - after[i].g) + Math.Abs(before[i].b - after[i].b) > 20) changed++;
                        output.AppendLine(name + " LOD " + level + ": displacement=" + tree.displacement.Max(d => d.magnitude).ToString("F4") + "; changed pixels=" + changed);
                        if (changed < 20) throw new InvalidOperationException(name + " LOD " + level + " did not visibly deform.");
                        if (name.StartsWith("tree0", StringComparison.Ordinal))
                        {
                            tree.ResetField();
                            var section = clone.GetComponent<SceneTreeObstacle>().trunkSections.OrderBy(s => s.bottom).First();
                            Vector3 stem = new Vector3(section.center.x + section.radius + .2f,
                                (section.bottom + section.top) * .5f, section.center.y);
                            contact = new FoliageContact { start = stem, end = stem + Vector3.up * .2f,
                                previousStart = stem + Vector3.right * .2f, previousEnd = stem + Vector3.up * .2f + Vector3.right * .2f,
                                radius = .5f, speed = 2f, valid = true };
                            for (int step = 0; step < 60; step++) { tree.ClearForces(); tree.Accumulate(contact); tree.Simulate(1f / 60f, settings); }
                            Color32[] stemImage = Read(camera, target, name + "_trunk_contact");
                            int stemChanged = 0;
                            for (int i = 0; i < before.Length; i++)
                                if (Math.Abs(before[i].r - stemImage[i].r) + Math.Abs(before[i].g - stemImage[i].g) + Math.Abs(before[i].b - stemImage[i].b) > 20) stemChanged++;
                            float peak = tree.displacement.Max(d => d.magnitude);
                            for (int step = 0; step < 120; step++) { tree.ClearForces(); tree.Simulate(1f / 60f, settings); }
                            float settled = tree.displacement.Max(d => d.magnitude);
                            output.AppendLine(name + " trunk contact: displacement=" + peak.ToString("F4") + "; changed pixels=" + stemChanged + "; settled=" + settled.ToString("F6"));
                            if (peak < .01f || stemChanged < 20 || settled > .005f) throw new InvalidOperationException(name + " trunk response/rebound failed.");
                        }
                    }
                    UnityEngine.Object.DestroyImmediate(clone);
                }
                output.AppendLine("PASS: all green model LODs render controlled contact deformation.");
            }
            catch (Exception error) { output.AppendLine("FAIL: " + error); Debug.LogException(error); }
            finally
            {
                if (camera != null) camera.targetTexture = null;
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                EditorSceneManager.ClosePreviewScene(scene);
                File.WriteAllText("Logs/FoliageInteraction/GreenRendering.txt", output.ToString());
            }
        }

        private static Color32[] Read(Camera camera, RenderTexture target, string name)
        {
            Shader.SetGlobalVector("_Time", Vector4.zero); camera.Render();
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(512, 512, TextureFormat.RGB24, false);
            try
            {
                image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); image.Apply();
                File.WriteAllBytes("Logs/FoliageInteraction/" + name + ".png", image.EncodeToPNG());
                return image.GetPixels32();
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
        }
    }
}
