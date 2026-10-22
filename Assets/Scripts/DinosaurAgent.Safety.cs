using UnityEngine;

namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        // Safety is directional: smaller roaming dinosaurs yield to a rex.
        private bool IsSafetyPeer(DinosaurAgent peer) => UsesSimpleFreeRoam && IsGroundTrafficPeer(peer)
            && peer.species == DinosaurSpecies.Tyrannosaurus
            && !HasRightOfWay(peer)
            && Mathf.Abs(MovementHeight - peer.MovementHeight) <= PeerHeightTolerance;

        private bool SafetySegmentAllowed(Vector2 from, Vector2 to, bool retreat)
        {
            if (!UsesSimpleFreeRoam) return true;
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
                float limit = retreat || start < safetyDistance * safetyDistance ? start : safetyDistance * safetyDistance;
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

        // Compatibility entry point for roaming and the existing safety harness.
        private bool UpdateSafetyAvoidance() => UpdateGroundTraffic();

        private bool MoveRaptorFree()
        {
            if (!hasCuriosityPath || PathFinished()) return false;
            Vector3 direction = curiosityCorners[curiosityCorner] - transform.position; direction.y = 0f;
            if (direction.sqrMagnitude < .00000001f) return false;
            Quaternion desired = Quaternion.LookRotation(direction) * Quaternion.Euler(0f, -VisualYaw, 0f);
            Quaternion rotation = Quaternion.RotateTowards(transform.rotation, desired,
                (gaitProfile != null ? gaitProfile.turnSpeed : 120f) * Time.deltaTime);
            Vector3 heading = rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
            float angle = Vector3.Angle(heading, direction);
            // The validated path rotates at a corner before translating. Advancing
            // while still facing up to 60 degrees away cuts outside that swept body
            // corridor and can wedge the animal against the very tree it avoided.
            float step = angle > 1f ? 0f : Mathf.Min(WalkSpeed * Time.deltaTime, direction.magnitude);
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
                    return gaitProfile != null;
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
            return moved || gaitProfile != null && turned;
        }
    }
}
