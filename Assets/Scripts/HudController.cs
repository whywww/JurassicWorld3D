using UnityEngine;
using UnityEngine.UI;

namespace JurassicWorld
{
    public sealed class HudController : MonoBehaviour
    {
        private static Text errorText;
        private static Text statusText;
        private static int loadedCount;
        private const int ExpectedDinosaurCount = 5;

        public static void Create(Camera camera)
        {
            loadedCount = 0;
            var root = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            root.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            root.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920f, 1080f);

            Text help = MakeText(root.transform, "Help", TextAnchor.LowerLeft, 24);
            RectTransform helpRect = help.rectTransform;
            helpRect.anchorMin = new Vector2(0f, 0f);
            helpRect.anchorMax = new Vector2(0f, 0f);
            helpRect.pivot = new Vector2(0f, 0f);
            helpRect.anchoredPosition = new Vector2(34f, 30f);
            helpRect.sizeDelta = new Vector2(820f, 100f);
            help.text = "W/S：前进/后退    A/D：转向并前进    按住鼠标右键：自由观察\n空格键：召唤画面中的恐龙";

            statusText = MakeText(root.transform, "Status", TextAnchor.UpperRight, 24);
            RectTransform statusRect = statusText.rectTransform;
            statusRect.anchorMin = new Vector2(1f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(1f, 1f);
            statusRect.anchoredPosition = new Vector2(-34f, -30f);
            statusRect.sizeDelta = new Vector2(980f, 90f);
            statusText.text = "正在加载 5 种恐龙模型…";

            errorText = MakeText(root.transform, "Error", TextAnchor.UpperCenter, 27);
            errorText.color = new Color(1f, 0.45f, 0.35f);
            RectTransform errorRect = errorText.rectTransform;
            errorRect.anchorMin = new Vector2(0.5f, 1f);
            errorRect.anchorMax = new Vector2(0.5f, 1f);
            errorRect.pivot = new Vector2(0.5f, 1f);
            errorRect.anchoredPosition = new Vector2(0f, -30f);
            errorRect.sizeDelta = new Vector2(1300f, 100f);
        }

        public static void ShowError(string message)
        {
            if (errorText != null)
                errorText.text = message;
        }

        public static void ShowStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }

        public static void ReportLoaded(string displayName)
        {
            loadedCount++;
            ShowStatus(loadedCount >= ExpectedDinosaurCount
                ? "5 种恐龙已就绪：霸王龙 / 胜王龙 / 伶盗龙 / 阿根廷龙 / 翼龙"
                : "已加载 " + loadedCount + "/" + ExpectedDinosaurCount + "：" + displayName);
        }

        private static Text MakeText(Transform parent, string name, TextAnchor alignment, int size)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(parent, false);
            Text text = obj.GetComponent<Text>();
            text.font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Arial" }, size);
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
