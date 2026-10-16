using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        // Safety is directional: only the raptor changes its behaviour around a rex.
        private bool IsSafetyPeer(DinosaurAgent peer) => species == DinosaurSpecies.Velociraptor
            && peer != null && peer != this && peer.gameObject.activeInHierarchy
            && peer.species == DinosaurSpecies.Tyrannosaurus
            && Mathf.Abs(MovementHeight - peer.MovementHeight) <= PeerHeightTolerance;

        private bool SafetySegmentAllowed(Vector2 from, Vector2 to, bool retreat)
        {
            if (species != DinosaurSpecies.Velociraptor) return true;
            foreach (var peer in peers)
            {
                if (!IsSafetyPeer(peer)) continue;
                Vector2 center = new Vector2(peer.transform.position.x, peer.transform.position.z);
                Vector2 delta = to - from;
                float t = delta.sqrMagnitude > 1e-10f
                    ? Mathf.Clamp01(Vector2.Dot(center - from, delta) / delta.sqrMagnitude) : 0f;
                float start = (from - center).sqrMagnitude;
                float minimum = (from + t * delta - center).sqrMagnitude;
                // Inside the circle, retreat must never get closer (including between corners).
                float limit = retreat ? start : safetyDistance * safetyDistance;
                if (minimum + .00001f < limit) return false;
            }
            return true;
        }

        private bool SafetyPathAllowed(Vector3[] corners, bool retreat)
        {
            Vector2 previous = new Vector2(transform.position.x, transform.position.z);
            foreach (var corner in corners)
            {
                Vector2 next = new Vector2(corner.x, corner.z);
                if (!SafetySegmentAllowed(previous, next, retreat)) return false;
                previous = next;
            }
            return true;
        }

        private bool UpdateSafetyAvoidance()
        {
            if (species != DinosaurSpecies.Velociraptor) return false;
            DinosaurAgent nearest = null;
            float distance = float.PositiveInfinity;
            foreach (var peer in peers)
            {
                if (!IsSafetyPeer(peer)) continue;
                float candidate = FlatDistance(transform.position, peer.transform.position);
                if (candidate < distance) { nearest = peer; distance = candidate; }
            }
            float release = Mathf.Max(safetyDistance + 1f, safetyReleaseDistance);
            if (IsAvoidingTyrannosaurus && distance >= release)
            {
                IsAvoidingTyrannosaurus = false;
                AvoidanceStatus = "Inactive";
                FreeIdle(0f);
            }
            if (!IsAvoidingTyrannosaurus && distance < safetyDistance)
            {
                IsAvoidingTyrannosaurus = true;
                hasCuriosityPath = false;
                nextSafetyReplan = 0f;
                MovementCorridorCrossings.Release(this);
            }
            if (!IsAvoidingTyrannosaurus) return false;

            if (Time.time >= nextSafetyReplan)
            {
                nextSafetyReplan = Time.time + .5f;
                if (!hasCuriosityPath || PathFinished() || !SafetyPathAllowed(RemainingSafetyPath(), true))
                    PickSafetyGoal(nearest, release);
            }
            bool moved = hasCuriosityPath && !PathFinished() && MoveRaptorFree();
            AvoidanceStatus = moved ? "Retreating from T-Rex"
                : hasCuriosityPath ? "Waiting for safe retreat / turning" : "No safe retreat path";
            if (moved) PlayMovementClip(WalkSpeed); else PlayClip(idleClip);
            return true;
        }

        private Vector3[] RemainingSafetyPath()
        {
            int count = Mathf.Max(0, curiosityCorners.Length - curiosityCorner);
            var remaining = new Vector3[count];
            System.Array.Copy(curiosityCorners, curiosityCorner, remaining, 0, count);
            return remaining;
        }

        private void PickSafetyGoal(DinosaurAgent rex, float release)
        {
            hasCuriosityPath = false;
            if (rex == null) return;
            Vector3 away = transform.position - rex.transform.position; away.y = 0f;
            if (away.sqrMagnitude < .001f) away = -rex.GetVisualForward();
            away.Normalize();
            // Prefer directly away, then stable alternatives; no per-frame random side changes.
            float[] bearings = { 0f, 30f, -30f, 60f, -60f, 85f, -85f };
            foreach (float angle in bearings)
            {
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * away;
                Vector3 candidate = transform.position + direction * (release + 2f);
                if (!NavMesh.SamplePosition(candidate, out var hit, 2f, NavigationFilter)) continue;
                bool clear = true;
                foreach (var peer in peers)
                    if (IsSafetyPeer(peer) && FlatDistance(hit.position, peer.transform.position) < release + .5f) clear = false;
                if (!clear || FindBlockingPeer(new Vector2(hit.position.x, hit.position.z)) != null) continue;
                if (interactionSettings.PoseBlocked(new Vector2(hit.position.x, hit.position.z),
                    new Vector2(direction.x, direction.z), interactionBody)) continue;
                if (!PlanCuriosityPath(hit.position, true)) continue;
                freeGoal = hit.position;
                curiosityBlockedTime = 0f;
                CurrentState = DinosaurState.RandomWalk;
                return;
            }
            AvoidanceStatus = "No safe retreat path";
        }

        private bool MoveRaptorFree()
        {
            if (!hasCuriosityPath || PathFinished()) return false;
            Vector3 direction = curiosityCorners[curiosityCorner] - transform.position; direction.y = 0f;
            if (direction.sqrMagnitude < .0001f) return false;
            Quaternion desired = Quaternion.LookRotation(direction) * Quaternion.Euler(0f, -VisualYaw, 0f);
            Quaternion rotation = Quaternion.RotateTowards(transform.rotation, desired, 120f * Time.deltaTime);
            Vector3 heading = rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
            float angle = Vector3.Angle(heading, direction);
            float step = angle > 60f ? 0f : Mathf.Min(WalkSpeed * Time.deltaTime, direction.magnitude);
            Vector3 position = transform.position + heading * step;
            Vector2 from = new Vector2(transform.position.x, transform.position.z), to = new Vector2(position.x, position.z);
            // During a sharp retreat turn, rotate safely in place until translation increases separation.
            if (!SafetySegmentAllowed(from, to, IsAvoidingTyrannosaurus)) position = transform.position;
            if (!StepPoseSafe(transform.position, position, transform.rotation, rotation, false))
            {
                // At a NavMesh edge, the old heading can step outside even though the path and
                // the new heading are valid. Reject translation, not the safe rotation as well.
                if (Quaternion.Angle(transform.rotation, rotation) > .001f
                    && StepPoseSafe(transform.position, transform.position, transform.rotation, rotation, false))
                {
                    transform.rotation = rotation;
                    curiosityBlockedTime = 0f;
                    return false;
                }
                curiosityBlockedTime += Time.deltaTime;
                if (IsAvoidingTyrannosaurus && curiosityBlockedTime >= .5f) hasCuriosityPath = false;
                return false;
            }
            bool moved = FlatDistance(transform.position, position) > .00001f;
            bool turned = Quaternion.Angle(transform.rotation, rotation) > .001f;
            transform.SetPositionAndRotation(position, rotation);
            if (moved || turned) curiosityBlockedTime = 0f;
            else curiosityBlockedTime += Time.deltaTime;
            return moved;
        }
    }
}
