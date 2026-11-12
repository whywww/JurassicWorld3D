using UnityEngine;

namespace JurassicWorld
{
    // World-only scaling: overlay canvases and IMGUI remain at window resolution.
    public sealed class WorldRenderScale : MonoBehaviour
    {
        private Camera world, presenter;
        private RenderTexture target, previous;
        private float scale = 1f;
        public static float CurrentScale { get; private set; } = 1f;
        public void Configure(Camera camera, float renderScale)
        {
            Release(); world = camera;
            scale = Mathf.Clamp(renderScale, .5f, 1f);
            if (RuntimePerformanceBenchmark.Requested)
                scale = Mathf.Clamp(float.Parse(RuntimePerformanceBenchmark.Argument("-jwRenderScale", scale.ToString(System.Globalization.CultureInfo.InvariantCulture)), System.Globalization.CultureInfo.InvariantCulture), .5f, 1f);
            CurrentScale = scale;
            if (world != null) previous = world.targetTexture;
            Resize();
        }
        private void LateUpdate() => Resize();
        private void Resize()
        {
            if (world == null || scale >= .999f) return;
            int width = Mathf.Max(1, Mathf.RoundToInt(Screen.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(Screen.height * scale));
            if (target != null && target.width == width && target.height == height) return;
            if (target != null) { world.targetTexture = previous; target.Release(); Dispose(target); }
            target = new RenderTexture(width, height, 24, world.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default)
            { name = "Scaled world", filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            target.Create(); world.targetTexture = target;
            if (presenter == null)
            {
                var root = new GameObject("World presentation", typeof(Camera), typeof(WorldPresentation));
                root.transform.SetParent(transform, false);
                presenter = root.GetComponent<Camera>();
                presenter.cullingMask = 0; presenter.clearFlags = CameraClearFlags.Nothing;
                presenter.allowHDR = presenter.allowMSAA = false;
            }
            presenter.depth = world.depth + 1;
            presenter.GetComponent<WorldPresentation>().source = target;
        }
        private void Release()
        {
            if (world != null && world.targetTexture == target) world.targetTexture = previous;
            if (presenter != null) { presenter.enabled = false; Dispose(presenter.gameObject); }
            if (target != null) { target.Release(); Dispose(target); }
            presenter = null; target = null; world = null; previous = null;
        }
        private static void Dispose(Object value) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        private void OnDestroy() => Release();
    }
    public sealed class WorldPresentation : MonoBehaviour
    {
        public RenderTexture source;
        private void OnRenderImage(RenderTexture ignored, RenderTexture destination)
        { if (source != null) Graphics.Blit(source, destination); else Graphics.Blit(ignored, destination); }
    }
}
