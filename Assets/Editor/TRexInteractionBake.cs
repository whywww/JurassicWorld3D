using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class TRexInteractionBake
    {
        private const string Folder = "Assets/Navigation";
        private const string ProfilePath = Folder + "/TRexCuriosity.asset";
        private const string DataPath = Folder + "/TRexInteractionNavMesh.asset";
        private const float ReturnTurnDegrees = 170f;
        public const string SideTestProfilePath = "Assets/Scenes/TRexFrontTestAssets/SideInteractionProfile.asset";

        [MenuItem("Jurassic World/Tests/Bake Side Interaction Animations")]
        public static void BakeSideTest()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save the current scene before baking side animations.");
            var scene = EditorSceneManager.OpenScene(TRexFrontTestController.ScenePath);
            var rex = Object.FindObjectOfType<DinosaurAgent>();
            var profile = BuildSideTestProfile(rex);
            var serialized = new SerializedObject(rex);
            serialized.FindProperty("interactionProfile").objectReferenceValue = profile;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("TREX_SIDE_TEST_BAKED: test profile only; shared Main animations and return calibration retained.");
        }

        public static DinosaurInteractionProfile BuildSideTestProfile(DinosaurAgent rex)
        {
            const string folder = "Assets/Scenes/TRexFrontTestAssets";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Scenes", "TRexFrontTestAssets");
            var sourceProfile = AssetDatabase.LoadAssetAtPath<DinosaurInteractionProfile>(ProfilePath);
            if (sourceProfile == null) throw new InvalidOperationException("Missing calibrated shared profile.");
            var copy = Object.Instantiate(sourceProfile);
            copy.name = "SideInteractionProfile";
            var animation = rex.GetComponentInChildren<Animation>(true);
            var originalEulerHints = animation.GetComponentsInChildren<Transform>(true).ToDictionary(t => t,
                t => new SerializedObject(t).FindProperty("m_LocalEulerAnglesHint").vector3Value);
            var mappings = new[] {
                (InteractionMotionRole.LookLeft, "look left"), (InteractionMotionRole.LookRight, "look right"),
                (InteractionMotionRole.SideRoarLeft, "roar left"), (InteractionMotionRole.SideRoarRight, "roar right"),
                (InteractionMotionRole.SideStartRunLeft, "idle attack to run left"), (InteractionMotionRole.SideStartRunRight, "idle attack to run right") };
            var motions = new List<DinosaurInteractionProfile.Motion>(copy.motions);
            var report = new StringBuilder("# Side test calibration\n\nOnly test assets are written. Front and return motions retain their existing calibration.\n");
            try
            {
                foreach (var mapping in mappings)
                {
                    var role = mapping.Item1; string name = mapping.Item2;
                    var source = animation[name]?.clip;
                    if (source == null) throw new InvalidOperationException("Missing exact side animation: " + name);
                    var clip = Object.Instantiate(source); clip.name = "SideTest_" + role; clip.legacy = true;
                    bool locomotion = role == InteractionMotionRole.SideStartRunLeft || role == InteractionMotionRole.SideStartRunRight;
                    if (locomotion)
                        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                            if ((binding.path == "Hips/jt_Cog_C" || binding.path == "")
                                && (binding.propertyName == "m_LocalPosition.x" || binding.propertyName == "m_LocalPosition.z"))
                            {
                                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, clip.length, curve.Evaluate(0f)));
                            }
                    if (role == InteractionMotionRole.SideStartRunLeft || role == InteractionMotionRole.SideStartRunRight)
                        HoldSideStartYaw(clip);
                    var motion = new DinosaurInteractionProfile.Motion { role = role, sourceName = name, clip = clip };
                    MeasureMotion(rex, animation, source, motion, report);
                    TRexTurnCalibration.Sample(rex, animation, motion);
                    if (role == InteractionMotionRole.LookLeft || role == InteractionMotionRole.LookRight)
                        TRexTurnCalibration.ReconstructTurn(motion);
                    if (locomotion)
                    {
                        float distance = 0f;
                        for (int i = 1; i < motion.footFrames.Length; i++)
                        {
                            var a = motion.footFrames[i - 1]; var b = motion.footFrames[i];
                            float va = role == InteractionMotionRole.Run ? motion.groundSpeed : Mathf.Max(0f, motion.speed.Evaluate(a.time));
                            float vb = role == InteractionMotionRole.Run ? motion.groundSpeed : Mathf.Max(0f, motion.speed.Evaluate(b.time));
                            distance += (va + vb) * .5f * (b.time - a.time);
                            motion.footFrames[i].rootOffset = new Vector2(0f, distance);
                        }
                    }
                    motion.bodyBands = RouteObstacleClearance.BakeMotionBands(rex, clip);
                    string path = folder + "/" + clip.name + ".anim";
                    var existingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (existingClip == null) { AssetDatabase.CreateAsset(clip, path); existingClip = clip; }
                    else { EditorUtility.CopySerialized(clip, existingClip); Object.DestroyImmediate(clip); EditorUtility.SetDirty(existingClip); }
                    motion.clip = existingClip;
                    int index = motions.FindIndex(m => m.role == role);
                    if (index < 0) motions.Add(motion); else motions[index] = motion;
                }
                copy.motions = motions.ToArray();
                var existingProfile = AssetDatabase.LoadAssetAtPath<DinosaurInteractionProfile>(SideTestProfilePath);
                if (existingProfile == null) { AssetDatabase.CreateAsset(copy, SideTestProfilePath); existingProfile = copy; }
                else { EditorUtility.CopySerialized(copy, existingProfile); EditorUtility.SetDirty(existingProfile); }
                Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/trex-side-test-calibration.md", report.ToString());
                return existingProfile;
            }
            finally
            {
                foreach (var hint in originalEulerHints)
                {
                    var transformData = new SerializedObject(hint.Key);
                    transformData.FindProperty("m_LocalEulerAnglesHint").vector3Value = hint.Value;
                    transformData.ApplyModifiedPropertiesWithoutUndo();
                }
                if (!AssetDatabase.Contains(copy)) Object.DestroyImmediate(copy);
            }
        }
        internal static Quaternion SideYawTwist(Quaternion rotation)
        {
            float length = Mathf.Sqrt(rotation.y * rotation.y + rotation.w * rotation.w);
            return length < .00001f ? Quaternion.identity : new Quaternion(0f, rotation.y / length, 0f, rotation.w / length);
        }

        private static void HoldSideStartYaw(AnimationClip clip)
        {
            const string path = "Hips/jt_Cog_C";
            string[] components = { "x", "y", "z", "w" };
            var bindings = components.Select(c => EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation." + c)).ToArray();
            var source = bindings.Select(b => AnimationUtility.GetEditorCurve(clip, b)).ToArray();
            if (source.Any(c => c == null)) throw new InvalidOperationException("Missing COG quaternion curves: " + clip.name);
            Quaternion Read(float time) => new Quaternion(source[0].Evaluate(time), source[1].Evaluate(time), source[2].Evaluate(time), source[3].Evaluate(time)).normalized;
            Quaternion initial = Read(0f), fixedYaw = SideYawTwist(initial), previous = initial;
            var curves = components.Select(_ => new AnimationCurve()).ToArray();
            // Preserve source key times and sample between them so pitch/roll retain their motion.
            var times = new SortedSet<float>(source.SelectMany(c => c.keys.Select(k => k.time)));
            int count = Mathf.CeilToInt(clip.length * 120f);
            for (int i = 0; i <= count; i++) times.Add(clip.length * i / count);
            foreach (float time in times)
            {
                Quaternion authored = Read(time);
                Quaternion swing = Quaternion.Inverse(SideYawTwist(authored)) * authored;
                Quaternion corrected = (fixedYaw * swing).normalized;
                if (Quaternion.Dot(previous, corrected) < 0f) corrected = new Quaternion(-corrected.x, -corrected.y, -corrected.z, -corrected.w);
                previous = corrected;
                float[] values = { corrected.x, corrected.y, corrected.z, corrected.w };
                for (int c = 0; c < curves.Length; c++) curves[c].AddKey(new Keyframe(time, values[c]));
            }
            for (int c = 0; c < curves.Length; c++)
            {
                for (int k = 0; k < curves[c].length; k++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curves[c], k, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curves[c], k, AnimationUtility.TangentMode.Linear);
                }
                AnimationUtility.SetEditorCurve(clip, bindings[c], curves[c]);
            }
        }
        private static readonly Vector2[] Clearings = { new Vector2(-22f, 8f), new Vector2(23f, 18f), new Vector2(0f, 49f) };
        private static readonly string[] Names = { "look forward", "look left", "look right", "idle to run", "run head loop", "run to stop", "turn 90 left", "turn 90 right", "walk slow loop",
            "idle investigate", "idle investigae out", "Big roar step", "Chase Roar", "ROAR", "mama cave bite", "mama cave bite out", "turn 180 right",
            "walk head low", "bite close left", "bite close right", "idle attack", "look forward 2" };

        public static void BakeTurn180Right()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before baking the turn.");
            EditorSceneManager.OpenScene(TRexFrontTestController.ScenePath);
            var rex = Object.FindObjectOfType<DinosaurAgent>();
            var animation = rex.GetComponentInChildren<Animation>(true);
            var source = animation["turn 180 right"]?.clip;
            if (source == null) throw new InvalidOperationException("Missing exact animation: turn 180 right. Existing turn retained.");
            var profile = AssetDatabase.LoadAssetAtPath<DinosaurInteractionProfile>(ProfilePath);
            var report = new StringBuilder("# turn 180 right trial\n\nOnly the shared 180-degree motion is replaced. Other motions, scenes and navigation remain unchanged.\n");
            var clip = Object.Instantiate(source); clip.name = "Curiosity_Turn180Right"; clip.legacy = true;
            try
            {
                // Keep the existing serialized role so scenes and stepping code need no migration.
                var motion = new DinosaurInteractionProfile.Motion { role = InteractionMotionRole.Turn180Look, sourceName = "turn 180 right", clip = clip };
                MeasureMotion(rex, animation, source, motion, report);
                motion.turnDegrees = ReturnTurnDegrees;
                TRexTurnCalibration.Sample(rex, animation, motion);
                motion.bodyBands = RouteObstacleClearance.BakeMotionBands(rex, clip);
                motion.turnPlaybackSpeed = 1f;
                var first = motion.footFrames[0]; var last = motion.footFrames[motion.footFrames.Length - 1];
                float heading = last.yaw + last.authoredYaw;
                if (Mathf.Abs(Mathf.DeltaAngle(heading, ReturnTurnDegrees)) > 2f) throw new InvalidOperationException("Calibrated turn is not 170 degrees. Existing turn retained.");
                float maximumStep = 0f;
                for (int i = 1; i < motion.footFrames.Length; i++)
                    maximumStep = Mathf.Max(maximumStep, Vector2.Distance(motion.footFrames[i - 1].rootOffset, motion.footFrames[i].rootOffset));
                report.AppendLine($"Duration={source.length:F3}s; net turn={heading:F3}deg; playback=1; blend={motion.blendDuration:F3}s; root end={last.rootOffset}; maximum 60Hz root step={maximumStep:F4}m.");
                report.AppendLine($"Start contacts L/R={first.leftContact}/{first.rightContact}; end contacts L/R={last.leftContact}/{last.rightContact}; six prediction poses are rebuilt from this motion on startup.");
                string path = Folder + "/Curiosity_Turn180Right.anim";
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (existing == null) { AssetDatabase.CreateAsset(clip, path); existing = clip; }
                else { EditorUtility.CopySerialized(clip, existing); EditorUtility.SetDirty(existing); }
                motion.clip = existing;
                int slot = Array.FindIndex(profile.motions, m => m.role == InteractionMotionRole.Turn180Look);
                if (slot < 0) throw new InvalidOperationException("Missing existing 180-degree role.");
                profile.motions[slot] = motion; EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();
                Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/trex-turn180-right-report.md", report.ToString());
                Debug.Log("TREX_TURN180_RIGHT_BAKED\n" + report);
            }
            finally { if (!AssetDatabase.Contains(clip)) Object.DestroyImmediate(clip); }
        }

        public static void BakeReturnFootMetadata()
        {
            EditorSceneManager.OpenScene(TRexFrontTestController.ScenePath);
            var rex = Object.FindObjectOfType<DinosaurAgent>(); var animation = rex.GetComponentInChildren<Animation>();
            var profile = AssetDatabase.LoadAssetAtPath<DinosaurInteractionProfile>(ProfilePath);
            var lf = TRexTurnCalibration.Bone(animation.transform, "jt_Foot_L"); var rf = TRexTurnCalibration.Bone(animation.transform, "jt_Foot_R");
            var ls = TRexTurnCalibration.Bone(animation.transform, "jt_ClawMiddle_L"); var rs = TRexTurnCalibration.Bone(animation.transform, "jt_ClawMiddle_R");
            Vector3 lforward = lf.InverseTransformDirection(ls.position - lf.position).normalized;
            Vector3 rforward = rf.InverseTransformDirection(rs.position - rf.position).normalized;
            Transform lk = lf.parent, rk = rf.parent;
            while (!lk.name.Contains("Knee")) lk = lk.parent;
            while (!rk.name.Contains("Knee")) rk = rk.parent;
            var inverse = Quaternion.Inverse(rex.transform.rotation * Quaternion.Euler(0f, rex.VisualYaw, 0f));
            AnimationMode.StartAnimationMode();
            try
            {
                foreach (var role in new[] { InteractionMotionRole.Walk, InteractionMotionRole.Turn180Look })
                {
                    var motion = profile.Find(role);
                    for (int i = 0; i < motion.footFrames.Length; i++)
                    {
                        var frame = motion.footFrames[i];
                        AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(animation.gameObject, motion.clip, frame.time); AnimationMode.EndSampling();
                        frame.leftHeading = inverse * lf.TransformDirection(lforward); frame.leftHeading.y = 0f; frame.leftHeading.Normalize();
                        frame.rightHeading = inverse * rf.TransformDirection(rforward); frame.rightHeading.y = 0f; frame.rightHeading.Normalize();
                        frame.leftKnee = inverse * (lk.position - rex.transform.position); frame.rightKnee = inverse * (rk.position - rex.transform.position);
                        motion.footFrames[i] = frame;
                    }
                }
            }
            finally { AnimationMode.StopAnimationMode(); }
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();
            Debug.Log("RETURN_FOOT_METADATA_BAKED: headings and knees only; existing walk speed, contacts and curves retained.");
        }

        public static void BakeFrontAdditions()
        {
            EditorSceneManager.OpenScene(TRexFrontTestController.ScenePath);
            var rex = Object.FindObjectOfType<DinosaurAgent>();
            var animation = rex.GetComponentInChildren<Animation>(true);
            var profile = AssetDatabase.LoadAssetAtPath<DinosaurInteractionProfile>(ProfilePath);
            var motions = new List<DinosaurInteractionProfile.Motion>(profile.motions);
            var report = new StringBuilder("# Front interaction additions\n");
            for (int index = (int)InteractionMotionRole.FollowWalk; index < Names.Length; index++)
            {
                var role = (InteractionMotionRole)index;
                if (profile.Find(role)?.sourceName == Names[index]) continue;
                var source = animation[Names[index]]?.clip;
                if (source == null) throw new InvalidOperationException("Missing exact animation: " + Names[index]);
                string path = Folder + "/Curiosity_" + role + ".anim";
                var clip = Object.Instantiate(source); clip.name = "Curiosity_" + role; clip.legacy = true;
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (existing == null) AssetDatabase.CreateAsset(clip, path);
                else { EditorUtility.CopySerialized(clip, existing); Object.DestroyImmediate(clip); clip = existing; EditorUtility.SetDirty(clip); }
                var motion = new DinosaurInteractionProfile.Motion { role = role, sourceName = Names[index], clip = clip };
                MeasureMotion(rex, animation, source, motion, report);
                TRexTurnCalibration.Sample(rex, animation, motion);
                motion.bodyBands = RouteObstacleClearance.BakeMotionBands(rex, clip);
                motions.RemoveAll(m => m.role == role); motions.Add(motion);
            }
            profile.motions = motions.ToArray(); EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();
            File.WriteAllText("Logs/trex-front-additions-report.md", report.ToString());
            TRexFrontTestScene.Create();
            Debug.Log("TREX_FRONT_ADDITIONS_BAKED");
        }
        public static void BakeFrontPlayerBodies()
        {
            EditorSceneManager.OpenScene(TRexFrontTestController.ScenePath);
            var rex = Object.FindObjectOfType<DinosaurAgent>();
            var animation = rex.GetComponentInChildren<Animation>();
            var profile = AssetDatabase.LoadAssetAtPath<DinosaurInteractionProfile>(ProfilePath);
            foreach (var motion in profile.motions)
            {
                if (motion.role != InteractionMotionRole.FollowWalk && motion.role != InteractionMotionRole.Walk
                    && motion.role != InteractionMotionRole.TurnLeft && motion.role != InteractionMotionRole.TurnRight
                    && motion.role != InteractionMotionRole.Turn180Look && motion.role != InteractionMotionRole.Roar
                    && motion.role != InteractionMotionRole.LookForwardTwo && motion.role != InteractionMotionRole.BiteCloseLeft
                    && motion.role != InteractionMotionRole.BiteCloseRight)
                { motion.playerPoses = Array.Empty<DinosaurInteractionProfile.PlayerPose>(); continue; }
                var sampled = new DinosaurInteractionProfile.Motion { role = motion.role, clip = motion.clip, sourceName = motion.sourceName, turnDegrees = motion.turnDegrees };
                TRexTurnCalibration.Sample(rex, animation, sampled);
                motion.playerPoses = sampled.playerPoses;
                for (int i = 0; i < motion.footFrames.Length; i++)
                {
                    int index = Mathf.Clamp(Mathf.RoundToInt(motion.footFrames[i].time / motion.clip.length * (sampled.footFrames.Length - 1)), 0, sampled.footFrames.Length - 1);
                    motion.footFrames[i].playerBodyBands = sampled.footFrames[index].playerBodyBands;
                    if (motion.role == InteractionMotionRole.Turn180Look)
                    {
                        motion.footFrames[i].rootOffset = sampled.footFrames[index].rootOffset;
                        motion.footFrames[i].yaw = sampled.footFrames[index].yaw;
                    }
                }
                if (motion.role == InteractionMotionRole.StopRun)
                {
                    float distance = 0f;
                    for (int i = 1; i < motion.footFrames.Length; i++)
                    {
                        var a = motion.footFrames[i - 1]; var b = motion.footFrames[i];
                        distance += (Mathf.Clamp(motion.speed.Evaluate(a.time), 0f, motion.groundSpeed) + Mathf.Clamp(motion.speed.Evaluate(b.time), 0f, motion.groundSpeed)) * .5f * (b.time - a.time);
                        motion.footFrames[i].rootOffset = new Vector2(0f, distance);
                    }
                }
            }
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();
            Debug.Log("FRONT_PLAYER_POSES_BAKED");
        }

        [MenuItem("Jurassic World/Interaction/Bake Curious T-Rex")]
        public static void ConfigureMainScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before baking.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/Main.unity")
            {
                if (scene.isDirty) throw new InvalidOperationException("Save the current scene before switching to Main.");
                scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            }
            BakeCurrentScene(false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("TREX_CURIOSITY_BAKE_OK");
        }

        internal static void BakeCurrentScene(bool adjustForest = true)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "Navigation");
            var rex = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex");
            var profile = Calibrate(rex);
            var body = RouteObstacleClearance.BakeInteractionBody(rex, profile.motions.Where(m => (int)m.role <= (int)InteractionMotionRole.Walk).Select(m => m.clip).ToArray());
            foreach (var motion in profile.motions) motion.bodyBands = RouteObstacleClearance.BakeMotionBands(rex, motion.clip);
            EditorUtility.SetDirty(profile);
            var serialized = new SerializedObject(rex);
            serialized.FindProperty("playerInteractionEnabled").boolValue = true;
            serialized.FindProperty("roamingMode").enumValueIndex = (int)DinosaurAgent.RoamingMode.Free;
            serialized.FindProperty("interactionProfile").objectReferenceValue = profile;
            serialized.FindProperty("interactionBody").objectReferenceValue = body;
            serialized.FindProperty("lostSightGrace").floatValue = 3f;
            serialized.FindProperty("interactionCooldown").floatValue = 10f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var settings = Object.FindObjectOfType<DinosaurInteractionSettings>();
            if (settings == null) settings = new GameObject("Dinosaur Interaction Settings").AddComponent<DinosaurInteractionSettings>();
            // Keep the existing walking navigation clearance when adding performances.
            // Their larger animated envelope is still enforced by full-body trajectory checks.
            if (settings.navigationRadius <= 0f) settings.navigationRadius = body.BroadRadius + .35f;
            if (adjustForest) PrepareForest(settings.navigationRadius);
            TerrainTreeObstacleRegistry.Invalidate();
            var solids = SolidProps().Select(t => BoundsOf(t)).Where(b => b.size.sqrMagnitude > .001f).ToArray();
            settings.solidObstacles = solids;
            BuildNavigation(settings, body);
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(rex);
        }

        private static DinosaurInteractionProfile Calibrate(DinosaurAgent rex)
        {
            var animation = rex.GetComponentInChildren<Animation>(true);
            var profile = AssetDatabase.LoadAssetAtPath<DinosaurInteractionProfile>(ProfilePath);
            if (profile == null) { profile = ScriptableObject.CreateInstance<DinosaurInteractionProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
            var motions = new List<DinosaurInteractionProfile.Motion>();
            var report = new StringBuilder("# T-Rex curiosity animation calibration\n\nImported clips remain unchanged. Interaction copies hold locomotion root translation/yaw; the agent owns world movement.\n\n");
            foreach (string sourceName in Names)
            {
                AnimationClip source = null;
                foreach (AnimationState state in animation) if (state.name == sourceName) source = state.clip;
                if (source == null) { report.AppendLine("Missing optional clip: " + sourceName); continue; }
                var role = (InteractionMotionRole)Array.IndexOf(Names, sourceName);
                string path = Folder + "/Curiosity_" + role + ".anim";
                var clip = Object.Instantiate(source);
                clip.name = "Curiosity_" + role;
                clip.legacy = true;
                // Freeze the translating locomotion node. Pose animation below it remains intact.
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    bool locomotion = binding.path == "Hips/jt_Cog_C" || binding.path == "";
                    bool planar = binding.propertyName == "m_LocalPosition.x" || binding.propertyName == "m_LocalPosition.z";
                    bool turn = role == InteractionMotionRole.TurnLeft || role == InteractionMotionRole.TurnRight;
                    if (turn || role == InteractionMotionRole.Walk || (int)role >= (int)InteractionMotionRole.Investigate) continue;
                    if (!locomotion || (!planar && !(turn && (binding.propertyName.StartsWith("m_LocalRotation", StringComparison.Ordinal)
                        || binding.propertyName.StartsWith("localEulerAngles", StringComparison.Ordinal))))) continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, source.length, curve.Evaluate(0f)));
                }
                var old = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (old == null) AssetDatabase.CreateAsset(clip, path);
                else { EditorUtility.CopySerialized(clip, old); Object.DestroyImmediate(clip); clip = old; EditorUtility.SetDirty(clip); }
                var motion = new DinosaurInteractionProfile.Motion { role = role, sourceName = sourceName, clip = clip };
                MeasureMotion(rex, animation, source, motion, report);
                TRexTurnCalibration.Sample(rex, animation, motion);
                if (role == InteractionMotionRole.StopRun)
                {
                    float distance = 0f;
                    for (int i = 1; i < motion.footFrames.Length; i++)
                    {
                        var a = motion.footFrames[i - 1]; var b = motion.footFrames[i];
                        distance += (Mathf.Clamp(motion.speed.Evaluate(a.time), 0f, motion.groundSpeed)
                            + Mathf.Clamp(motion.speed.Evaluate(b.time), 0f, motion.groundSpeed)) * .5f * (b.time - a.time);
                        motion.footFrames[i].rootOffset = new Vector2(0f, distance);
                    }
                }
                motions.Add(motion);
            }
            profile.motions = motions.ToArray();
            if (profile.Find(InteractionMotionRole.Run) == null) throw new InvalidOperationException("Missing safe run clip.");
            EditorUtility.SetDirty(profile);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/trex-curiosity-animation-report.md", report.ToString());
            return profile;
        }

        private static void MeasureMotion(DinosaurAgent rex, Animation animation, AnimationClip source,
            DinosaurInteractionProfile.Motion motion, StringBuilder report)
        {
            var feet = new List<Transform>();
            DinosaurFootLock.DiscoverFeet(animation.transform, new[] { "jt_Foot_L", "jt_Foot_R" }, feet, out _);
            const int frames = 60;
            var positions = new Vector3[frames + 1, feet.Count];
            float low = float.MaxValue;
            bool owns = !AnimationMode.InAnimationMode();
            if (owns) AnimationMode.StartAnimationMode();
            Vector3 initialHeading = Vector3.forward, endHeading = Vector3.forward;
            Transform cog = animation.transform.Find("Hips/jt_Cog_C");
            try
            {
                for (int frame = 0; frame <= frames; frame++)
                {
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(animation.gameObject, motion.clip, motion.clip.length * frame / frames);
                    AnimationMode.EndSampling();
                    for (int foot = 0; foot < feet.Count; foot++) { positions[frame, foot] = feet[foot].position; low = Mathf.Min(low, feet[foot].position.y); }
                }
                if (cog != null)
                {
                    AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(animation.gameObject, source, 0f); AnimationMode.EndSampling(); initialHeading = cog.forward;
                    AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(animation.gameObject, source, source.length); AnimationMode.EndSampling(); endHeading = cog.forward;
                }
            }
            finally { if (owns) AnimationMode.StopAnimationMode(); }
            var raw = new float[frames];
            var samples = new List<float>();
            float dt = motion.clip.length / frames;
            for (int frame = 0; frame < frames; frame++)
            {
                var speeds = new List<float>();
                for (int foot = 0; foot < feet.Count; foot++)
                {
                    if (Mathf.Max(positions[frame, foot].y, positions[frame + 1, foot].y) > low + .18f) continue;
                    Vector3 delta = positions[frame + 1, foot] - positions[frame, foot]; delta.y = 0f;
                    speeds.Add(delta.magnitude / dt);
                }
                speeds.Sort(); raw[frame] = speeds.Count == 0 ? 0f : speeds[speeds.Count / 2];
                if (raw[frame] > .05f) samples.Add(raw[frame]);
            }
            samples.Sort();
            motion.groundSpeed = samples.Count == 0 ? rex.MoveAnimationGroundSpeed : samples[samples.Count / 2];
            var curve = new AnimationCurve();
            for (int frame = 0; frame < frames; frame += 3)
            {
                float value = (raw[frame] + raw[Mathf.Min(frame + 1, frames - 1)] + raw[Mathf.Min(frame + 2, frames - 1)]) / 3f;
                curve.AddKey(frame * dt, Mathf.Min(value, motion.groundSpeed * 1.5f));
            }
            if (motion.role == InteractionMotionRole.StartRun || motion.role == InteractionMotionRole.SideStartRunLeft || motion.role == InteractionMotionRole.SideStartRunRight)
            { curve.MoveKey(0, new Keyframe(0f, 0f)); curve.AddKey(motion.clip.length, motion.groundSpeed); }
            if (motion.role == InteractionMotionRole.StopRun) { curve.MoveKey(0, new Keyframe(0f, motion.groundSpeed)); curve.AddKey(motion.clip.length, 0f); }
            motion.speed = curve;
            float sourceYaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(initialHeading, Vector3.up), Vector3.ProjectOnPlane(endHeading, Vector3.up), Vector3.up);
            // These verified assets are in-place turns (the skeleton returns to its initial heading).
            // Their authored 90-degree turn is supplied once by the locomotion controller.
            motion.turnDegrees = motion.role == InteractionMotionRole.TurnLeft ? -90f : motion.role == InteractionMotionRole.TurnRight ? 90f
                : motion.role == InteractionMotionRole.Turn180Look ? ReturnTurnDegrees : 0f;
            report.AppendLine($"- {source.name}: {source.length:F3}s; support-foot speed {motion.groundSpeed:F3}m/s; measured root yaw {sourceYaw:F1}°; controller turn {motion.turnDegrees:F1}°; planar root held in interaction copy.");
        }

        private static IEnumerable<Transform> SolidProps()
        {
            var selected = new HashSet<Transform>();
            foreach (string path in new[] { "Environment Props", "Understory Pass/Rocks", "Understory Pass/Deadwood", "Ground Pass Rocks", "Ground Ecology", "Autumn Deadwood Ecology Belt", "Phase 2 Vegetation Islands" })
            {
                GameObject root = GameObject.Find(path); if (root == null) continue;
                foreach (Transform child in root.GetComponentsInChildren<Transform>())
                {
                    if (child == root.transform) continue;
                    string name = child.name.ToLowerInvariant();
                    bool rock = name.Contains("rock");
                    bool soft = name.Contains("moss") || name.Contains("fern") || name.Contains("grass") || name.Contains("bush");
                    if (!rock && (soft || !(name.Contains("log") || name.Contains("stump") || name.Contains("snag") || name.Contains("deadwood")))) continue;
                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject) && child.GetComponent<Renderer>() == null) continue;
                    bool nested = false;
                    for (Transform ancestor = child.parent; ancestor != null; ancestor = ancestor.parent)
                        if (selected.Contains(ancestor)) { nested = true; break; }
                    if (!nested && selected.Add(child)) yield return child;
                }
            }
        }

        private static Bounds BoundsOf(Transform target)
        {
            var renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(target.position, Vector3.zero);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static bool InInteractionSpace(Vector2 point, float reach, float radius)
        {
            foreach (Vector2 clearing in Clearings) if (Vector2.Distance(clearing, point) < 12f + reach + radius) return true;
            for (int route = 0; route < MovementCorridor.RouteCount; route++)
            {
                MovementCorridor.ProjectToCorridor(route, point, out float arc, out _);
                if (Vector2.Distance(point, MovementCorridor.PointAt(route, arc)) < radius + 1.5f + reach) return true;
            }
            return false;
        }

        private static Vector2 Relocate(Vector2 original, float reach, float radius)
        {
            if (!InInteractionSpace(original, reach, radius)) return original;
            for (float distance = 1f; distance <= 65f; distance += 1f)
                for (int direction = 0; direction < 48; direction++)
                {
                    float angle = direction * Mathf.PI * 2f / 48f;
                    Vector2 p = original + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                    if (p.x < -85f || p.x > 85f || p.y < -85f || p.y > 100f) continue;
                    if (!InInteractionSpace(p, reach + .25f, radius)) return p;
                }
            throw new InvalidOperationException("Cannot relocate obstacle while retaining rainforest: " + original);
        }

        private static void PrepareForest(float radius)
        {
            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null) throw new InvalidOperationException("Interaction requires Main terrain.");
            string terrainPath = Folder + "/MainInteractionTerrain.asset";
            if (AssetDatabase.GetAssetPath(terrain.terrainData) != terrainPath)
            {
                var data = AssetDatabase.LoadAssetAtPath<TerrainData>(terrainPath);
                if (data == null) { data = Object.Instantiate(terrain.terrainData); AssetDatabase.CreateAsset(data, terrainPath); }
                else EditorUtility.CopySerialized(terrain.terrainData, data);
                terrain.terrainData = data;
                var collider = terrain.GetComponent<TerrainCollider>(); if (collider != null) collider.terrainData = data;
            }
            int movedTrees = 0, movedProps = 0;
            var trees = terrain.terrainData.treeInstances;
            var profiles = Object.FindObjectOfType<ForestTrunkProfiles>();
            for (int i = 0; i < trees.Length; i++)
            {
                if (trees[i].widthScale <= 0f) continue;
                Vector3 world = terrain.transform.position + Vector3.Scale(trees[i].position, terrain.terrainData.size);
                float reach = .65f * trees[i].widthScale;
                if (profiles != null && trees[i].prototypeIndex < profiles.prototypes.Length)
                    foreach (var section in profiles.prototypes[trees[i].prototypeIndex].sections)
                        reach = Mathf.Max(reach, (section.center.magnitude + section.radius) * trees[i].widthScale);
                Vector2 p = Relocate(new Vector2(world.x, world.z), reach, radius);
                if (Vector2.Distance(p, new Vector2(world.x, world.z)) < .01f) continue;
                world.x = p.x; world.z = p.y; world.y = terrain.SampleHeight(world) + terrain.transform.position.y;
                trees[i].position = new Vector3((world.x - terrain.transform.position.x) / terrain.terrainData.size.x,
                    (world.y - terrain.transform.position.y) / terrain.terrainData.size.y, (world.z - terrain.transform.position.z) / terrain.terrainData.size.z);
                movedTrees++;
            }
            terrain.terrainData.SetTreeInstances(trees, false);
            EditorUtility.SetDirty(terrain.terrainData);
            foreach (var marker in Object.FindObjectsOfType<SceneTreeObstacle>())
            {
                float reach = marker.Radius;
                foreach (var section in marker.trunkSections) reach = Mathf.Max(reach, section.center.magnitude + section.radius);
                movedTrees += RelocateTransform(marker.transform, reach, radius, terrain) ? 1 : 0;
            }
            foreach (var prop in SolidProps())
            {
                var bounds = BoundsOf(prop);
                float reach = new Vector2(bounds.extents.x, bounds.extents.z).magnitude + new Vector2(bounds.center.x - prop.position.x, bounds.center.z - prop.position.z).magnitude;
                if (reach > 25f) throw new InvalidOperationException("Unexpected solid prop envelope: " + prop.name + " position=" + prop.position + " bounds=" + bounds + " reach=" + reach);
                movedProps += RelocateTransform(prop, reach, radius, terrain) ? 1 : 0;
            }
            Debug.Log($"CURIOSITY_FOREST: relocated {movedTrees} trunks and {movedProps} solid props; no trees removed; canopies and soft understory retained; radius={radius:F2}.");
        }

        private static bool RelocateTransform(Transform target, float reach, float radius, Terrain terrain)
        {
            Vector3 old = target.position;
            Vector2 p = Relocate(new Vector2(old.x, old.z), reach, radius);
            if (Vector2.Distance(p, new Vector2(old.x, old.z)) < .01f) return false;
            float offset = old.y - terrain.SampleHeight(old) - terrain.transform.position.y;
            Vector3 position = new Vector3(p.x, old.y, p.y);
            position.y = terrain.SampleHeight(position) + terrain.transform.position.y + offset;
            target.position = position;
            EditorUtility.SetDirty(target);
            return true;
        }

        private static void BuildNavigation(DinosaurInteractionSettings settings, DinosaurCoreBody body)
        {
            var sources = new List<NavMeshBuildSource>();
            Rect area = WorldBounds.DinosaurActivityXZ;
            float bodyHeight = Mathf.Max(.5f, body.bands.Max(b => b.bounds.max.y));
            sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                transform = Matrix4x4.TRS(new Vector3(area.center.x, -.1f, area.center.y), Quaternion.identity, Vector3.one),
                size = new Vector3(area.width, .2f, area.height), area = 0 });
            foreach (var section in TerrainTreeObstacleRegistry.GetTrunkSections())
            {
                if (section.top <= 0f || section.bottom >= bodyHeight) continue;
                AddObstacle(sources, new Bounds(new Vector3(section.center.x, bodyHeight * .5f, section.center.y), new Vector3(section.radius * 2f, bodyHeight, section.radius * 2f)));
            }
            foreach (var obstacle in settings.solidObstacles) AddObstacle(sources, obstacle);
            var build = NavMesh.GetSettingsByIndex(0);
            build.agentRadius = settings.navigationRadius;
            build.agentHeight = bodyHeight;
            build.agentClimb = .1f;
            build.agentSlope = 10f;
            build.overrideVoxelSize = true; build.voxelSize = .25f;
            build.minRegionArea = 4f;
            settings.navigationAgentTypeId = build.agentTypeID;
            var bounds = new Bounds(new Vector3(area.center.x, 2f, area.center.y), new Vector3(area.width, 12f, area.height));
            var data = NavMeshBuilder.BuildNavMeshData(build, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data == null) throw new InvalidOperationException("NavMesh build failed.");
            data.name = "TRex Interaction NavMesh";
            var existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(DataPath);
            if (existing == null) AssetDatabase.CreateAsset(data, DataPath);
            else { EditorUtility.CopySerialized(data, existing); Object.DestroyImmediate(data); data = existing; EditorUtility.SetDirty(data); }
            settings.navigationData = data;
            var instance = NavMesh.AddNavMeshData(data);
            try
            {
                var filter = new NavMeshQueryFilter { agentTypeID = settings.navigationAgentTypeId, areaMask = 1 };
                var path = new NavMeshPath();
                foreach (Vector2 clearing in Clearings)
                {
                    if (!NavMesh.SamplePosition(new Vector3(clearing.x, 0f, clearing.y), out _, 1f, filter))
                        throw new InvalidOperationException("Clearing is not navigable: " + clearing);
                    if (!NavMesh.CalculatePath(new Vector3(Clearings[0].x, 0f, Clearings[0].y), new Vector3(clearing.x, 0f, clearing.y), filter, path)
                        || path.status != NavMeshPathStatus.PathComplete) throw new InvalidOperationException("Interaction clearing is disconnected: " + clearing);
                }
                for (int route = 0; route < MovementCorridor.RouteCount; route++)
                {
                    Vector2 p = MovementCorridor.PointAt(route, 0f);
                    if (!NavMesh.CalculatePath(new Vector3(p.x, 0f, p.y), new Vector3(Clearings[0].x, 0f, Clearings[0].y), filter, path)
                        || path.status != NavMeshPathStatus.PathComplete) throw new InvalidOperationException("Route does not connect to interaction spaces: " + route);
                    for (float arc = 0f; arc < MovementCorridor.Length(route); arc += 5f)
                    {
                        Vector2 point = MovementCorridor.PointAt(route, arc);
                        if (!NavMesh.SamplePosition(new Vector3(point.x, 0f, point.y), out var hit, .5f, filter))
                            throw new InvalidOperationException("Daily route has no navigation entry at " + route + ":" + arc);
                    }
                }
                Debug.Log($"CURIOSITY_NAVIGATION_OK: {sources.Count} build proxies; all six routes connect to clearings; navigation radius {settings.navigationRadius:F2}m, animated-body radius {body.BroadRadius:F2}m.");
            }
            finally { instance.Remove(); }
        }

        private static void AddObstacle(List<NavMeshBuildSource> sources, Bounds b)
        {
            // Explicit not-walkable proxies include Terrain trees without physics colliders.
            sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                transform = Matrix4x4.TRS(b.center, Quaternion.identity, Vector3.one), size = b.size, area = 1 });
        }
    }
}
