using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace JurassicWorld.Editor
{
    /// <summary>Derives a flatter log for the autumn belt; the source Poly Haven prefab stays intact.</summary>
    internal static class AutumnGroundedLogAssetBuilder
    {
        internal const string PrefabPath = "Assets/Environment/RocksAndDeadwood/AutumnGroundedLog/Prepared/Prefabs/AutumnGroundedLog.prefab";
        private const string Folder = "Assets/Environment/RocksAndDeadwood/AutumnGroundedLog/Prepared/Models";
        private const string SourcePath = "Assets/Environment/RocksAndDeadwood/Deadwood/Prepared/Prefabs/DeadwoodLogTrunk02.prefab";

        internal static void EnsureAssets()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
            if (!AssetDatabase.IsValidFolder(Folder)) JurassicWorldSceneBuilder.EnsureFolder(Folder);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            if (source == null) throw new InvalidOperationException("Missing source deadwood log prefab.");
            Mesh main = source.transform.Find("LOD0").GetComponent<MeshFilter>().sharedMesh;
            float[] profile = GetBodyProfile(ReadVertices(main), main.bounds);
            float[] sorted = (float[])profile.Clone();
            Array.Sort(sorted);
            float target = sorted[sorted.Length / 2];
            var generated = new Mesh[4];
            for (int i = 0; i < generated.Length; i++)
            {
                Mesh sourceMesh = source.transform.Find("LOD" + i).GetComponent<MeshFilter>().sharedMesh;
                string path = Folder + "/AutumnGroundedLog_LOD" + i + ".asset";
                generated[i] = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (generated[i] == null)
                {
                    generated[i] = Flatten(sourceMesh, main.bounds, profile, target);
                    AssetDatabase.CreateAsset(generated[i], path);
                }
            }
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                instance.name = "AutumnGroundedLog";
                for (int i = 0; i < generated.Length; i++)
                    instance.transform.Find("LOD" + i).GetComponent<MeshFilter>().sharedMesh = generated[i];
                PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
            AssetDatabase.SaveAssets();
            Debug.Log("AUTUMN_GROUNDED_LOG_ASSET_READY: " + PrefabPath);
        }

        private static Vector3[] ReadVertices(Mesh mesh)
        {
            using (var data = Mesh.AcquireReadOnlyMeshData(mesh))
            using (var native = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp))
            {
                data[0].GetVertices(native);
                var vertices = new Vector3[mesh.vertexCount];
                native.CopyTo(vertices);
                return vertices;
            }
        }

        private static float[] GetBodyProfile(Vector3[] vertices, Bounds bounds)
        {
            var profile = new float[7];
            for (int band = 0; band < profile.Length; band++)
            {
                float x = Mathf.Lerp(bounds.min.x + 0.18f, bounds.max.x - 0.18f, band / 6f);
                var y = new List<float>();
                foreach (Vector3 vertex in vertices)
                    if (Mathf.Abs(vertex.x - x) < 0.18f && Mathf.Abs(vertex.z - bounds.center.z) < 0.30f)
                        y.Add(vertex.y);
                if (y.Count < 8) throw new InvalidOperationException("Too few log vertices for body profile.");
                y.Sort();
                profile[band] = y[Mathf.FloorToInt(y.Count * 0.30f)];
            }
            return profile;
        }

        private static Mesh Flatten(Mesh source, Bounds mainBounds, float[] profile, float target)
        {
            using (var data = Mesh.AcquireReadOnlyMeshData(source))
            using (var native = new NativeArray<Vector3>(source.vertexCount, Allocator.Temp))
            using (var uv = new NativeArray<Vector2>(source.vertexCount, Allocator.Temp))
            {
                Mesh.MeshData meshData = data[0];
                meshData.GetVertices(native);
                meshData.GetUVs(0, uv);
                var vertices = new Vector3[source.vertexCount];
                var uvs = new Vector2[source.vertexCount];
                native.CopyTo(vertices);
                uv.CopyTo(uvs);
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 v = vertices[i];
                    float t = Mathf.Clamp01((v.x - mainBounds.min.x - 0.18f) /
                        (mainBounds.size.x - 0.36f)) * 6f;
                    int left = Mathf.Min(5, Mathf.FloorToInt(t));
                    float bodyY = Mathf.Lerp(profile[left], profile[left + 1], t - left);
                    v.y -= bodyY - target;
                    vertices[i] = v;
                }

                int count = (int)meshData.GetSubMesh(0).indexCount;
                var indices = new int[count];
                if (source.indexFormat == IndexFormat.UInt16)
                {
                    NativeArray<ushort> raw = meshData.GetIndexData<ushort>();
                    for (int i = 0; i < count; i++) indices[i] = raw[i];
                }
                else
                {
                    NativeArray<uint> raw = meshData.GetIndexData<uint>();
                    for (int i = 0; i < count; i++) indices[i] = (int)raw[i];
                }
                var result = new Mesh { name = "AutumnGrounded_" + source.name, indexFormat = source.indexFormat };
                result.vertices = vertices;
                result.uv = uvs;
                result.SetTriangles(indices, 0);
                result.RecalculateNormals();
                result.RecalculateTangents();
                result.RecalculateBounds();
                return result;
            }
        }
    }
}
