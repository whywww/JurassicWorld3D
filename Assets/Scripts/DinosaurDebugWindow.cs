using UnityEngine;

namespace JurassicWorld
{
    [DisallowMultipleComponent]
    public sealed class DinosaurDebugWindow : MonoBehaviour
    {
        public DinosaurAgent dinosaur;
        public Camera playerCamera;
        public bool visibleOnStart;
        public KeyCode toggleKey = KeyCode.F4;
        public bool anchorBottom = true;
        public bool showResetHint;
        public bool IsVisible { get; private set; }
        private GUIStyle label, title;

        private void Awake() => IsVisible = visibleOnStart;
        private void Update()
        {
            if (Input.GetKeyDown(toggleKey)) IsVisible = !IsVisible;
        }

        private void OnGUI()
        {
            if (!IsVisible || dinosaur == null || playerCamera == null) return;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
                title = new GUIStyle(label) { fontSize = 22, fontStyle = FontStyle.Bold };
            }
            const float width = 650f, height = 416f;
            float scale = Mathf.Max(.1f, Mathf.Min(1f, (Screen.width - 24f) / width, (Screen.height - 24f) / height));
            float y = anchorBottom ? Mathf.Max(12f, Screen.height - height * scale - 12f) : 12f;
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(12f, y, 0f), Quaternion.identity, Vector3.one * scale);
            float distance = Vector2.Distance(new Vector2(playerCamera.transform.position.x, playerCamera.transform.position.z),
                new Vector2(dinosaur.transform.position.x, dinosaur.transform.position.z));
            string stage = dinosaur.IsFrontInteractionActive ? dinosaur.FrontInteractionStage
                : dinosaur.InteractionCooldownRemaining > 0f ? $"Inactive (Cooldown: {dinosaur.InteractionCooldownRemaining:F1}s)"
                : dinosaur.IsWaitingForInteraction ? "Waiting for interaction" : dinosaur.InteractionPhase.ToString();
            GUI.Box(new Rect(0, 0, width, height), GUIContent.none);
            GUI.Label(new Rect(16, 10, 610, 30), "Debug window", title);
            GUI.Label(new Rect(16, 42, 610, 28), $"Angle: {dinosaur.PlayerInteractionAngle:F1} degrees    Distance: {distance:F1} m", label);
            GUI.Label(new Rect(16, 74, 610, 28), $"Stage: {stage}", label);
            GUI.Label(new Rect(16, 104, 610, 28), $"Animation: {dinosaur.CurrentAnimationSourceName}  {dinosaur.FrontActionPlaybackTime:F2}s", label);
            GUI.Label(new Rect(16, 134, 610, 28), "Blocked: " + (string.IsNullOrEmpty(dinosaur.FrontBlockingReason) ? "--" : dinosaur.FrontBlockingReason), label);
            GUI.Label(new Rect(16, 164, 610, 28), $"Visibility: {dinosaur.PlayerVisibilityStatus}    Lost: {dinosaur.PlayerLostSightTime:F2}s", label);
            GUI.Label(new Rect(16, 194, 610, 28), "End reason: " + dinosaur.FrontEndReason, label);
            GUI.Label(new Rect(16, 224, 610, 52), "Last walk block: " + dinosaur.LastFrontSpaceFailure, label);
            GUI.Label(new Rect(16, 280, 610, 28), $"Return: {dinosaur.FrontExitStatus}  {dinosaur.FrontExitAngle:F0} degrees  target={dinosaur.FrontExitConnection:F1}", label);
            GUI.Label(new Rect(16, 310, 610, 52), "Return failure: " + dinosaur.FrontExitFailure, label);
            GUI.Label(new Rect(16, 378, 610, 28), $"{toggleKey}: show/hide" + (showResetHint ? "  |  R: reset" : ""), label);
            GUI.matrix = previousMatrix;
        }
    }
}
