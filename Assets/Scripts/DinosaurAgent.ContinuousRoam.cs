using UnityEngine;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        private bool IsContinuousWalker => IsContinuousRoaming;
        [Header("Predictive walking (all ground dinosaurs)")]
        [SerializeField, Min(.5f), InspectorName("行走前视距离（米）")] private float continuousLookAheadDistance = 3f;
        [SerializeField, Min(.5f), InspectorName("最小转弯半径（米）")] private float continuousMinTurnRadius = 2.5f;
        [SerializeField, Min(3f), InspectorName("提前绕障距离（米）")] private float continuousProbeDistance = 3f;
        [SerializeField, Min(1f), InspectorName("转速变化上限（度/秒²）")] private float continuousYawAcceleration = 30f;
        private float continuousProbeRemaining, continuousYawRate, continuousTargetYawRate, continuousGoalRetry;
        private Vector3 continuousProbeTarget;
        private bool continuousArcBlocked;
        public string ContinuousRoamingStatus { get; private set; } = "Inactive";

        private void ResetContinuousSteering()
        {
            continuousProbeRemaining = continuousYawRate = continuousTargetYawRate = continuousGoalRetry = 0f;
            continuousArcBlocked = false;
        }
        private float ContinuousMaximumYawRate => Mathf.Min(gaitProfile != null ? gaitProfile.turnSpeed : 45f,
            SteeringSpeed / Mathf.Max(.5f, continuousMinTurnRadius) * Mathf.Rad2Deg);

        private float ContinuousPathRemaining()
        {
            float distance = 0f; Vector3 previous = transform.position;
            for (int i = curiosityCorner; i < curiosityCorners.Length; i++)
            { distance += FlatDistance(previous, curiosityCorners[i]); previous = curiosityCorners[i]; }
            return distance;
        }
        private bool ContinuousGoalHasExit(Vector3[] corners)
        {
            if (corners.Length < 2) return false;
            Vector3 end = corners[corners.Length - 1];
            Vector3 forward = end - corners[corners.Length - 2]; forward.y = 0f;
            if (forward.sqrMagnitude < .001f) return false;
            Quaternion rotation = Quaternion.LookRotation(forward) * Quaternion.Euler(0f, -VisualYaw, 0f);
            Vector3 next = end + forward.normalized * Mathf.Max(3f, continuousLookAheadDistance);
            return StepPoseSafe(end, next, rotation, rotation, false);
        }
        private bool TryContinueGoal()
        {
            var corners = curiosityCorners; int corner = curiosityCorner;
            bool path = hasCuriosityPath; Vector3 goal = freeGoal;
            if (PickFreeGoalCore(false)) { continuousProbeRemaining = 0f; freeRetryGoal = false; return true; }
            curiosityCorners = corners; curiosityCorner = corner; hasCuriosityPath = path; freeGoal = goal;
            return false;
        }
        private bool UpdateContinuousRoaming(float deltaTime)
        {
            CurrentState = DinosaurState.RandomWalk;
            stateTimer = 0f;
            continuousGoalRetry -= deltaTime;
            // Spawning already validated a short forward exit; use it before
            // searching farther through the forest for a new roaming goal.
            if (freeRetryGoal && !hasCuriosityPath)
            {
                freeRetryGoal = false;
                if (PlanCuriosityPath(freeGoal, true)) continuousProbeRemaining = 0f;
            }
            bool finished = !hasCuriosityPath || PathFinished();
            if (continuousGoalRetry <= 0f && (finished || ContinuousPathRemaining() < continuousLookAheadDistance))
            {
                TryContinueGoal();
                continuousGoalRetry = .25f;
            }
            bool moved = hasCuriosityPath && !PathFinished() && MoveContinuousPath(deltaTime);
            if (moved)
            {
                PlayMovementClip(WalkSpeed); RoamingBlockReason = "";
                ContinuousRoamingStatus = Mathf.Abs(continuousYawRate) > .1f ? "Walking curve" : "Walking";
            }
            else
            {
                PlayClip(idleClip);
                ContinuousRoamingStatus = "Waiting for safe walking arc";
                RoamingBlockReason = ContinuousRoamingStatus;
                if (continuousGoalRetry <= 0f)
                { TryContinueGoal(); continuousGoalRetry = .25f; continuousProbeRemaining = 0f; }
            }
            return true;
        }
    }
}
