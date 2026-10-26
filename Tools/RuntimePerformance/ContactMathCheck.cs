using System;
using UnityEngine;
using Contact = JurassicWorld.FoliageContact;
using Original = Baseline.FoliageContact;

internal static class ContactMathCheck
{
    private static readonly System.Random Random = new System.Random(73021);
    private static float Range(float low, float high) => low + (high - low) * (float)Random.NextDouble();
    private static Vector3 Point(float range) => new Vector3(Range(-range, range), Range(-range, range), Range(-range, range));
    private static int Main()
    {
        float maximumError = 0;
        int nonzero = 0, checks = 0;
        for (int sample = 0; sample < 10000; sample++)
        {
            Vector3 a = Point(10), b = a + Point(5), previousA = a + Point(2), previousB = b + Point(2);
            if (sample % 5 == 0) { previousA = a; previousB = b; }
            if (sample % 7 == 0) { b = a; previousB = previousA; }
            float radius = Range(.01f, 3), speed = Range(0, 20);
            var old = new Original { start = a, end = b, previousStart = previousA, previousEnd = previousB, radius = radius, speed = speed, valid = true };
            var current = new Contact { start = a, end = b, previousStart = previousA, previousEnd = previousB, radius = radius, speed = speed, valid = true };
            var prepared = current.PreparePush();
            for (int point = 0; point < 24; point++)
            {
                Vector3 position = Vector3.Lerp(previousA, b, Range(0, 1)) + Point(radius * 2);
                float padding = point == 0 ? -radius : Range(0, 2);
                Vector3 expected = old.Push(position, padding), actual = prepared.Push(position, padding);
                float error = (actual - expected).magnitude;
                if (float.IsNaN(error) || error > .00001f)
                    throw new Exception($"Contact regression at {sample}/{point}: {expected} vs {actual}, error {error}");
                if (expected.sqrMagnitude > 0) nonzero++;
                maximumError = Mathf.Max(maximumError, error); checks++;
            }
        }
        Console.WriteLine($"PASS contact math: {checks} comparisons, {nonzero} nonzero forces, maximum error {maximumError:G9}. Includes stationary, zero-length, swept and nonpositive-reach cases.");
        CheckSprings();
        return 0;
    }

    private static void CheckSprings()
    {
        float positionError = 0, velocityError = 0;
        for (int sample = 0; sample < 10000; sample++)
        {
            Vector3 oldPosition = Vector3.zero, position = Vector3.zero, oldVelocity = Point(2), velocity = oldVelocity;
            float omega = 2 * Mathf.PI * Range(.25f, 10), stiffness = omega * omega;
            float damping = 2 * Range(.05f, 1) * omega, maximum = Range(.05f, .45f);
            for (int frame = 0; frame < 24; frame++)
            {
                var target = Vector3.ClampMagnitude(Point(2), 1) * maximum;
                // Cover the runtime solver's stable substeps, including the 40 Hz slow-frame budget.
                int steps = 1 + frame % 12; float step = Range(1f / 480, Mathf.Min(1f / 40, .6f / omega));
                for (int s = 0; s < steps; s++)
                {
                    oldVelocity += ((target - oldPosition) * stiffness - oldVelocity * damping) * step;
                    oldPosition += oldVelocity * step;
                    if (oldPosition.sqrMagnitude <= maximum * maximum) continue;
                    oldPosition = oldPosition.normalized * maximum;
                    float outward = Vector3.Dot(oldVelocity, oldPosition.normalized);
                    if (outward > 0) oldVelocity -= oldPosition.normalized * outward;
                }
                JurassicWorld.FoliageSpringSolver.Integrate(ref position, ref velocity, target, steps, step, stiffness, damping, maximum);
                float dp = (position - oldPosition).magnitude, dv = (velocity - oldVelocity).magnitude;
                // Clamp boundary branches can amplify float-rounding differences. Keep position
                // within 1 mm and velocity within 5 cm/s under these rapidly changing stress forces.
                if (float.IsNaN(dp + dv) || dp > .001f || dv > .05f || position.magnitude > maximum + .00001f)
                    throw new Exception($"Spring regression at {sample}/{frame}: position error={dp}, velocity error={dv}, maximum={maximum}, length={position.magnitude}");
                positionError = Mathf.Max(positionError, dp); velocityError = Mathf.Max(velocityError, dv);
            }
        }
        Console.WriteLine($"PASS scalar spring: 240000 frame comparisons, maximum position error {positionError:G9} m, velocity error {velocityError:G9} m/s; displacement bounds preserved.");
    }
}
