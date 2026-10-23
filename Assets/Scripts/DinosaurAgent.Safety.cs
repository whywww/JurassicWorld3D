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
            return MovePredictedPath(Time.deltaTime, WalkSpeed);
        }
    }
}
