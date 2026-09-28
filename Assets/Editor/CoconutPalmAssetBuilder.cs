using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace JurassicWorld.Editor
{
    /// <summary>Builds compact, opaque pinnate coconut palms without the orange alpha fringe in the downloaded pack.</summary>
    internal static class CoconutPalmAssetBuilder
    {
        private const string Folder = "Assets/VegetationDiversity/GeneratedPalms";

        internal static GameObject[] EnsurePrefabs()
        {
            EnsureFolder(Folder);
            Material bark = EnsureMaterial("Coconut_Bark", new Color(0.30f, 0.20f, 0.12f));
            Material barkBand = EnsureMaterial("Coconut_BarkBand", new Color(0.20f, 0.13f, 0.08f));
            Material[] greens =
            {
                EnsureMaterial("Coconut_LeafDeep", new Color(0.16f, 0.30f, 0.095f)),
                EnsureMaterial("Coconut_LeafMid", new Color(0.25f, 0.40f, 0.12f)),
                EnsureMaterial("Coconut_LeafLight", new Color(0.34f, 0.47f, 0.14f)),
            };
            GameObject[] prefabs = new GameObject[2];
            for (int variant = 0; variant < prefabs.Length; variant++)
                prefabs[variant] = BuildPrefab(variant, bark, barkBand, greens);
            AssetDatabase.SaveAssets();
            return prefabs;
        }

        private static GameObject BuildPrefab(int variant, Material bark, Material barkBand, Material[] greens)
        {
            string name = "CoconutPalm_" + (variant == 0 ? "A" : "B");
            string path = Folder + "/" + name + ".prefab";
            GameObject old = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            float height = variant == 0 ? 6.6f : 7.2f;
            float lean = variant == 0 ? 0.12f : -0.22f;
            Mesh trunkMesh = MakeTrunk(name + "_TrunkMesh", height, lean);
            string trunkPath = Folder + "/" + name + "_Trunk.asset";
            trunkMesh = SaveMesh(trunkMesh, trunkPath);
            if (old != null)
            {
                for (int lod = 0; lod < 3; lod++)
                    SaveMesh(MakeFronds(name + "_Fronds_LOD" + lod, height, lean, lod, variant),
                        Folder + "/" + name + "_Fronds_LOD" + lod + ".asset");
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Renderer loadedTrunkRenderer = contents.transform.Find("Trunk").GetComponent<Renderer>();
                    Renderer[] leaves = new Renderer[3];
                    for (int lod = 0; lod < 3; lod++)
                    {
                        Transform child = contents.transform.Find("Fronds_LOD" + lod);
                        child.gameObject.SetActive(true);
                        leaves[lod] = child.GetComponent<Renderer>();
                    }
                    contents.GetComponent<LODGroup>().SetLODs(new[]
                    {
                        new LOD(0.32f, new[] { loadedTrunkRenderer, leaves[0] }),
                        new LOD(0.09f, new[] { loadedTrunkRenderer, leaves[1] }),
                        new LOD(0.025f, new[] { loadedTrunkRenderer, leaves[2] }),
                    });
                    contents.GetComponent<LODGroup>().RecalculateBounds();
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }

            var root = new GameObject(name);
            var trunk = new GameObject("Trunk");
            trunk.transform.SetParent(root.transform, false);
            trunk.AddComponent<MeshFilter>().sharedMesh = trunkMesh;
            var trunkRenderer = trunk.AddComponent<MeshRenderer>();
            trunkRenderer.sharedMaterials = new[] { bark, barkBand };
            trunkRenderer.shadowCastingMode = ShadowCastingMode.On;
            trunkRenderer.receiveShadows = true;

            var lodLeafRenderers = new Renderer[3];
            for (int lod = 0; lod < 3; lod++)
            {
                Mesh fronds = MakeFronds(name + "_Fronds_LOD" + lod, height, lean, lod, variant);
                fronds = SaveMesh(fronds, Folder + "/" + name + "_Fronds_LOD" + lod + ".asset");
                var leaves = new GameObject("Fronds_LOD" + lod);
                leaves.transform.SetParent(root.transform, false);
                leaves.AddComponent<MeshFilter>().sharedMesh = fronds;
                var renderer = leaves.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = greens;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                lodLeafRenderers[lod] = renderer;
            }
            LODGroup group = root.AddComponent<LODGroup>();
            group.SetLODs(new[]
            {
                new LOD(0.32f, new[] { trunkRenderer, lodLeafRenderers[0] }),
                new LOD(0.09f, new[] { trunkRenderer, lodLeafRenderers[1] }),
                new LOD(0.025f, new[] { trunkRenderer, lodLeafRenderers[2] }),
            });
            group.RecalculateBounds();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static Mesh MakeTrunk(string name, float height, float lean)
        {
            const int sides = 12;
            const int rings = 9;
            var vertices = new Vector3[sides * rings];
            var uv = new Vector2[vertices.Length];
            var triangles = new List<int>[2] { new List<int>(), new List<int>() };
            float crownY = height * 0.79f;
            for (int ring = 0; ring < rings; ring++)
            {
                float t = ring / (float)(rings - 1);
                float r = Mathf.Lerp(0.30f, 0.16f, t);
                float cx = lean * t * t;
                for (int side = 0; side < sides; side++)
                {
                    float a = side * Mathf.PI * 2f / sides;
                    int v = ring * sides + side;
                    vertices[v] = new Vector3(cx + Mathf.Cos(a) * r, crownY * t, Mathf.Sin(a) * r);
                    uv[v] = new Vector2(side / (float)sides, t * 5f);
                    if (ring == 0 || side == sides - 1) continue;
                }
            }
            for (int ring = 0; ring < rings - 1; ring++)
            for (int side = 0; side < sides; side++)
            {
                int a = ring * sides + side;
                int b = ring * sides + (side + 1) % sides;
                int c = (ring + 1) * sides + (side + 1) % sides;
                int d = (ring + 1) * sides + side;
                int material = ring % 3 == 2 ? 1 : 0;
                triangles[material].AddRange(new[] { a, d, c, a, c, b });
            }
            var mesh = new Mesh { name = name };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(triangles[0], 0);
            mesh.SetTriangles(triangles[1], 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh MakeFronds(string name, float height, float lean, int lod, int variant)
        {
            int frondCount = lod == 0 ? 9 : lod == 1 ? 7 : 5;
            int leafletsPerSide = lod == 0 ? 22 : lod == 1 ? 14 : 8;
            float crownY = height * 0.79f;
            float length = height * (lod == 2 ? 0.40f : 0.45f);
            float droop = height * 0.27f;
            var vertices = new List<Vector3>();
            var triangles = new List<int>[3] { new List<int>(), new List<int>(), new List<int>() };
            var colors = new List<Color>();

            for (int frond = 0; frond < frondCount; frond++)
            {
                float angle = (frond + (variant == 0 ? 0f : 0.5f)) * Mathf.PI * 2f / frondCount;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 side = new Vector3(-radial.z, 0f, radial.x);
                int submesh = frond % 3;
                Vector3 prior = new Vector3(lean, crownY, 0f);
                for (int step = 1; step <= leafletsPerSide + 1; step++)
                {
                    float t = step / (float)(leafletsPerSide + 1);
                    Vector3 next = new Vector3(lean, crownY, 0f) + radial * (length * t) +
                                    Vector3.down * (droop * t * t);
                    float width = height * 0.012f * (1f - 0.45f * t);
                    AddDoubleSidedQuad(vertices, triangles[submesh], colors,
                        prior - side * width, prior + side * width,
                        next + side * width, next - side * width, Color.white);
                    prior = next;
                }
                for (int n = 0; n < leafletsPerSide; n++)
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    float t = (n + 1f) / (leafletsPerSide + 1f);
                    float stemDistance = length * t;
                    Vector3 root = new Vector3(lean, crownY, 0f) + radial * stemDistance + Vector3.down * (droop * t * t);
                    float leafletLength = height * 0.115f * Mathf.Pow(1f - t, 0.55f) + height * 0.018f;
                    Vector3 direction = (radial * 0.30f + side * sign * 0.90f + Vector3.down * (0.14f + 0.2f * t)).normalized;
                    Vector3 tip = root + direction * leafletLength;
                    Vector3 width = Vector3.Cross(direction, radial).normalized * (height * 0.017f * (1f - t * 0.5f));
                    Vector3[] shape =
                    {
                        root - width * 0.16f, root + width * 0.16f,
                        Vector3.Lerp(root, tip, 0.58f) + width, tip,
                        Vector3.Lerp(root, tip, 0.58f) - width,
                    };
                    Color tint = ((frond + n + variant) % 5 == 0) ? new Color(0.86f, 0.91f, 0.75f) : Color.white;
                    int start = vertices.Count;
                    vertices.AddRange(shape);
                    for (int k = 0; k < shape.Length; k++) colors.Add(tint);
                    int[] front = { start, start + 1, start + 2, start, start + 2, start + 4, start + 4, start + 2, start + 3 };
                    foreach (int index in front) triangles[submesh].Add(index);
                    int backStart = vertices.Count;
                    vertices.AddRange(shape);
                    for (int k = 0; k < shape.Length; k++) colors.Add(tint);
                    for (int k = 0; k < front.Length; k += 3)
                    {
                        triangles[submesh].Add(backStart + front[k + 2] - start);
                        triangles[submesh].Add(backStart + front[k + 1] - start);
                        triangles[submesh].Add(backStart + front[k] - start);
                    }
                }
            }
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.subMeshCount = 3;
            for (int submesh = 0; submesh < 3; submesh++) mesh.SetTriangles(triangles[submesh], submesh);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddDoubleSidedQuad(List<Vector3> vertices, List<int> triangles,
            List<Color> colors, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            int start = vertices.Count;
            vertices.AddRange(new[] { a, b, c, d, a, b, c, d });
            for (int i = 0; i < 8; i++) colors.Add(color);
            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3,
                start + 6, start + 5, start + 4, start + 7, start + 6, start + 4 });
        }

        private static Material EnsureMaterial(string name, Color color)
        {
            string path = Folder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Standard")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            material.SetFloat("_Glossiness", 0.06f);
            if (name.StartsWith("Coconut_Leaf")) material.SetInt("_Cull", 0);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh SaveMesh(Mesh generated, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }
            EditorUtility.CopySerialized(generated, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(generated);
            return existing;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string name = System.IO.Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
