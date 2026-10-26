using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace JurassicWorld
{
    [DisallowMultipleComponent]
    public sealed class DinosaurVisibility : MonoBehaviour
    {
        public sealed class Target
        {
            public readonly MonoBehaviour Owner;
            public readonly bool IsFlying;
            internal readonly Renderer[] Renderers;
            internal readonly int[] Submeshes;
            internal readonly DinosaurVisibilityRenderer[] Trackers;
            private readonly Mesh[] meshes;
            private readonly MeshFilter[] filters;
            public string DisplayName => IsFlying ? ((PteradactalFlightController)Owner).DisplayName : ((DinosaurAgent)Owner).DisplayName;
            public string Description => IsFlying ? ((PteradactalFlightController)Owner).Description : ((DinosaurAgent)Owner).Description;

            internal Target(MonoBehaviour owner, bool flying, List<DinosaurVisibilityRenderer> ownedTrackers, Renderer[] renderers = null)
            {
                Owner = owner; IsFlying = flying;
                Renderers = renderers ?? owner.GetComponentsInChildren<Renderer>(true);
                Submeshes = new int[Renderers.Length];
                meshes = new Mesh[Renderers.Length]; filters = new MeshFilter[Renderers.Length];
                Trackers = new DinosaurVisibilityRenderer[Renderers.Length];
                for (int i = 0; i < Renderers.Length; i++)
                {
                    var skin = Renderers[i] as SkinnedMeshRenderer;
                    var filter = Renderers[i].GetComponent<MeshFilter>();
                    Mesh mesh = skin != null ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
                    meshes[i] = mesh; filters[i] = filter;
                    Submeshes[i] = mesh != null ? mesh.subMeshCount : 0;
                    if (mesh == null) continue;
                    var tracker = Renderers[i].GetComponent<DinosaurVisibilityRenderer>();
                    if (tracker == null)
                    {
                        tracker = Renderers[i].gameObject.AddComponent<DinosaurVisibilityRenderer>();
                        tracker.hideFlags = HideFlags.HideInInspector | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                        ownedTrackers.Add(tracker);
                    }
                    Trackers[i] = tracker;
                }
            }

            internal bool Matches(List<Renderer> renderers)
            {
                if (Renderers.Length != renderers.Count) return false;
                for (int i = 0; i < Renderers.Length; i++)
                {
                    if (Renderers[i] == null || Renderers[i] != renderers[i]) return false;
                    var skin = Renderers[i] as SkinnedMeshRenderer;
                    if (skin == null && filters[i] == null && Renderers[i].GetComponent<MeshFilter>() != null) return false;
                    Mesh mesh = skin != null ? skin.sharedMesh : filters[i] != null ? filters[i].sharedMesh : null;
                    if (mesh != meshes[i] || (mesh != null && mesh.subMeshCount != Submeshes[i]) ||
                        (Submeshes[i] > 0 && Trackers[i] == null)) return false;
                }
                return true;
            }

            internal bool CanRender(int index, Camera camera)
            {
                var renderer = Renderers[index];
                return Submeshes[index] > 0 && renderer != null && renderer.enabled && !renderer.forceRenderingOff &&
                    renderer.gameObject.activeInHierarchy && (camera.cullingMask & (1 << renderer.gameObject.layer)) != 0;
            }

            public bool IsInView(Camera camera, Plane[] planes, out Bounds bounds)
            {
                bounds = default;
                if (Owner == null || !Owner.isActiveAndEnabled) return false;
                bool first = true, visible = false;
                for (int i = 0; i < Renderers.Length; i++)
                {
                    if (!CanRender(i, camera)) continue;
                    var box = Renderers[i].bounds;
                    if (first) { bounds = box; first = false; }
                    else bounds.Encapsulate(box);
                    visible |= GeometryUtility.TestPlanesAABB(planes, box);
                }
                return visible;
            }
        }

        public readonly struct Observation
        {
            public readonly Target Target;
            public readonly int VisiblePixels;
            public readonly float VisibleRatio, Area, Distance;
            public readonly Vector2 Center;
            internal Observation(Target target, int visible, int total, Vector2 center, float distance, int screenPixels)
            {
                Target = target; VisiblePixels = visible;
                VisibleRatio = total > 0 ? Mathf.Clamp01((float)visible / total) : 0f;
                Center = center; Distance = distance; Area = (float)visible / screenPixels;
            }
        }

        // One shared scene depth render, followed by visible/reference masks for each candidate.
        // Using original scene materials preserves alpha-tested leaves and vertex deformation.
        private sealed class Buffers : IDisposable
        {
            internal readonly RenderTexture Scene, Mask, Atlas;
            internal readonly Material VisibleMaterial, ReferenceMaterial;
            internal Texture2D CpuPixels;
            internal readonly int Width, Height, Columns, Rows;
            internal bool Retired;

            internal Buffers(int width, int height, int count, Material template)
            {
                Width = width; Height = height;
                Columns = Mathf.CeilToInt(Mathf.Sqrt(count * 2));
                Rows = Mathf.CeilToInt((float)(count * 2) / Columns);
                Scene = Make(width, height, 24);
                Mask = Make(width, height, 0);
                Atlas = Make(width * Columns, height * Rows, 0);
                VisibleMaterial = new Material(template) { hideFlags = HideFlags.HideAndDontSave };
                ReferenceMaterial = new Material(template) { hideFlags = HideFlags.HideAndDontSave };
                VisibleMaterial.SetInt("_ZTest", (int)CompareFunction.LessEqual);
                ReferenceMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
            }

            private static RenderTexture Make(int width, int height, int depth)
            {
                var texture = new RenderTexture(width, height, depth, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                { name = "Dinosaur Visibility Buffer", hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1, filterMode = FilterMode.Point };
                texture.Create();
                return texture;
            }

            public void Dispose()
            {
                DestroyObject(Scene); DestroyObject(Mask); DestroyObject(Atlas);
                DestroyObject(VisibleMaterial); DestroyObject(ReferenceMaterial); DestroyObject(CpuPixels);
            }
        }

        private readonly List<Target> targets = new List<Target>();
        private readonly Dictionary<MonoBehaviour, Target> targetCache = new Dictionary<MonoBehaviour, Target>();
        private readonly HashSet<MonoBehaviour> refreshedOwners = new HashSet<MonoBehaviour>();
        private readonly List<MonoBehaviour> removedOwners = new List<MonoBehaviour>();
        private readonly List<Renderer> rendererScratch = new List<Renderer>();
        private readonly List<Target> candidates = new List<Target>();
        private readonly List<float> distances = new List<float>();
        // A single readback can be in flight, so these snapshots can be reused until its callback.
        private Target[] snapshot = Array.Empty<Target>();
        private float[] distanceSnapshot = Array.Empty<float>();
        private CommandBuffer maskCommands;
        private static readonly ProfilerMarker CaptureMarker = new ProfilerMarker("JurassicWorld.Visibility.Capture");
        private static readonly ProfilerMarker SceneMarker = new ProfilerMarker("JurassicWorld.Visibility.SceneRender");
        private static readonly ProfilerMarker ReadMarker = new ProfilerMarker("JurassicWorld.Visibility.ReadResults");
        private readonly List<DinosaurVisibilityRenderer> ownedTrackers = new List<DinosaurVisibilityRenderer>();
        private readonly List<Observation> observations = new List<Observation>();
        private readonly Plane[] planes = new Plane[6];
        private Camera sourceCamera, probeCamera;
        private Material template;
        private Buffers buffers;
        private bool pending, forceSynchronous;
        [SerializeField] private bool reuseMainCameraDepth = true;
        private bool waitingForMainRender;
        private int generation;
        private float nextSample, nextRefresh;
        private Matrix4x4 projection;
        private float aspect;
        public IReadOnlyList<Observation> Observations => observations;
        public float LastResultTime { get; private set; } = float.NegativeInfinity;
        public double LastCaptureMilliseconds { get; private set; }
        public bool IsPending => pending;
        public int MainDepthCaptureCount { get; private set; }
        public int ProbeCaptureCount { get; private set; }

        private void OnEnable()
        {
            Camera.onPostRender += OnCameraRendered;
            SceneManager.sceneLoaded += SceneLoaded;
            SceneManager.sceneUnloaded += SceneUnloaded;
            SceneManager.activeSceneChanged += ActiveSceneChanged;
        }

        private void OnDisable()
        {
            Camera.onPostRender -= OnCameraRendered;
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneUnloaded -= SceneUnloaded;
            SceneManager.activeSceneChanged -= ActiveSceneChanged;
            ResetCapture(); targets.Clear(); targetCache.Clear();
            maskCommands?.Dispose(); maskCommands = null;
            foreach (var tracker in ownedTrackers) DestroyObject(tracker);
            ownedTrackers.Clear();
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode) => InvalidateScene();
        private void SceneUnloaded(Scene scene) => InvalidateScene();
        private void ActiveSceneChanged(Scene from, Scene to) => InvalidateScene();
        private void InvalidateScene() { ResetCapture(); targets.Clear(); targetCache.Clear(); nextRefresh = 0f; }

        private void ResetCapture()
        {
            generation++;
            observations.Clear(); LastResultTime = float.NegativeInfinity;
            if (buffers != null)
            {
                // A readback owns its atlas until its callback, even when the component is disabled.
                if (pending) buffers.Retired = true;
                else buffers.Dispose();
                buffers = null;
            }
            pending = false;
            waitingForMainRender = false;
            if (probeCamera != null) DestroyObject(probeCamera.gameObject);
            probeCamera = null; sourceCamera = null; nextSample = 0f;
        }

        private static void DestroyObject(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        public void Tick(Camera camera)
        {
            if (!isActiveAndEnabled) return;
            if (camera == null || !camera.isActiveAndEnabled)
            {
                if (sourceCamera != null || buffers != null || observations.Count > 0) ResetCapture();
                return;
            }
            if (camera != sourceCamera || camera.projectionMatrix != projection || !Mathf.Approximately(camera.aspect, aspect))
            {
                ResetCapture(); sourceCamera = camera;
                projection = camera.projectionMatrix; aspect = camera.aspect;
            }
            if (pending || Time.unscaledTime < nextSample) return;
            if (reuseMainCameraDepth && camera.enabled && (camera.depthTextureMode & DepthTextureMode.Depth) != 0)
            {
                RefreshTargets();
                waitingForMainRender = true;
                return;
            }
            Capture(camera, false);
        }

        private void OnCameraRendered(Camera camera)
        {
            if (!waitingForMainRender || camera != sourceCamera || pending) return;
            waitingForMainRender = false;
            using (CaptureMarker.Auto()) CaptureSample(camera, false, true);
        }

        // Synchronous path is also used by graphical acceptance, with the same masks as runtime.
        public void CaptureNow(Camera camera)
        {
            if (pending) throw new InvalidOperationException("A visibility readback is already pending.");
            waitingForMainRender = false;
            if (sourceCamera != camera) ResetCapture();
            sourceCamera = camera; projection = camera.projectionMatrix; aspect = camera.aspect;
            Capture(camera, true);
        }

        private void Capture(Camera camera, bool synchronous)
        {
            using (CaptureMarker.Auto()) CaptureSample(camera, synchronous);
        }

        private void RefreshTarget(MonoBehaviour owner, bool flying)
        {
            refreshedOwners.Add(owner);
            rendererScratch.Clear(); owner.GetComponentsInChildren(true, rendererScratch);
            if (!targetCache.TryGetValue(owner, out Target target) || !target.Matches(rendererScratch))
            {
                target = new Target(owner, flying, ownedTrackers, rendererScratch.ToArray());
                targetCache[owner] = target;
            }
            targets.Add(target);
        }

        private void RefreshTargets()
        {
            if (Time.unscaledTime >= nextRefresh)
            {
                ownedTrackers.RemoveAll(tracker => tracker == null);
                targets.Clear();
                refreshedOwners.Clear(); removedOwners.Clear();
                foreach (var agent in FindObjectsByType<DinosaurAgent>(FindObjectsSortMode.None)) RefreshTarget(agent, false);
                foreach (var flyer in FindObjectsByType<PteradactalFlightController>(FindObjectsSortMode.None)) RefreshTarget(flyer, true);
                // Preserve the original ground-then-flyer, instance-ID tie order used by card scoring.
                targets.Sort(CompareTargets);
                foreach (var entry in targetCache) if (!refreshedOwners.Contains(entry.Key)) removedOwners.Add(entry.Key);
                foreach (var owner in removedOwners) targetCache.Remove(owner);
                nextRefresh = Time.unscaledTime + 1f;
            }
        }

        private static int CompareTargets(Target a, Target b)
        {
            int kind = a.IsFlying.CompareTo(b.IsFlying);
            return kind != 0 ? kind : a.Owner.GetInstanceID().CompareTo(b.Owner.GetInstanceID());
        }

        private void CaptureSample(Camera camera, bool synchronous, bool mainDepth = false)
        {
            nextSample = Time.unscaledTime + ((!forceSynchronous && SystemInfo.supportsAsyncGPUReadback) ? .1f : .2f);
            RefreshTargets();
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            candidates.Clear(); distances.Clear();
            foreach (var target in targets)
                if (target.IsInView(camera, planes, out var box))
                {
                    candidates.Add(target);
                    distances.Add(Vector3.Distance(camera.transform.position, box.ClosestPoint(camera.transform.position)));
                }
            if (candidates.Count == 0)
            {
                observations.Clear(); LastResultTime = Time.unscaledTime;
                LastCaptureMilliseconds = 0;
                return;
            }
            if (template == null) template = Resources.Load<Material>("DinosaurVisibilityMask");
            if (template == null || !template.shader.isSupported)
            {
                observations.Clear(); LastResultTime = float.NegativeInfinity;
                return;
            }
            const int height = 180;
            int width = Mathf.Max(1, Mathf.RoundToInt(height * camera.aspect));
            int columns = Mathf.CeilToInt(Mathf.Sqrt(candidates.Count * 2));
            int rows = Mathf.CeilToInt((float)(candidates.Count * 2) / columns);
            if (width * columns > SystemInfo.maxTextureSize || height * rows > SystemInfo.maxTextureSize)
            {
                observations.Clear(); LastResultTime = float.NegativeInfinity;
                return;
            }
            if (buffers == null || buffers.Width != width || buffers.Height != height || buffers.Columns != columns || buffers.Rows != rows)
            {
                buffers?.Dispose();
                buffers = new Buffers(width, height, candidates.Count, template);
            }
            if (!mainDepth && probeCamera == null)
            {
                var root = new GameObject("Dinosaur Visibility Camera") { hideFlags = HideFlags.HideAndDontSave };
                probeCamera = root.AddComponent<Camera>();
                probeCamera.enabled = false;
            }
            var timer = System.Diagnostics.Stopwatch.StartNew();
            if (!mainDepth)
            {
                probeCamera.CopyFrom(camera);
                probeCamera.enabled = false;
                probeCamera.targetTexture = buffers.Scene;
                probeCamera.rect = new Rect(0, 0, 1, 1);
                probeCamera.allowMSAA = false; probeCamera.allowHDR = false;
                probeCamera.depthTextureMode = DepthTextureMode.None;
                probeCamera.useOcclusionCulling = false;
                probeCamera.clearFlags = CameraClearFlags.SolidColor;
                probeCamera.backgroundColor = Color.clear;
                probeCamera.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
                probeCamera.worldToCameraMatrix = camera.worldToCameraMatrix;
                probeCamera.projectionMatrix = camera.projectionMatrix;
            }
            buffers.VisibleMaterial.SetInt("_ZTest", (int)(mainDepth ? CompareFunction.Always : CompareFunction.LessEqual));
            buffers.VisibleMaterial.SetFloat("_UseSceneDepth", mainDepth ? 1f : 0f);
            RenderTexture previous = RenderTexture.active;
            try
            {
                int captureSerial = ++DinosaurVisibilityRenderer.CaptureSerial;
                if (!mainDepth)
                {
                    DinosaurVisibilityRenderer.CaptureCamera = probeCamera;
                    try { using (SceneMarker.Auto()) probeCamera.Render(); ProbeCaptureCount++; }
                    finally { DinosaurVisibilityRenderer.CaptureCamera = null; }
                }
                else MainDepthCaptureCount++;
                if (maskCommands == null) maskCommands = new CommandBuffer { name = "Dinosaur visibility masks" };
                var commands = maskCommands;
                commands.Clear();
                {
                    commands.BeginSample("DinosaurVisibility.Masks");
                    commands.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        DrawMask(commands, candidates[i], buffers.VisibleMaterial, i * 2, captureSerial, mainDepth);
                        DrawMask(commands, candidates[i], buffers.ReferenceMaterial, i * 2 + 1, captureSerial, mainDepth);
                    }
                    commands.EndSample("DinosaurVisibility.Masks");
                    Graphics.ExecuteCommandBuffer(commands);
                }
                var captured = buffers;
                int count = candidates.Count;
                if (snapshot.Length < count) { snapshot = new Target[count]; distanceSnapshot = new float[count]; }
                candidates.CopyTo(snapshot); distances.CopyTo(distanceSnapshot);
                // A reset can start a new generation while the old readback retires.
                // Capture the arrays as well as the buffer, never mutable instance fields.
                var capturedTargets = snapshot;
                var capturedDistances = distanceSnapshot;
                int version = generation;
                if (!synchronous && !forceSynchronous && SystemInfo.supportsAsyncGPUReadback)
                {
                    pending = true;
                    AsyncGPUReadback.Request(captured.Atlas, 0, TextureFormat.RGBA32, request =>
                    {
                        if (captured.Retired) { captured.Dispose(); return; }
                        if (this == null || version != generation) return;
                        pending = false;
                        if (request.hasError)
                        {
                            forceSynchronous = true; observations.Clear(); LastResultTime = float.NegativeInfinity;
                            return;
                        }
                        ReadResults(request.GetData<Color32>(), captured, capturedTargets, capturedDistances, count);
                    });
                }
                else
                {
                    if (captured.CpuPixels == null)
                        captured.CpuPixels = new Texture2D(captured.Atlas.width, captured.Atlas.height, TextureFormat.RGBA32, false, true)
                            { hideFlags = HideFlags.HideAndDontSave };
                    RenderTexture.active = captured.Atlas;
                    captured.CpuPixels.ReadPixels(new Rect(0, 0, captured.Atlas.width, captured.Atlas.height), 0, 0, false);
                    ReadResults(captured.CpuPixels.GetRawTextureData<Color32>(), captured, capturedTargets, capturedDistances, count);
                }
            }
            finally
            {
                RenderTexture.active = previous;
                timer.Stop(); LastCaptureMilliseconds = timer.Elapsed.TotalMilliseconds;
            }
        }

        private void DrawMask(CommandBuffer commands, Target target, Material material, int tile, int captureSerial, bool mainDepth)
        {
            if (mainDepth) commands.SetRenderTarget(buffers.Mask);
            else commands.SetRenderTarget(new RenderTargetIdentifier(buffers.Mask), new RenderTargetIdentifier(buffers.Scene));
            commands.SetViewport(new Rect(0, 0, buffers.Width, buffers.Height));
            commands.ClearRenderTarget(false, true, Color.clear);
            for (int r = 0; r < target.Renderers.Length; r++)
            {
                var tracker = target.Trackers[r];
                if (!target.CanRender(r, sourceCamera) || tracker == null) continue;
                if (mainDepth ? tracker.LastCamera != sourceCamera || tracker.LastFrame != Time.frameCount
                    : tracker.LastCapture != captureSerial) continue;
                for (int submesh = 0; submesh < target.Submeshes[r]; submesh++)
                    commands.DrawRenderer(target.Renderers[r], material, submesh, 0);
            }
            commands.CopyTexture(buffers.Mask, 0, 0, 0, 0, buffers.Width, buffers.Height, buffers.Atlas, 0, 0,
                tile % buffers.Columns * buffers.Width, tile / buffers.Columns * buffers.Height);
        }

        private void ReadResults(NativeArray<Color32> pixels, Buffers captured, Target[] snapshot, float[] distances, int count)
        {
            using (ReadMarker.Auto()) DecodeResults(pixels, captured, snapshot, distances, count);
        }

        private void DecodeResults(NativeArray<Color32> pixels, Buffers captured, Target[] snapshot, float[] distances, int count)
        {
            observations.Clear();
            for (int i = 0; i < count; i++)
            {
                int visible = 0, total = 0;
                long sumX = 0, sumY = 0;
                int visibleTile = i * 2, referenceTile = visibleTile + 1;
                int vx = visibleTile % captured.Columns * captured.Width;
                int vy = visibleTile / captured.Columns * captured.Height;
                int rx = referenceTile % captured.Columns * captured.Width;
                int ry = referenceTile / captured.Columns * captured.Height;
                for (int y = 0; y < captured.Height; y++)
                    for (int x = 0; x < captured.Width; x++)
                    {
                        if (pixels[(ry + y) * captured.Atlas.width + rx + x].r > 127) total++;
                        if (pixels[(vy + y) * captured.Atlas.width + vx + x].r <= 127) continue;
                        visible++; sumX += x; sumY += y;
                    }
                Vector2 center = visible > 0 ? new Vector2(((float)sumX / visible + .5f) / captured.Width,
                    ((float)sumY / visible + .5f) / captured.Height) : new Vector2(.5f, .5f);
                observations.Add(new Observation(snapshot[i], visible, total, center, distances[i], captured.Width * captured.Height));
            }
            LastResultTime = Time.unscaledTime;
        }
    }
}
