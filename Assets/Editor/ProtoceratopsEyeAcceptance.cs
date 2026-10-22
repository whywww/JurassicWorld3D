using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class ProtoceratopsEyeAcceptance
    {
        [MenuItem("Jurassic World/Acceptance/Verify Protoceratops Eyes")]
        public static void VerifyMain()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save scene edits first.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var agent = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Protoceratops");
            var clone = Object.Instantiate(agent.GetComponent<DinosaurFbxLoader>().ModelRoot);
            var mesh = new Mesh();
            try
            {
                clone.transform.position = Vector3.zero;
                foreach (Transform node in clone.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 31;
                var skins = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Require(skins.Length == 3, "Body, eyes and cornea renderers are required.");
                var eyes = skins.Single(r => r.sharedMaterials.Single().name == "Eyes");
                var lens = skins.Single(r => r.sharedMaterials.Single().name == "Lens");
                Require(eyes.sharedMesh.vertexCount == 7044 && lens.sharedMesh.vertexCount == 14144,
                    "Original eye or cornea geometry was lost.");
                Require(eyes.sharedMaterial.mainTexture != null && lens.sharedMaterial.renderQueue == 3000,
                    "Eye texture or transparent cornea material missing.");
                var head = clone.GetComponentsInChildren<Transform>(true).Single(t => t.name == "protoceratops_head.M_32");
                foreach (var skin in new[] { eyes, lens })
                {
                    Require(skin.bones.Contains(head), "Eye mesh does not reference the head bone.");
                    Require(skin.sharedMesh.boneWeights.All(w => w.weight0 > .999f && skin.bones[w.boneIndex0] == head),
                        "Eyes and corneas must follow the head rigidly.");
                }
                var animation = clone.GetComponentInChildren<Animation>(true);
                var clip = animation["WalkCycle"].clip;
                Vector3[][] reference = new Vector3[2][];
                float maximumDrift = 0f;
                foreach (float time in new[] { 0f, .5f, 1f, 1.5f, 1.99f })
                {
                    clip.SampleAnimation(animation.gameObject, time);
                    int part = 0;
                    foreach (var skin in new[] { eyes, lens })
                    {
                        SauropeltaMeshSampling.Bake(skin, mesh);
                        var vertices = mesh.vertices;
                        int[] indices = { 0, vertices.Length / 4, vertices.Length / 2, vertices.Length - 1 };
                        var points = indices.Select(i => head.InverseTransformPoint(skin.transform.TransformPoint(vertices[i]))).ToArray();
                        var expected = skin.bones[skin.sharedMesh.boneWeights[0].boneIndex0].TransformPoint(
                            skin.sharedMesh.bindposes[skin.sharedMesh.boneWeights[0].boneIndex0].MultiplyPoint3x4(skin.sharedMesh.vertices[0]));
                        Require(Vector3.Distance(skin.transform.TransformPoint(vertices[0]), expected) < .0001f,
                            "Eye mesh sampling does not match its head bind pose.");
                        if (reference[part] == null) reference[part] = points;
                        for (int i = 0; i < points.Length; i++)
                            maximumDrift = Mathf.Max(maximumDrift, head.TransformVector(points[i] - reference[part][i]).magnitude);
                        part++;
                    }
                    Require(maximumDrift < .001f, "Eyes drift relative to the animated head: " + maximumDrift);
                    Capture(clone, eyes, time, mesh);
                }
                File.WriteAllText("Logs/protoceratops-eyes-acceptance.txt",
                    $"PASS: 3 renderers; original 7044 eye / 14144 cornea vertices; rigid head binding; 5 animation phases; maximum drift={maximumDrift:F6}m; left/front/right close-ups captured.\n");
                Debug.Log("PROTOCERATOPS_EYES_PASS");
            }
            finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(clone); }
        }

        private static void Capture(GameObject clone, SkinnedMeshRenderer eyes, float time, Mesh mesh)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("Eye acceptance requires a graphics device.");
            SauropeltaMeshSampling.Bake(eyes, mesh);
            var vertices = mesh.vertices.Select(v => eyes.transform.TransformPoint(v)).ToArray();
            Vector3 center = vertices.Aggregate(Vector3.zero, (sum, p) => sum + p) / vertices.Length;
            var cameraObject = new GameObject("Eye acceptance camera", typeof(Camera));
            var lightObject = new GameObject("Eye acceptance light", typeof(Light));
            var target = RenderTexture.GetTemporary(800, 640, 24);
            var pixels = new Texture2D(800, 640, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            bool fog = RenderSettings.fog;
            try
            {
                RenderSettings.fog = false;
                var camera = cameraObject.GetComponent<Camera>();
                camera.enabled = false;
                camera.targetTexture = target;
                camera.cullingMask = 1 << 31;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.12f, .15f, .17f);
                camera.nearClipPlane = .01f;
                camera.farClipPlane = 10f;
                camera.fieldOfView = 35f;
                var light = lightObject.GetComponent<Light>();
                light.type = LightType.Directional;
                light.cullingMask = 1 << 31;
                light.intensity = 1.4f;
                var forward = clone.transform.forward;
                var right = clone.transform.right;
                // The prefab already aligns the visual model to +Z.
                foreach (var view in new[] { (name: "left", side: -1f), (name: "front", side: 0f), (name: "right", side: 1f) })
                {
                    var direction = (forward * .85f + right * view.side).normalized;
                    camera.transform.position = center + direction * .65f + Vector3.up * .045f;
                    camera.transform.LookAt(center);
                    light.transform.rotation = Quaternion.LookRotation(-direction + Vector3.down * .4f);
                    camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 800, 640), 0, 0);
                    pixels.Apply();
                    File.WriteAllBytes($"Logs/protoceratops-eye-{view.name}-{time:F2}.png", pixels.EncodeToPNG());
                }
            }
            finally
            {
                RenderSettings.fog = fog;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(lightObject);
            }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
