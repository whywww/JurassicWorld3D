using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    // Isolated edit-mode checks; never saves or rebuilds the user's scene.
    public static class DinosaurInfoCardAcceptance
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            int exitCode = 0;
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var camera = new GameObject("Test Camera").AddComponent<Camera>();
                camera.aspect = 1f;
                camera.fieldOfView = 90f;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 200f;

                var card = new GameObject("Test Card").AddComponent<DinosaurInfoCard>();
                var title = new GameObject("Title", typeof(RectTransform)).AddComponent<Text>();
                var body = new GameObject("Body", typeof(RectTransform)).AddComponent<Text>();
                var group = card.gameObject.AddComponent<CanvasGroup>();
                Set(card, "canvasGroup", group);
                Set(card, "titleText", title);
                Set(card, "bodyText", body);
                Set(card, "cachedCamera", camera);
                Set(card, "agentCache", Array.Empty<DinosaurAgent>());
                Invoke(card, "Awake");

                var model = GameObject.CreatePrimitive(PrimitiveType.Cube);
                model.transform.localScale = new Vector3(4f, 2f, 2f);
                var flyer = model.AddComponent<PteradactalFlightController>();
                Set(card, "flyerCache", new[] { flyer });
                void Check(bool visible, string name, string expectedTitle = "翼龙")
                {
                    Invoke(card, "LateUpdate");
                    float target = (float)Get(card, "fadeTarget");
                    if (target != (visible ? 1f : 0f) || (visible && title.text != expectedTitle))
                        throw new InvalidOperationException(name + ": unexpected card state/title.");
                    Debug.Log("INFO_CARD_PASS: " + name);
                }

                model.transform.position = new Vector3(0f, 0f, -10f);
                Check(false, "behind camera, no ground dinosaurs");
                model.transform.position = new Vector3(0f, 0f, 60f);
                Check(true, "visible beyond ground dinosaur distance");
                if (body.text != flyer.Description) throw new InvalidOperationException("Missing description.");
                foreach (Vector3 edge in new[] {
                    new Vector3(12f, 0f, 10f), new Vector3(-12f, 0f, 10f),
                    new Vector3(0f, 11.5f, 10f), new Vector3(0f, -11.5f, 10f) })
                {
                    model.transform.position = edge;
                    Check(true, "partial bounds at edge " + edge);
                }
                model.transform.position = new Vector3(20f, 0f, 10f);
                Check(false, "fully outside frame");
                model.transform.position = new Vector3(0f, 0f, 10f);
                Check(true, "re-enter frame");
                camera.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                Check(false, "camera turns away");
                camera.transform.rotation = Quaternion.identity;
                Check(true, "camera returns");
                flyer.enabled = false;
                Check(false, "disabled controller");
                flyer.enabled = true;
                model.SetActive(false);
                Check(false, "inactive flyer");
                model.SetActive(true);
                var renderer = model.GetComponent<Renderer>();
                renderer.enabled = false;
                Check(false, "disabled renderer");
                renderer.enabled = true;
                camera.cullingMask = 0;
                Check(false, "culled layer");
                camera.cullingMask = -1;

                var ground = new GameObject("Ground Dinosaur").AddComponent<DinosaurAgent>();
                ground.transform.position = new Vector3(0f, -ground.ViewFocusHeight, 10f);
                Set(card, "agentCache", new[] { ground });
                Check(true, "flyer takes priority over centered ground dinosaur");
                model.transform.position = new Vector3(20f, 0f, 10f);
                Check(true, "ground card restored", ground.DisplayName);
                model.transform.position = new Vector3(0f, 0f, 10f);
                Check(true, "flyer takes priority again");
                Object.DestroyImmediate(model);
                Check(true, "destroyed flyer restores ground card", ground.DisplayName);
                ground.transform.position = new Vector3(0f, 0f, -10f);
                Check(false, "no eligible targets closes card");

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PteradactalSceneIntegration.PrefabPath);
                if (prefab == null) throw new InvalidOperationException("Missing production flyer prefab.");
                var realFlyer = Object.Instantiate(prefab).GetComponent<PteradactalFlightController>();
                realFlyer.transform.position = new Vector3(0f, 0f, 20f);
                Set(card, "flyerCache", new[] { realFlyer });
                Check(true, "production skinned flyer bounds");
                realFlyer.transform.position = new Vector3(0f, 0f, -20f);
                Check(false, "production flyer behind camera");
                Debug.Log("INFO_CARD_ACCEPTANCE_PASS");
            }
            catch (Exception exception)
            {
                exitCode = 1;
                Debug.LogException(exception);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
            if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        }

        private static object Get(object target, string field) =>
            target.GetType().GetField(field, Private).GetValue(target);
        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, Private).SetValue(target, value);
        private static void Invoke(object target, string method) =>
            target.GetType().GetMethod(method, Private).Invoke(target, null);
    }
}
