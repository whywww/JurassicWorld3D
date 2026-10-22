using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        [Header("Curiosity interaction (T-Rex)")]
        [SerializeField] private DinosaurInteractionProfile interactionProfile;
        [SerializeField] private DinosaurCoreBody interactionBody;
        [SerializeField, Min(0f)] private float detectionDistance = 24f;
        [SerializeField, Range(0f, 180f)] private float detectionAngle = 100f;
        [SerializeField, Min(0f)] private float detectionHold = .3f;
        [SerializeField, Min(.1f)] private float lostSightGrace = 3f;
        [SerializeField, Min(0f)] private float interactionCooldown = 10f;
        [SerializeField, Min(.1f)] private float replanInterval = .5f;
        [SerializeField, Min(.1f)] private float playerReplanDistance = 2f;
        [SerializeField, Min(.1f)] private float blockedReplanDelay = 2f;
        private DinosaurInteractionSettings interactionSettings;
        private NavMeshPath curiosityPath;
        private Vector3[] curiosityCorners = System.Array.Empty<Vector3>();
        private int curiosityCorner;
        private Vector3 lastSeenPlayer, plannedPlayer;
        private float visibleTime, invisibleTime, cooldownUntil, nextReplan;
        private float curiosityBlockedTime, returnArc;
        private float actualInteractionSpeed;
        private bool curiosityBound, hasCuriosityPath;
        public bool IsOffRouteInteraction => InteractionPhase != PlayerInteractionPhase.Inactive;

        private void BindCuriosityAnimations()
        {
            BindSideHandoffBones();
            if (curiosityBound || !playerInteractionEnabled || legacyAnimation == null) return;
            curiosityBound = true;
            interactionSettings = FindObjectOfType<DinosaurInteractionSettings>();
            if (interactionProfile != null)
            {
                foreach (var motion in interactionProfile.motions)
                    if (motion.clip != null) legacyAnimation.AddClip(motion.clip, motion.StateName);
                // The imported first "idle" match is "idle attack"; curiosity uses a neutral idle.
                var neutral = interactionProfile.Find(InteractionMotionRole.LookForward);
                if (neutral != null) idleClip = neutral.StateName;
            }
            interactionReady = interactionSettings != null && interactionSettings.EnabledAtStartup
                && interactionSettings.NavigationReady && interactionBody != null && interactionProfile != null;
            if (interactionReady || IsFreeRoaming) curiosityPath = new NavMeshPath();
            if (interactionSettings != null && interactionSettings.EnabledAtStartup && !interactionReady)
                Debug.LogWarning("[T-Rex curiosity] Bake the interaction navigation and animation profile before enabling curiosity.", this);
        }

        private bool CanSeePlayer() => PlayerVisibilityFailure() == null;

        private string PlayerVisibilityFailure()
        {
            if (viewCamera == null) return "No player";
            Vector3 offset = viewCamera.transform.position - transform.position;
            offset.y = 0f;
            float angle = Vector3.Angle(GetVisualForward(), offset);
            bool frontRules = frontActive || FrontActionsAvailable();
            float baseRange = angle <= 20f ? frontDetectionDistance : detectionDistance;
            float range = frontRules ? frontDetectionDistance : IsOffRouteInteraction ? baseRange * 1.5f : baseRange;
            float maxAngle = frontRules ? 50f : detectionAngle * .5f;
            // Quaternion-derived headings can put an exact boundary a few float ULPs over the limit.
            if (offset.sqrMagnitude > range * range) return "OutOfRange";
            if (angle > maxAngle + .0001f) return "OutOfAngle";
            Vector3 eye = transform.position + Vector3.up * ViewFocusHeight;
            if (TerrainTreeObstacleRegistry.IsSightBlocked(eye, viewCamera.transform.position)) return "TreeOcclusion";
            return interactionSettings != null && interactionSettings.IsRockSightBlocked(eye, viewCamera.transform.position)
                ? "RockOcclusion" : null;
        }

        private void StartInteraction()
        {
            frontFeet?.EndReturnHandoff();
            Vector3 playerDirection = viewCamera.transform.position - transform.position; playerDirection.y = 0f;
            float angle = Vector3.SignedAngle(GetVisualForward(), playerDirection, Vector3.up);
            if (Mathf.Abs(angle) <= 20f && FrontActionsAvailable()) { BeginFrontInteraction(); return; }
            if (Mathf.Abs(angle) <= 50f + .0001f && SideActionsAvailable()) { BeginInteractionSequence(true, angle < 0f); return; }
            Debug.LogWarning("[T-Rex curiosity] Interaction profile lacks front/side actions; ignoring player.", this);
            visibleTime = 0f;
            cooldownUntil = Time.time + interactionCooldown;
        }

        private NavMeshQueryFilter NavigationFilter => new NavMeshQueryFilter
        {
            agentTypeID = interactionSettings.navigationAgentTypeId,
            areaMask = 1 << 0
        };

        private bool PlanCuriosityPath(Vector3 target, bool completeOnly, bool skipRoamingPathFits = false)
        {
            RoamingBlockReason = "";
            target.y = MovementHeight;
            var filter = NavigationFilter;
            hasCuriosityPath = false;
            if (!NavMesh.SamplePosition(transform.position, out var start, 1f, filter)
                || !NavMesh.SamplePosition(target, out var end, completeOnly ? .75f : 12f, filter)
                || !NavMesh.CalculatePath(start.position, end.position, filter, curiosityPath)
                || curiosityPath.status == NavMeshPathStatus.PathInvalid
                || (completeOnly && curiosityPath.status != NavMeshPathStatus.PathComplete)) return false;
            curiosityCorners = InsetNavigationCorners(curiosityPath.corners);
            curiosityCorner = 0;
            hasCuriosityPath = curiosityCorners.Length != 0;
            NormalizeCuriosityStart(start.position);
            // Interaction follow/return connectors check every accepted step instead of
            // rejecting the whole route here; ordinary roaming keeps the full preflight.
            if (!skipRoamingPathFits && IsFreeRoaming && !RoamingPathFits(transform.position, transform.rotation, curiosityCorners, curiosityCorner))
            { hasCuriosityPath = false; return false; }
            if (!SafetyPathAllowed(curiosityCorners, IsAvoidingTyrannosaurus))
            { hasCuriosityPath = false; return false; }
            return hasCuriosityPath;
        }

        private void NormalizeCuriosityStart(Vector3 navigationStart)
        {
            // Only the projected origin gets the navigation tolerance. Real bends retain
            // the tighter arrival tolerance and must never be cut through an obstacle.
            if (curiosityCorners.Length < 2 || FlatDistance(curiosityCorners[0], navigationStart) > .001f
                || FlatDistance(transform.position, navigationStart) > .12f) return;
            TraceFreeRoamTurn("Projected start before normalization");
            Vector3 next = curiosityCorners[1], delta = next - transform.position; delta.y = 0f;
            if (delta.sqrMagnitude < .00000001f) return;
            Vector2 from = new Vector2(transform.position.x, transform.position.z);
            Vector2 to = new Vector2(next.x, next.z);
            Vector2 heading = new Vector2(delta.x, delta.z).normalized;
            if (!NavigationStepFits(transform.position, next)
                || interactionSettings.StepBlocked(from, to, CoreForward, heading, interactionBody)
                || !SafetySegmentAllowed(from, to, IsAvoidingTyrannosaurus)) return;
            Vector2 previous = from;
            int samples = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / .1f));
            for (int i = 1; i <= samples; i++)
            {
                Vector2 point = Vector2.Lerp(from, to, (float)i / samples);
                if (FindBlockingStep(previous, point) != null) return;
                previous = point;
            }
            curiosityCorner = 1;
            TraceFreeRoamTurn("Projected start skipped");
        }

        private bool PlanReturn()
        {
            nextReplan = Time.time + blockedReplanDelay;
            Vector2 xz = new Vector2(transform.position.x, transform.position.z);
            MovementCorridor.ProjectToCorridor(routeIndex, xz, out float nearest, out _);
            // Start with the closest entry, then test both directions on the original route.
            for (int i = 0; i < 21; i++)
            {
                float offset = i == 0 ? 0f : ((i + 1) / 2) * 5f * (i % 2 == 1 ? 1f : -1f);
                float arc = Mathf.Repeat(nearest + 6f + offset, MovementCorridor.Length(routeIndex));
                Vector2 p = MovementCorridor.PointAt(routeIndex, arc);
                MovementCorridor.GetSampleAt(routeIndex, arc, out _, out Vector2 heading);
                if (interactionSettings.PoseBlocked(p, heading, interactionBody)) continue;
                if (!PlanCuriosityPath(new Vector3(p.x, MovementHeight, p.y), true)) continue;
                // The sampled endpoint must connect to the exact centreline without teleporting.
                curiosityCorners[curiosityCorners.Length - 1] = new Vector3(p.x, MovementHeight, p.y);
                int originalCount = curiosityCorners.Length;
                System.Array.Resize(ref curiosityCorners, originalCount + 12);
                for (int join = 1; join <= 12; join++)
                {
                    Vector2 point = MovementCorridor.PointAt(routeIndex, arc + join * .5f);
                    curiosityCorners[originalCount + join - 1] = new Vector3(point.x, MovementHeight, point.y);
                }
                returnArc = Mathf.Repeat(arc + 6f, MovementCorridor.Length(routeIndex));
                return true;
            }
            return false;
        }

        private bool PathFinished()
        {
            while (hasCuriosityPath && curiosityCorner < curiosityCorners.Length
                && FlatDistance(transform.position, curiosityCorners[curiosityCorner]) <= .08f)
            {
                // Arrival tolerance cannot cut a tight navigation corner. Replanning
                // used to discard that same near corner repeatedly, leaving the
                // next segment blocked forever despite a complete path.
                if (curiosityCorner + 1 < curiosityCorners.Length
                    && FlatDistance(transform.position, curiosityCorners[curiosityCorner]) > .001f
                    && !NavigationStepFits(transform.position, curiosityCorners[curiosityCorner + 1])) break;
                curiosityCorner++;
            }
            return hasCuriosityPath && curiosityCorner >= curiosityCorners.Length;
        }

        private bool MoveCuriosity(float speed)
        {
            actualInteractionSpeed = 0f;
            if (!hasCuriosityPath || PathFinished()) return false;
            Vector3 direction = curiosityCorners[curiosityCorner] - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < .00000001f) return false;
            float turnAngle = Vector3.SignedAngle(GetVisualForward(), direction, Vector3.up);
            if (Mathf.Abs(turnAngle) >= 40f && !FinishingReturnFootsteps)
            {
                // A nearby point which could not be safely skipped is a blocked
                // connector, not a useful direction for a large turn performance.
                if (IsOrdinaryFreeRoam && direction.magnitude <= .12f)
                {
                    if (curiosityBlockedTime == 0f) TraceFreeRoamTurn("Near corner blocked");
                    RoamingBlockReason = "Near path corner requires a safe connector";
                    curiosityBlockedTime += Time.deltaTime;
                    return false;
                }
                bool wander = IsFreeRoaming && InteractionPhase == PlayerInteractionPhase.Inactive;
                if (!TryBeginSteppingTurn(turnAngle, wander ? TurnUse.Wander : TurnUse.Return))
                {
                    curiosityBlockedTime += Time.deltaTime;
                }
                return false;
            }
            Quaternion desired = Quaternion.LookRotation(direction.normalized) * Quaternion.Euler(0f, -VisualYaw, 0f);
            Quaternion rotation = Quaternion.RotateTowards(transform.rotation, desired, (FinishingReturnFootsteps ? 20f : 65f) * Time.deltaTime);
            Vector3 heading = rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
            Vector2 from = new Vector2(transform.position.x, transform.position.z);
            float step = Mathf.Min(Mathf.Max(0f, speed) * Time.deltaTime, direction.magnitude);

            Vector2 to = from + new Vector2(heading.x, heading.z) * step;
            bool blocked = interactionSettings.StepBlocked(from, to, CoreForward, new Vector2(heading.x, heading.z), interactionBody)
                || !SafetySegmentAllowed(from, to, IsAvoidingTyrannosaurus)
                || FindBlockingStep(from, to) != null;
            if (!blocked && (to - from).sqrMagnitude > .000001f)
            {
                blocked = !NavigationStepFits(transform.position, new Vector3(to.x, MovementHeight, to.y));
            }
            if (blocked) { curiosityBlockedTime += Time.deltaTime; return false; }
            transform.SetPositionAndRotation(new Vector3(to.x, MovementHeight, to.y), rotation);
            bool moved = Vector2.Distance(from, to) > .00001f;
            if (moved) RecordFreeRoamWalk(Vector2.Distance(from, to));
            if (moved) actualInteractionSpeed = Vector2.Distance(from, to) / Mathf.Max(.00001f, Time.deltaTime);
            interactionMoved |= moved;
            if (moved || Quaternion.Angle(rotation, desired) > 1f) curiosityBlockedTime = 0f;
            return moved;
        }

        private void CompleteReturn()
        {
            ClearInteractionNavigation();
            routeArc = returnArc;
            InteractionPhase = PlayerInteractionPhase.Inactive;
            cooldownUntil = Time.time + interactionCooldown;
            BeginIdle();
        }

        private bool UpdatePlayerInteraction()
        {
            interactionMoved = false;
            if (!interactionReady) return false;
            // Also recover an interrupted/hot-reloaded exit that no longer owns its turn state.
            if (!frontActive && IsSteppingTurn && stepTurnUse != TurnUse.Route && stepTurnUse != TurnUse.Wander
                && InteractionPhase != PlayerInteractionPhase.Turning) RecoverInterruptedTurn();
            if (InteractionPhase == PlayerInteractionPhase.Inactive)
            {
                if (Time.time < cooldownUntil || CurrentState == DinosaurState.Approaching) { visibleTime = 0f; return false; }
                visibleTime = CanSeePlayer() ? visibleTime + Time.deltaTime : 0f;
                if (visibleTime < detectionHold) return false;
                if (frontTestControlled)
                {
                    Vector3 direction = viewCamera.transform.position - transform.position; direction.y = 0f;
                    bool started = PlayerInteractionAngle <= 20f ? TriggerFrontInteractionTest()
                        : TriggerSideInteractionTest(Vector3.SignedAngle(GetVisualForward(), direction, Vector3.up) < 0f);
                    if (!started) return false;
                    visibleTime = 0f;
                }
                else StartInteraction();
            }
            if (frontActive) return TickFrontInteraction();
            switch (InteractionPhase)
            {
                case PlayerInteractionPhase.Turning:
                    TickSteppingTurn();
                    break;
                case PlayerInteractionPhase.Returning:
                    if (!hasCuriosityPath && Time.time >= nextReplan) PlanReturn();
                    if (!hasCuriosityPath) { PlayClip(idleClip); break; }
                    if (PathFinished())
                    {
                        MovementCorridor.GetSampleAt(routeIndex, returnArc, out _, out Vector2 tangent);
                        Quaternion desired = Quaternion.LookRotation(new Vector3(tangent.x, 0f, tangent.y)) * Quaternion.Euler(0f, -VisualYaw, 0f);
                        if (Quaternion.Angle(transform.rotation, desired) < .1f) { CompleteReturn(); break; }
                        float angle = Vector3.SignedAngle(GetVisualForward(), new Vector3(tangent.x, 0f, tangent.y), Vector3.up);
                        if (Mathf.Abs(angle) >= 25f)
                        {
                            if (!TryBeginSteppingTurn(angle, TurnUse.Return)) { hasCuriosityPath = false; nextReplan = Time.time + blockedReplanDelay; }
                        }
                        else
                        {
                            // Tiny heading error after a tangential walking join; feet stay anchored.
                            transform.rotation = desired;
                            CompleteReturn();
                        }
                        break;
                    }
                    bool returningMove = MoveCuriosity(WalkSpeed);
                    if (IsSteppingTurn) break;
                    if (returningMove) PlayMovementClip(WalkSpeed); else PlayClip(idleClip);
                    if (curiosityBlockedTime >= blockedReplanDelay)
                    { hasCuriosityPath = false; curiosityBlockedTime = 0f; nextReplan = Time.time + blockedReplanDelay; }
                    break;
            }
            return true;
        }
    }
}
