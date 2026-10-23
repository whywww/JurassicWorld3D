using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class BaurusuchusSceneIntegration
    {
        internal const string Root = "Assets/Dinosaurs/Baurusuchus/Prepared";
        internal const string PrefabPath = Root + "/BaurusuchusLegacy.prefab";
        internal const string SourceRoot = "Assets/Dinosaurs/Baurusuchus/Source";
        private const string ModelPath = SourceRoot + "/source/Baurusuchus.fbx";
        private const string RunPath = Root + "/Run.anim";
        private const string MaterialPath = Root + "/Baurusuchus.mat";
        private const string PortraitPath = "Assets/Dinosaurs/Baurusuchus/Prepared/Baurusuchus.png";

        [MenuItem("Jurassic World/Scene/Add or Update Baurusuchus")]
        public static void AddToMainScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.isDirty) throw new InvalidOperationException("Save current scene edits before adding Baurusuchus.");
            if (scene.path != "Assets/Scenes/Main.unity") scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            EnsureAssets();
            var previous = Object.FindObjectsOfType<DinosaurAgent>(true).Where(a => a.name == "Baurusuchus").ToArray();
            DinosaurAgent added = null;
            try
            {
                added = JurassicWorldSceneBuilder.AddBaurusuchusToCurrentScene();
                if (File.Exists(FoliageInteractionPreparation.EnabledPath)) FoliageInteractionPreparation.BakeInteractor(added);
            }
            catch
            {
                foreach (var a in Object.FindObjectsOfType<DinosaurAgent>(true).Where(a => a.name == "Baurusuchus" && !previous.Contains(a)))
                    Object.DestroyImmediate(a.gameObject);
                throw;
            }
            foreach (var a in previous) Object.DestroyImmediate(a.gameObject);
            EditorUtility.SetDirty(added);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = added.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log("BAURUSUCHUS_ADDED: 5m, Legacy Run only, always running, free roaming, no player interaction.");
        }

        internal static void EnsureAssets()
        {
            if (!AssetDatabase.IsValidFolder(Root)) JurassicWorldSceneBuilder.EnsureFolder(Root);
            JurassicWorldSceneBuilder.EnsureFolder(Root);
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Baurusuchus FBX missing: " + ModelPath);
            if (!importer.importAnimation || importer.animationType != ModelImporterAnimationType.Legacy || !importer.isReadable)
            {
                importer.importAnimation = true;
                importer.animationType = ModelImporterAnimationType.Legacy;
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
            EnsureTexture(SourceRoot + "/textures/AlbedoMap.png", TextureImporterType.Default, true);
            EnsureTexture(SourceRoot + "/textures/NormalMap.png", TextureImporterType.NormalMap, false);
            EnsureTexture(SourceRoot + "/textures/AmbientOcclusionMap.png", TextureImporterType.Default, false);
            EnsureTexture(SourceRoot + "/textures/RoughnessMap.png", TextureImporterType.Default, false);
            EnsureTexture(SourceRoot + "/textures/SpecularMap.png", TextureImporterType.Default, false);

            var original = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                .Single(c => !c.name.StartsWith("__preview__"));
            var run = CopyRun(original);
            var material = EnsureMaterial();
            var holder = new GameObject("Baurusuchus Legacy");
            try
            {
                var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), holder.transform, false);
                model.name = "Baurusuchus Model";
                foreach (var animator in model.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
                foreach (var existing in model.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(existing);
                var animation = model.AddComponent<Animation>();
                animation.playAutomatically = false;
                animation.cullingType = AnimationCullingType.AlwaysAnimate;
                foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    renderer.sharedMaterials = Enumerable.Repeat(material, Mathf.Max(1, renderer.sharedMaterials.Length)).ToArray();
                    if (renderer.sharedMesh != null) renderer.localBounds = renderer.sharedMesh.bounds;
                    renderer.updateWhenOffscreen = true;
                }
                animation.AddClip(run, "Run");
                animation.clip = run;
                DinosaurForestMaterials.Apply(holder, "Baurusuchus");
                PrefabUtility.SaveAsPrefabAsset(holder, PrefabPath);
            }
            finally { Object.DestroyImmediate(holder); }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) throw new InvalidOperationException("Baurusuchus prefab was not saved: " + PrefabPath);
            if (!File.Exists(PortraitPath))
            {
                try
                {
                    DinosaurPortraitBuilder.CaptureModel(prefab, "Baurusuchus", PortraitPath);
                    DinosaurPortraitBuilder.ConfigurePortraitImport(PortraitPath);
                    Debug.Log("BAURUSUCHUS_PORTRAIT_READY: " + PortraitPath);
                }
                catch (Exception error)
                {
                    Debug.LogWarning("BAURUSUCHUS_PORTRAIT_FAILED: " + error.Message);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("BAURUSUCHUS_ASSETS_READY: clip=" + original.name + ", run=" + RunPath + ", prefab=" + PrefabPath + ".");
        }

        internal static void Configure(DinosaurAgent agent)
        {
            var animation = agent.GetComponentInChildren<Animation>(true);
            if (animation == null || animation["Run"] == null)
                throw new InvalidOperationException("Baurusuchus requires the Legacy Run animation.");
            RouteObstacleClearance.BakeCoreProfile(agent);
            var body = RouteObstacleClearance.BakeInteractionBody(agent, new[] { animation["Run"].clip });
            var data = new SerializedObject(agent);
            data.FindProperty("species").enumValueIndex = (int)DinosaurAgent.DinosaurSpecies.Baurusuchus;
            data.FindProperty("roamingMode").enumValueIndex = (int)DinosaurAgent.RoamingMode.ContinuousFree;
            if (agent.GaitProfile != null) data.FindProperty("continuousMinTurnRadius").floatValue
                = Mathf.Max(.5f, agent.WalkSpeed / (agent.GaitProfile.turnSpeed * Mathf.Deg2Rad));
            data.FindProperty("playerInteractionEnabled").boolValue = false;
            data.FindProperty("interactionBody").objectReferenceValue = body;
            data.FindProperty("safetyDistance").floatValue = 15f;
            data.FindProperty("safetyReleaseDistance").floatValue = 20f;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(body);
            EditorUtility.SetDirty(agent);
        }

        private static AnimationClip CopyRun(AnimationClip source)
        {
            var copy = BaurusuchusWalkingClip.Crop(source);
            copy.name = "Run";
            copy.legacy = true;
            copy.wrapMode = WrapMode.Loop;
            AnimationUtility.SetAnimationEvents(copy, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(copy, settings);
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(RunPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(copy, RunPath);
                return copy;
            }
            EditorUtility.CopySerialized(copy, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(copy);
            return existing;
        }

        private static Material EnsureMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            var shader = Shader.Find("Standard");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else if (shader != null && material.shader != shader)
            {
                material.shader = shader;
            }
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(SourceRoot + "/textures/NormalMap.png");
            material.color = Color.white;
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Glossiness", 0.25f);
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(SourceRoot + "/textures/AlbedoMap.png"));
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            material.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(SourceRoot + "/textures/AmbientOcclusionMap.png"));
            material.SetFloat("_OcclusionStrength", 0.65f);
            if (normal != null) material.EnableKeyword("_NORMALMAP"); else material.DisableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureTexture(string path, TextureImporterType type, bool srgb)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException("Baurusuchus texture missing: " + path);
            if (importer.textureType == type && importer.sRGBTexture == srgb) return;
            importer.textureType = type;
            importer.sRGBTexture = srgb;
            importer.SaveAndReimport();
        }
    }
}
