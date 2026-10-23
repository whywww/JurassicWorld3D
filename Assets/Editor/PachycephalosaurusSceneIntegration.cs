using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class PachycephalosaurusSceneIntegration
    {
        internal const string Root = "Assets/Dinosaurs/Pachycephalosaurus/Prepared";
        internal const string PrefabPath = Root + "/PachycephalosaurusRedLegacy.prefab";
        internal const string PortraitPath = Root + "/Pachycephalosaurus.png";
        internal static readonly DinosaurPortraitBuilder.PortraitFraming PortraitSettings =
            new DinosaurPortraitBuilder.PortraitFraming(
                new[] { "Head", "TopJaw", "BottomJaw" },
                0.68f,
                new Vector3(1f, 0.2f, 1f),
                0.08f);
        private const string Source = "Assets/Dinosaurs/Pachycephalosaurus/Source/FerociousIndustries";

        [MenuItem("Jurassic World/Scene/Add or Update Red Pachycephalosaurus")]
        public static void AddToMainScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save the current scene before adding Pachycephalosaurus.");
            const string scenePath = "Assets/Scenes/Main.unity";
            if (EditorSceneManager.GetActiveScene().path != scenePath) EditorSceneManager.OpenScene(scenePath);
            EnsureAssets();
            var previous = Object.FindObjectsOfType<DinosaurAgent>(true).Where(a => a.name == "Pachycephalosaurus").ToArray();
            DinosaurAgent added = null;
            try
            {
                added = JurassicWorldSceneBuilder.AddPachycephalosaurusToCurrentScene();
                DinosaurPortraitBuilder.BindPortrait(added, PortraitPath);
                if (File.Exists(FoliageInteractionPreparation.EnabledPath)) FoliageInteractionPreparation.BakeInteractor(added);
            }
            catch
            {
                foreach (var agent in Object.FindObjectsOfType<DinosaurAgent>(true).Where(a => a.name == "Pachycephalosaurus" && !previous.Contains(a)))
                    Object.DestroyImmediate(agent.gameObject);
                throw;
            }
            foreach (var agent in previous) Object.DestroyImmediate(agent.gameObject);
            EditorSceneManager.MarkSceneDirty(added.gameObject.scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(added.gameObject.scene);
            Debug.Log("PACHYCEPHALOSAURUS_ADDED: red; Legacy Idle/Walk; free roaming; T-Rex safety 15m/20m.");
        }

        internal static void EnsureAssets()
        {
            JurassicWorldSceneBuilder.EnsureFolder(Root);
            var clips = AssetDatabase.LoadAllAssetsAtPath(Source + "/Models/LODG/Pachycephalasaurus.fbx").OfType<AnimationClip>().ToArray();
            var idle = CopyClip(clips, "Armature|Pachycephalasaurus_IdleA", "Idle");
            var walk = CopyClip(clips, "Armature|Pachycephalasaurus_Walk", "Walk");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source + "/Prefabs/PBR/Pachycephalasaurus_Red.prefab");
            if (source == null) throw new InvalidOperationException("Red Pachycephalasaurus source prefab missing.");
            var holder = new GameObject("Pachycephalosaurus Red Legacy");
            try
            {
                var model = Object.Instantiate(source, holder.transform, false);
                model.name = "Pachycephalosaurus Model";
                foreach (var animator in model.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
                foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(behaviour);
                var animation = model.AddComponent<Animation>();
                animation.AddClip(idle, idle.name);
                animation.AddClip(walk, walk.name);
                animation.clip = idle;
                animation.playAutomatically = false;
                animation.cullingType = AnimationCullingType.AlwaysAnimate;
                var bones = model.GetComponentsInChildren<Transform>(true);
                var head = bones.Single(b => b.name == "Head");
                var tail = bones.Single(b => b.name == "Tail1");
                Vector3 forward = model.transform.InverseTransformDirection(head.position - tail.position);
                model.transform.localRotation *= Quaternion.Euler(0f, -Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 0f);
                foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = true;
                DinosaurForestMaterials.Apply(holder, "Pachycephalosaurus");
                PrefabUtility.SaveAsPrefabAsset(holder, PrefabPath);
                DinosaurPortraitBuilder.EnsureHeadshotIfMissing(holder, "Pachycephalosaurus", PortraitPath, PortraitSettings);
            }
            finally { Object.DestroyImmediate(holder); }
        }

        private static AnimationClip CopyClip(AnimationClip[] clips, string sourceName, string name)
        {
            var copy = Object.Instantiate(clips.Single(c => c.name == sourceName));
            copy.name = name;
            copy.legacy = true;
            copy.wrapMode = WrapMode.Loop;
            AnimationUtility.SetAnimationEvents(copy, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(copy, settings);
            string path = Root + "/" + name + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null) { AssetDatabase.CreateAsset(copy, path); return copy; }
            EditorUtility.CopySerialized(copy, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(copy);
            return existing;
        }

        internal static void Configure(DinosaurAgent agent)
        {
            var animation = agent.GetComponentInChildren<Animation>(true);
            if (animation == null || animation["Idle"] == null || animation["Walk"] == null)
                throw new InvalidOperationException("Pachycephalosaurus requires Legacy Idle and Walk animations.");
            RouteObstacleClearance.BakeCoreProfile(agent);
            var body = RouteObstacleClearance.BakeInteractionBody(agent, new[] { animation["Idle"].clip, animation["Walk"].clip });
            var data = new SerializedObject(agent);
            data.FindProperty("species").enumValueIndex = (int)DinosaurAgent.DinosaurSpecies.Pachycephalosaurus;
            data.FindProperty("roamingMode").enumValueIndex = (int)DinosaurAgent.RoamingMode.Free;
            data.FindProperty("playerInteractionEnabled").boolValue = false;
            data.FindProperty("interactionBody").objectReferenceValue = body;
            data.FindProperty("safetyDistance").floatValue = 15f;
            data.FindProperty("safetyReleaseDistance").floatValue = 20f;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(body);
            EditorUtility.SetDirty(agent);
        }
    }
}
