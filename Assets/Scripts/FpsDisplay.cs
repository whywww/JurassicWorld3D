using UnityEngine;

namespace JurassicWorld
{
    public sealed class FpsDisplay : MonoBehaviour
    {
        private bool visible;
        private int frameCount;
        private double sampleStart;
        private string displayText = "FPS: --";
        private GUIStyle labelStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            var display = new GameObject("FPS Display");
            DontDestroyOnLoad(display);
            display.AddComponent<FpsDisplay>();
        }

        private void Awake()
        {
            sampleStart = Time.realtimeSinceStartupAsDouble;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.L))
                visible = !visible;

            frameCount++;
            double now = Time.realtimeSinceStartupAsDouble;
            double elapsed = now - sampleStart;
            if (elapsed < 0.5)
                return;

            displayText = "FPS: " + Mathf.RoundToInt((float)(frameCount / elapsed));
            frameCount = 0;
            sampleStart = now;
        }

        private void OnGUI()
        {
            if (!visible)
                return;

            if (labelStyle == null)
                labelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 18,
                    wordWrap = false,
                    normal = { textColor = Color.white }
                };

            var rect = new Rect(Mathf.Max(0f, Screen.width - 136f), Mathf.Max(0f, Screen.height - 48f), 120f, 32f);
            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(rect, displayText, labelStyle);
            GUI.color = previousColor;
        }
    }
}
