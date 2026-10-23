using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        private DinosaurAgent trafficBlocker;
        private bool trafficLocked, trafficInteraction, trafficMoving, trafficSavedPath, trafficReverse, trafficHolding;
        private Vector3[] trafficOriginalCorners;
        private int trafficOriginalCorner, trafficCandidateOffset;
        private float trafficBegan, trafficRetryAt, trafficProgressAt;
        private Vector3 trafficProgressPosition, trafficGoal;
        private DinosaurState trafficOriginalState;
        private const float TrafficMargin = 1f;
        private static readonly float[] TrafficAngles = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };
        public string GroundTrafficStatus { get; private set; } = "Inactive";
        public string GroundTrafficBlocker => trafficBlocker != null ? trafficBlocker.DisplayName : "--";
        public float GroundTrafficDuration => trafficLocked ? Time.time - trafficBegan : 0f;
        public bool HasInteractionRightOfWay => trafficLocked ? trafficInteraction : WantsInteractionRightOfWay;
        private bool WantsInteractionRightOfWay => CurrentState == DinosaurState.Approaching
            || frontActive && frontStage != FrontStage.Ending && frontStage != FrontStage.TurnBack && frontStage != FrontStage.ReturnWalk
            || !frontActive && InteractionPhase != PlayerInteractionPhase.Inactive && InteractionPhase != PlayerInteractionPhase.Returning;

        // Never renumber the serialized species enum. Old scenes used Other for these three.
        public static DinosaurSpecies GroundSpeciesForName(string objectName)
        {
            switch (objectName.Replace("(Clone)", "").Trim())
            {
                case "Tyrannosaurus Rex": return DinosaurSpecies.Tyrannosaurus;
                case "Argentinosaurus": return DinosaurSpecies.Argentinosaurus;
                case "Stegosaurus": return DinosaurSpecies.Stegosaurus;
                case "Rajasaurus": return DinosaurSpecies.Rajasaurus;
                case "Sauropelta": return DinosaurSpecies.Sauropelta;
                case "Pachycephalosaurus": return DinosaurSpecies.Pachycephalosaurus;
                case "Baurusuchus": return DinosaurSpecies.Baurusuchus;
                case "Protoceratops": return DinosaurSpecies.Protoceratops;
                case "Velociraptor": return DinosaurSpecies.Velociraptor;
                default: return DinosaurSpecies.Other;
            }
        }
        private DinosaurSpecies GroundSpecies => species == DinosaurSpecies.Other ? GroundSpeciesForName(name) : species;
        public int GroundPriorityRank
        {
            get
            {
                switch (GroundSpecies)
                {
                    case DinosaurSpecies.Tyrannosaurus: return 1;
                    case DinosaurSpecies.Argentinosaurus: return 2;
                    case DinosaurSpecies.Stegosaurus: return 3;
                    case DinosaurSpecies.Rajasaurus: return 4;
                    case DinosaurSpecies.Sauropelta: return 5;
                    case DinosaurSpecies.Pachycephalosaurus: return 6;
                    case DinosaurSpecies.Baurusuchus: return 7;
                    case DinosaurSpecies.Velociraptor: return 8;
                    case DinosaurSpecies.Protoceratops: return 9;
                    default: return 10;
                }
            }
        }
        private bool IsGroundTrafficPeer(DinosaurAgent peer) => groundObstacleAware && peer != null && peer != this
            && peer.gameObject.activeInHierarchy && peer.groundObstacleAware
            && Mathf.Abs(MovementHeight - peer.MovementHeight) <= PeerHeightTolerance;

        private bool GroundRightOfWay(DinosaurAgent peer)
        {
            if (HasInteractionRightOfWay != peer.HasInteractionRightOfWay) return HasInteractionRightOfWay;
            if (GroundPriorityRank != peer.GroundPriorityRank) return GroundPriorityRank < peer.GroundPriorityRank;
            if (GroundPriorityRank == 9 && !Mathf.Approximately(collisionRadius, peer.collisionRadius))
                return collisionRadius > peer.collisionRadius;
            if (laneIndex != peer.laneIndex) return laneIndex < peer.laneIndex;
            return CompareByStableKey(this, peer) < 0;
        }

        private float BandPenetration(Vector3 position, Quaternion rotation, DinosaurCoreBody.Band a,
            DinosaurAgent peer, DinosaurCoreBody.Band b)
            => BandPenetration(position, rotation, a, peer.transform.position, peer.transform.rotation, peer.VisualYaw, peer.MovementHeight, b);

        private float BandPenetration(Vector3 position, Quaternion rotation, DinosaurCoreBody.Band a,
            Vector3 peerPosition, Quaternion peerRotation, float peerYaw, float peerHeight, DinosaurCoreBody.Band b)
        {
            if (a.bounds.max.y + MovementHeight <= b.bounds.min.y + peerHeight
                || b.bounds.max.y + peerHeight <= a.bounds.min.y + MovementHeight) return 0f;
            Vector3 f = rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
            Vector3 p = peerRotation * Quaternion.Euler(0f, peerYaw, 0f) * Vector3.forward;
            Vector2 af = new Vector2(f.x, f.z), ar = new Vector2(af.y, -af.x);
            Vector2 bf = new Vector2(p.x, p.z), br = new Vector2(bf.y, -bf.x);
            Vector2 ac = new Vector2(position.x, position.z) + ar * a.bounds.center.x + af * a.bounds.center.z;
            Vector2 bc = new Vector2(peerPosition.x, peerPosition.z) + br * b.bounds.center.x + bf * b.bounds.center.z;
            Vector2 d = bc - ac;
            Vector3 ae = a.bounds.extents, be = b.bounds.extents;
            float Depth(Vector2 axis) => Mathf.Abs(Vector2.Dot(ar, axis)) * ae.x + Mathf.Abs(Vector2.Dot(af, axis)) * ae.z
                + Mathf.Abs(Vector2.Dot(br, axis)) * be.x + Mathf.Abs(Vector2.Dot(bf, axis)) * be.z - Mathf.Abs(Vector2.Dot(d, axis));
            return Mathf.Max(0f, Mathf.Min(Mathf.Min(Depth(ar), Depth(af)), Mathf.Min(Depth(br), Depth(bf))));
        }
        private float PeerPenetration(Vector3 position, Quaternion rotation, DinosaurCoreBody body, DinosaurAgent peer)
        {
            var other = peer.interactionBody != null ? peer.interactionBody : peer.coreBody;
            if (body == null || other == null || FlatDistance(position, peer.transform.position) > body.BroadRadius + other.BroadRadius + .1f) return 0f;
            float depth = 0f;
            foreach (var a in body.bands) foreach (var b in other.bands)
                depth = Mathf.Max(depth, BandPenetration(position, rotation, a, peer, b));
            return depth;
        }
        // Check each pair of body bands, not just a summed overlap: leaving one
        // overlap never grants permission to enter a different body part.
        private bool GroundPeerSweepSafe(Vector3 from, Vector3 to, Quaternion fromRotation, Quaternion toRotation, DinosaurCoreBody body)
        {
            if (body == null) return true;
            int count = Mathf.Max(1, Mathf.CeilToInt(FlatDistance(from, to) / .1f), Mathf.CeilToInt(Quaternion.Angle(fromRotation, toRotation) / 2f));
            foreach (var peer in peers)
            {
                if (!IsGroundTrafficPeer(peer)) continue;
                var other = peer.interactionBody != null ? peer.interactionBody : peer.coreBody;
                if (other == null || Mathf.Min(FlatDistance(from, peer.transform.position), FlatDistance(to, peer.transform.position))
                    > body.BroadRadius + other.BroadRadius + FlatDistance(from, to) + .1f) continue;
                foreach (var a in body.bands) foreach (var b in other.bands)
                {
                    float previous = BandPenetration(from, fromRotation, a, peer, b);
                    for (int i = 1; i <= count; i++)
                    {
                        float t = (float)i / count;
                        float depth = BandPenetration(Vector3.Lerp(from, to, t), Quaternion.Slerp(fromRotation, toRotation, t), a, peer, b);
                        if (depth > previous + .00001f) return false;
                        previous = depth;
                    }
                }
            }
            return true;
        }
        private float TrafficDistance(DinosaurAgent peer, bool release)
        {
            if (UsesSimpleFreeRoam && peer.GroundSpecies == DinosaurSpecies.Tyrannosaurus && !HasRightOfWay(peer))
                return release ? Mathf.Max(safetyDistance + 1f, safetyReleaseDistance) : safetyDistance;
            return collisionRadius + peer.collisionRadius + (release ? TrafficMargin : .25f);
        }
        private void LockTrafficPriority()
        {
            if (trafficLocked) return;
            trafficInteraction = WantsInteractionRightOfWay;
            trafficLocked = true;
            trafficBegan = trafficProgressAt = Time.time;
            trafficProgressPosition = transform.position;
        }
        private void RegisterGroundConflict(DinosaurAgent peer)
        {
            if (!IsGroundTrafficPeer(peer)) return;
            LockTrafficPriority(); peer.LockTrafficPriority();
            if (trafficBlocker == null || peer.HasRightOfWay(trafficBlocker)) trafficBlocker = peer;
            if (peer.trafficBlocker == null || HasRightOfWay(peer.trafficBlocker)) peer.trafficBlocker = this;
        }

        private Vector3 TrafficIntent()
        {
            if (hasCuriosityPath && curiosityCorner < curiosityCorners.Length) return curiosityCorners[curiosityCorner];
            return CurrentState == DinosaurState.RandomWalk || CurrentState == DinosaurState.Approaching
                ? target : transform.position;
        }
        private bool GroundConflictNearby(DinosaurAgent peer)
        {
            float distance = FlatDistance(transform.position, peer.transform.position);
            if (distance < TrafficDistance(peer, false)) return true;
            var body = interactionBody != null ? interactionBody : coreBody;
            if (PeerPenetration(transform.position, transform.rotation, body, peer) > 0f) return true;
            // Detect a standing animal in our next metre before a movement gate rejects it.
            Vector3 delta = TrafficIntent() - transform.position; delta.y = 0f;
            if (delta.sqrMagnitude < .001f) return false;
            Vector3 ahead = transform.position + delta.normalized * Mathf.Min(1f, delta.magnitude);
            return FlatDistance(ahead, peer.transform.position) < collisionRadius + peer.collisionRadius + .25f
                || PeerPenetration(ahead, transform.rotation, body, peer) > 0f;
        }

        // Called before all locomotion/interaction branches, including idle blockers.
        private bool UpdateGroundTraffic()
        {
            trafficHolding = false;
            if (!groundObstacleAware) return false;
            foreach (var peer in peers)
                if (IsGroundTrafficPeer(peer) && GroundConflictNearby(peer)) RegisterGroundConflict(peer);
            if (!trafficLocked) return false;
            bool connected = false;
            DinosaurAgent highest = null;
            foreach (var peer in peers)
            {
                if (!IsGroundTrafficPeer(peer)) continue;
                bool near = FlatDistance(transform.position, peer.transform.position) < Mathf.Max(TrafficDistance(peer, true), peer.TrafficDistance(this, true))
                    || PeerPenetration(transform.position, transform.rotation, interactionBody ?? coreBody, peer) > 0f;
                if (!near) continue;
                connected = true;
                if (highest == null || peer.HasRightOfWay(highest)) highest = peer;
            }
            // Finish the selected clearing connector before restoring the original
            // goal; releasing at the first safe metre can send the yielding animal
            // straight back into the same oncoming lane.
            if (!connected && !IsSteppingTurn && (!trafficMoving || !hasCuriosityPath || IsAvoidingTyrannosaurus || PathFinished()))
            {
                FinishGroundTraffic();
                return false;
            }
            if (highest != null) trafficBlocker = highest;
            if (!IsGroundTrafficPeer(trafficBlocker))
            {
                FinishGroundTraffic(); return false;
            }
            bool yielding = !HasRightOfWay(trafficBlocker);
            IsAvoidingTyrannosaurus = yielding && UsesSimpleFreeRoam && trafficBlocker.GroundSpecies == DinosaurSpecies.Tyrannosaurus;
            if (!yielding && !trafficMoving && !GroundConflictNearby(trafficBlocker))
            { GroundTrafficStatus = "Proceeding with right of way"; return false; }
            if (!trafficMoving && !yielding && Time.time - trafficBegan < RightOfWayGraceSeconds)
            {
                GroundTrafficStatus = "Waiting for lower priority peer";
                HoldTrafficPose(); return true;
            }
            // Recovery retains its stricter static-overlap snapshot. Do not replace it with a normal walk.
            if (freeRecoveryActive || frontRecoveryActive)
            {
                GroundTrafficStatus = "Waiting / safe static recovery";
                return false;
            }
            if (!trafficMoving) BeginGroundTrafficMove(yielding);
            if (IsSteppingTurn)
            {
                TickSteppingTurn();
                CheckTrafficProgress();
                return true;
            }
            if (Time.time >= trafficRetryAt && (!hasCuriosityPath || PathFinished()))
            {
                trafficRetryAt = Time.time + .5f;
                if (!PlanGroundTraffic(yielding)) GroundTrafficStatus = "Waiting: no safe clearance path";
            }
            bool moved = hasCuriosityPath && !PathFinished() && MoveGroundTraffic();
            if (moved) { PlayMovementClip(trafficReverse ? -WalkSpeed : WalkSpeed); GroundTrafficStatus = yielding ? "Yielding" : "Passing around"; }
            else if (!IsSteppingTurn) HoldTrafficPose();
            CheckTrafficProgress();
            AvoidanceStatus = GroundTrafficStatus;
            return true;
        }
        private void HoldTrafficPose()
        {
            trafficHolding = true;
            actualInteractionSpeed = 0f;
            if (frontActive && !trafficMoving)
            {
                if (legacyAnimation != null) foreach (AnimationState state in legacyAnimation) state.speed = 0f;
            }
            else if (!IsSteppingTurn) PlayClip(idleClip);
        }
        private void BeginGroundTrafficMove(bool yielding)
        {
            trafficMoving = true;
            trafficOriginalCorners = curiosityCorners;
            trafficOriginalCorner = curiosityCorner;
            trafficSavedPath = hasCuriosityPath;
            trafficOriginalState = CurrentState;
            if (frontActive) BeginFrontPeerClearance(trafficBlocker, yielding);
            else if (IsSteppingTurn)
            {
                GetComponentInChildren<DinosaurFootLock>()?.HoldSupportDuringRecovery(.35f);
                ClearSteppingTurn();
            }
            hasCuriosityPath = false;
            trafficRetryAt = 0f;
            trafficProgressAt = Time.time;
            trafficProgressPosition = transform.position;
            MovementCorridorCrossings.Release(this);
        }
        private void CheckTrafficProgress()
        {
            if (FlatDistance(transform.position, trafficProgressPosition) >= .25f)
            { trafficProgressAt = Time.time; trafficProgressPosition = transform.position; return; }
            if (Time.time - trafficProgressAt < 3f) return;
            if (IsSteppingTurn) ClearSteppingTurn();
            hasCuriosityPath = false;
            trafficCandidateOffset = (trafficCandidateOffset + 1) % TrafficAngles.Length;
            trafficRetryAt = Time.time + .5f;
            trafficProgressAt = Time.time;
            GroundTrafficStatus = "Waiting: replanning blocked clearance";
        }
        private bool PlanGroundTraffic(bool yielding)
        {
            if (interactionSettings == null) interactionSettings = FindObjectOfType<DinosaurInteractionSettings>();
            if (interactionBody == null) interactionBody = coreBody;
            if (curiosityPath == null) curiosityPath = new NavMeshPath();
            if (interactionSettings == null || !interactionSettings.NavigationReady || interactionBody == null) return false;
            Vector3 away = transform.position - trafficBlocker.transform.position; away.y = 0f;
            if (away.sqrMagnitude < .001f) away = -trafficBlocker.GetVisualForward();
            away.Normalize();
            if (!yielding) away = Vector3.Cross(Vector3.up, away);
            float separation = TrafficDistance(trafficBlocker, true) + 1f;
            Vector3 peerHeading = trafficBlocker.TrafficIntent() - trafficBlocker.transform.position; peerHeading.y = 0f;
            if (peerHeading.sqrMagnitude < .01f) peerHeading = trafficBlocker.GetVisualForward();
            Vector3 peerSide = Vector3.Cross(Vector3.up, peerHeading.normalized);
            bool clearLane = yielding && !IsAvoidingTyrannosaurus
                && PeerPenetration(transform.position, transform.rotation, interactionBody, trafficBlocker) <= .00001f;
            // Prefer clearing the lane. If a narrow section has no lateral room,
            // retreat along it first so the next connector can use a wider section.
            for (int clearancePass = 0; clearancePass < (clearLane ? 2 : 1); clearancePass++)
            foreach (float distance in new[] { 2f, 4f, 8f, separation + 2f })
                for (int i = 0; i < TrafficAngles.Length; i++)
                {
                    Vector3 direction = Quaternion.Euler(0f, TrafficAngles[(i + trafficCandidateOffset) % TrafficAngles.Length], 0f) * away;
                    Vector3 candidate = transform.position + direction * distance;
                    if (!NavMesh.SamplePosition(candidate, out var hit, 1f, NavigationFilter)
                        || !NavigationRestPoint(hit.position)
                        || FlatDistance(hit.position, trafficBlocker.transform.position) < separation
                        || FindBlockingPeer(new Vector2(hit.position.x, hit.position.z)) != null) continue;
                    if (clearLane && clearancePass == 0 && Mathf.Abs(Vector3.Dot(hit.position - trafficBlocker.transform.position, peerSide))
                        < collisionRadius + trafficBlocker.collisionRadius + TrafficMargin) continue;
                    if (!PlanCuriosityPath(hit.position, true, true)) continue;
                    trafficReverse = false;
                    if (!GroundTrafficPathFits())
                    {
                        // An initially overlapping, head-on pair may have no safe turning arc.
                        // Back out with unchanged heading and a reversed walk cycle first.
                        trafficReverse = true;
                        if (!GroundTrafficPathFits()) continue;
                    }
                    trafficGoal = hit.position;
                    curiosityBlockedTime = 0f;
                    GroundTrafficStatus = yielding ? "Yielding" : "Passing around";
                    return true;
                }
            hasCuriosityPath = false;
            return false;
        }
        private bool GroundTrafficPathFits()
        {
            if (UsesWalkingSteering && !trafficReverse) return RoamingPathFits(transform.position, transform.rotation, curiosityCorners, curiosityCorner);
            Vector3 previous = transform.position;
            Quaternion rotation = transform.rotation;
            for (int i = curiosityCorner; i < curiosityCorners.Length; i++)
            {
                Vector3 next = curiosityCorners[i], delta = next - previous; delta.y = 0f;
                if (delta.sqrMagnitude < .000001f) continue;
                if (trafficReverse && Vector3.Dot(rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward, delta.normalized) > -.999f) return false;
                Quaternion heading = trafficReverse ? rotation : Quaternion.LookRotation(delta) * Quaternion.Euler(0f, -VisualYaw, 0f);
                if (!StepPoseSafe(previous, previous, rotation, heading, false)
                    || !StepPoseSafe(previous, next, heading, heading, false)) return false;
                previous = next; rotation = heading;
            }
            return true;
        }
        private bool MoveGroundTraffic()
        {
            if ((UsesWalkingSteering || IsContinuousWalker) && !trafficReverse) return MovePredictedPath(Time.deltaTime, WalkSpeed);
            Vector3 delta = curiosityCorners[curiosityCorner] - transform.position; delta.y = 0f;
            if (delta.sqrMagnitude < .000001f) { curiosityCorner++; return false; }
            float angle = Vector3.SignedAngle(GetVisualForward(), delta, Vector3.up);
            if (!trafficReverse && !UsesWalkingSteering && HasFootPhaseData && Mathf.Abs(angle) >= 40f)
            { TryBeginSteppingTurn(angle, TurnUse.Traffic); return false; }
            Quaternion desired = trafficReverse ? transform.rotation : Quaternion.LookRotation(delta) * Quaternion.Euler(0f, -VisualYaw, 0f);
            Quaternion rotation = Quaternion.RotateTowards(transform.rotation, desired,
                (gaitProfile != null ? gaitProfile.turnSpeed : 120f) * Time.deltaTime);
            bool aligned = Quaternion.Angle(rotation, desired) < 1f;
            Vector3 endpoint = curiosityCorners[curiosityCorner]; endpoint.y = MovementHeight;
            Vector3 position = aligned ? Vector3.MoveTowards(transform.position, endpoint, WalkSpeed * Time.deltaTime) : transform.position;
            if (!StepPoseSafe(transform.position, position, transform.rotation, rotation, false)) return false;
            float distance = FlatDistance(transform.position, position);
            transform.SetPositionAndRotation(position, rotation);
            actualInteractionSpeed = distance / Mathf.Max(.00001f, Time.deltaTime);
            interactionMoved |= distance > .00001f;
            if (frontActive && frontStage != FrontStage.ReturnWalk) RecordFrontTrail();
            return distance > .00001f;
        }
        private void FinishGroundTraffic()
        {
            if (IsSteppingTurn && stepTurnUse == TurnUse.Traffic) ClearSteppingTurn();
            if (trafficMoving)
            {
                hasCuriosityPath = trafficSavedPath;
                curiosityCorners = trafficOriginalCorners;
                curiosityCorner = trafficOriginalCorner;
                CurrentState = trafficOriginalState;
                if (frontActive)
                {
                    FinishFrontPeerClearance();
                    if (frontStage == FrontStage.ReturnWalk && trafficSavedPath && trafficOriginalCorners.Length > 0)
                        PlanFrontConnection(trafficOriginalCorners[trafficOriginalCorners.Length - 1], true);
                }
                else if (trafficSavedPath && trafficOriginalCorners.Length > 0)
                {
                    // Reconnect from the displaced pose; an old first segment is no longer valid.
                    if (!PlanCuriosityPath(trafficOriginalCorners[trafficOriginalCorners.Length - 1], true))
                    { if (IsFreeRoaming) FreeIdle(0f); else hasCuriosityPath = false; }
                }
            }
            trafficBlocker = null; trafficLocked = trafficMoving = false;
            trafficOriginalCorners = null; trafficCandidateOffset = 0;
            IsAvoidingTyrannosaurus = false; AvoidanceStatus = GroundTrafficStatus = "Inactive";
        }
    }
}
