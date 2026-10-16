using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace JurassicWorld.Editor
{
    internal static class TRexSideYawAudit
    {
        internal static void Verify(DinosaurAgent rex)
        {
            var animation = rex.GetComponentInChildren<Animation>(true);
            var profile = AssetDatabase.LoadAssetAtPath<DinosaurInteractionProfile>(TRexInteractionBake.SideTestProfilePath);
            foreach (var role in new[] { InteractionMotionRole.SideStartRunLeft, InteractionMotionRole.SideStartRunRight })
            {
                var motion = profile.Find(role);
                var source = animation[motion.sourceName].clip;
                if (Mathf.Abs(motion.clip.length - source.length) > .0001f) throw new InvalidOperationException("Start-run duration changed.");
                var fixedYaw = TRexInteractionBake.SideYawTwist(Read(motion.clip, 0f));
                float yawError = 0f, swingError = 0f;
                for (int i = 0; i <= 600; i++)
                {
                    float time = source.length * i / 600f;
                    Quaternion original = Read(source, time), corrected = Read(motion.clip, time);
                    Quaternion originalSwing = Quaternion.Inverse(TRexInteractionBake.SideYawTwist(original)) * original;
                    Quaternion correctedSwing = Quaternion.Inverse(TRexInteractionBake.SideYawTwist(corrected)) * corrected;
                    yawError = Mathf.Max(yawError, Quaternion.Angle(fixedYaw, TRexInteractionBake.SideYawTwist(corrected)));
                    swingError = Mathf.Max(swingError, Quaternion.Angle(originalSwing, correctedSwing));
                }
                if (yawError > .1f || swingError > .15f) throw new InvalidOperationException($"Start yaw/swing error: {role}, yaw={yawError}, swing={swingError}");
                foreach (var binding in AnimationUtility.GetCurveBindings(source))
                {
                    bool edited = binding.path == "Hips/jt_Cog_C" && (binding.propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal)
                        || binding.propertyName == "m_LocalPosition.x" || binding.propertyName == "m_LocalPosition.z");
                    if (edited) continue;
                    var original = AnimationUtility.GetEditorCurve(source, binding);
                    var corrected = AnimationUtility.GetEditorCurve(motion.clip, binding);
                    if (corrected == null || !original.keys.SequenceEqual(corrected.keys))
                        throw new InvalidOperationException("Non-COG animation changed: " + binding.path + "/" + binding.propertyName);
                }
                Debug.Log($"PASS side yaw {role}: twist error={yawError:F4}deg, pitch/roll swing error={swingError:F4}deg; other bone curves unchanged.");
            }
        }

        private static Quaternion Read(AnimationClip clip, float time)
        {
            float Value(string component) => AnimationUtility.GetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("Hips/jt_Cog_C", typeof(Transform), "m_LocalRotation." + component)).Evaluate(time);
            return new Quaternion(Value("x"), Value("y"), Value("z"), Value("w")).normalized;
        }
    }
}
