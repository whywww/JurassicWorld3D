using UnityEngine;
using UnityEngine.UI;

namespace JurassicWorld
{
    // HUD 现在是场景预置对象：Canvas 与各 Text 由 JurassicWorldSceneBuilder 在编辑态烘焙好并通过
    // [SerializeField] 引用挂上。Awake() 把实例引用注册回静态成员，保证 DinosaurAgent / 绑定器里
    // 现有的静态调用（ShowStatus / ShowError / ReportLoaded）继续工作。
    public sealed class HudController : MonoBehaviour
    {
        [SerializeField] private Text errorText;
        [SerializeField] private Text statusText;
        [SerializeField] private Text helpText;
        [SerializeField] private int expectedDinosaurCount = 2;

        private static Text errorTextRef;
        private static Text statusTextRef;
        private static int expectedCountRef = 2;
        private static int loadedCount;

        private void Awake()
        {
            errorTextRef = errorText;
            statusTextRef = statusText;
            expectedCountRef = Mathf.Max(1, expectedDinosaurCount);
            loadedCount = 0;
        }

        public static void ShowError(string message)
        {
            if (errorTextRef != null)
                errorTextRef.text = message;
        }

        public static void ShowStatus(string message)
        {
            if (statusTextRef != null)
                statusTextRef.text = message;
        }

        public static void ReportLoaded(string displayName)
        {
            loadedCount++;
            // 就绪文案只报数量、不再硬编码恐龙名字：名字清单的唯一来源是
            // JurassicWorldSceneBuilder.CreateTemplates()，此处重复一份会在增删恐龙时失同步。
            ShowStatus(loadedCount >= expectedCountRef
                ? expectedCountRef + " 种恐龙已就绪"
                : "已加载 " + loadedCount + "/" + expectedCountRef + "：" + displayName);
        }
    }
}
