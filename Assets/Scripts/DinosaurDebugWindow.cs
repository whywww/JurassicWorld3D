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
            const float width = 650f, height = 314f;
            float scale = Mathf.Max(.1f, Mathf.Min(1f, (Screen.width - 24f) / width, (Screen.height - 24f) / height));
            float y = anchorBottom ? Mathf.Max(12f, Screen.height - height * scale - 12f) : 12f;
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(12f, y, 0f), Quaternion.identity, Vector3.one * scale);
            float distance = Vector2.Distance(new Vector2(playerCamera.transform.position.x, playerCamera.transform.position.z),
                new Vector2(dinosaur.transform.position.x, dinosaur.transform.position.z));
            string stage = dinosaur.IsFrontInteractionActive ? dinosaur.FrontInteractionStage
                : dinosaur.InteractionCooldownRemaining > 0f ? $"Cooldown: {dinosaur.InteractionCooldownRemaining:F1}s"
                : dinosaur.IsWaitingForInteraction ? (dinosaur.IsFreeRoaming ? "Free roam" : "Waiting")
                : dinosaur.InteractionPhase.ToString();
            int stageDetail = stage.IndexOf(" (");
            if (stageDetail >= 0) stage = stage.Substring(0, stageDetail);
            if (dinosaur.IsEmergencyEscaping) stage = "Emergency escape";
            string visibility = dinosaur.PlayerVisibilityStatus;
            if (dinosaur.PlayerLostSightTime > 0f) visibility += $"   lost {dinosaur.PlayerLostSightTime:F2}s";
            string reason = "--";
            if (dinosaur.IsFrontInteractionActive)
            {
                if (dinosaur.FrontEndReason != "--") reason = "End · " + dinosaur.FrontEndReason;
                else if (!string.IsNullOrEmpty(dinosaur.FrontBlockingReason)) reason = "Blocked · " + dinosaur.FrontBlockingReason;
                else if (dinosaur.FrontActionWaitingForSpace) reason = "No safe space";
            }
            GUI.Box(new Rect(0, 0, width, height), GUIContent.none);
            GUI.Label(new Rect(16, 10, 610, 30), "Debug window", title);
            GUI.Label(new Rect(16, 42, 610, 28), $"Angle: {dinosaur.PlayerInteractionAngle:F1}°   Distance: {distance:F1} m   Visibility: {visibility}", label);
            GUI.Label(new Rect(16, 74, 610, 28), $"Stage: {stage}", label);
            GUI.Label(new Rect(16, 104, 610, 28), $"Animation: {dinosaur.CurrentAnimationSourceName}  {dinosaur.CurrentAnimationPlaybackTime:F2}s", label);
            GUI.Label(new Rect(16, 134, 610, 28), "Reason: " + reason, label);
            GUI.Label(new Rect(16, 164, 610, 28), $"Priority: {dinosaur.GroundPriorityRank}   Player interaction: {dinosaur.HasInteractionRightOfWay}", label);
            GUI.Label(new Rect(16, 194, 610, 48), $"Traffic: {dinosaur.GroundTrafficStatus} | {dinosaur.GroundTrafficBlocker} | {dinosaur.GroundTrafficDuration:F1}s", label);
            GUI.Label(new Rect(16, 244, 610, 28), $"Stall: {dinosaur.NoMovementDuration:F1}/8s | {dinosaur.EmergencyEscapeStatus}", label);
            GUI.Label(new Rect(16, 280, 610, 28), $"{toggleKey}: show/hide" + (showResetHint ? "  |  R: reset" : ""), label);
            GUI.matrix = previousMatrix;
        }
    }
}
