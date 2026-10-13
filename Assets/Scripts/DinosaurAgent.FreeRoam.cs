using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        public enum RoamingMode { [InspectorName("路线漫游")] Route, [InspectorName("自由漫游")] Free }
        [Header("Daily roaming")]
        [SerializeField, InspectorName("漫游方式"), Tooltip("仅启动时读取。自由漫游需要此恐龙的导航、身体和动作数据。")]
        private RoamingMode roamingMode;
        private RoamingMode startupRoamingMode;
        public bool IsFreeRoaming => startupRoamingMode == RoamingMode.Free;
        public bool UsesRouteScheduling => !IsFreeRoaming && !IsOffRouteInteraction;
        private Vector3 freeGoal;
        private int freeReplanFailures;
        private bool freeTurnRelocation, freeNavigationWarning, freeRetryGoal;

        private bool FreeNavigationReady()
        {
            if (interactionSettings == null) interactionSettings = FindObjectOfType<DinosaurInteractionSettings>();
            if (curiosityPath == null) curiosityPath = new NavMeshPath();
            return interactionSettings != null && interactionSettings.NavigationReady && interactionBody != null && HasFootPhaseData;
        }

        internal bool TryFreeSpawn(bool hasCamera, Vector2 camera, List<Vector2> occupied, List<float> radii,
            System.Random spawnRandom, out Vector2 position)
        {
            position = default;
            if (!FreeNavigationReady()) return false;
            Rect area = WorldBounds.DinosaurActivityXZ;
            for (int attempt = 0; attempt < 400; attempt++)
            {
                Vector3 candidate = new Vector3(Mathf.Lerp(area.xMin, area.xMax, (float)spawnRandom.NextDouble()), MovementHeight,
                    Mathf.Lerp(area.yMin, area.yMax, (float)spawnRandom.NextDouble()));
                if (!NavMesh.SamplePosition(candidate, out var hit, 2f, NavigationFilter)) continue;
                Vector2 p = new Vector2(hit.position.x, hit.position.z);
                if (hasCamera && Vector2.Distance(p, camera) < 25f) continue;
                bool overlap = false;
                for (int i = 0; i < occupied.Count; i++)
                    if (Vector2.Distance(p, occupied[i]) < CollisionRadius + radii[i] + 6f) { overlap = true; break; }
                if (overlap) continue;
                float yaw = (float)spawnRandom.NextDouble() * 360f;
                Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                if (interactionSettings.PoseBlocked(p, new Vector2(forward.x, forward.z), interactionBody)) continue;
                transform.SetPositionAndRotation(new Vector3(p.x, MovementHeight, p.y), Quaternion.Euler(0f, yaw - VisualYaw, 0f));
                spawnPoint = transform.position;
                MovementCorridorCrossings.Release(this);
                GetComponentInChildren<DinosaurFootLock>()?.ResetSupportAnchors();
                position = p;
                return true;
            }
            return false;
        }

        private void FreeIdle(float delay)
        {
            hasCuriosityPath = false;
            freeTurnRelocation = false;
            freeRetryGoal = false;
            curiosityBlockedTime = 0f;
            currentInteractionSpeed = actualInteractionSpeed = 0f;
            BeginIdle();
            stateTimer = delay;
        }

        private bool PickFreeGoal()
        {
            Vector3 forward = GetVisualForward();
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float angle = random.NextDouble() < .7 ? Range(-75f, 75f) : Range(-180f, 180f);
                Vector3 candidate = transform.position + Quaternion.Euler(0f, angle, 0f) * forward * Range(12f, 30f);
                if (!NavMesh.SamplePosition(candidate, out var hit, 2f, NavigationFilter)) continue;
                float distance = FlatDistance(transform.position, hit.position);
                if (distance < 12f || distance > 30f) continue;
                Vector3 arrival = hit.position - transform.position; arrival.y = 0f;
                arrival.Normalize();
                if (interactionSettings.PoseBlocked(new Vector2(hit.position.x, hit.position.z), new Vector2(arrival.x, arrival.z), interactionBody)
                    || FindBlockingPeer(new Vector2(hit.position.x, hit.position.z)) != null) continue;
                if (!PlanCuriosityPath(hit.position, true)) continue;
                freeGoal = hit.position;
                freeReplanFailures = 0;
                freeTurnRelocation = false;
                curiosityBlockedTime = 0f;
                CurrentState = DinosaurState.RandomWalk;
                MovementCorridorCrossings.Release(this);
                return true;
            }
            FreeIdle(2f);
            return false;
        }

        private bool FindFreeTurnSpace(float angle)
        {
            if (freeTurnRelocation) return false;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector3 direction = Quaternion.Euler(0f, Range(-25f, 25f), 0f) * GetVisualForward();
                Vector3 candidate = transform.position + direction * Range(2f, 6f);
                if (!NavMesh.SamplePosition(candidate, out var hit, .5f, NavigationFilter)
                    || NavMesh.Raycast(transform.position, hit.position, out _, NavigationFilter)) continue;
                Quaternion rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(0f, -VisualYaw, 0f);
                if (!StepPoseSafe(transform.position, hit.position, transform.rotation, rotation, false)
                    || !FreeTurnFitsAt(hit.position, rotation, angle)) continue;
                if (!PlanCuriosityPath(hit.position, true)) continue;
                freeTurnRelocation = true;
                curiosityBlockedTime = 0f;
                return true;
            }
            return false;
        }

        private bool UpdateFreeRoaming()
        {
            if (!IsFreeRoaming) return false;
            if (!FreeNavigationReady())
            {
                PlayClip(idleClip);
                if (!freeNavigationWarning) { Debug.LogWarning("[Free roaming] Missing navigation/body/foot data; waiting safely.", this); freeNavigationWarning = true; }
                return true;
            }
            if (CurrentState == DinosaurState.Idle)
            {
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f)
                {
                    if (freeRetryGoal)
                    {
                        freeRetryGoal = false;
                        if (PlanCuriosityPath(freeGoal, true)) CurrentState = DinosaurState.RandomWalk;
                        else FreeIdle(2f);
                    }
                    else PickFreeGoal();
                }
                return true;
            }
            if (!hasCuriosityPath) { FreeIdle(2f); return true; }
            if (PathFinished())
            {
                if (freeTurnRelocation)
                {
                    freeTurnRelocation = false;
                    if (!PlanCuriosityPath(freeGoal, true)) FreeIdle(2f);
                }
                else FreeIdle(Range(3f, 7f));
                return true;
            }
            bool moved = MoveCuriosity(WalkSpeed, false);
            if (IsSteppingTurn) return true;
            if (moved) PlayMovementClip(WalkSpeed); else PlayClip(idleClip);
            if (curiosityBlockedTime >= 2f)
            {
                curiosityBlockedTime = 0f;
                if (++freeReplanFailures >= 2) FreeIdle(2f);
                else if (!PlanCuriosityPath(freeGoal, true))
                {
                    // Keep the first failure stopped for a second attempt instead of abandoning immediately.
                    CurrentState = DinosaurState.Idle;
                    stateTimer = 2f;
                    freeRetryGoal = true;
                }
            }
            return true;
        }

        private void FinishFreeInteraction()
        {
            ClearInteractionNavigation();
            InteractionPhase = PlayerInteractionPhase.Inactive;
            cooldownUntil = Time.time + interactionCooldown;
            isReturning = false;
            curiosityMotion = null;
            freeReplanFailures = 0;
            FreeIdle(Range(3f, 7f));
        }
    }
}
