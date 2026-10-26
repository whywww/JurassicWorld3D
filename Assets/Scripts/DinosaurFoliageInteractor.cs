using System;
using System.Collections.Generic;
using UnityEngine;

namespace JurassicWorld
{
    // Reads the final animated pose, including DinosaurFootLock (execution order 1000).
    [DefaultExecutionOrder(1100)]
    public sealed class DinosaurFoliageInteractor : MonoBehaviour
    {
        [Serializable]
        public struct Capsule
        {
            public Transform bone;
            public Vector3 start, end; // Bone-local, fitted to the skinned vertices in edit mode.
            public float radius; // World metres at the baked model scale.
        }

        public Capsule[] capsules = Array.Empty<Capsule>();
        public Bounds animationEnvelope;
        [Min(.1f)] public float teleportDistance = 3f;
        internal static readonly List<DinosaurFoliageInteractor> Sources = new List<DinosaurFoliageInteractor>();
        internal FoliageContact[] contacts;
        private Vector3 previousRoot;
        private bool hasPrevious;

        private void OnEnable()
        {
            if (!Sources.Contains(this)) Sources.Add(this);
            contacts = new FoliageContact[capsules.Length];
            ResetHistory();
        }

        private void OnDisable() { Sources.Remove(this); hasPrevious = false; }
        public void ResetHistory() => hasPrevious = false;

        private void LateUpdate() => SamplePose(Time.deltaTime);

        internal void SamplePose(float deltaTime)
        {
            if (contacts == null || contacts.Length != capsules.Length) contacts = new FoliageContact[capsules.Length];
            bool reset = !hasPrevious || deltaTime <= 0f ||
                (transform.position - previousRoot).sqrMagnitude > teleportDistance * teleportDistance;
            for (int i = 0; i < capsules.Length; i++)
            {
                Capsule capsule = capsules[i];
                FoliageContact old = contacts[i];
                if (capsule.bone == null) { contacts[i] = default; continue; }
                Vector3 a = capsule.bone.TransformPoint(capsule.start), b = capsule.bone.TransformPoint(capsule.end);
                contacts[i] = new FoliageContact
                {
                    start = a, end = b, previousStart = reset ? a : old.start, previousEnd = reset ? b : old.end,
                    radius = capsule.radius, valid = !reset && old.radius > 0f,
                    speed = reset ? 0f : Mathf.Max(Vector3.Distance(a, old.start), Vector3.Distance(b, old.end)) / deltaTime
                };
            }
            previousRoot = transform.position;
            hasPrevious = true;
        }
    }

    internal struct FoliageContact
    {
        internal Vector3 start, end, previousStart, previousEnd;
        internal float radius, speed;
        internal bool valid;

        internal Bounds SweptBounds
        {
            get
            {
                Bounds bounds = new Bounds(start, Vector3.zero);
                bounds.Encapsulate(end); bounds.Encapsulate(previousStart); bounds.Encapsulate(previousEnd);
                bounds.Expand(radius * 2f);
                return bounds;
            }
        }

        internal static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return a + t * ab;
        }

        // Distance to the swept capsule centre surface (a quad split into two triangles).
        // This includes endpoints and the swept middle, unlike four edge-only segment tests.
        internal Vector3 Push(Vector3 point, float padding)
            => PreparePush().Push(point, padding);

        internal PreparedPush PreparePush() => new PreparedPush(this);

        internal readonly struct PreparedPush
        {
            private readonly FoliageContact contact;
            private readonly Vector3 motionDirection;
            private readonly bool moving;
            private readonly float strength;
            private readonly bool singleSegment;
            private readonly Vector3 segmentStart, segmentEnd;
            internal PreparedPush(FoliageContact contact)
            {
                this.contact = contact;
                Vector3 motion = (contact.start + contact.end - contact.previousStart - contact.previousEnd) * .5f;
                motionDirection = motion.normalized; moving = motion.sqrMagnitude > 1e-8f;
                strength = Mathf.Lerp(.6f, 1f, Mathf.Clamp01(contact.speed / 2f));
                bool stationary = (contact.start - contact.previousStart).sqrMagnitude == 0f &&
                    (contact.end - contact.previousEnd).sqrMagnitude == 0f;
                bool pointCapsule = (contact.start - contact.end).sqrMagnitude == 0f &&
                    (contact.previousStart - contact.previousEnd).sqrMagnitude == 0f;
                singleSegment = stationary || pointCapsule;
                segmentStart = stationary ? contact.start : contact.previousStart;
                segmentEnd = contact.end;
            }
            internal Vector3 Push(Vector3 point, float padding)
            {
                Vector3 p;
                if (singleSegment)
                {
                    // Keep both segment orientations and exact endpoints: the original
                    // degenerate-triangle path uses them to resolve float rounding near its axis.
                    p = ClosestOnSegment(point, segmentStart, segmentEnd);
                    Vector3 reverse = ClosestOnSegment(point, segmentEnd, segmentStart);
                    if ((point - segmentEnd).sqrMagnitude < (point - p).sqrMagnitude) p = segmentEnd;
                    if ((point - reverse).sqrMagnitude < (point - p).sqrMagnitude) p = reverse;
                    if ((point - segmentStart).sqrMagnitude < (point - p).sqrMagnitude) p = segmentStart;
                }
                else
                {
                    p = ClosestOnTriangle(point, contact.previousStart, contact.previousEnd, contact.end);
                    Vector3 q = ClosestOnTriangle(point, contact.previousStart, contact.end, contact.start);
                    if ((point - q).sqrMagnitude < (point - p).sqrMagnitude) p = q;
                }
                Vector3 outward = point - p;
                float distanceSquared = outward.sqrMagnitude, reach = contact.radius + padding;
                if (reach <= 0f || distanceSquared >= reach * reach) return Vector3.zero;
                float distance = Mathf.Sqrt(distanceSquared);
                Vector3 direction = distanceSquared > 1e-8f ? outward / distance : motionDirection;
                if (direction.sqrMagnitude < .1f) direction = Vector3.up;
                if (moving) direction = (direction + motionDirection * .35f).normalized;
                float depth = Mathf.Clamp01((reach - distance) / Mathf.Max(reach, .05f));
                return direction * depth * strength;
            }
        }

        private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a;
            if (Vector3.Cross(ab, ac).sqrMagnitude < 1e-10f)
            {
                Vector3 x = ClosestOnSegment(p, a, b), y = ClosestOnSegment(p, b, c), z = ClosestOnSegment(p, c, a);
                if ((p - y).sqrMagnitude < (p - x).sqrMagnitude) x = y;
                return (p - z).sqrMagnitude < (p - x).sqrMagnitude ? z : x;
            }
            Vector3 ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float inverse = 1f / (va + vb + vc);
            return a + ab * (vb * inverse) + ac * (vc * inverse);
        }
    }
}
