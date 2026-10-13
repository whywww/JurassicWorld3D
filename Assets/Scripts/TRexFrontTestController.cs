using UnityEngine;
using UnityEngine.SceneManagement;

namespace JurassicWorld
{
    [DefaultExecutionOrder(-1200)]
    public sealed class TRexFrontTestController : MonoBehaviour
    {
        public const string ScenePath = "Assets/Scenes/TRexFrontInteractionTest.unity";
        public DinosaurAgent dinosaur;
        public Camera playerCamera;
        private bool triggered;
        private GUIStyle label, title;
        public bool Triggered => triggered;
        public bool IsInsideTrigger => playerCamera != null && Mathf.Abs(playerCamera.transform.position.x) <= 2f
            && playerCamera.transform.position.z >= 30f && playerCamera.transform.position.z <= 32f;

        private void Awake()
        {
            dinosaur.ArmFrontInteractionTest();
            // The independent flat test uses its own baked area; Main keeps its existing activity bounds.
            FindObjectOfType<DinosaurInteractionSettings>().activityBoundsOverride = new Rect(-150f, -140f, 300f, 300f);
        }
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.R)) { Reload(); return; }
            if (!triggered && IsInsideTrigger) triggered = dinosaur.TriggerFrontInteractionTest();
        }
        public void Reload()
        {
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(gameObject.scene.name);
#endif
        }
        private void OnGUI()
        {
            if (dinosaur == null || playerCamera == null) return;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
                title = new GUIStyle(label) { fontSize = 22, fontStyle = FontStyle.Bold };
            }
            float distance = Vector2.Distance(new Vector2(playerCamera.transform.position.x, playerCamera.transform.position.z),
                new Vector2(dinosaur.transform.position.x, dinosaur.transform.position.z));
            GUI.Box(new Rect(12, 12, 590, 190), GUIContent.none);
            GUI.Label(new Rect(28, 22, 550, 30), "T-REX FRONT INTERACTION TEST", title);
            GUI.Label(new Rect(28, 58, 550, 30), $"Stage: {(triggered ? dinosaur.FrontInteractionStage : "Waiting for yellow zone")}    Distance: {distance:F1} m", label);
            GUI.Label(new Rect(28, 88, 550, 28), $"Animation: {dinosaur.CurrentAnimationSourceName}  {dinosaur.FrontActionPlaybackTime:F2}s"
                + (dinosaur.FrontActionWaitingForSpace ? "  " + dinosaur.FrontBlockingReason : ""), label);
            GUI.Label(new Rect(28, 120, 550, 76), "W/S: forward/back  |  A/D: turn  |  Right mouse: look  |  R: reset\nEnter the yellow 30-32m zone, then stop for discovery.\nFollow: walk only. Near: stay to return, retreat beyond 15m to resume.", label);
        }
    }
}
