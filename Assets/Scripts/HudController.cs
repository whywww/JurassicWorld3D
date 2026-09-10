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
        [SerializeField] private int expectedDinosaurCount = 5;

        private static Text errorTextRef;
        private static Text statusTextRef;
        private static int expectedCountRef = 5;
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
            ShowStatus(loadedCount >= expectedCountRef
                ? expectedCountRef + " 种恐龙已就绪：霸王龙 / 胜王龙 / 伶盗龙 / 阿根廷龙 / 风神翼龙"
                : "已加载 " + loadedCount + "/" + expectedCountRef + "：" + displayName);
        }
    }
}
