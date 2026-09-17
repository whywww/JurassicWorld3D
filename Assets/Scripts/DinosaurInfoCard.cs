using UnityEngine;
using UnityEngine.UI;

namespace JurassicWorld
{
    // 信息卡是 HUD Canvas 下的屏幕空间子面板（左上角锚定）。
    // 运行期自动检测：当恐龙出现在视野中心附近且距离较近时淡入显示，偏离时淡出。
    // 烘焙态通过 SceneBuilder SetField 写入 canvasGroup / titleText / bodyText 引用。
    // 阈值参数在 Inspector 中可调（有代码默认值，不需要 SetField 注入）。
    public sealed class DinosaurInfoCard : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Text titleText;
        [SerializeField] private Text bodyText;

        // ---- 可调阈值（Inspector 可调，代码默认值即烘焙值）----
        [SerializeField] private float enterDistance = 30f;
        [SerializeField] private float exitDistance = 38f;
        [SerializeField] private float enterCenterRadius = 0.20f;
        [SerializeField] private float exitCenterRadius = 0.30f;

        private static DinosaurInfoCard instance;
        private float fadeTarget;
        private const float FadeSpeed = 4f;

        // ---- 运行期状态 ----
        private Camera cachedCamera;
        private DinosaurAgent[] agentCache;
        private DinosaurAgent currentTarget;

        private void Awake()
        {
            instance = this;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
            }
        }

        private void Start()
        {
            cachedCamera = Camera.main;
            agentCache = FindObjectsOfType<DinosaurAgent>();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        /// <summary>手动显示信息卡（保留为外部逃生通道）。</summary>
        public static void Show(string title, string body)
        {
            if (instance == null) return;
            instance.ApplyText(title, body);
            instance.currentTarget = null; // 打断自动锁定
            instance.fadeTarget = 1f;
        }

        /// <summary>手动隐藏信息卡。</summary>
        public static void Hide()
        {
            if (instance == null) return;
            instance.currentTarget = null;
            instance.fadeTarget = 0f;
        }

        private void LateUpdate()
        {
            // 惰性获取相机（防御首帧 null）
            if (cachedCamera == null)
            {
                cachedCamera = Camera.main;
                if (cachedCamera == null) return;
            }
            // 惰性获取 Agent 列表
            if (agentCache == null || agentCache.Length == 0)
            {
                agentCache = FindObjectsOfType<DinosaurAgent>();
                if (agentCache.Length == 0) return;
            }

            EvaluateAutoDisplay();
        }

        private void Update()
        {
            if (canvasGroup == null) return;
            if (Mathf.Approximately(canvasGroup.alpha, fadeTarget)) return;
            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, fadeTarget, Time.deltaTime * FadeSpeed);
        }

        // ---- 自动检测核心 ----

        private void EvaluateAutoDisplay()
        {
            DinosaurAgent best = null;
            float bestCenterSqr = float.MaxValue;
            float currentTargetCenterSqr = float.MaxValue;

            float enterR2 = enterCenterRadius * enterCenterRadius;
            float exitR2 = exitCenterRadius * exitCenterRadius;
            float enterDist2 = enterDistance * enterDistance;
            float exitDist2 = exitDistance * exitDistance;

            Vector3 camPos = cachedCamera.transform.position;

            for (int i = 0; i < agentCache.Length; i++)
            {
                DinosaurAgent agent = agentCache[i];
                if (agent == null) continue;

                // 距离预筛（sqrMagnitude 避免 Sqrt 开销）
                Vector3 agentPos = agent.transform.position;
                float distSqr = (camPos - agentPos).sqrMagnitude;

                // 迟滞：当前目标用宽松退出阈值，其他候选用严格进入阈值
                float maxDist2 = (agent == currentTarget) ? exitDist2 : enterDist2;
                if (distSqr > maxDist2) continue;

                // 视口中心检测
                Vector3 focusPoint = agentPos + Vector3.up * agent.ViewFocusHeight;
                Vector3 vp = cachedCamera.WorldToViewportPoint(focusPoint);
                if (vp.z <= 0f) continue; // 在相机后方

                float dx = vp.x - 0.5f;
                float dy = vp.y - 0.5f;
                float centerSqr = dx * dx + dy * dy;

                // 迟滞：当前目标用宽松退出半径
                float maxR2 = (agent == currentTarget) ? exitR2 : enterR2;
                if (centerSqr > maxR2) continue;

                // 记录当前目标的分数（用于后续粘性比较）
                if (agent == currentTarget)
                    currentTargetCenterSqr = centerSqr;

                if (centerSqr < bestCenterSqr)
                {
                    bestCenterSqr = centerSqr;
                    best = agent;
                }
            }

            // 当前目标粘性：如果 currentTarget 仍有效且新候选未显著更优，保持当前
            if (currentTarget != null && best != currentTarget && currentTargetCenterSqr <= exitR2)
            {
                // 新候选的 centerSqr 需小于当前目标的 70% 才值得切换
                if (bestCenterSqr > currentTargetCenterSqr * 0.7f)
                {
                    best = currentTarget;
                }
            }

            // 切换逻辑
            if (best != currentTarget)
            {
                currentTarget = best;
                if (best != null)
                    ApplyText(best.DisplayName, best.Description);
                fadeTarget = best != null ? 1f : 0f;
            }
            else if (best == null)
            {
                fadeTarget = 0f;
            }
            else
            {
                fadeTarget = 1f;
            }
        }

        private void ApplyText(string title, string body)
        {
            string displayBody = FormatBody(body);
            if (titleText != null && titleText.text != title)
                titleText.text = title;
            if (bodyText != null && bodyText.text != displayBody)
                bodyText.text = displayBody;
        }

        /// <summary>跳过描述首行（格式为 "中文名  EnglishName\n\n正文..."）。</summary>
        private static string FormatBody(string body)
        {
            if (string.IsNullOrEmpty(body)) return body;
            int split = body.IndexOf("\n\n");
            return split >= 0 ? body.Substring(split + 2) : body;
        }
    }
}
