using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    internal static class BaurusuchusWalkingClip
    {
        // The preferred 1.265..2.630 window has a 0.83 degree joint seam;
        // this complete walking cycle has only a 0.29 degree worst-joint seam.
        internal const float Start = .5f, End = 1.85f;
        internal const float Duration = End - Start;
        internal const string ProfilePath = BaurusuchusSceneIntegration.Root + "/Gait.asset";
        internal static AnimationClip Source => AssetDatabase.LoadAllAssetsAtPath(
            BaurusuchusSceneIntegration.SourceRoot + "/source/Baurusuchus.fbx")
            .OfType<AnimationClip>().Single(c => !c.name.StartsWith("__preview__"));

        internal static AnimationClip Crop(AnimationClip source)
        {
            var result = new AnimationClip { name = "Run", legacy = true, frameRate = source.frameRate, wrapMode = WrapMode.Loop };
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (curve.keys.Any(k => k.weightedMode != WeightedMode.None))
                    throw new InvalidOperationException("Expected unweighted source curve: " + binding.path + "/" + binding.propertyName);
                var keys = curve.keys.Where(k => k.time > Start && k.time < End).ToList();
                keys.Insert(0, Boundary(curve, Start)); keys.Add(Boundary(curve, End));
                for (int i = 0; i < keys.Count; i++) { var key = keys[i]; key.time -= Start; keys[i] = key; }
                var cropped = new AnimationCurve(keys.ToArray());
                cropped.preWrapMode = curve.preWrapMode; cropped.postWrapMode = curve.postWrapMode;
                AnimationUtility.SetEditorCurve(result, binding, cropped);
            }
            var settings = AnimationUtility.GetAnimationClipSettings(source);
            settings.startTime = 0f; settings.stopTime = Duration; settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(result, settings);
            AnimationUtility.SetAnimationEvents(result, Array.Empty<AnimationEvent>());
            return result;
        }
        private static Keyframe Boundary(AnimationCurve curve, float time)
        {
            var keys = curve.keys;
            foreach (var key in keys) if (Mathf.Abs(key.time - time) < .000001f) { var exact = key; exact.time = time; return exact; }
            float slope = 0f;
            for (int i = 0; i + 1 < keys.Length; i++)
            {
                var a = keys[i]; var b = keys[i + 1];
                if (time < a.time || time > b.time) continue;
                if (float.IsInfinity(a.outTangent) || float.IsInfinity(b.inTangent)) break;
                float h = b.time - a.time, u = (time - a.time) / h;
                slope = (6f * u * u - 6f * u) * a.value / h + (3f * u * u - 4f * u + 1f) * a.outTangent
                    + (-6f * u * u + 6f * u) * b.value / h + (3f * u * u - 2f * u) * b.inTangent;
                break;
            }
            return new Keyframe(time, curve.Evaluate(time), slope, slope);
        }
        internal static bool IsWalkingClip(DinosaurAgent agent)
        {
            var animation = agent.GetComponentInChildren<Animation>(true);
            return agent.name == "Baurusuchus" && animation != null && animation["Run"] != null
                && Mathf.Abs(animation["Run"].length - Duration) < .001f;
        }
        internal static void CropContacts(DinosaurGaitProfile profile)
        {
            if (profile.loopEnd > Duration + .01f)
            {
                // Crop the accepted contact curves, retaining sole offsets, leg chains,
                // native speed and body height. The old loopEnd is the original clip duration.
                float oldLength = profile.loopEnd;
                int steps = Mathf.CeilToInt(Duration * 120f);
                foreach (var foot in profile.feet)
                {
                    var original = foot.contact;
                    foot.contact = new float[steps + 1];
                    for (int i = 0; i <= steps; i++)
                    {
                        float frame = (Start + Duration * i / steps) / oldLength * (original.Length - 1);
                        int a = Mathf.Clamp(Mathf.FloorToInt(frame), 0, original.Length - 2);
                        foot.contact[i] = Mathf.Lerp(original[a], original[a + 1], frame - a);
                    }
                }
                profile.loopStart = 0f; profile.loopEnd = Duration;
            }
        }
        internal static DinosaurGaitProfile ApplyProfile(DinosaurAgent agent)
        {
            var profile = AssetDatabase.LoadAssetAtPath<DinosaurGaitProfile>(ProfilePath);
            DinosaurGaitCalibration.Require(profile != null, "Baurusuchus gait calibration asset missing.");
            float previousHeight = agent.GaitProfile != null ? agent.GaitProfile.heightCorrection : 0f;
            CropContacts(profile);
            EditorUtility.SetDirty(profile);
            DinosaurGaitCalibration.Set(agent, "gaitProfile", profile);
            DinosaurGaitCalibration.Number(agent, "walkSpeed", profile.groundSpeed);
            DinosaurGaitCalibration.Number(agent, "moveAnimationGroundSpeed", profile.groundSpeed);
            DinosaurGaitCalibration.Number(agent, "moveClipLoopStart", 0f);
            DinosaurGaitCalibration.Number(agent, "moveClipLoopEnd", Duration);
            var loader = agent.GetComponent<DinosaurFbxLoader>();
            loader.ModelRoot.transform.position += Vector3.up * (profile.heightCorrection - previousHeight);
            var data = new SerializedObject(loader); data.FindProperty("enableFootLock").boolValue = true;
            data.ApplyModifiedPropertiesWithoutUndo();
            var agentData = new SerializedObject(agent);
            var description = agentData.FindProperty("description");
            description.stringValue = description.stringValue.Replace("资源仅含 Running 动画，站定与行走均持续播放。",
                "使用纯走路循环持续漫游，转弯保持前进；受阻时安全等待。");
            agentData.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(agent); EditorSceneManager.MarkSceneDirty(agent.gameObject.scene);
            return profile;
        }
    }
}
