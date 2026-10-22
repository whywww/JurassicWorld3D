using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        public enum RoamingMode { [InspectorName("路线漫游")] Route, [InspectorName("自由漫游")] Free }
        public enum DinosaurSpecies { Other, Tyrannosaurus, Velociraptor, Pachycephalosaurus, Sauropelta, Baurusuchus,
            Rajasaurus, Stegosaurus, Argentinosaurus, Protoceratops }
        private bool UsesSimpleFreeRoam => species == DinosaurSpecies.Velociraptor || species == DinosaurSpecies.Pachycephalosaurus
            || species == DinosaurSpecies.Sauropelta || species == DinosaurSpecies.Baurusuchus
            || species == DinosaurSpecies.Protoceratops;
        [SerializeField] private DinosaurSpecies species;
        [SerializeField, Min(1f), InspectorName("霸王龙安全距离（米）")] private float safetyDistance = 15f;
        [SerializeField, Min(1f), InspectorName("避让解除距离（米）")] private float safetyReleaseDistance = 20f;
        public DinosaurSpecies Species => species;
        public bool IsAvoidingTyrannosaurus { get; private set; }
        public string AvoidanceStatus { get; private set; } = "Inactive";
        [Header("Daily roaming")]
        [SerializeField, InspectorName("漫游方式"), Tooltip("仅启动时读取。自由漫游需要此恐龙的导航、身体和动作数据。")]
        private RoamingMode roamingMode;
        private RoamingMode startupRoamingMode;
        public bool IsFreeRoaming => startupRoamingMode == RoamingMode.Free;
        public bool UsesRouteScheduling => !IsFreeRoaming && !IsOffRouteInteraction;
        private Vector3 freeGoal;
        private int freeReplanFailures;
        private bool freeNavigationWarning, freeRetryGoal;
        private float roamingWalkBlendUntil;
        [SerializeField, InspectorName("记录自由漫游转身诊断")] private bool traceFreeRoamTurns;
        private int freeTurnsWithoutWalk;
        private float freeWalkSinceTurn, freeRoamWalkTravel;
        private bool IsOrdinaryFreeRoam => IsFreeRoaming && species == DinosaurSpecies.Tyrannosaurus
            && InteractionPhase == PlayerInteractionPhase.Inactive && !frontActive
            && !freeRecoveryActive && !IsAvoidingTyrannosaurus;
        public string RoamingBlockReason { get; private set; } = "";
        public string LastMovementBlock { get; private set; } = "";

        private bool NavigationStepFits(Vector3 from, Vector3 to, bool recoverOffMesh = false)
        {
            // Rasterized NavMesh edges need not coincide exactly with the safe
            // geometry pose. Query the two nearby nav points, while the separate
            // body sweep continues to validate the actual, unsnapped root motion.
            // Raycasting from the unsnapped root can report an immediate edge hit
            // even for an in-place rotation, permanently trapping the animal.
            if (!NavMesh.SamplePosition(from, out var start, .25f, NavigationFilter)
                || !NavMesh.SamplePosition(to, out var end, .25f, NavigationFilter)
                || FlatDistance(from, start.position) > .12f || FlatDistance(to, end.position) > .12f)
            {
                // An accepted performance may legally leave the root just outside the
                // baked mesh. Recovery steps may only shorten that distance, so the
                // animal walks back onto navigation instead of being trapped at the
                // start pose; normal walking still requires both endpoints on the mesh.
                if (recoverOffMesh && NavMesh.SamplePosition(from, out start, 1f, NavigationFilter)
                    && NavMesh.SamplePosition(to, out end, 1f, NavigationFilter))
                {
                    float fromOff = FlatDistance(from, start.position), toOff = FlatDistance(to, end.position);
                    if (fromOff <= 1f && toOff <= Mathf.Max(.12f, fromOff) + .002f) return true;
                }
                LastMovementBlock = "NavMesh endpoint"; return false;
            }
            if (FlatDistance(start.position, end.position) > .001f && NavMesh.Raycast(start.position, end.position, out _, NavigationFilter))
            {
                // At a shared polygon vertex the ray query can select the other
                // incident polygon. Require an independently calculated, complete
                // path along this very segment (two millimetres of raster error),
                // never an arbitrary straight-line fallback across an obstacle.
                var edgePath = new NavMeshPath();
                if (!NavMesh.CalculatePath(start.position, end.position, NavigationFilter, edgePath)
                    || edgePath.status != NavMeshPathStatus.PathComplete)
                { LastMovementBlock = "NavMesh projected segment"; return false; }
                float length = 0f;
                var corners = edgePath.corners;
                if (corners.Length < 2) { LastMovementBlock = "NavMesh empty segment"; return false; }
                Vector2 a = new Vector2(start.position.x, start.position.z), b = new Vector2(end.position.x, end.position.z);
                Vector2 segment = b - a;
                foreach (var corner in corners)
                {
                    Vector2 p = new Vector2(corner.x, corner.z);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, segment) / segment.sqrMagnitude);
                    if (Vector2.Distance(p, a + segment * t) > .002f)
                    { LastMovementBlock = "NavMesh detour required"; return false; }
                }
                for (int i = 1; i < corners.Length; i++) length += FlatDistance(corners[i - 1], corners[i]);
                if (length > FlatDistance(start.position, end.position) + .002f)
                { LastMovementBlock = "NavMesh detour required"; return false; }
            }
            return true;
        }

        private Vector3[] InsetNavigationCorners(Vector3[] corners)
        {
            for (int i = 1; i + 1 < corners.Length; i++)
            {
                Vector3 inward = (corners[i - 1] - corners[i]).normalized + (corners[i + 1] - corners[i]).normalized;
                inward.y = 0f;
                if (inward.sqrMagnitude < .0001f) continue;
                foreach (float inset in new[] { .04f, .02f, .01f })
                {
                    Vector3 candidate = corners[i] + inward.normalized * inset;
                    if (!NavMesh.SamplePosition(candidate, out var hit, .05f, NavigationFilter)
                        || FlatDistance(candidate, hit.position) > .002f
                        || !NavigationStepFits(corners[i - 1], hit.position)
                        || !NavigationStepFits(hit.position, corners[i + 1])) continue;
                    corners[i] = hit.position; break;
                }
            }
            return corners;
        }

        private bool NavigationRestPoint(Vector3 point)
        {
            if (!NavMesh.FindClosestEdge(point, out var edge, NavigationFilter) || edge.distance < .05f) return false;
            if (species != DinosaurSpecies.Pachycephalosaurus && species != DinosaurSpecies.Sauropelta
                && species != DinosaurSpecies.Baurusuchus && species != DinosaurSpecies.Protoceratops) return true;
            // The longer animal must be able to turn after resting, rather than
            // finish a valid forward path inside a pocket with no safe next heading.
            Vector2 position = new Vector2(point.x, point.z);
            Vector2 heading = Vector2.up;
            for (int quarter = 0; quarter < 4; quarter++)
            {
                Vector2 next = new Vector2(-heading.y, heading.x);
                if (interactionSettings.StepBlocked(position, position, heading, next, interactionBody)) return false;
                heading = next;
            }
            return true;
        }

        // Navigation is shared with smaller animals. A complete root path alone is
        // insufficient: check body translation; wander turns use the performance gate.
        private bool RoamingPathFits(Vector3 origin, Quaternion rotation, Vector3[] corners, int firstCorner = 0)
        {
            Vector2 previous = new Vector2(origin.x, origin.z);
            Vector3 visual = rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
            Vector2 heading = new Vector2(visual.x, visual.z);
            for (int i = firstCorner; i < corners.Length; i++)
            {
                Vector3 corner = corners[i];
                Vector2 next = new Vector2(corner.x, corner.z), delta = next - previous;
                if (delta.sqrMagnitude < .0064f) continue;
                Vector2 direction = delta.normalized;
                string reason = UsesSimpleFreeRoam
                    ? interactionSettings.StepBlockReason(previous, previous, heading, direction, interactionBody) : null;
                reason = reason ?? interactionSettings.StepBlockReason(previous, next, direction, direction, interactionBody);
                if (reason != null) { RoamingBlockReason = reason + " / path clearance"; return false; }
                heading = direction; previous = next;
            }
            RoamingBlockReason = "";
            return true;
        }

        private bool SpawnExitFits(Vector3 origin, Vector3 forward, out Vector3 exit)
        {
            exit = origin + forward * 6f;
            if (!NavMesh.SamplePosition(exit, out var hit, .25f, NavigationFilter)
                || FlatDistance(exit, hit.position) > .12f
                || !NavigationRestPoint(hit.position) || !NavigationStepFits(origin, hit.position)) return false;
            var from = new Vector2(origin.x, origin.z);
            var to = new Vector2(exit.x, exit.z);
            var heading = new Vector2(forward.x, forward.z);
            if (interactionSettings.StepBlocked(from, to, heading, heading, interactionBody)
                || !SafetySegmentAllowed(from, to, false)) return false;
            exit = hit.position;
            return true;
        }

        private bool FreeNavigationReady()
        {
            if (interactionSettings == null) interactionSettings = FindObjectOfType<DinosaurInteractionSettings>();
            if (curiosityPath == null) curiosityPath = new NavMeshPath();
            return interactionSettings != null && interactionSettings.NavigationReady && interactionBody != null
                && (UsesSimpleFreeRoam || HasFootPhaseData);
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
                if (!NavigationRestPoint(hit.position)) continue;
                if (hasCamera && Vector2.Distance(p, camera) < 25f) continue;
                if (!SafetySegmentAllowed(p, p, false)) continue;
                bool overlap = false;
                for (int i = 0; i < occupied.Count; i++)
                    if (Vector2.Distance(p, occupied[i]) < CollisionRadius + radii[i] + 6f) { overlap = true; break; }
                if (overlap) continue;
                float yaw = (float)spawnRandom.NextDouble() * 360f;
                Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                if (interactionSettings.PoseBlocked(p, new Vector2(forward.x, forward.z), interactionBody)) continue;
                var origin = new Vector3(p.x, MovementHeight, p.y);
                if (!SpawnExitFits(origin, forward, out var exit)) continue;
                for (int i = 0; i < occupied.Count; i++)
                {
                    Vector2 delta = new Vector2(exit.x, exit.z) - p;
                    float t = Mathf.Clamp01(Vector2.Dot(occupied[i] - p, delta) / delta.sqrMagnitude);
                    if (Vector2.Distance(p + delta * t, occupied[i]) < CollisionRadius + radii[i] + 6f) overlap = true;
                }
                if (overlap) continue;
                transform.SetPositionAndRotation(new Vector3(p.x, MovementHeight, p.y), Quaternion.Euler(0f, yaw - VisualYaw, 0f));
                spawnPoint = transform.position;
                freeGoal = exit;
                freeRetryGoal = true;
                MovementCorridorCrossings.Release(this);
                GetComponentInChildren<DinosaurFootLock>()?.ResetSupportAnchors();
                position = p;
                return true;
            }
            return false;
        }

        private void FreeIdle(float delay)
        {
            freeTurnsWithoutWalk = 0;
            freeWalkSinceTurn = 0f;
            roamingWalkBlendUntil = 0f;
            frontFeet?.EndReturnHandoff();
            hasCuriosityPath = false;
            freeRetryGoal = false;
            curiosityBlockedTime = 0f;
            actualInteractionSpeed = 0f;
            BeginIdle();
            stateTimer = delay;
        }

        private bool PickFreeGoal() => PickFreeGoalCore(true);

        private bool PickFreeGoalCore(bool waitOnFailure)
        {
            Vector3 forward = GetVisualForward();
            for (int attempt = 0; attempt < 32; attempt++)
            {
                // Try forward progress first, then explore other reachable headings.
                float angle = IsContinuousWalker && attempt == 0 ? 0f
                    : attempt < 8 ? (attempt / 2 + 1) * 7f * (attempt % 2 == 0 ? 1f : -1f) : Range(-180f, 180f);
                Vector3 candidate = transform.position + Quaternion.Euler(0f, angle, 0f) * forward * (attempt < 8 && !IsContinuousWalker ? Range(4f, 16f) : Range(12f, 30f));
                if (!NavMesh.SamplePosition(candidate, out var hit, 2f, NavigationFilter)) continue;
                if (!NavigationRestPoint(hit.position)) continue;
                float distance = FlatDistance(transform.position, hit.position);
                if (distance < 4f || distance > 30f) continue;
                Vector3 arrival = hit.position - transform.position; arrival.y = 0f;
                arrival.Normalize();
                if (interactionSettings.PoseBlocked(new Vector2(hit.position.x, hit.position.z), new Vector2(arrival.x, arrival.z), interactionBody)
                    || FindBlockingPeer(new Vector2(hit.position.x, hit.position.z)) != null) continue;
                if (!PlanCuriosityPath(hit.position, true)) continue;
                freeGoal = hit.position;
                freeTurnsWithoutWalk = 0;
                freeWalkSinceTurn = 0f;
                freeReplanFailures = 0;
                curiosityBlockedTime = 0f;
                CurrentState = DinosaurState.RandomWalk;
                MovementCorridorCrossings.Release(this);
                return true;
            }
            if (waitOnFailure) FreeIdle(2f);
            RoamingBlockReason = "Waiting for safe exit";
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
            if (Time.time < roamingWalkBlendUntil)
            {
                if (moveState != null) moveState.speed = 0f;
                return true;
            }
            if (freeRecoveryActive) { UpdateFreeRecovery(); return true; }
            if (UpdateSafetyAvoidance()) return true;
            if (IsContinuousWalker) return UpdateContinuousRoaming(Time.deltaTime);
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
                FreeIdle(species == DinosaurSpecies.Pachycephalosaurus ? Range(1f, 2f) : Range(3f, 7f));
                return true;
            }
            bool moved = UsesSimpleFreeRoam ? MoveRaptorFree() : MoveCuriosity(WalkSpeed);
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
            freeReplanFailures = 0;
            if (!PickFreeGoal()) FreeIdle(2f);
        }

        private void TraceFreeRoamTurn(string stage)
        {
            if (!traceFreeRoamTurns || !IsFreeRoaming) return;
            Vector3 corner = hasCuriosityPath && curiosityCorner < curiosityCorners.Length
                ? curiosityCorners[curiosityCorner] : transform.position;
            Vector3 delta = corner - transform.position; delta.y = 0f;
            string first = curiosityCorners.Length > 0 ? curiosityCorners[0].ToString("F3") : "none";
            Debug.Log($"[Free roam turn] {stage}: root={transform.position:F3}, goal={freeGoal:F3}, "
                + $"first={first}, corner={curiosityCorner}/{curiosityCorners.Length}, point={corner:F3}, "
                + $"distance={delta.magnitude:F4}, angle={Vector3.SignedAngle(GetVisualForward(), delta, Vector3.up):F2}, "
                + $"turns={freeTurnsWithoutWalk}, walk={freeWalkSinceTurn:F3}", this);
        }

        private bool FreeRoamTurnAllowed()
        {
            if (!IsOrdinaryFreeRoam || freeTurnsWithoutWalk < 2) return true;
            TraceFreeRoamTurn("No walking progress; abandoning path");
            FreeIdle(2f);
            RoamingBlockReason = "Repeated turns without walking progress";
            return false;
        }

        private void RecordFreeRoamTurnCompletion()
        {
            if (!IsOrdinaryFreeRoam) return;
            if (freeTurnsWithoutWalk == 0) freeWalkSinceTurn = 0f;
            freeTurnsWithoutWalk++;
            TraceFreeRoamTurn("Completed");
        }

        private void RecordFreeRoamWalk(float distance)
        {
            if (!IsOrdinaryFreeRoam) return;
            freeRoamWalkTravel += distance;
            if (freeTurnsWithoutWalk == 0) return;
            freeWalkSinceTurn += distance;
            if (freeWalkSinceTurn < .5f) return;
            freeTurnsWithoutWalk = 0;
            freeWalkSinceTurn = 0f;
        }
    }
}
