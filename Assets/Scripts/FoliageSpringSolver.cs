using UnityEngine;

namespace JurassicWorld
{
    internal static class FoliageSpringSolver
    {
        // Same semi-implicit spring and displacement clamp, with array writes outside the substep loop.
        // Scalar locals avoid the many Vector3 operator calls in the Editor's debug compilation.
        internal static void Integrate(ref Vector3 position, ref Vector3 velocity, Vector3 target,
            int steps, float step, float stiffness, float damping, float maximum)
        {
            float x = position.x, y = position.y, z = position.z;
            float vx = velocity.x, vy = velocity.y, vz = velocity.z;
            float maximumSquared = maximum * maximum;
            for (int s = 0; s < steps; s++)
            {
                vx += ((target.x - x) * stiffness - vx * damping) * step;
                vy += ((target.y - y) * stiffness - vy * damping) * step;
                vz += ((target.z - z) * stiffness - vz * damping) * step;
                x += vx * step; y += vy * step; z += vz * step;
                float squared = x * x + y * y + z * z;
                if (squared <= maximumSquared) continue;
                float length = Mathf.Sqrt(squared);
                if (length > .00001f)
                { x = x / length * maximum; y = y / length * maximum; z = z / length * maximum; }
                else x = y = z = 0f;
                length = Mathf.Sqrt(x * x + y * y + z * z);
                float nx = 0f, ny = 0f, nz = 0f;
                if (length > .00001f) { nx = x / length; ny = y / length; nz = z / length; }
                float outward = vx * nx + vy * ny + vz * nz;
                if (outward > 0f) { vx -= nx * outward; vy -= ny * outward; vz -= nz * outward; }
            }
            position = new Vector3(x, y, z); velocity = new Vector3(vx, vy, vz);
        }
    }
}
