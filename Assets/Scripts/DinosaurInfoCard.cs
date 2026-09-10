using UnityEngine;
using UnityEngine.UI;

namespace JurassicWorld
{
    public sealed class DinosaurInfoCard : MonoBehaviour
    {
        private Camera viewCamera;
        private DinosaurAgent agent;
        private CanvasGroup canvasGroup;

        public static DinosaurInfoCard Create(Transform dinosaur, Camera camera, DinosaurAgent dinosaurAgent, string description, Vector3 localPosition, float scale = 0.012f)
        {
            var root = new GameObject("Dinosaur Info Card", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            root.transform.SetParent(dinosaur, false);
            root.transform.localPosition = localPosition;
            root.transform.localScale = Vector3.one * scale;

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(420f, 245f);

            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(root.transform, false);
            RectTransform bgRect = background.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            background.GetComponent<Image>().color = new Color(0.035f, 0.075f, 0.055f, 0.92f);

            Text text = CreateText(background.transform);
            text.text = description;

            DinosaurInfoCard card = root.AddComponent<DinosaurInfoCard>();
            card.viewCamera = camera;
            card.agent = dinosaurAgent;
            card.canvasGroup = root.GetComponent<CanvasGroup>();
            card.canvasGroup.alpha = 0f;
            card.canvasGroup.blocksRaycasts = false;
            return card;
        }

        private static Text CreateText(Transform parent)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(24f, 18f);
            rect.offsetMax = new Vector2(-24f, -18f);
            Text text = textObject.GetComponent<Text>();
            text.font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Arial" }, 24);
            text.fontSize = 24;
            text.color = new Color(0.93f, 0.96f, 0.84f);
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private void LateUpdate()
        {
            if (viewCamera == null)
                return;

            transform.rotation = Quaternion.LookRotation(transform.position - viewCamera.transform.position, Vector3.up);
            Vector3 focus = transform.parent.position + Vector3.up * agent.ViewFocusHeight;
            Vector3 viewport = viewCamera.WorldToViewportPoint(focus);
            float distance = Vector3.Distance(viewCamera.transform.position, focus);
            bool centered = viewport.z > 0f && viewport.x >= 0.32f && viewport.x <= 0.68f && viewport.y >= 0.25f && viewport.y <= 0.75f;
            bool relevant = agent.HasBeenSummoned && distance <= 24f;
            float targetAlpha = centered && relevant ? 1f : 0f;
            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, Time.deltaTime * 3f);
        }
    }
}
