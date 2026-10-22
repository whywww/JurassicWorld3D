using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace JurassicWorld
{
    // Reuses the baked HUD card; visibility is measured from scene depth, not colliders.
    public sealed class DinosaurInfoCard : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Text titleText;
        [SerializeField] private Text bodyText;

        private static DinosaurInfoCard instance;
        private const float FadeSpeed = 4f;
        private float fadeTarget;
        private DinosaurVisibility visibility;
        private DinosaurVisibility.Target currentTarget, challenger;
        private float challengerSince;
        private Camera cachedCamera;
        private readonly Plane[] planes = new Plane[6];
        private readonly List<DinosaurVisibility.Observation> eligible = new List<DinosaurVisibility.Observation>();

        private void Awake()
        {
            instance = this;
            visibility = GetComponent<DinosaurVisibility>();
            if (visibility == null) visibility = gameObject.AddComponent<DinosaurVisibility>();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
            }
        }

        private void OnDisable()
        {
            currentTarget = challenger = null;
            fadeTarget = 0f;
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            if (visibility != null) visibility.enabled = false;
        }

        private void OnEnable()
        {
            if (visibility != null) visibility.enabled = true;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        public static void Show(string title, string body)
        {
            if (instance == null) return;
            instance.ApplyText(title, body);
            instance.currentTarget = instance.challenger = null;
            instance.fadeTarget = 1f;
        }

        public static void Hide()
        {
            if (instance == null) return;
            instance.currentTarget = instance.challenger = null;
            instance.fadeTarget = 0f;
        }

        private void Update()
        {
            if (canvasGroup != null)
                canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, fadeTarget, Time.deltaTime * FadeSpeed);
        }

        private void LateUpdate()
        {
            var camera = Camera.main;
            if (camera != cachedCamera)
            {
                currentTarget = challenger = null;
                cachedCamera = camera;
            }
            visibility.Tick(camera);
            if (camera == null || !camera.isActiveAndEnabled || Time.unscaledTime - visibility.LastResultTime > .5f)
            {
                Select(null);
                return;
            }
            EvaluateAutoDisplay(visibility.Observations, camera, Time.unscaledTime);
        }

        private static Vector2 CenterOffset(Vector2 center, float aspect) =>
            new Vector2((center.x - .5f) * aspect, center.y - .5f);

        private static bool IsCore(DinosaurVisibility.Observation item, float aspect) =>
            !item.Target.IsFlying && CenterOffset(item.Center, aspect).sqrMagnitude <= .12f * .12f;

        private static float Score(DinosaurVisibility.Observation item, float aspect)
        {
            float halfDiagonal = .5f * Mathf.Sqrt(aspect * aspect + 1f);
            float center = 1f - Mathf.Clamp01(CenterOffset(item.Center, aspect).magnitude / halfDiagonal);
            return .65f * center + .20f * Mathf.Clamp01(item.Area / .05f) + .15f / (1f + item.Distance / 20f);
        }

        private static bool SameTarget(DinosaurVisibility.Target a, DinosaurVisibility.Target b) =>
            a != null && b != null && a.Owner != null && a.Owner == b.Owner;

        private void EvaluateAutoDisplay(IReadOnlyList<DinosaurVisibility.Observation> observations, Camera camera, float now)
        {
            eligible.Clear();
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            bool hasCore = false;
            foreach (var item in observations)
            {
                if (item.VisiblePixels < 16 || item.VisibleRatio < .5f ||
                    !item.Target.IsInView(camera, planes, out _)) continue;
                eligible.Add(item);
                hasCore |= IsCore(item, camera.aspect);
            }
            DinosaurVisibility.Target best = null;
            float bestScore = float.NegativeInfinity, currentScore = float.NegativeInfinity;
            foreach (var item in eligible)
            {
                if (hasCore && !IsCore(item, camera.aspect)) continue;
                float score = Score(item, camera.aspect);
                if (SameTarget(item.Target, currentTarget)) currentScore = score;
                if (score > bestScore) { best = item.Target; bestScore = score; }
            }
            if (best == null) { Select(null); return; }
            // Invalid or core-excluded targets lose eligibility without the switch delay.
            if (float.IsNegativeInfinity(currentScore)) { Select(best); return; }
            if (SameTarget(best, currentTarget) || bestScore < currentScore * 1.15f)
            {
                challenger = null;
                fadeTarget = 1f;
                return;
            }
            if (!SameTarget(best, challenger))
            {
                challenger = best; challengerSince = now;
                return;
            }
            if (now - challengerSince >= .2f) Select(best);
        }

        private void Select(DinosaurVisibility.Target target)
        {
            currentTarget = target;
            challenger = null;
            if (target != null) ApplyText(target.DisplayName, target.Description);
            fadeTarget = target != null ? 1f : 0f;
        }

        private void ApplyText(string title, string body)
        {
            if (titleText != null && titleText.text != title) titleText.text = title;
            if (bodyText != null && bodyText.text != body) bodyText.text = body;
        }
    }
}
