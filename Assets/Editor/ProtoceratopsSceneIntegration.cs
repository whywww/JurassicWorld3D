using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class ProtoceratopsSceneIntegration
    {
        internal const string Root = "Assets/Dinosaurs/Protoceratops/Prepared";
        internal const string PrefabPath = Root + "/ProtoceratopsLegacy.prefab";
        private const string ModelPath = Root + "/Protoceratops.fbx";

        [MenuItem("Jurassic World/Scene/Add or Update Protoceratops")]
        public static void AddToMainScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.isDirty) throw new InvalidOperationException("Save current scene edits before adding Protoceratops.");
            if (scene.path != "Assets/Scenes/Main.unity") scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            EnsureAssets();
            var previous = Object.FindObjectsOfType<DinosaurAgent>(true).Where(a => a.name == "Protoceratops").ToArray();
            DinosaurAgent added = null;
            try { added = JurassicWorldSceneBuilder.AddProtoceratopsToCurrentScene(); }
            catch
            {
                foreach (var a in Object.FindObjectsOfType<DinosaurAgent>(true).Where(a => a.name == "Protoceratops" && !previous.Contains(a)))
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
            Debug.Log("PROTOCERATOPS_ADDED: 2m, original WalkCycle only, shared continuous free roaming.");
        }

        internal static void EnsureAssets()
        {
            JurassicWorldSceneBuilder.EnsureFolder(Root);
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Protoceratops FBX missing.");
            if (!importer.importAnimation || importer.animationType != ModelImporterAnimationType.Legacy || !importer.isReadable)
            {
                importer.importAnimation = true;
                importer.animationType = ModelImporterAnimationType.Legacy;
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
            EnsureTexture("textures/protoceratops_dffs.png", false, true);
            EnsureTexture("textures/proto_eye.png", false, true);
            EnsureTexture("textures/protoceratops_nmls_02.png", true, false);
            EnsureTexture("textures/protoceratops_AO.png", false, false);
            var packed = (TextureImporter)AssetImporter.GetAtPath(Root + "/SpecularSmoothness.png");
            if (packed.sRGBTexture) { packed.sRGBTexture = false; packed.SaveAndReimport(); }
            var original = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Single(c => !c.name.StartsWith("__preview__"));
            var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "/WalkCycle.anim");
            var copy = Object.Instantiate(original);
            copy.name = "WalkCycle";
            copy.legacy = true;
            copy.wrapMode = WrapMode.Loop;
            AnimationUtility.SetAnimationEvents(copy, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(copy, settings);
            if (walk == null) { AssetDatabase.CreateAsset(copy, Root + "/WalkCycle.anim"); walk = copy; }
            else { EditorUtility.CopySerialized(copy, walk); Object.DestroyImmediate(copy); EditorUtility.SetDirty(walk); }
            var bodyMaterial = Material("Body", "Standard (Specular setup)");
            bodyMaterial.mainTexture = Texture("protoceratops_dffs.png");
            bodyMaterial.SetTexture("_BumpMap", Texture("protoceratops_nmls_02.png"));
            bodyMaterial.SetTexture("_OcclusionMap", Texture("protoceratops_AO.png"));
            bodyMaterial.SetTexture("_SpecGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/SpecularSmoothness.png"));
            bodyMaterial.SetFloat("_GlossMapScale", .35f);
            bodyMaterial.EnableKeyword("_NORMALMAP"); bodyMaterial.EnableKeyword("_SPECGLOSSMAP");
            var eyeMaterial = Material("Eyes", "Standard");
            eyeMaterial.mainTexture = Texture("proto_eye.png");
            eyeMaterial.SetFloat("_Glossiness", .4f);
            var lensMaterial = Material("Lens", "Standard");
            lensMaterial.SetColor("_Color", new Color(1f, 1f, 1f, .12f));
            lensMaterial.SetFloat("_Mode", 3f);
            lensMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            lensMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            lensMaterial.SetFloat("_ZWrite", 0f);
            lensMaterial.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            lensMaterial.SetOverrideTag("RenderType", "Transparent");
            lensMaterial.renderQueue = 3000;
            lensMaterial.SetFloat("_Glossiness", .9f);
            foreach (var m in new[] { bodyMaterial, eyeMaterial, lensMaterial }) EditorUtility.SetDirty(m);
            var holder = new GameObject("Protoceratops Legacy");
            try
            {
                var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), holder.transform, false);
                model.name = "Protoceratops Model";
                foreach (var animator in model.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
                foreach (var existing in model.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(existing);
                var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                string[] sourceMaterials = skins.SelectMany(r => r.sharedMaterials).Select(m => m.name).ToArray();
                if (skins.Length != 3 || !sourceMaterials.Contains("eyes") || !sourceMaterials.Contains("shinny lens"))
                    throw new InvalidOperationException("Protoceratops requires body, eyes and cornea meshes. Re-run the model converter.");
                var animation = model.AddComponent<Animation>();
                animation.playAutomatically = false; animation.cullingType = AnimationCullingType.AlwaysAnimate;
                foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(m => m.name.Contains("lens") ? lensMaterial : m.name.Contains("eyes") ? eyeMaterial : bodyMaterial).ToArray();
                    renderer.localBounds = renderer.sharedMesh.bounds;
                    renderer.updateWhenOffscreen = true;
                }
                var bones = model.GetComponentsInChildren<Transform>(true);
                Vector3 forward = bones.Single(b => b.name == "protoceratops_head.M_32").position - bones.Single(b => b.name == "protoceratops_pelvis.M_64").position;
                forward = model.transform.InverseTransformDirection(forward);
                model.transform.localRotation *= Quaternion.Euler(0f, -Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 0f);
                animation.AddClip(walk, "WalkCycle"); animation.clip = walk;
                PrefabUtility.SaveAsPrefabAsset(holder, PrefabPath);
                string portrait = Root + "/Protoceratops.png";
                DinosaurPortraitBuilder.CaptureModel(holder, "Protoceratops", portrait);
                DinosaurPortraitBuilder.ConfigurePortraitImport(portrait);
            }
            finally { Object.DestroyImmediate(holder); }
        }

        private static Texture2D Texture(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Dinosaurs/Protoceratops/Source/textures/" + file);

        private static Material Material(string name, string shader)
        {
            string path = Root + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(material, path); }
            return material;
        }

        private static void EnsureTexture(string file, bool normal, bool srgb)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Dinosaurs/Protoceratops/Source/" + file);
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType == type && importer.sRGBTexture == srgb) return;
            importer.textureType = type; importer.sRGBTexture = srgb; importer.SaveAndReimport();
        }

        internal static void Configure(DinosaurAgent agent)
        {
            var animation = agent.GetComponentInChildren<Animation>(true);
            if (animation == null || animation["WalkCycle"] == null) throw new InvalidOperationException("Protoceratops Walk missing.");
            RouteObstacleClearance.BakeCoreProfile(agent);
            var body = RouteObstacleClearance.BakeInteractionBody(agent, new[] { animation["WalkCycle"].clip });
            var envelope = body.bands[0].bounds;
            foreach (var band in body.bands.Skip(1)) envelope.Encapsulate(band.bounds);
            float bodyLength = Mathf.Max(envelope.size.x, envelope.size.z);
            if (bodyLength < 1.7f || bodyLength > 2.5f)
                throw new InvalidOperationException("Protoceratops navigation body does not match the 2m model: " + envelope.size);
            var data = new SerializedObject(agent);
            data.FindProperty("species").enumValueIndex = (int)DinosaurAgent.DinosaurSpecies.Protoceratops;
            data.FindProperty("roamingMode").enumValueIndex = (int)DinosaurAgent.RoamingMode.ContinuousFree;
            data.FindProperty("playerInteractionEnabled").boolValue = false;
            data.FindProperty("interactionBody").objectReferenceValue = body;
            data.FindProperty("safetyDistance").floatValue = 15f;
            data.FindProperty("safetyReleaseDistance").floatValue = 20f;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(body); EditorUtility.SetDirty(agent);
        }
    }
}
