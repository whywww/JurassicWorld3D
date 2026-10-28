using UnityEditor;
using UnityEngine;
namespace JurassicWorld.BCI.MindWave.Editor
{
    public static class MindWaveSettingsWindow
    {
        [MenuItem("Jurassic World/BCI/MindWave Settings")]
        public static void Open()
        {
            var input = Object.FindObjectOfType<MindWavePlayerInput>(true);
            if (input == null)
            {
                Debug.LogWarning("[MindWave] 请打开主场景，在 Main Camera 的 Inspector 中选择脑控组件。");
                return;
            }
            Selection.activeGameObject = input.gameObject;
            EditorGUIUtility.PingObject(input);
        }
    }
}
