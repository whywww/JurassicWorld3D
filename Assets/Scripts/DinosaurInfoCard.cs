using UnityEngine;
using UnityEngine.UI;

namespace JurassicWorld
{
    // 信息卡现在是场景预置对象：Canvas / Background / Text 层级由 JurassicWorldSceneBuilder 在编辑态
    // 烘焙好，并通过 [SerializeField] 引用挂上（字体使用可序列化的内置字体资产，不再用不可序列化的
    // Font.CreateDynamicFontFromOSFont）。运行行为保持不变：LateUpdate 朝向相机 + 按条件淡入淡出。
    public sealed class DinosaurInfoCard : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private DinosaurAgent agent;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Text descriptionText;

        private void Awake()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
            }
        }

        private void LateUpdate()
        {
            if (viewCamera == null || agent == null || canvasGroup == null)
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
