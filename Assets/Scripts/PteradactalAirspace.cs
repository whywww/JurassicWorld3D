using System;
using UnityEngine;

namespace JurassicWorld
{
    [Serializable]
    public struct PteradactalOrbit
    {
        public Vector2 center;
        public float radius;
        public float height;
        public Vector2 Point(float angle) => center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    // Scene-specific mesh clearance, including branches/leaves. Ground trunk navigation is separate.
    public sealed class PteradactalAirspace : ScriptableObject
    {
        public Vector3 origin;
        public Vector3Int dimensions;
        public float cellSize = .5f;
        public byte[] clearance;
        public PteradactalOrbit[] orbits;
        public float bodyRadius = 3.061f;
        public const float Margin = .5f;
        public const float BobAmplitude = .35f;
        public float RouteRadius => bodyRadius + Margin + BobAmplitude;
        private Terrain terrain;

        public float Clearance(Vector3 point)
        {
            Vector3 local = (point - origin) / cellSize;
            int x = Mathf.RoundToInt(local.x), y = Mathf.RoundToInt(local.y), z = Mathf.RoundToInt(local.z);
            if (x < 0 || y < 0 || z < 0 || x >= dimensions.x || y >= dimensions.y || z >= dimensions.z) return 0f;
            return clearance[x + dimensions.x * (y + dimensions.y * z)] * .1f;
        }

        public float Ground(float x, float z)
        {
            if (terrain == null) terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                Vector3 p = terrain.transform.position, s = terrain.terrainData.size;
                if (x >= p.x && x <= p.x + s.x && z >= p.z && z <= p.z + s.z)
                    return terrain.SampleHeight(new Vector3(x, 0f, z)) + p.y;
            }
            return 0f;
        }

        public Vector3 Position(Vector2 xz, float height) => new Vector3(xz.x, Ground(xz.x, xz.y) + height, xz.y);

        public bool IsClear(Vector3 point, float radius)
        {
            return Mathf.Abs(point.x) + radius <= PteradactalFlightController.HorizontalLimit &&
                Mathf.Abs(point.z) + radius <= PteradactalFlightController.HorizontalLimit && Clearance(point) >= radius;
        }

        public bool Sightline(Vector3 from, Vector3 to)
        {
            int count = Mathf.CeilToInt(Vector3.Distance(from, to) / .5f);
            for (int i = 2; i < count; i++)
                if (Clearance(Vector3.Lerp(from, to, (float)i / count)) < .1f) return false;
            return true;
        }
    }
}
