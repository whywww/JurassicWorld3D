using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public sealed class PteradactalImportSettings : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (assetPath != PteradactalSceneIntegration.Root + "/Pteradactal.fbx") return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Legacy;
            importer.importAnimation = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
            importer.animationRotationError = .1f;
            importer.animationPositionError = .1f;
            importer.animationScaleError = .1f;
            importer.clipAnimations = new[] { new ModelImporterClipAnimation {
                name = "Flight", takeName = "Scene", firstFrame = 0, lastFrame = 256.6f,
                loopTime = true, wrapMode = WrapMode.Loop } };
        }
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(PteradactalSceneIntegration.Root + "/") || !assetPath.EndsWith(".png")) return;
            var importer = (TextureImporter)assetImporter;
            importer.maxTextureSize = 2048;
            if (assetPath.EndsWith("Texture0.png")) importer.textureType = TextureImporterType.NormalMap;
            if (assetPath.EndsWith("Texture2.png") || assetPath.EndsWith("Texture3.png") || assetPath.EndsWith("MetallicSmoothness.png")) importer.sRGBTexture = false;
        }
    }

    public static class PteradactalSceneIntegration
    {
        public const string Root = "Assets/Dinosaurs/Pteradactal/Prepared";
        public const string PrefabPath = Root + "/Pteradactal.prefab";
        public const int Count = 1;

        [MenuItem("Jurassic World/Scene/Add or Update Flying Pteradactal")]
        public static void AddToMainScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty) throw new InvalidOperationException("Save the current scene before integrating the winged dinosaur.");
            if (scene.path != "Assets/Scenes/Main.unity") EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) EnsureAssets();
            AddToCurrentScene();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log("PTERADACTAL_ADDED: one airborne dinosaur; existing environment preserved.");
        }

        public static void EnsureAssets()
        {
            var importer = AssetImporter.GetAtPath(Root + "/Pteradactal.fbx") as ModelImporter;
            if (importer != null && importer.animationCompression != ModelImporterAnimationCompression.KeyframeReduction)
            {
                importer.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
                importer.SaveAndReimport();
            }
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Pteradactal.fbx");
            if (source == null) throw new InvalidOperationException("Run Tools/Pteradactal/convert_model.py with Blender first.");
            AnimationClip original = AssetDatabase.LoadAllAssetsAtPath(Root + "/Pteradactal.fbx")
                .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
            var clip = Object.Instantiate(original);
            clip.name = "Flight"; clip.legacy = true; clip.wrapMode = WrapMode.Loop;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            // Remove locomotion only from unskinned outer transforms; bone motion remains intact.
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (binding.type == typeof(Transform) && binding.propertyName.StartsWith("m_LocalPosition.") &&
                    (binding.path == "" || binding.path == "Flying pterodactyl"))
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, clip.length, curve.Evaluate(0f)));
                }
            clip = SaveAsset(clip, Root + "/Flight.anim");
            Material material = BuildMaterial();
            var holder = new GameObject("Pteradactal");
            try
            {
                var pivot = new GameObject("Flight Visual"); pivot.transform.SetParent(holder.transform, false);
                var model = Object.Instantiate(source, pivot.transform, false); model.name = "Pteradactal Model";
                foreach (var animator in model.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
                foreach (var old in model.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(old);
                var animation = model.AddComponent<Animation>();
                animation.AddClip(clip, "Flight"); animation.clip = clip;
                animation.playAutomatically = false; animation.wrapMode = WrapMode.Loop;
                animation.cullingType = AnimationCullingType.AlwaysAnimate;
                clip.SampleAnimation(model, 0f);
                var bones = model.GetComponentsInChildren<Transform>(true);
                Transform head = bones.First(b => b.name.StartsWith("head."));
                Transform pelvis = bones.First(b => b.name.StartsWith("pelvis."));
                Vector3 forward = head.position - pelvis.position; forward.y = 0f;
                model.transform.rotation = Quaternion.FromToRotation(forward.normalized, Vector3.forward) * model.transform.rotation;
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterial = material;
                    if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
                }
                Bounds bounds = SampleBounds(model, clip);
                float scale = 5f / bounds.size.x;
                model.transform.localScale *= scale;
                bounds = SampleBounds(model, clip);
                model.transform.localPosition -= bounds.center;
                // Conservatively covers every wing pose and the maximum bank/pitch rotation.
                float radius = bounds.extents.magnitude;
                var controller = holder.AddComponent<PteradactalFlightController>();
                var serialized = new SerializedObject(controller);
                serialized.FindProperty("visualPivot").objectReferenceValue = pivot.transform;
                serialized.FindProperty("flightAnimation").objectReferenceValue = animation;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                DinosaurForestMaterials.Apply(holder, "Pteradactal");
                PrefabUtility.SaveAsPrefabAsset(holder, PrefabPath);
                File.WriteAllText("Tools/Pteradactal/model-metrics.json", JsonUtility.ToJson(new Metrics {
                    clipLength = clip.length, wingspan = bounds.size.x, radius = radius,
                    size = bounds.size, modelYaw = model.transform.localEulerAngles.y }, true));
            }
            finally { Object.DestroyImmediate(holder); }
        }

        [Serializable] private class Metrics { public float clipLength, wingspan, radius, modelYaw; public Vector3 size; }

        public static Bounds SampleBounds(GameObject model, AnimationClip clip)
        {
            Bounds bounds = new Bounds(); bool first = true;
            var baked = new Mesh();
            try
            {
                int samples = Mathf.CeilToInt(clip.length * 60f);
                for (int i = 0; i <= samples; i++)
                {
                    clip.SampleAnimation(model, clip.length * i / samples);
                    foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        skin.BakeMesh(baked, true);
                        foreach (Vector3 vertex in baked.vertices)
                        {
                            Vector3 p = skin.transform.TransformPoint(vertex);
                            if (model.transform.parent != null) p = model.transform.parent.InverseTransformPoint(p);
                            if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                            else bounds.Encapsulate(p);
                        }
                    }
                }
                clip.SampleAnimation(model, 0f);
            }
            finally { Object.DestroyImmediate(baked); }
            if (first || bounds.size.x < .001f) throw new InvalidOperationException("Winged dinosaur has no valid skinned mesh.");
            return bounds;
        }

        private static Material BuildMaterial()
        {
            var shader = Shader.Find("JurassicWorld/Pteradactal Double Sided");
            if (shader == null) throw new InvalidOperationException("Pteradactal double-sided shader missing.");
            var material = new Material(shader) { name = "Pteradactal" };
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Texture1.png"));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Texture0.png"));
            material.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Texture3.png"));
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/MetallicSmoothness.png"));
            material.color = new Color(.9963f, .8945f, .6672f, 1f);
            material.SetFloat("_Metallic", .0506f); material.SetFloat("_Glossiness", 1f);
            material.EnableKeyword("_NORMALMAP");
            return SaveAsset(material, Root + "/Pteradactal.mat");
        }

        private static T SaveAsset<T>(T value, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null) { AssetDatabase.CreateAsset(value, path); return value; }
            EditorUtility.CopySerialized(value, existing); EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(value); return existing;
        }

        public static void AddToCurrentScene()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) EnsureAssets();
            var previous = Object.FindObjectsOfType<PteradactalFlightController>(true);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            try
            {
                var animation = instance.GetComponentInChildren<Animation>();
                Bounds envelope = SampleBounds(animation.gameObject, animation.clip);
                var data = PteradactalAirspaceBaker.Bake(envelope.extents.magnitude + envelope.center.magnitude);
                UpdatePrefabSettings(data);
                var controller = instance.GetComponent<PteradactalFlightController>();
                var serialized = new SerializedObject(controller);
                serialized.FindProperty("minimumHeight").floatValue = 6f;
                serialized.FindProperty("maximumHeight").floatValue = 10f;
                serialized.FindProperty("speed").floatValue = 5f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                controller.PrepareFlight(data, Camera.main);
                PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
                if (instance.GetComponentInChildren<Animation>().clip == null) throw new InvalidOperationException("Missing flight clip.");
                foreach (var old in previous) Object.DestroyImmediate(old.gameObject);
                EditorSceneManager.MarkSceneDirty(instance.scene);
                Debug.Log($"PTERADACTAL_LOW_FLIGHT: 6..10m AGL; safeOrbits={data.orbits.Length}; spawn={instance.transform.position}; mode={controller.Mode}");
            }
            catch { Object.DestroyImmediate(instance); throw; }
        }

        private static void UpdatePrefabSettings(PteradactalAirspace data)
        {
            var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var settings = new SerializedObject(prefab.GetComponent<PteradactalFlightController>());
                settings.FindProperty("airspace").objectReferenceValue = data;
                settings.FindProperty("minimumHeight").floatValue = 6f;
                settings.FindProperty("maximumHeight").floatValue = 10f;
                settings.FindProperty("speed").floatValue = 5f;
                settings.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }

    }
}
