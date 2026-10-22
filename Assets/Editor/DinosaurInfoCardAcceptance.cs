using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    // Requires a real graphics device: -batchmode (without -nographics).
    public static partial class DinosaurInfoCardAcceptance
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static Scene previous, scene;
        private static Camera camera;
        private static DinosaurVisibility detector;
        private static DinosaurInfoCard card;
        private static GameObject root;
        private static readonly List<Object> ownedAssets = new List<Object>();
        private static int passed;
        private static double asyncDeadline;
        private static bool retiring;
        private static readonly string Output = "Logs/InfoCard";
        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            passed++; Debug.Log("INFO_CARD_PASS: " + message);
        }

        public static void Run()
        {
            try
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                    throw new InvalidOperationException("Visibility acceptance requires a graphics device.");
                Directory.CreateDirectory(Output);
                previous = SceneManager.GetActiveScene();
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                    Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
                root = new GameObject("Visibility Acceptance");
                camera = new GameObject("Test Camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform);
                camera.aspect = 1f; camera.fieldOfView = 90f;
                camera.nearClipPlane = .1f; camera.farClipPlane = 200f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.gray;
                card = new GameObject("Test Card").AddComponent<DinosaurInfoCard>();
                card.transform.SetParent(root.transform);
                Set(card, "titleText", new GameObject("Title", typeof(RectTransform), typeof(Text)).GetComponent<Text>());
                Set(card, "bodyText", new GameObject("Body", typeof(RectTransform), typeof(Text)).GetComponent<Text>());
                Invoke(card, "Awake");
                detector = card.GetComponent<DinosaurVisibility>();

                var ground = Cube("Ground A", new Vector3(0, 0, 10), new Vector3(4, 4, 1)).AddComponent<DinosaurAgent>();
                var other = Cube("Ground B", new Vector3(0, 0, -20), new Vector3(2, 2, 1)).AddComponent<DinosaurAgent>();
                var flyer = Cube("Flying", new Vector3(0, 0, -10), new Vector3(4, 2, 1)).AddComponent<PteradactalFlightController>();
                Set(ground, "displayName", "Ground A"); Set(other, "displayName", "Ground B");
                var wall = Cube("Occluder without collider", new Vector3(0, 0, 8), new Vector3(1, 10, .1f));
                Object.DestroyImmediate(wall.GetComponent<Collider>());
                wall.SetActive(false);

                var item = Capture(ground);
                Require(item.VisiblePixels >= 16 && item.VisibleRatio > .95f, "unoccluded silhouette");
                var groundTarget = item.Target;
                int originalPixels = item.VisiblePixels;
                var unusedLod = Cube("Large inactive LOD", Vector3.zero, Vector3.one);
                unusedLod.transform.SetParent(ground.transform, false);
                unusedLod.transform.localScale = new Vector3(3, 3, 1);
                var lodGroup = ground.gameObject.AddComponent<LODGroup>();
                lodGroup.SetLODs(new[] {
                    new LOD(.1f, new[] { ground.GetComponent<Renderer>() }),
                    new LOD(.01f, new[] { unusedLod.GetComponent<Renderer>() }) });
                lodGroup.RecalculateBounds(); Set(detector, "nextRefresh", 0f);
                item = Capture(ground);
                Require(item.VisibleRatio > .95f && Mathf.Abs(item.VisiblePixels - originalPixels) < 5,
                    "inactive LOD geometry is excluded from both masks");
                Object.DestroyImmediate(lodGroup); Object.DestroyImmediate(unusedLod);
                Set(detector, "nextRefresh", 0f);
                float projectedWidth = 4f * 8f / 9.5f;
                Action<float> cover = fraction =>
                {
                    wall.SetActive(true);
                    wall.transform.localScale = new Vector3(projectedWidth * fraction, 10, .001f);
                    wall.transform.position = new Vector3(-projectedWidth * .5f + projectedWidth * fraction * .5f, 0, 8);
                };
                cover(.4f); item = Capture(ground);
                Require(item.VisibleRatio > .55f && item.VisibleRatio < .65f, "40 percent occluded remains eligible: " + item.VisibleRatio);
                cover(.6f); item = Capture(ground);
                Require(item.VisibleRatio > .35f && item.VisibleRatio < .45f, "60 percent occluded is rejected: " + item.VisibleRatio);
                Evaluate(detector.Observations, 0);
                Require(Current() == null, "mostly hidden ground card closes");
                cover(1f); item = Capture(ground);
                Require(item.VisiblePixels == 0, "complete occlusion without collider");
                wall.SetActive(false);

                var leaves = GameObject.CreatePrimitive(PrimitiveType.Quad);
                leaves.name = "Alpha cutout leaves without collider"; leaves.transform.SetParent(root.transform);
                leaves.transform.position = new Vector3(0, 0, 8); leaves.transform.localScale = new Vector3(10, 10, 1);
                Object.DestroyImmediate(leaves.GetComponent<Collider>());
                var leafMaterial = Own(new Material(Shader.Find("Jurassic World/Foliage Cutout Double Sided")));
                var alpha = Own(new Texture2D(8, 8, TextureFormat.RGBA32, false));
                alpha.filterMode = FilterMode.Point;
                var colors = new Color32[64];
                Action<byte> setAlpha = a => { for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(255, 255, 255, a); alpha.SetPixels32(colors); alpha.Apply(); };
                leafMaterial.mainTexture = alpha; leaves.GetComponent<Renderer>().sharedMaterial = leafMaterial;
                setAlpha(0); item = Capture(ground);
                Require(item.VisibleRatio > .95f, "transparent leaf texels do not occlude");
                setAlpha(255); item = Capture(ground);
                Require(item.VisiblePixels == 0, "opaque leaf texels occlude");
                for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) colors[y * 8 + x] = new Color32(255, 255, 255, (byte)(x < 4 ? 0 : 255));
                alpha.SetPixels32(colors); alpha.Apply(); item = Capture(ground);
                Require(item.VisibleRatio > .45f && item.VisibleRatio < .55f, "leaf cutout holes preserve actual visibility: " + item.VisibleRatio);
                leaves.SetActive(false);

                var terrainData = Own(new TerrainData { heightmapResolution = 33, size = new Vector3(20, 10, 4) });
                var heights = new float[33, 33];
                for (int z = 0; z < 33; z++) for (int x = 0; x < 33; x++) heights[z, x] = Mathf.Sin(z * Mathf.PI / 32) * .9f;
                terrainData.SetHeights(0, 0, heights);
                var hill = Terrain.CreateTerrainGameObject(terrainData);
                hill.transform.SetParent(root.transform); hill.transform.position = new Vector3(-10, -3, 4);
                Object.DestroyImmediate(hill.GetComponent<TerrainCollider>());
                item = Capture(ground);
                Require(item.VisiblePixels == 0, "terrain hill occludes without collider");
                hill.SetActive(false);

                // Another dinosaur is part of scene depth, not an ignored collider layer.
                other.transform.position = new Vector3(0, 0, 7); other.transform.localScale = new Vector3(5, 5, 1);
                item = Capture(ground);
                Require(item.VisiblePixels == 0, "near dinosaur occludes far dinosaur");
                other.transform.position = new Vector3(-3, 0, 10); other.transform.localScale = Vector3.one;
                flyer.transform.position = new Vector3(3, 0, 10);
                Capture(ground);
                var otherTarget = detector.Observations.First(o => o.Target.Owner == other).Target;
                var flyerTarget = detector.Observations.First(o => o.Target.Owner == flyer).Target;
                SelectionChecks(groundTarget, otherTarget, flyerTarget);

                ground.gameObject.SetActive(false); other.gameObject.SetActive(false);
                flyer.transform.position = new Vector3(0, 0, 10);
                item = Capture(flyer);
                Require(item.VisibleRatio > .95f, "flyer visible with no active ground dinosaurs");
                cover(1f); item = Capture(flyer);
                Require(item.VisiblePixels == 0, "flyer hidden by foreground geometry");
                wall.SetActive(false);
                camera.transform.rotation = Quaternion.Euler(0, 180, 0);
                detector.CaptureNow(camera);
                Require(detector.Observations.Count == 0, "turning away removes all candidates");
                camera.transform.rotation = Quaternion.identity;
                flyer.enabled = false; detector.CaptureNow(camera);
                Require(detector.Observations.Count == 0, "disabled flyer loses eligibility");
                flyer.enabled = true;
                flyer.GetComponent<Renderer>().enabled = false; detector.CaptureNow(camera);
                Require(detector.Observations.Count == 0, "disabled renderer loses eligibility");
                flyer.GetComponent<Renderer>().enabled = true;
                camera.cullingMask = 0; detector.CaptureNow(camera);
                Require(detector.Observations.Count == 0, "camera culling mask respected");
                camera.cullingMask = -1;
                var production = AssetDatabase.LoadAssetAtPath<GameObject>(PteradactalSceneIntegration.PrefabPath);
                var realFlyer = Object.Instantiate(production, root.transform).GetComponent<PteradactalFlightController>();
                realFlyer.transform.position = new Vector3(0, 0, 10);
                var flight = realFlyer.GetComponentInChildren<Animation>();
                flight.clip.SampleAnimation(flight.gameObject, .5f);
                flyer.gameObject.SetActive(false);
                Set(detector, "nextRefresh", 0f);
                item = Capture(realFlyer);
                Require(item.VisiblePixels >= 16 && item.VisibleRatio > .85f,
                    "production skinned flyer visible: " + item.VisibleRatio + ", pixels " + item.VisiblePixels);
                SaveView("production-flyer");
                Object.DestroyImmediate(realFlyer.gameObject);
                detector.CaptureNow(camera);
                Require(detector.Observations.Count == 0, "destroyed flyer leaves no stale observations");
                flyer.gameObject.SetActive(true);
                if (SystemInfo.supportsAsyncGPUReadback)
                {
                    Set(detector, "nextRefresh", 0f);
                    Set(detector, "nextSample", 0f);
                    detector.Tick(camera);
                    Require(detector.IsPending, "runtime starts one async readback");
                    detector.Tick(camera);
                    Require(detector.IsPending, "runtime does not stack readback requests");
                    asyncDeadline = EditorApplication.timeSinceStartup + 30;
                    retiring = false;
                    EditorApplication.update += AwaitReadback;
                }
                else { Debug.Log("INFO_CARD_ASYNC_UNAVAILABLE"); Finish(null); }
            }
            catch (Exception error) { Finish(error); }
        }

        private static void SelectionChecks(DinosaurVisibility.Target ground, DinosaurVisibility.Target other, DinosaurVisibility.Target flyer)
        {
            var list = new List<DinosaurVisibility.Observation>();
            Action<DinosaurVisibility.Target, Vector2, float, float> add = (target, center, distance, area) =>
                list.Add(Mock(target, center, distance, area));
            Action reset = () => { list.Clear(); Invoke(card, "Select", new object[] { null }); };
            reset(); add(ground, new Vector2(.5f, .5f), 80, .01f); add(flyer, new Vector2(.5f, .5f), 1, .1f);
            Evaluate(list, 0); Require(Current() == ground.Owner, "core ground dinosaur overrides closer larger flyer");
            reset(); add(ground, new Vector2(.8f, .5f), 40, .005f); add(flyer, new Vector2(.5f, .5f), 20, .02f);
            Evaluate(list, 0); Require(Current() == flyer.Owner, "flyer competes when ground is outside core");
            list[0] = Mock(ground, new Vector2(.5f, .5f), 80, .005f);
            Evaluate(list, .01f); Require(Current() == ground.Owner, "core priority bypasses hysteresis immediately");
            reset(); add(ground, new Vector2(.7f, .5f), 15, .02f); add(other, new Vector2(.9f, .5f), 15, .02f);
            Evaluate(list, 0); Require(Current() == ground.Owner, "center position dominates equal distance and area");
            reset(); add(ground, new Vector2(.7f, .5f), 5, .02f); add(other, new Vector2(.7f, .5f), 80, .02f);
            Evaluate(list, 0); Require(Current() == ground.Owner, "nearer target wins equal center and area");
            reset(); add(ground, new Vector2(.7f, .5f), 20, .04f); add(other, new Vector2(.7f, .5f), 20, .001f);
            Evaluate(list, 0); Require(Current() == ground.Owner, "larger visible area wins equal center and distance");
            reset(); add(ground, new Vector2(.8f, .5f), 40, .001f); Evaluate(list, 0);
            add(other, new Vector2(.65f, .5f), 5, .05f);
            Evaluate(list, .1f); Require(Current() == ground.Owner, "challenger must persist before switching");
            Evaluate(list, .31f); Require(Current() == other.Owner, "15 percent better challenger switches after 0.2 seconds");
            reset(); add(ground, new Vector2(.7f, .5f), 20, .02f); Evaluate(list, 0);
            add(other, new Vector2(.69f, .5f), 20, .02f);
            Evaluate(list, .1f); Evaluate(list, 1f);
            Require(Current() == ground.Owner, "similar scores do not flicker");
            list[0] = Mock(ground, new Vector2(.7f, .5f), 20, .02f, 49, 100);
            Evaluate(list, 1.01f); Require(Current() == other.Owner, "mostly occluded current target loses eligibility immediately");
            reset(); list.Add(Mock(ground, new Vector2(.5f, .5f), 10, .02f, 50, 100));
            Evaluate(list, 0); Require(Current() == ground.Owner, "exactly 50 percent visible is eligible");
            reset(); list.Add(Mock(ground, new Vector2(.5f, .5f), 10, .02f, 15, 15));
            Evaluate(list, 0); Require(Current() == null, "fewer than 16 pixels is not an identifiable target");
        }

        private static DinosaurVisibility.Observation Mock(DinosaurVisibility.Target target, Vector2 center, float distance, float area, int visible = 100, int total = 100) =>
            (DinosaurVisibility.Observation)Activator.CreateInstance(typeof(DinosaurVisibility.Observation), Private, null,
                new object[] { target, visible, total, center, distance, Mathf.RoundToInt(visible / area) }, null);
        private static void AwaitReadback()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > asyncDeadline) throw new TimeoutException("GPU readback timeout.");
                if (retiring)
                {
                    if (Resources.FindObjectsOfTypeAll<RenderTexture>().Any(t => t.name == "Dinosaur Visibility Buffer")) return;
                    Require(detector.Observations.Count == 0, "retired readback cannot restore stale observations");
                    Require(!Resources.FindObjectsOfTypeAll<Camera>().Any(c => c.name == "Dinosaur Visibility Camera"), "probe camera released");
                    Require(true, "all visibility buffers released after pending readback");
                    Finish(null);
                    return;
                }
                if (detector.IsPending) return;
                Require(detector.Observations.Any(o => o.VisiblePixels >= 16), "async GPU readback returns real visible pixels");
                Set(detector, "nextSample", 0f);
                detector.Tick(camera);
                Require(detector.IsPending, "second async capture starts before camera replacement");
                var replacement = new GameObject("Replacement Camera").AddComponent<Camera>();
                replacement.transform.SetParent(root.transform);
                replacement.transform.rotation = Quaternion.Euler(0, 180, 0);
                detector.Tick(replacement);
                Require(detector.Observations.Count == 0, "camera replacement clears old visibility immediately");
                detector.enabled = false;
                Require(detector.Observations.Count == 0, "disable clears observations and releases resources");
                retiring = true;
            }
            catch (Exception error) { Finish(error); }
        }

        private static DinosaurVisibility.Observation Capture(MonoBehaviour owner)
        {
            detector.CaptureNow(camera);
            return detector.Observations.First(o => o.Target.Owner == owner);
        }
        private static MonoBehaviour Current() => ((DinosaurVisibility.Target)Get(card, "currentTarget"))?.Owner;
        private static void Evaluate(IReadOnlyList<DinosaurVisibility.Observation> items, float now) => Invoke(card, "EvaluateAutoDisplay", items, camera, now);
        private static GameObject Cube(string name, Vector3 position, Vector3 scale)
        {
            var value = GameObject.CreatePrimitive(PrimitiveType.Cube); value.name = name;
            value.transform.SetParent(root.transform); value.transform.position = position; value.transform.localScale = scale;
            return value;
        }
        private static T Own<T>(T value) where T : Object { ownedAssets.Add(value); return value; }
        private static void SaveView(string name)
        {
            var target = new RenderTexture(640, 360, 24); var pixels = new Texture2D(640, 360, TextureFormat.RGB24, false);
            var old = RenderTexture.active; var oldTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); pixels.Apply();
                File.WriteAllBytes(Output + "/" + name + ".png", pixels.EncodeToPNG());
            }
            finally { camera.targetTexture = oldTarget; RenderTexture.active = old; Object.DestroyImmediate(target); Object.DestroyImmediate(pixels); }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= AwaitReadback;
            if (detector != null) detector.enabled = false;
            if (scene.IsValid() && previous.IsValid() && previous.isLoaded)
            { SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true); }
            foreach (var asset in ownedAssets) if (asset != null) Object.DestroyImmediate(asset);
            ownedAssets.Clear();
            if (error != null) Debug.LogException(error);
            else Debug.Log("INFO_CARD_ACCEPTANCE_PASS: " + passed + " checks");
            File.WriteAllText(Output + "/acceptance.txt", error == null ? "PASS: " + passed + " checks" : error.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
        }
        private static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        private static object Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    }
}
