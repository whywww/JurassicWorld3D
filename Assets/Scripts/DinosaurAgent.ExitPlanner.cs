using System;
using UnityEngine;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        // Departure diagnostics kept under the original public API. The route itself
        // is now the recorded follow trail walked in reverse; no exit is predicted.
        private string frontExitStatus = "Inactive", frontExitFailure;
        private int frontExitQueriesThisFrame, frontExitQueriesTotal, frontExitSearches, frontExitReviews;
        private long frontExitAllocated;
        private bool frontExitAllocationAvailable;
        private float frontExitMaxQueryMs, frontExitMaxPoseMs;
        private int frontExitPoseOverruns, frontExitBudgetOverruns, frontExitFootSafetyChecks;
        private float frontExitFootSafetyTotalMs, frontExitFootSafetyMaxMs;
        private Vector3 frontReturnConnection;
        private float frontReturnAngle;
        public string FrontExitStatus => frontActive ? frontExitStatus : "Inactive";
        public string FrontExitFailure => frontExitFailure ?? "--";
        public Vector3[] FrontExitPath => frontActive ? frontTrail.ToArray() : Array.Empty<Vector3>();
        public Vector3 FrontExitConnection => frontActive ? frontReturnConnection : transform.position;
        public float FrontExitAngle => frontActive ? frontReturnAngle : 0f;
        public int FrontExitPathQueries => frontExitQueriesTotal;
        public int FrontExitLastFrameQueries => frontExitQueriesThisFrame;
        public int FrontExitBudgetOverruns => frontExitBudgetOverruns;
        public float FrontExitMaxQueryMs => frontExitMaxQueryMs;
        public float FrontExitMaxPoseMs => frontExitMaxPoseMs;
        public int FrontExitPoseOverruns => frontExitPoseOverruns;
        public int FrontExitFootSafetyChecks => frontExitFootSafetyChecks;
        public float FrontExitFootSafetyTotalMs => frontExitFootSafetyTotalMs;
        public float FrontExitFootSafetyMaxMs => frontExitFootSafetyMaxMs;
        public long FrontExitAllocatedBytes => frontExitAllocated;
        public bool FrontExitAllocationAvailable => frontExitAllocationAvailable;
        public int FrontExitSearchCount => frontExitSearches;
        public int FrontExitReviewCount => frontExitReviews;
        public float[] FrontExitCostSamples => Array.Empty<float>();

        private void ResetFrontExitPlanner()
        {
            frontExitQueriesTotal = frontExitQueriesThisFrame = frontExitBudgetOverruns = 0;
            frontExitSearches = frontExitReviews = 0; frontExitAllocated = 0;
            frontExitMaxQueryMs = frontExitMaxPoseMs = 0f;
            frontExitPoseOverruns = frontExitFootSafetyChecks = 0;
            frontExitFootSafetyTotalMs = frontExitFootSafetyMaxMs = 0f;
            long allocationProbe = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(new byte[128]);
            frontExitAllocationAvailable = GC.GetAllocatedBytesForCurrentThread() > allocationProbe;
            frontExitStatus = "Inactive"; frontExitFailure = null;
            frontReturnConnection = transform.position; frontReturnAngle = 0f;
        }
        private void UpdateFrontReturnDiagnostics()
        {
            frontExitQueriesThisFrame = 0;
            if (!frontActive)
            {
                frontExitStatus = "Inactive"; frontReturnConnection = transform.position; frontReturnAngle = 0f;
                return;
            }
            frontExitStatus = frontWaiting || frontPeerClearing ? "WaitingForClearance"
                : frontStage == FrontStage.TurnBack ? "TurningBack"
                : frontStage == FrontStage.ReturnWalk ? "Returning" : "Following";
            Vector3 target = transform.position + GetVisualForward();
            if (frontStage == FrontStage.ReturnWalk && frontReturnIndex >= 0 && frontReturnIndex < frontTrail.Count)
                target = frontTrail[frontReturnIndex];
            else if (hasCuriosityPath && curiosityCorner < curiosityCorners.Length)
                target = curiosityCorners[curiosityCorner];
            Vector3 delta = target - transform.position; delta.y = 0f;
            frontReturnAngle = delta.sqrMagnitude < .0001f ? 0f : Vector3.SignedAngle(GetVisualForward(), delta, Vector3.up);
        }
        private void OnDrawGizmosSelected()
        {
            if (!frontActive || frontTrail.Count < 2) return;
            Gizmos.color = Color.cyan;
            for (int i = 1; i < frontTrail.Count; i++)
                Gizmos.DrawLine(frontTrail[i - 1] + Vector3.up * .3f, frontTrail[i] + Vector3.up * .3f);
            if (frontStage == FrontStage.ReturnWalk && frontReturnIndex >= 0 && frontReturnIndex < frontTrail.Count)
            { Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(frontTrail[frontReturnIndex] + Vector3.up * .3f, .4f); }
        }
    }
}
