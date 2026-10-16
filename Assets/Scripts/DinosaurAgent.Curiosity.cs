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
        [SerializeField, Min(1f)] private float followDistance = 10f;
        [SerializeField, Min(1f)] private float interactionDuration = 15f;
        [SerializeField, Min(.1f)] private float lostSightGrace = 3f;
        [SerializeField, Min(0f)] private float interactionCooldown = 10f;
        [SerializeField, Min(.1f)] private float replanInterval = .5f;
        [SerializeField, Min(.1f)] private float playerReplanDistance = 2f;
        [SerializeField, Min(.1f)] private float blockedReplanDelay = 2f;
        private DinosaurInteractionSettings interactionSettings;
        private DinosaurInteractionProfile.Motion curiosityMotion;
        private NavMeshPath curiosityPath;
        private Vector3[] curiosityCorners = System.Array.Empty<Vector3>();
        private int curiosityCorner, failedReplans;
        private Vector3 lastSeenPlayer, plannedPlayer;
        private float visibleTime, invisibleTime, cooldownUntil, interactionStarted, phaseStarted, nextReplan;
        private float curiosityBlockedTime, currentInteractionSpeed, stopInitialSpeed, returnArc;
        private float actualInteractionSpeed;
        private bool curiosityBound, hasCuriosityPath, isReturning;
        private Quaternion turnStartRotation, turnEndRotation;
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

        private bool CanSeePlayer()
        {
            if (viewCamera == null) return false;
            Vector3 offset = viewCamera.transform.position - transform.position;
            offset.y = 0f;
            float angle = Vector3.Angle(GetVisualForward(), offset);
            bool waitingForTest = frontTestControlled && InteractionPhase == PlayerInteractionPhase.Inactive;
            float baseRange = waitingForTest || frontActive || FrontActionsAvailable() || angle <= 20f ? frontDetectionDistance : detectionDistance;
            float range = IsOffRouteInteraction ? baseRange * 1.5f : baseRange;
            float maxAngle = waitingForTest || sideTestSequence && frontActive || SideActionsAvailable()
                ? (frontActive ? 90f : 50f) : detectionAngle * .5f;
            // Quaternion-derived headings can put an exact boundary a few float ULPs over the limit.
            if (offset.sqrMagnitude > range * range || angle > maxAngle + .0001f) return false;
            Vector3 eye = transform.position + Vector3.up * ViewFocusHeight;
            if (TerrainTreeObstacleRegistry.IsSightBlocked(eye, viewCamera.transform.position)) return false;
            return interactionSettings == null || !interactionSettings.IsRockSightBlocked(eye, viewCamera.transform.position);
        }

        private void PlayCuriosity(InteractionMotionRole role, bool loop = false)
        {
            curiosityMotion = interactionProfile.Find(role);
            phaseStarted = Time.time;
            if (curiosityMotion == null) { PlayClip(idleClip); return; }
            string name = curiosityMotion.StateName;
            var state = legacyAnimation[name];
            state.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
            float referenceSpeed = curiosityMotion.groundSpeed;
            if (role == InteractionMotionRole.StartRun)
                foreach (var key in curiosityMotion.speed.keys) referenceSpeed = Mathf.Max(referenceSpeed, key.value);
            state.speed = role == InteractionMotionRole.StartRun || role == InteractionMotionRole.StopRun
                ? Mathf.Clamp((role == InteractionMotionRole.StopRun ? stopInitialSpeed : ApproachSpeed) / Mathf.Max(.01f, referenceSpeed),
                    role == InteractionMotionRole.StopRun ? .5f : .1f, 1.5f) : 1f;
            state.time = 0f;
            legacyAnimation.CrossFade(name, .35f);
            activeClip = name;
        }

        private float MotionTime => curiosityMotion != null && legacyAnimation[curiosityMotion.StateName] != null
            ? legacyAnimation[curiosityMotion.StateName].time : Mathf.Max(0f, Time.time - phaseStarted);
        private float MotionPlaybackSpeed => curiosityMotion != null ? legacyAnimation[curiosityMotion.StateName].speed : 1f;
        private bool MotionFinished => curiosityMotion == null ? MotionTime >= 1f : MotionTime >= curiosityMotion.clip.length;
        private InteractionMotionRole LookRole()
        {
            Vector3 direction = lastSeenPlayer - transform.position;
            direction.y = 0f;
            float angle = Vector3.SignedAngle(GetVisualForward(), direction, Vector3.up);
            return angle < -20f ? InteractionMotionRole.LookLeft : angle > 20f ? InteractionMotionRole.LookRight : InteractionMotionRole.LookForward;
        }

        private void StartInteraction()
        {
            frontFeet?.EndReturnHandoff();
            Vector3 playerDirection = viewCamera.transform.position - transform.position; playerDirection.y = 0f;
            float angle = Vector3.SignedAngle(GetVisualForward(), playerDirection, Vector3.up);
            if (Mathf.Abs(angle) <= 20f && FrontActionsAvailable()) { BeginFrontInteraction(); return; }
            if (Mathf.Abs(angle) <= 50f + .0001f && SideActionsAvailable()) { BeginInteractionSequence(true, angle < 0f); return; }
            CurrentState = DinosaurState.RandomWalk;
            InteractionPhase = PlayerInteractionPhase.Observing;
            lastSeenPlayer = viewCamera.transform.position;
            interactionStarted = Time.time;
            visibleTime = invisibleTime = curiosityBlockedTime = currentInteractionSpeed = 0f;
            actualInteractionSpeed = 0f;
            failedReplans = 0;
            isReturning = false;
            hasCuriosityPath = waitingAtCrossing = false;
            MovementCorridorCrossings.Release(this);
            PlayCuriosity(LookRole());
        }

        private NavMeshQueryFilter NavigationFilter => new NavMeshQueryFilter
        {
            agentTypeID = interactionSettings.navigationAgentTypeId,
            areaMask = 1 << 0
        };

        private bool PlanCuriosityPath(Vector3 target, bool completeOnly)
        {
            target.y = MovementHeight;
            var filter = NavigationFilter;
            hasCuriosityPath = false;
            if (!NavMesh.SamplePosition(transform.position, out var start, 1f, filter)
                || !NavMesh.SamplePosition(target, out var end, completeOnly ? .75f : 12f, filter)
                || !NavMesh.CalculatePath(start.position, end.position, filter, curiosityPath)
                || curiosityPath.status == NavMeshPathStatus.PathInvalid
                || (completeOnly && curiosityPath.status != NavMeshPathStatus.PathComplete)) return false;
            curiosityCorners = curiosityPath.corners;
            if (!SafetyPathAllowed(curiosityCorners, IsAvoidingTyrannosaurus)) return false;
            curiosityCorner = 0;
            hasCuriosityPath = curiosityCorners.Length != 0;
            return hasCuriosityPath;
        }

        private bool PlanFollow()
        {
            plannedPlayer = lastSeenPlayer;
            nextReplan = Time.time + Mathf.Max(.5f, replanInterval);
            Vector3 away = transform.position - lastSeenPlayer;
            away.y = 0f;
            if (away.sqrMagnitude < .01f) away = -GetVisualForward();
            Vector3 target = lastSeenPlayer + away.normalized * followDistance;
            return PlanCuriosityPath(target, false);
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
                && FlatDistance(transform.position, curiosityCorners[curiosityCorner]) <= .08f) curiosityCorner++;
            return hasCuriosityPath && curiosityCorner >= curiosityCorners.Length;
        }

        private void StartTurningOrRunning()
        {
            if (!PlanFollow()) { BeginCuriosityStop(); return; }
            PathFinished();
            if (curiosityCorner >= curiosityCorners.Length) { BeginCuriosityStop(); return; }
            Vector3 direction = curiosityCorners[curiosityCorner] - transform.position;
            direction.y = 0f;
            float angle = Vector3.SignedAngle(GetVisualForward(), direction, Vector3.up);
            if (Mathf.Abs(angle) >= 40f && TryBeginSteppingTurn(angle, TurnUse.Follow)) return;
            BeginCuriosityRun();
        }

        private void BeginCuriosityRun()
        {
            InteractionPhase = PlayerInteractionPhase.Starting;
            currentInteractionSpeed = 0f;
            PlayCuriosity(InteractionMotionRole.StartRun);
        }

        private bool MoveCuriosity(float speed, bool keepPlayerDistance)
        {
            actualInteractionSpeed = 0f;
            if (!hasCuriosityPath || PathFinished()) return false;
            Vector3 direction = curiosityCorners[curiosityCorner] - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < .0001f) return false;
            float turnAngle = Vector3.SignedAngle(GetVisualForward(), direction, Vector3.up);
            if (Mathf.Abs(turnAngle) >= 40f && !FinishingReturnFootsteps)
            {
                bool wander = IsFreeRoaming && InteractionPhase == PlayerInteractionPhase.Inactive;
                if (!TryBeginSteppingTurn(turnAngle, wander ? TurnUse.Wander : isReturning ? TurnUse.Return : TurnUse.Follow))
                {
                    if (!wander || !FindFreeTurnSpace(turnAngle)) curiosityBlockedTime += Time.deltaTime;
                }
                return false;
            }
            Quaternion desired = Quaternion.LookRotation(direction.normalized) * Quaternion.Euler(0f, -VisualYaw, 0f);
            Quaternion rotation = Quaternion.RotateTowards(transform.rotation, desired, (FinishingReturnFootsteps ? 20f : 65f) * Time.deltaTime);
            Vector3 heading = rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
            Vector2 from = new Vector2(transform.position.x, transform.position.z);
            float step = Mathf.Min(Mathf.Max(0f, speed) * Time.deltaTime, direction.magnitude);

            Vector2 to = from + new Vector2(heading.x, heading.z) * step;
            if (keepPlayerDistance && viewCamera != null)
            {
                Vector2 player = new Vector2(viewCamera.transform.position.x, viewCamera.transform.position.z);
                // Allow retreat if the player approaches us; never move closer inside the safety distance.
                if (Vector2.Distance(to, player) < followDistance && Vector2.Distance(to, player) < Vector2.Distance(from, player))
                    to = from;
            }
            bool blocked = interactionSettings.StepBlocked(from, to, CoreForward, new Vector2(heading.x, heading.z), interactionBody)
                || !SafetySegmentAllowed(from, to, IsAvoidingTyrannosaurus)
                || FindBlockingStep(from, to) != null;
            if (!blocked && (to - from).sqrMagnitude > .000001f)
            {
                var filter = NavigationFilter;
                blocked = !NavMesh.SamplePosition(new Vector3(to.x, MovementHeight, to.y), out var hit, .3f, filter)
                    || Vector2.Distance(to, new Vector2(hit.position.x, hit.position.z)) > .12f
                    || NavMesh.Raycast(transform.position, new Vector3(to.x, MovementHeight, to.y), out _, filter);
            }
            if (blocked) { curiosityBlockedTime += Time.deltaTime; return false; }
            transform.SetPositionAndRotation(new Vector3(to.x, MovementHeight, to.y), rotation);
            bool moved = Vector2.Distance(from, to) > .00001f;
            if (moved) actualInteractionSpeed = Vector2.Distance(from, to) / Mathf.Max(.00001f, Time.deltaTime);
            interactionMoved |= moved;
            if (moved || Quaternion.Angle(rotation, desired) > 1f) curiosityBlockedTime = 0f;
            if (moved) failedReplans = 0;
            return moved;
        }

        private void UpdateRunAnimation(bool moved, float speed)
        {
            var run = interactionProfile.Find(InteractionMotionRole.Run);
            if (!moved) { PlayClip(idleClip); return; }
            if (run == null) { PlayMovementClip(speed); return; }
            PlayClip(run.StateName, speed / Mathf.Max(.01f, run.groundSpeed));
        }

        private void BeginCuriosityStop()
        {
            if (IsSteppingTurn) { stopAfterStepTurn = true; return; }
            if (InteractionPhase == PlayerInteractionPhase.Stopping || InteractionPhase == PlayerInteractionPhase.Looking) return;
            InteractionPhase = PlayerInteractionPhase.Stopping;
            hasCuriosityPath = false;
            stopInitialSpeed = actualInteractionSpeed;
            if (stopInitialSpeed > .05f) PlayCuriosity(InteractionMotionRole.StopRun);
            else { curiosityMotion = null; phaseStarted = Time.time; PlayClip(idleClip); }
        }

        private void MoveCuriosityStop(float speed)
        {
            actualInteractionSpeed = 0f;
            if (speed <= .001f) return;
            Vector3 position = transform.position + GetVisualForward() * (speed * Time.deltaTime);
            // Stopping never consults old path corners or starts another turn.
            Vector3 player = viewCamera != null ? viewCamera.transform.position : position + GetVisualForward() * followDistance;
            bool approachingPlayer = FlatDistance(position, player) < followDistance
                && FlatDistance(position, player) < FlatDistance(transform.position, player);
            if (approachingPlayer || !StepPoseSafe(transform.position, position, transform.rotation, transform.rotation, false))
            {
                stopInitialSpeed = currentInteractionSpeed = 0f;
                curiosityMotion = null;
                phaseStarted = Time.time;
                GetComponent<DinosaurFootLock>()?.HoldSupportDuringRecovery(.35f);
                PlayClip(idleClip);
                return;
            }
            transform.position = position;
            actualInteractionSpeed = speed;
            interactionMoved = true;
        }

        private void CompleteReturn()
        {
            ClearInteractionNavigation();
            routeArc = returnArc;
            InteractionPhase = PlayerInteractionPhase.Inactive;
            cooldownUntil = Time.time + interactionCooldown;
            isReturning = false;
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
            bool following = !isReturning && InteractionPhase != PlayerInteractionPhase.Returning && InteractionPhase != PlayerInteractionPhase.Looking
                && InteractionPhase != PlayerInteractionPhase.Stopping;
            if (following)
            {
                if (CanSeePlayer()) { lastSeenPlayer = viewCamera.transform.position; invisibleTime = 0f; }
                else invisibleTime += Time.deltaTime;
                if (invisibleTime >= lostSightGrace || Time.time - interactionStarted >= interactionDuration)
                {
                    BeginCuriosityStop();
                }
            }
            switch (InteractionPhase)
            {
                case PlayerInteractionPhase.Observing:
                    if (MotionFinished) StartTurningOrRunning();
                    break;
                case PlayerInteractionPhase.Turning:
                    TickSteppingTurn();
                    break;
                case PlayerInteractionPhase.Starting:
                    currentInteractionSpeed = curiosityMotion == null ? Mathf.MoveTowards(currentInteractionSpeed, ApproachSpeed, ApproachSpeed * Time.deltaTime)
                        : Mathf.Max(0f, Mathf.Min(ApproachSpeed, curiosityMotion.speed.Evaluate(MotionTime) * MotionPlaybackSpeed));
                    bool startingMove = MoveCuriosity(currentInteractionSpeed, true);
                    if (IsSteppingTurn) break;
                    if (curiosityMotion == null) UpdateRunAnimation(startingMove, currentInteractionSpeed);
                    if (!startingMove && curiosityBlockedTime > .1f) { BeginCuriosityStop(); break; }
                    if (MotionFinished) { InteractionPhase = PlayerInteractionPhase.Following; curiosityMotion = null; }
                    break;
                case PlayerInteractionPhase.Following:
                    if (Time.time >= nextReplan && (FlatDistance(lastSeenPlayer, plannedPlayer) > playerReplanDistance || !hasCuriosityPath))
                    {
                        if (!PlanFollow() && ++failedReplans >= 2) { BeginCuriosityStop(); break; }
                    }
                    currentInteractionSpeed = Mathf.MoveTowards(currentInteractionSpeed, ApproachSpeed, ApproachSpeed * Time.deltaTime);
                    bool moved = MoveCuriosity(currentInteractionSpeed, true);
                    if (IsSteppingTurn) break;
                    UpdateRunAnimation(moved, currentInteractionSpeed);
                    if (curiosityBlockedTime >= blockedReplanDelay && Time.time >= nextReplan)
                    {
                        failedReplans++;
                        curiosityBlockedTime = 0f;
                        if (failedReplans >= 2 || !PlanFollow()) BeginCuriosityStop();
                    }
                    break;
                case PlayerInteractionPhase.Stopping:
                    float stopLength = curiosityMotion == null ? .35f : curiosityMotion.clip.length;
                    float measured = curiosityMotion == null || curiosityMotion.groundSpeed < .01f ? 1f - Mathf.Clamp01(MotionTime / stopLength)
                        : Mathf.Clamp01(curiosityMotion.speed.Evaluate(MotionTime) / curiosityMotion.groundSpeed);
                    currentInteractionSpeed = stopInitialSpeed * measured;
                    MoveCuriosityStop(currentInteractionSpeed);
                    if (MotionFinished || MotionTime >= stopLength)
                    {
                        currentInteractionSpeed = 0f;
                        InteractionPhase = PlayerInteractionPhase.Looking;
                        PlayCuriosity(LookRole());
                    }
                    break;
                case PlayerInteractionPhase.Looking:
                    if (MotionFinished)
                    {
                        if (IsFreeRoaming) { FinishFreeInteraction(); break; }
                        InteractionPhase = PlayerInteractionPhase.Returning;
                        isReturning = true;
                        hasCuriosityPath = false;
                        curiosityBlockedTime = 0f;
                        nextReplan = 0f;
                    }
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
                    bool returningMove = MoveCuriosity(WalkSpeed, false);
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
