using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class SauropeltaSceneIntegration
    {
        internal const string Root = "Assets/Dinosaurs/Sauropelta/Prepared";
        internal const string PrefabPath = Root + "/SauropeltaLegacy.prefab";
        private const string ModelPath = "Assets/Dinosaurs/Sauropelta/Source/Models/Sauropelta.fbx";

        [MenuItem("Jurassic World/Scene/Add or Update Sauropelta")]
        public static void AddToMainScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.isDirty) throw new InvalidOperationException("Save current scene edits before adding Sauropelta.");
            if (scene.path != "Assets/Scenes/Main.unity") scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            EnsureAssets();
            var previous = Object.FindObjectsOfType<DinosaurAgent>(true).Where(a => a.name == "Sauropelta").ToArray();
            DinosaurAgent added = null;
            try { added = JurassicWorldSceneBuilder.AddSauropeltaToCurrentScene(); }
            catch
            {
                foreach (var a in Object.FindObjectsOfType<DinosaurAgent>(true).Where(a => a.name == "Sauropelta" && !previous.Contains(a)))
                    Object.DestroyImmediate(a.gameObject);
                throw;
            }
            foreach (var a in previous) Object.DestroyImmediate(a.gameObject);
            if (File.Exists(FoliageInteractionPreparation.EnabledPath)) FoliageInteractionPreparation.BakeInteractor(added);
            EditorUtility.SetDirty(added);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = added.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log("SAUROPELTA_ADDED: 5m, original Walk only, frozen stops, free roaming.");
        }

        internal static void EnsureAssets()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Sauropelta FBX missing.");
            if (!importer.importAnimation || importer.animationType != ModelImporterAnimationType.Legacy || !importer.isReadable)
            {
                importer.importAnimation = true;
                importer.animationType = ModelImporterAnimationType.Legacy;
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
            foreach (string file in new[] { "NormalMap.jpg", "AmbientOcclusionMap.png", "SpecularMap.png", "AlbedoMap.jpg" })
            {
                var texture = (TextureImporter)AssetImporter.GetAtPath("Assets/Dinosaurs/Sauropelta/Source/textures/" + file);
                bool normal = file == "NormalMap.jpg", srgb = file == "AlbedoMap.jpg";
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (texture.textureType == type && texture.sRGBTexture == srgb) continue;
                texture.textureType = type;
                texture.sRGBTexture = srgb;
                texture.SaveAndReimport();
            }
            var original = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Single(c => !c.name.StartsWith("__preview__"));
            var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "/Walk.anim");
            var copy = Object.Instantiate(original);
            copy.name = "Walk";
            copy.legacy = true;
            copy.wrapMode = WrapMode.Loop;
            AnimationUtility.SetAnimationEvents(copy, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(copy, settings);
            if (walk == null) { AssetDatabase.CreateAsset(copy, Root + "/Walk.anim"); walk = copy; }
            else { EditorUtility.CopySerialized(copy, walk); Object.DestroyImmediate(copy); EditorUtility.SetDirty(walk); }
            string materialPath = Root + "/Sauropelta.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null) { material = new Material(Shader.Find("Standard (Specular setup)")); AssetDatabase.CreateAsset(material, materialPath); }
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Dinosaurs/Sauropelta/Source/textures/AlbedoMap.jpg");
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Dinosaurs/Sauropelta/Source/textures/NormalMap.jpg"));
            material.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Dinosaurs/Sauropelta/Source/textures/AmbientOcclusionMap.png"));
            material.SetTexture("_SpecGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Dinosaurs/Sauropelta/Source/textures/SpecularMap.png"));
            material.SetFloat("_GlossMapScale", .2f);
            material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_SPECGLOSSMAP");
            EditorUtility.SetDirty(material);
            var holder = new GameObject("Sauropelta Legacy");
            try
            {
                var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), holder.transform, false);
                model.name = "Sauropelta Model";
                foreach (var animator in model.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
                foreach (var existing in model.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(existing);
                var animation = model.AddComponent<Animation>();
                animation.playAutomatically = false; animation.cullingType = AnimationCullingType.AlwaysAnimate;
                foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                    renderer.localBounds = renderer.sharedMesh.bounds;
                    renderer.updateWhenOffscreen = true;
                }
                var bones = model.GetComponentsInChildren<Transform>(true);
                Vector3 forward = bones.Single(b => b.name == "Bone.006").position - bones.Single(b => b.name == "Bone.007").position;
                forward = model.transform.InverseTransformDirection(forward);
                model.transform.localRotation *= Quaternion.Euler(0f, -Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 0f);
                animation.AddClip(walk, "Walk"); animation.clip = walk;
                PrefabUtility.SaveAsPrefabAsset(holder, PrefabPath);
            }
            finally { Object.DestroyImmediate(holder); }
        }

        internal static void Configure(DinosaurAgent agent)
        {
            var animation = agent.GetComponentInChildren<Animation>(true);
            if (animation == null || animation["Walk"] == null) throw new InvalidOperationException("Sauropelta Walk missing.");
            RouteObstacleClearance.BakeCoreProfile(agent);
            var body = RouteObstacleClearance.BakeInteractionBody(agent, new[] { animation["Walk"].clip });
            var envelope = body.bands[0].bounds;
            foreach (var band in body.bands.Skip(1)) envelope.Encapsulate(band.bounds);
            float bodyLength = Mathf.Max(envelope.size.x, envelope.size.z);
            if (bodyLength < 4f || bodyLength > 6f)
                throw new InvalidOperationException("Sauropelta navigation body does not match the 5m model: " + envelope.size);
            var data = new SerializedObject(agent);
            data.FindProperty("species").enumValueIndex = (int)DinosaurAgent.DinosaurSpecies.Sauropelta;
            data.FindProperty("roamingMode").enumValueIndex = (int)DinosaurAgent.RoamingMode.Free;
            data.FindProperty("playerInteractionEnabled").boolValue = false;
            data.FindProperty("interactionBody").objectReferenceValue = body;
            data.FindProperty("safetyDistance").floatValue = 15f;
            data.FindProperty("safetyReleaseDistance").floatValue = 20f;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(body); EditorUtility.SetDirty(agent);
        }
    }
}
