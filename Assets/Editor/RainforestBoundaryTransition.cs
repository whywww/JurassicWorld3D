using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    /// <summary>Offline-only graded forest outside the union of gameplay bounds.</summary>
    public static class RainforestBoundaryTransition
    {
        internal const int MaximumTrees = 192;
        internal const string Output = RainforestLightForest.Report;
        private const string Materials = "Assets/Environment/Backdrop/Prepared/Materials/";

        internal static Rect ProtectedBounds
        {
            get
            {
                Rect player = WorldBounds.PlayerExplorationXZ, dinosaur = WorldBounds.DinosaurActivityXZ;
                return Rect.MinMaxRect(Mathf.Min(player.xMin, dinosaur.xMin), Mathf.Min(player.yMin, dinosaur.yMin),
                    Mathf.Max(player.xMax, dinosaur.xMax), Mathf.Max(player.yMax, dinosaur.yMax));
            }
        }

        [Serializable] private sealed class Distribution
        {
            public Rect protectedBounds;
            public int[] bands;
            public float[] treesPerSquareMetre;
            public Vector3[] positions;
        }

        public static void ApplyBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Directory.CreateDirectory(Output);
            if (!File.Exists(Output + "/Main.Before.unity"))
            {
                EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), Output + "/Main.Before.unity", true);
                File.Copy("Assets/Environment/Backdrop/Prepared/RainforestBackdrop.prefab", Output + "/Backdrop.Before.prefab", false);
                Capture(Camera.main, "Before");
            }
            Transform ridges = GameObject.Find(RainforestBackdropStageThree.RootName).transform.Find("Distant Cliff Ridges");
            string beforeRidges = RidgeState(ridges);
            RainforestLightForest.BakeAtlases();
            RainforestBackdropStageThree.ApplyBatch();
            ridges = GameObject.Find(RainforestBackdropStageThree.RootName).transform.Find("Distant Cliff Ridges");
            if (RidgeState(ridges) != beforeRidges) throw new InvalidOperationException("Boundary adjustment changed the existing ridges.");
            Capture(Camera.main, "After");
            Debug.Log("BOUNDARY_TRANSITION_READY: gradual forest and connected ground saved; pause for manual Play review.");
        }

        public static void ApplyOpenEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty ||
                EditorSceneManager.GetActiveScene().path != "Assets/Scenes/Main.unity")
                throw new InvalidOperationException("Stop Play and save Main.unity before applying the boundary transition.");
            ApplyBatch();
        }

        internal static string RidgeState(Transform root) => string.Join("\n", root.Cast<Transform>().Select(t =>
            t.name + "|" + t.localPosition.ToString("R") + "|" + t.localRotation.ToString("R") + "|" + t.localScale.ToString("R") +
            "|" + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(t.GetComponent<MeshFilter>().sharedMesh)) +
            "|" + string.Join(",", t.GetComponent<Renderer>().sharedMaterials.Select(m => AssetDatabase.GetAssetPath(m)))));

        internal static void Populate(Transform parent, Terrain terrain, GameObject[] prototypes, Mesh[] meshes, int layer)
        {
            Rect bounds = ProtectedBounds;
            var random = new System.Random(731904);
            var original = new Material[prototypes.Length][];
            for (int type = 0; type < prototypes.Length; type++)
            {
                Material[] source = prototypes[type].GetComponentInChildren<MeshFilter>().GetComponent<Renderer>().sharedMaterials;
                original[type] = new Material[source.Length];
                for (int slot = 0; slot < source.Length; slot++)
                {
                    original[type][slot] = MaterialCopy("LightNear" + type + "_" + slot, source[slot]);
                }
            }
            int[] targets = { 128, 64 };
            for (int band = 0; band < 2; band++)
            {
                for (int index = 0; index < targets[band]; index++)
                {
                    float distance = Mathf.Lerp(band == 0 ? 3.5f : 21f,band == 0 ? 19f : 44f,(float)random.NextDouble());
                    Vector2 p = RainforestLightForest.Position(bounds,index,targets[band],distance,random);
                    int type = random.Next(prototypes.Length);
                    float height = Mathf.Lerp(band == 0 ? 10f : 14f,band == 0 ? 18f : 25f,(float)random.NextDouble());
                    float scale = height / Mathf.Max(1f, meshes[type].bounds.size.y);
                    GameObject tree = RainforestBackdropStageThree.Geometry("Transition Tree " + (band*128+index).ToString("000"),
                        parent, meshes[type], original[type], layer);
                    tree.transform.position = new Vector3(p.x, SurfaceHeight(terrain, p) - 0.12f, p.y);
                    tree.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                    float width = scale * Mathf.Lerp(0.82f, 1.06f, (float)random.NextDouble());
                    tree.transform.localScale = new Vector3(width, scale, width);
                }
            }
            Validate(parent, terrain);
            Directory.CreateDirectory(Output);
            int[] counts = BandCounts(parent);
            File.WriteAllText(Output + "/Distribution.json", JsonUtility.ToJson(new Distribution
            {
                protectedBounds = bounds, bands = counts, treesPerSquareMetre = Densities(counts),
                positions = parent.Cast<Transform>().Select(t => t.position).ToArray()
            }, true));
        }

        private static Material MaterialCopy(string name, Material source)
        {
            string path = Materials + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        internal static float Distance(Rect rect, Vector2 p) => Vector2.Distance(p,
            new Vector2(Mathf.Clamp(p.x, rect.xMin, rect.xMax), Mathf.Clamp(p.y, rect.yMin, rect.yMax)));

        internal static float SurfaceHeight(Terrain terrain, Vector2 p)
        {
            Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
            Rect rect = Rect.MinMaxRect(origin.x, origin.z, origin.x + size.x, origin.z + size.z);
            Vector3 edge = new Vector3(Mathf.Clamp(p.x, rect.xMin, rect.xMax), 0f, Mathf.Clamp(p.y, rect.yMin, rect.yMax));
            float height = terrain.SampleHeight(edge) + origin.y;
            float blend = Mathf.SmoothStep(0f, 1f, Distance(rect, p) / 60f);
            float d = Distance(rect,p);
            // Only isolated low hummocks, never a raised belt around the terrain.
            float patches = Mathf.SmoothStep(0f,1f,Mathf.Clamp01((Mathf.PerlinNoise(p.x*0.028f+32,p.y*0.028f+7)-0.52f)/0.18f));
            float hill = Mathf.SmoothStep(0f,1f,d/20f)*(1-Mathf.SmoothStep(0f,1f,(d-35f)/55f));
            return Mathf.Lerp(height, -1.3f, blend)+hill*patches*0.65f;
        }

        internal static Mesh SkirtMesh(Terrain terrain)
        {
            const string path = RainforestLightForest.Prepared+"/ForestTransitionGround.asset";
            float[] extents = { 98f, 100f, 104f, 110f, 118f, 132f, 150f, 175f, 210f, 300f, 510f };
            const int edgeSteps = 32, points = edgeSteps * 4;
            var vertices = new Vector3[points * extents.Length];
            var uv = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length];
            var triangles = new List<int>();
            Vector3 center = terrain.transform.position + terrain.terrainData.size * 0.5f;
            Vector2[] corners = { new Vector2(-1,-1), new Vector2(-1,1), new Vector2(1,1), new Vector2(1,-1) };
            for (int ring = 0; ring < extents.Length; ring++)
                for (int index = 0; index < points; index++)
                {
                    int edge = index / edgeSteps;
                    Vector2 p = Vector2.Lerp(corners[edge], corners[(edge + 1) % 4], (index % edgeSteps) / (float)edgeSteps) * extents[ring];
                    Vector2 world = p + new Vector2(center.x, center.z);
                    int vertex = ring * points + index;
                    vertices[vertex] = new Vector3(p.x, SurfaceHeight(terrain, world) - 0.08f, p.y);
                    uv[vertex] = world / 8f;
                    float variation = Mathf.PerlinNoise(world.x*0.07f+9,world.y*0.07f+23);
                    Color tint = Color.Lerp(new Color(0.86f,0.79f,0.66f),new Color(0.97f,1f,0.87f),variation);
                    float outside = Mathf.SmoothStep(0f,1f,Distance(new Rect(terrain.transform.position.x,terrain.transform.position.z,
                        terrain.terrainData.size.x,terrain.terrainData.size.z),world)/15f);
                    colors[vertex] = Color.Lerp(Color.white,tint,outside);
                    if (ring < extents.Length - 1)
                    {
                        int next = ring * points + (index + 1) % points, outer = vertex + points, outerNext = next + points;
                        triangles.AddRange(new[] { vertex, outer, next, next, outer, outerNext });
                    }
                }
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear();
            mesh.name = "Terrain Edge Transition Ground";
            mesh.vertices = vertices; mesh.uv = uv; mesh.colors = colors; mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static int[] BandCounts(Transform parent)
        {
            var counts = new int[3];
            foreach (Transform tree in parent)
            {
                float d = Distance(ProtectedBounds, new Vector2(tree.position.x, tree.position.z));
                counts[d < 20f ? 0 : d < 45f ? 1 : 2]++;
            }
            return counts;
        }

        private static float[] Densities(int[] counts)
        {
            Rect r = ProtectedBounds;
            float Area(float d) => 2f * (r.width + r.height) * d + Mathf.PI * d * d;
            return new[] { counts[0] / (Area(20f) - Area(3f)), counts[1] / (Area(45f) - Area(20f)), counts[2] / (Area(90f) - Area(45f)) };
        }

        internal static void Validate(Transform parent, Terrain terrain)
        {
            if (parent.childCount != MaximumTrees) throw new InvalidOperationException("Expected exactly 192 near boundary trees.");
            foreach (Transform tree in parent)
            {
                Vector2 p = new Vector2(tree.position.x, tree.position.z);
                if (Distance(ProtectedBounds, p) < 2.99f || Distance(ProtectedBounds, p) > 45.01f ||
                    Mathf.Abs(tree.position.y - (SurfaceHeight(terrain, p) - 0.12f)) > 0.02f)
                    throw new InvalidOperationException("Boundary tree entered the protection area or floats: " + tree.name);
            }
            int[] counts = BandCounts(parent);
            if (counts[0] != 128 || counts[1] != 64 || counts[2] != 0) throw new InvalidOperationException("Near forest band counts differ from the plan.");
            Debug.Log("BOUNDARY_TRANSITION_VALID: protected bounds " + ProtectedBounds + "; bands " +
                string.Join("/", counts));
        }

        internal static void BoundaryPose(int index, out Vector3 position, out Quaternion rotation)
        {
            Rect r = WorldBounds.PlayerExplorationXZ;
            Vector3[] positions =
            {
                new Vector3(r.xMax-2,0,0), new Vector3(0,0,r.yMax-2), new Vector3(r.xMin+2,0,0), new Vector3(0,0,r.yMin+2),
                new Vector3(r.xMax-2,0,r.yMax-2), new Vector3(r.xMin+2,0,r.yMax-2),
                new Vector3(r.xMin+2,0,r.yMin+2), new Vector3(r.xMax-2,0,r.yMin+2),
                new Vector3(r.xMax-2,0,r.yMax*0.45f),new Vector3(r.xMax-2,0,r.yMin*0.45f),
                new Vector3(r.xMax*0.45f,0,r.yMax-2),new Vector3(r.xMin*0.45f,0,r.yMax-2),
                new Vector3(r.xMin+2,0,r.yMax*0.45f),new Vector3(r.xMin+2,0,r.yMin*0.45f),
                new Vector3(r.xMax*0.45f,0,r.yMin+2),new Vector3(r.xMin*0.45f,0,r.yMin+2)
            };
            float[] yaw = { 90, 0, 270, 180, 45, 315, 225, 135,90,90,0,0,270,270,180,180 };
            position = positions[index];
            position.y = Terrain.activeTerrain.SampleHeight(position)+Terrain.activeTerrain.transform.position.y+1.7f;
            rotation = Quaternion.Euler(-3f,yaw[index],0);
        }

        internal static void Capture(Camera camera, string suffix, string directory = null)
        {
            directory = directory ?? Output;
            Directory.CreateDirectory(directory);
            Vector3 before = camera.transform.position; Quaternion rotation = camera.transform.rotation;
            float oldAspect = camera.aspect;
            RenderTexture previous = camera.targetTexture, active = RenderTexture.active;
            var target = new RenderTexture(1280, 720, 24);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var atmosphere = Object.FindObjectOfType<RainforestAtmosphereController>();
            Vector3 oldCloud = atmosphere.cloudRenderer.transform.position;
            try
            {
                camera.targetTexture = target;
                camera.aspect = 1280f/720f;
                for (int i = 0; i < 16; i++)
                {
                    BoundaryPose(i,out Vector3 p,out Quaternion look);
                    camera.transform.SetPositionAndRotation(p,look);
                    atmosphere.cloudRenderer.transform.position = p;
                    camera.Render(); RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0,0,1280,720),0,0); pixels.Apply();
                    File.WriteAllBytes(directory + "/Boundary-" + i + "." + suffix + ".png", pixels.EncodeToPNG());
                }
            }
            finally
            {
                camera.transform.SetPositionAndRotation(before, rotation); camera.targetTexture = previous;
                camera.aspect = oldAspect;
                atmosphere.cloudRenderer.transform.position = oldCloud; RenderTexture.active = active;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }
    }
}
