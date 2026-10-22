using UnityEditor;
using UnityEngine;

namespace JurassicWorld.Editor
{
    internal static class SauropeltaMeshSampling
    {
        internal static void Bake(SkinnedMeshRenderer skin, Mesh mesh)
        {
            skin.BakeMesh(mesh, false);
            string path = AssetDatabase.GetAssetPath(skin.sharedMesh);
            bool protoceratopsEye = path.StartsWith("Assets/Dinosaurs/Protoceratops/Prepared/") &&
                (skin.sharedMesh.name == "unnamed" || skin.sharedMesh.name == "unnamed.001");
            if (!path.StartsWith("Assets/Dinosaurs/Sauropelta/Source/") &&
                !path.StartsWith(BaurusuchusSceneIntegration.SourceRoot + "/") && !protoceratopsEye) return;
            // These imported rigs bake the renderer's world scale despite useScale:false.
            // Remove it before callers transform the sampled vertices to world space.
            Vector3 scale = skin.transform.lossyScale;
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = new Vector3(vertices[i].x / scale.x, vertices[i].y / scale.y, vertices[i].z / scale.z);
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }
    }
}
