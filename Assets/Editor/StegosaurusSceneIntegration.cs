using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class StegosaurusSceneIntegration
    {
        internal const string Root = "Assets/Dinosaurs/Stegosaurus/Prepared";
        internal const string PrefabPath = Root + "/StegosaurusGreenLegacy.prefab";
        private const string Source = "Assets/Dinosaurs/Stegosaurus/Source/FerociousIndustries";

        [MenuItem("Jurassic World/Scene/Add or Update Green Stegosaurus")]
        private static void AddMenu() => JurassicMenuGuard.Run(AddToMainScene, true);

        [MenuItem("Jurassic World/Scene/Add or Update Green Stegosaurus", true)]
        private static bool CanAdd() => JurassicMenuGuard.CanRun;

        internal static void EnsureAssets()
        {
            JurassicWorldSceneBuilder.EnsureFolder(Root);
            var clips = AssetDatabase.LoadAllAssetsAtPath(Source + "/Models/LODG/Stegasaurus_LODG.fbx").OfType<AnimationClip>().ToArray();
            AnimationClip idle = CopyClip(clips, "Armature|Stegasaurus_IdleA", Root + "/Idle.anim");
            AnimationClip walk = CopyClip(clips, "Armature|Stegasaurus_Walk", Root + "/Walk.anim");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source + "/Prefabs/LODG/Stegasaurus_Green.prefab");
            if (source == null) throw new InvalidOperationException("Green Stegosaurus source prefab missing.");
            var holder = new GameObject("Stegosaurus Green Legacy");
            try
            {
                GameObject model = Object.Instantiate(source, holder.transform, false);
                model.name = "Stegosaurus Model";
                foreach (Animator animator in model.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
                // FBX clip paths are relative to the original prefab root; keep Animation on that root.
                Animation animation = model.AddComponent<Animation>();
                animation.AddClip(idle, idle.name);
                animation.AddClip(walk, walk.name);
                animation.clip = idle;
                animation.playAutomatically = false;
                animation.cullingType = AnimationCullingType.AlwaysAnimate;
                Transform[] bones = model.GetComponentsInChildren<Transform>(true);
                Transform head = bones.Single(b => b.name == "Head");
                Transform tail = bones.Single(b => b.name == "TailBase");
                Vector3 forward = model.transform.InverseTransformDirection(head.position - tail.position);
                float yaw = -Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                model.transform.localRotation *= Quaternion.Euler(0f, yaw, 0f);
                foreach (SkinnedMeshRenderer skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = true;
                DinosaurForestMaterials.Apply(holder, "Stegosaurus");
                PrefabUtility.SaveAsPrefabAsset(holder, PrefabPath);
                Debug.Log($"STEGOSAURUS_ASSETS_READY: forwardCorrection={yaw:F1}; legacy idle/walk; six LODs.");
            }
            finally { Object.DestroyImmediate(holder); }
        }

        private static AnimationClip CopyClip(AnimationClip[] source, string name, string path)
        {
            AnimationClip original = source.Single(c => c.name == name);
            var copy = Object.Instantiate(original);
            copy.name = Path.GetFileNameWithoutExtension(path);
            copy.legacy = true;
            copy.wrapMode = WrapMode.Loop;
            var settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(copy, settings);
            AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null) { AssetDatabase.CreateAsset(copy, path); return copy; }
            EditorUtility.CopySerialized(copy, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(copy);
            return existing;
        }

        public static void AddToMainScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before adding Stegosaurus.");
            const string scenePath = "Assets/Scenes/Main.unity";
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != scenePath)
                EditorSceneManager.OpenScene(scenePath);
            EnsureAssets();
            var previous = Object.FindObjectsOfType<DinosaurAgent>().Where(a => a.name == "Stegosaurus").ToArray();
            DinosaurAgent stegosaurus = null;
            try
            {
                stegosaurus = JurassicWorldSceneBuilder.AddStegosaurusToCurrentScene();
                var modelRoot = stegosaurus.GetComponent<DinosaurFbxLoader>().ModelRoot;
                if (modelRoot.GetComponentInChildren<Animation>(true) == null)
                    throw new InvalidOperationException("Stegosaurus legacy animation missing; scene will not be saved.");
                RouteObstacleClearance.BakeCoreProfile(stegosaurus);
                if (File.Exists(FoliageInteractionPreparation.EnabledPath)) FoliageInteractionPreparation.BakeInteractor(stegosaurus);
            }
            catch
            {
                foreach (DinosaurAgent added in Object.FindObjectsOfType<DinosaurAgent>()
                    .Where(a => a.name == "Stegosaurus" && !previous.Contains(a)))
                    Object.DestroyImmediate(added.gameObject);
                throw;
            }
            foreach (DinosaurAgent agent in previous) Object.DestroyImmediate(agent.gameObject);
            EditorSceneManager.MarkSceneDirty(stegosaurus.gameObject.scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(stegosaurus.gameObject.scene);
            Debug.Log("STEGOSAURUS_ADDED: existing environment preserved; three dinosaurs share the six-route randomizer.");
        }
    }
}
