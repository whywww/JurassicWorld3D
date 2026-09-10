#if CODEX_STATIC_CHECK
using System.Threading.Tasks;
using UnityEngine;

namespace GLTFast
{
    // Used only by the documented standalone Roslyn check. Unity never defines
    // CODEX_STATIC_CHECK and compiles against the real glTFast package instead.
    public sealed class GltfAsset : MonoBehaviour
    {
        public bool LoadOnStartup { get; set; }
        public Task<bool> Load(string path)
        {
            return Task.FromResult(true);
        }
    }
}

namespace UnityEngine.UI
{
    public class Text : MonoBehaviour
    {
        public Font font;
        public int fontSize;
        public Color color;
        public TextAnchor alignment;
        public HorizontalWrapMode horizontalOverflow;
        public VerticalWrapMode verticalOverflow;
        public string text;
        public RectTransform rectTransform => GetComponent<RectTransform>();
    }

    public class Image : MonoBehaviour
    {
        public Color color;
    }

    public class CanvasScaler : MonoBehaviour
    {
        public enum ScaleMode
        {
            ConstantPixelSize,
            ScaleWithScreenSize
        }

        public ScaleMode uiScaleMode;
        public Vector2 referenceResolution;
    }
}
#endif
