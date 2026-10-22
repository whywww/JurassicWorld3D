using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class PteradactalAirspaceBaker
    {
        private const string Path = PteradactalSceneIntegration.Root + "/LowFlightAirspace.asset";
        private static readonly Dictionary<Mesh, (Vector3[] vertices, int[] triangles)> Meshes = new Dictionary<Mesh, (Vector3[], int[])>();
        private static PteradactalAirspace space;
        private static bool[] occupied;
        private static int nx, ny, nz;

        public static PteradactalAirspace Bake(float bodyRadius)
        {
            space = ScriptableObject.CreateInstance<PteradactalAirspace>();
            space.origin = new Vector3(-80f, -2f, -80f);
            space.dimensions = new Vector3Int(321, 37, 341);
            space.bodyRadius = bodyRadius;
            nx = space.dimensions.x; ny = space.dimensions.y; nz = space.dimensions.z;
            occupied = new bool[nx * ny * nz];
            Meshes.Clear();
            foreach (var renderer in Object.FindObjectsOfType<MeshRenderer>())
            {
                if (renderer.GetComponentInParent<PteradactalFlightController>() != null ||
                    renderer.GetComponentInParent<DinosaurAgent>() != null || !IsFirstLod(renderer)) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) Raster(filter.sharedMesh, filter.transform.localToWorldMatrix);
            }
            foreach (var terrain in Terrain.activeTerrains)
            {
                var data = terrain.terrainData;
                foreach (var tree in data.treeInstances)
                {
                    var prefab = data.treePrototypes[tree.prototypeIndex].prefab;
                    Matrix4x4 placement = terrain.transform.localToWorldMatrix * Matrix4x4.TRS(Vector3.Scale(tree.position, data.size),
                        Quaternion.Euler(0, tree.rotation * Mathf.Rad2Deg, 0), new Vector3(tree.widthScale, tree.heightScale, tree.widthScale));
                    foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var renderer = filter.GetComponent<Renderer>();
                        if (filter.sharedMesh != null && renderer != null && IsFirstLod(renderer))
                            Raster(filter.sharedMesh, placement * filter.transform.localToWorldMatrix);
                    }
                }
            }
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    float ground = space.Ground(space.origin.x + x * space.cellSize, space.origin.z + z * space.cellSize);
                    for (int y = 0; y < ny && space.origin.y + y * space.cellSize <= ground + .25f; y++) occupied[Index(x, y, z)] = true;
                }
            var distances = new float[occupied.Length];
            for (int i = 0; i < occupied.Length; i++) distances[i] = occupied[i] ? 0f : 1e10f;
            DistanceTransform(distances);
            space.clearance = new byte[distances.Length];
            // A voxel can contain a triangle up to half a cell diagonal from its center.
            // Another half diagonal covers the distance from a query to its nearest grid point.
            float error = Mathf.Sqrt(3f) * space.cellSize + .01f;
            for (int i = 0; i < distances.Length; i++)
                space.clearance[i] = (byte)Mathf.Clamp(Mathf.FloorToInt((Mathf.Sqrt(distances[i]) * space.cellSize - error) * 10f), 0, 255);
            var orbits = new List<PteradactalOrbit>();
            foreach (float height in new[] { 6.5f, 8f, 9.5f })
                foreach (float radius in new[] { 7f, 9f, 12f })
                    for (float z = -60f; z <= 60f; z += 4f)
                        for (float x = -60f; x <= 60f; x += 4f)
                        {
                            var orbit = new PteradactalOrbit { center = new Vector2(x, z), radius = radius, height = height };
                            bool safe = true;
                            int count = Mathf.CeilToInt(radius * Mathf.PI * 2f / .5f);
                            for (int i = 0; i < count; i++)
                            {
                                Vector2 p = orbit.Point((float)i / count * Mathf.PI * 2f);
                                if (!space.IsClear(space.Position(p, height), space.RouteRadius + .25f)) { safe = false; break; }
                            }
                            if (safe) orbits.Add(orbit);
                        }
            space.orbits = orbits.ToArray();
            KeepLargestRegion();
            Debug.Log($"PTERADACTAL_AIRSPACE: meshes={Meshes.Count}; occupied={occupied.Count(v => v)}; safeOrbits={space.orbits.Length} in largest connected low-air region (before filter {orbits.Count})");
            if (orbits.Count == 0) { Object.DestroyImmediate(space); throw new InvalidOperationException("No safe low flight loop fits the current environment. Existing flyer is preserved."); }
            var existing = AssetDatabase.LoadAssetAtPath<PteradactalAirspace>(Path);
            if (existing == null) AssetDatabase.CreateAsset(space, Path);
            else { EditorUtility.CopySerialized(space, existing); EditorUtility.SetDirty(existing); Object.DestroyImmediate(space); space = existing; }
            Meshes.Clear(); occupied = null;
            return space;
        }

        private static void KeepLargestRegion()
        {
            var regions = new int[space.clearance.Length];
            int threshold = Mathf.CeilToInt((space.RouteRadius + .25f) * 10f);
            for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++)
            {
                float ground = space.Ground(space.origin.x + x * space.cellSize, space.origin.z + z * space.cellSize);
                for (int y = 0; y < ny; y++)
                {
                    int index = Index(x, y, z);
                    float height = space.origin.y + y * space.cellSize - ground;
                    if (height < 6f || height > 10f || space.clearance[index] < threshold) regions[index] = -1;
                }
            }
            var sizes = new List<int> { 0 }; var queue = new Queue<int>();
            for (int i = 0; i < regions.Length; i++)
            {
                if (regions[i] != 0) continue;
                int label = sizes.Count, count = 0; regions[i] = label; queue.Enqueue(i);
                void Visit(int next) { if (regions[next] == 0) { regions[next] = label; queue.Enqueue(next); } }
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue(); count++;
                    int x = current % nx, y = (current / nx) % ny, z = current / (nx * ny);
                    if (x > 0) Visit(current - 1); if (x + 1 < nx) Visit(current + 1);
                    if (y > 0) Visit(current - nx); if (y + 1 < ny) Visit(current + nx);
                    if (z > 0) Visit(current - nx * ny); if (z + 1 < nz) Visit(current + nx * ny);
                }
                sizes.Add(count);
            }
            int Region(PteradactalOrbit orbit)
            {
                Vector2 p = orbit.Point(0f); Vector3 v = (space.Position(p, orbit.height) - space.origin) / space.cellSize;
                return regions[Index(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y), Mathf.RoundToInt(v.z))];
            }
            int chosen = 0, largest = 0;
            foreach (var orbit in space.orbits)
            {
                int region = Region(orbit);
                if (region > 0 && sizes[region] > largest) { largest = sizes[region]; chosen = region; }
            }
            space.orbits = space.orbits.Where(o => Region(o) == chosen).ToArray();
        }

        private static bool IsFirstLod(Renderer renderer)
        {
            var lod = renderer.GetComponentInParent<LODGroup>();
            return lod == null || lod.GetLODs().Length == 0 || lod.GetLODs()[0].renderers.Contains(renderer);
        }

        private static int Index(int x, int y, int z) => x + nx * (y + ny * z);

        private static void Raster(Mesh mesh, Matrix4x4 matrix)
        {
            if (!Meshes.TryGetValue(mesh, out var source))
            { source = (mesh.vertices, mesh.triangles); Meshes.Add(mesh, source); }
            var vertices = new Vector3[source.vertices.Length];
            for (int i = 0; i < vertices.Length; i++) vertices[i] = (matrix.MultiplyPoint3x4(source.vertices[i]) - space.origin) / space.cellSize;
            for (int t = 0; t < source.triangles.Length; t += 3)
            {
                Vector3 a = vertices[source.triangles[t]], b = vertices[source.triangles[t + 1]], c = vertices[source.triangles[t + 2]];
                Vector3 min = Vector3.Min(a, Vector3.Min(b, c)), max = Vector3.Max(a, Vector3.Max(b, c));
                if (max.y < 8f || min.y > ny || max.x < 0 || min.x > nx || max.z < 0 || min.z > nz) continue;
                int x0 = Mathf.Max(0, Mathf.CeilToInt(min.x - .8661f)), x1 = Mathf.Min(nx - 1, Mathf.FloorToInt(max.x + .8661f));
                int y0 = Mathf.Max(0, Mathf.CeilToInt(min.y - .8661f)), y1 = Mathf.Min(ny - 1, Mathf.FloorToInt(max.y + .8661f));
                int z0 = Mathf.Max(0, Mathf.CeilToInt(min.z - .8661f)), z1 = Mathf.Min(nz - 1, Mathf.FloorToInt(max.z + .8661f));
                for (int z = z0; z <= z1; z++) for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                {
                    int index = Index(x, y, z);
                    if (!occupied[index] && TriangleDistanceSquared(new Vector3(x, y, z), a, b, c) <= .7501f) occupied[index] = true;
                }
            }
        }

        private static float TriangleDistanceSquared(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return ap.sqrMagnitude;
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return bp.sqrMagnitude;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) return (p - (a + ab * (d1 / (d1 - d3)))).sqrMagnitude;
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return cp.sqrMagnitude;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) return (p - (a + ac * (d2 / (d2 - d6)))).sqrMagnitude;
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
                return (p - (b + (c - b) * ((d4 - d3) / ((d4 - d3) + d5 - d6)))).sqrMagnitude;
            float denominator = va + vb + vc;
            if (Mathf.Abs(denominator) < 1e-12f) return Mathf.Min(ap.sqrMagnitude, bp.sqrMagnitude, cp.sqrMagnitude);
            return (p - (a + ab * (vb / denominator) + ac * (vc / denominator))).sqrMagnitude;
        }

        private static void DistanceTransform(float[] field)
        {
            int size = Mathf.Max(nx, ny, nz);
            var f = new float[size]; var d = new float[size]; var sites = new int[size]; var borders = new float[size + 1];
            void Line(int start, int stride, int length)
            {
                for (int i = 0; i < length; i++) f[i] = field[start + i * stride];
                int k = 0; sites[0] = 0; borders[0] = float.NegativeInfinity; borders[1] = float.PositiveInfinity;
                for (int q = 1; q < length; q++)
                {
                    float s;
                    do { int v = sites[k]; s = ((f[q] + q * q) - (f[v] + v * v)) / (2f * (q - v)); if (s <= borders[k]) k--; else break; } while (k >= 0);
                    k++; sites[k] = q; borders[k] = s; borders[k + 1] = float.PositiveInfinity;
                }
                k = 0;
                for (int q = 0; q < length; q++) { while (borders[k + 1] < q) k++; float delta = q - sites[k]; d[q] = delta * delta + f[sites[k]]; }
                for (int i = 0; i < length; i++) field[start + i * stride] = d[i];
            }
            for (int z = 0; z < nz; z++) for (int y = 0; y < ny; y++) Line(Index(0, y, z), 1, nx);
            for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++) Line(Index(x, 0, z), nx, ny);
            for (int y = 0; y < ny; y++) for (int x = 0; x < nx; x++) Line(Index(x, y, 0), nx * ny, nz);
        }
    }
}
