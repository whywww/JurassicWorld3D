using System;
using System.Collections.Generic;
using Unity.Collections;
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
            public string DisplayName => IsFlying ? ((PteradactalFlightController)Owner).DisplayName : ((DinosaurAgent)Owner).DisplayName;
            public string Description => IsFlying ? ((PteradactalFlightController)Owner).Description : ((DinosaurAgent)Owner).Description;

            internal Target(MonoBehaviour owner, bool flying, List<DinosaurVisibilityRenderer> ownedTrackers)
            {
                Owner = owner; IsFlying = flying;
                Renderers = owner.GetComponentsInChildren<Renderer>(true);
                Submeshes = new int[Renderers.Length];
                Trackers = new DinosaurVisibilityRenderer[Renderers.Length];
                for (int i = 0; i < Renderers.Length; i++)
                {
                    var skin = Renderers[i] as SkinnedMeshRenderer;
                    var filter = Renderers[i].GetComponent<MeshFilter>();
                    Mesh mesh = skin != null ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
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
        private readonly List<DinosaurVisibilityRenderer> ownedTrackers = new List<DinosaurVisibilityRenderer>();
        private readonly List<Observation> observations = new List<Observation>();
        private readonly Plane[] planes = new Plane[6];
        private Camera sourceCamera, probeCamera;
        private Material template;
        private Buffers buffers;
        private bool pending, forceSynchronous;
        private int generation;
        private float nextSample, nextRefresh;
        private Matrix4x4 projection;
        private float aspect;
        public IReadOnlyList<Observation> Observations => observations;
        public float LastResultTime { get; private set; } = float.NegativeInfinity;
        public double LastCaptureMilliseconds { get; private set; }
        public bool IsPending => pending;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += SceneLoaded;
            SceneManager.sceneUnloaded += SceneUnloaded;
            SceneManager.activeSceneChanged += ActiveSceneChanged;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneUnloaded -= SceneUnloaded;
            SceneManager.activeSceneChanged -= ActiveSceneChanged;
            ResetCapture(); targets.Clear();
            foreach (var tracker in ownedTrackers) DestroyObject(tracker);
            ownedTrackers.Clear();
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode) => InvalidateScene();
        private void SceneUnloaded(Scene scene) => InvalidateScene();
        private void ActiveSceneChanged(Scene from, Scene to) => InvalidateScene();
        private void InvalidateScene() { ResetCapture(); targets.Clear(); nextRefresh = 0f; }

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
            Capture(camera, false);
        }

        // Synchronous path is also used by graphical acceptance, with the same masks as runtime.
        public void CaptureNow(Camera camera)
        {
            if (pending) throw new InvalidOperationException("A visibility readback is already pending.");
            if (sourceCamera != camera) ResetCapture();
            sourceCamera = camera; projection = camera.projectionMatrix; aspect = camera.aspect;
            Capture(camera, true);
        }

        private void Capture(Camera camera, bool synchronous)
        {
            nextSample = Time.unscaledTime + ((!forceSynchronous && SystemInfo.supportsAsyncGPUReadback) ? .1f : .2f);
            if (targets.Count == 0 || Time.unscaledTime >= nextRefresh)
            {
                ownedTrackers.RemoveAll(tracker => tracker == null);
                targets.Clear();
                foreach (var agent in FindObjectsOfType<DinosaurAgent>()) targets.Add(new Target(agent, false, ownedTrackers));
                foreach (var flyer in FindObjectsOfType<PteradactalFlightController>()) targets.Add(new Target(flyer, true, ownedTrackers));
                nextRefresh = Time.unscaledTime + 1f;
            }
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            var candidates = new List<Target>();
            var distances = new List<float>();
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
            if (probeCamera == null)
            {
                var root = new GameObject("Dinosaur Visibility Camera") { hideFlags = HideFlags.HideAndDontSave };
                probeCamera = root.AddComponent<Camera>();
                probeCamera.enabled = false;
            }
            var timer = System.Diagnostics.Stopwatch.StartNew();
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
            RenderTexture previous = RenderTexture.active;
            try
            {
                int captureSerial = ++DinosaurVisibilityRenderer.CaptureSerial;
                DinosaurVisibilityRenderer.CaptureCamera = probeCamera;
                try { probeCamera.Render(); }
                finally { DinosaurVisibilityRenderer.CaptureCamera = null; }
                using (var commands = new CommandBuffer { name = "Dinosaur visibility masks" })
                {
                    commands.BeginSample("DinosaurVisibility.Masks");
                    commands.SetViewProjectionMatrices(probeCamera.worldToCameraMatrix, probeCamera.projectionMatrix);
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        DrawMask(commands, candidates[i], buffers.VisibleMaterial, i * 2, captureSerial);
                        DrawMask(commands, candidates[i], buffers.ReferenceMaterial, i * 2 + 1, captureSerial);
                    }
                    commands.EndSample("DinosaurVisibility.Masks");
                    Graphics.ExecuteCommandBuffer(commands);
                }
                var captured = buffers;
                var snapshot = candidates.ToArray();
                var distanceSnapshot = distances.ToArray();
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
                        ReadResults(request.GetData<Color32>(), captured, snapshot, distanceSnapshot);
                    });
                }
                else
                {
                    if (captured.CpuPixels == null)
                        captured.CpuPixels = new Texture2D(captured.Atlas.width, captured.Atlas.height, TextureFormat.RGBA32, false, true)
                            { hideFlags = HideFlags.HideAndDontSave };
                    RenderTexture.active = captured.Atlas;
                    captured.CpuPixels.ReadPixels(new Rect(0, 0, captured.Atlas.width, captured.Atlas.height), 0, 0, false);
                    ReadResults(captured.CpuPixels.GetRawTextureData<Color32>(), captured, snapshot, distanceSnapshot);
                }
            }
            finally
            {
                RenderTexture.active = previous;
                timer.Stop(); LastCaptureMilliseconds = timer.Elapsed.TotalMilliseconds;
            }
        }

        private void DrawMask(CommandBuffer commands, Target target, Material material, int tile, int captureSerial)
        {
            commands.SetRenderTarget(new RenderTargetIdentifier(buffers.Mask), new RenderTargetIdentifier(buffers.Scene));
            commands.SetViewport(new Rect(0, 0, buffers.Width, buffers.Height));
            commands.ClearRenderTarget(false, true, Color.clear);
            for (int r = 0; r < target.Renderers.Length; r++)
            {
                if (!target.CanRender(r, sourceCamera) || target.Trackers[r] == null ||
                    target.Trackers[r].LastCapture != captureSerial) continue;
                for (int submesh = 0; submesh < target.Submeshes[r]; submesh++)
                    commands.DrawRenderer(target.Renderers[r], material, submesh, 0);
            }
            commands.CopyTexture(buffers.Mask, 0, 0, 0, 0, buffers.Width, buffers.Height, buffers.Atlas, 0, 0,
                tile % buffers.Columns * buffers.Width, tile / buffers.Columns * buffers.Height);
        }

        private void ReadResults(NativeArray<Color32> pixels, Buffers captured, Target[] snapshot, float[] distances)
        {
            observations.Clear();
            for (int i = 0; i < snapshot.Length; i++)
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
