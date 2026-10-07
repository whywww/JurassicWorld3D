using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace JurassicWorld.Editor
{
    /// <summary>
    /// 诊断工具：精确测量 T-Rex walk 动画的原生地速。
    /// 菜单 Jurassic World > Diagnostics > Measure T-Rex Ground Speed 执行。
    /// </summary>
    public static class GroundSpeedMeasure
    {
        [MenuItem("Jurassic World/Diagnostics/Measure T-Rex Ground Speed")]
        public static void Measure()
        {
            // 1. 加载 T-Rex FBX prefab
            GameObject prefab = Resources.Load<GameObject>("Dinosaurs/TRex/source/T-Rex");
            if (prefab == null)
            {
                Debug.LogError("[GroundSpeed] 未找到 T-Rex FBX: Resources/Dinosaurs/TRex/source/T-Rex");
                return;
            }

            GameObject instance = Object.Instantiate(prefab);
            try
            {
                // 2. 获取 Animation 组件
                Animation animation = instance.GetComponentInChildren<Animation>(true);
                if (animation == null)
                {
                    Debug.LogError("[GroundSpeed] T-Rex 缺少 Legacy Animation 组件");
                    return;
                }

                // 3. 列出所有动画片段
                Debug.Log("[GroundSpeed] === 动画片段列表 ===");
                foreach (AnimationState state in animation)
                {
                    if (state.clip != null)
                        Debug.Log($"  clip='{state.name}' length={state.clip.length:F4}s");
                }

                // 4. 找到 walk/move clip（与烘焙器相同的匹配逻辑）
                AnimationClip moveClip = null;
                foreach (AnimationState state in animation)
                {
                    if (state.clip == null) continue;
                    string name = state.name;
                    if (name.IndexOf("run", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("walk", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        bool isRM = name.IndexOf("_RM", System.StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!isRM)
                        {
                            moveClip = state.clip;
                            Debug.Log($"[GroundSpeed] 选定 move clip: '{name}' (length={moveClip.length:F4}s)");
                            break;
                        }
                    }
                }

                if (moveClip == null)
                {
                    Debug.LogError("[GroundSpeed] 未找到 walk/run 动画片段");
                    return;
                }

                // 5. 发现脚骨和身体参考骨
                var feet = new List<Transform>();
                string[] footBoneNames = {
                    "jt_Foot_L", "jt_Foot_R",
                    "jt_ToeInner_L", "jt_ToeMiddle_L", "jt_ToeOutter_L",
                    "jt_ToeInner_R", "jt_ToeMiddle_R", "jt_ToeOutter_R",
                    "jt_ClawInner_L", "jt_ClawMiddle_L", "jt_ClawOutter_L",
                    "jt_ClawInner_R", "jt_ClawMiddle_R", "jt_ClawOutter_R",
                };

                // 简单脚骨发现：按名字查找
                foreach (string boneName in footBoneNames)
                {
                    Transform bone = FindBone(instance.transform, boneName);
                    if (bone != null) feet.Add(bone);
                }
                Debug.Log($"[GroundSpeed] 发现脚骨: {feet.Count} 个");

                // 身体参考骨（pelvis/hips）
                Transform bodyRef = FindBodyReference(instance.transform);
                Debug.Log($"[GroundSpeed] 身体参考骨: {(bodyRef != null ? bodyRef.name : "null")}");

                if (feet.Count == 0 || bodyRef == null)
                {
                    Debug.LogError("[GroundSpeed] 脚骨或身体参考骨未找到");
                    return;
                }

                // 6. 采样动画，测量脚相对身体的水平轨迹
                int numSamples = 64;
                float clipLength = moveClip.length;
                var tracks = new List<Vector2>[feet.Count];
                for (int i = 0; i < tracks.Length; i++)
                    tracks[i] = new List<Vector2>(numSamples + 1);

                bool ownsMode = !AnimationMode.InAnimationMode();
                if (ownsMode) AnimationMode.StartAnimationMode();
                try
                {
                    for (int s = 0; s <= numSamples; s++)
                    {
                        float time = clipLength > 0f ? clipLength * (s / (float)numSamples) : 0f;
                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(animation.gameObject, moveClip, time);
                        AnimationMode.EndSampling();

                        Vector3 bodyPos = bodyRef.position;
                        for (int f = 0; f < feet.Count; f++)
                        {
                            if (feet[f] == null) continue;
                            Vector3 offset = feet[f].position - bodyPos;
                            tracks[f].Add(new Vector2(offset.x, offset.z));
                        }
                    }
                }
                finally
                {
                    if (ownsMode && AnimationMode.InAnimationMode())
                        AnimationMode.StopAnimationMode();
                }

                // 7. PCA 步幅估算（与烘焙器 EstimateStepLength 完全相同）
                var ranges = new List<float>();
                for (int f = 0; f < feet.Count; f++)
                {
                    List<Vector2> samples = tracks[f];
                    if (samples == null || samples.Count < 4) continue;

                    Vector2 mean = Vector2.zero;
                    foreach (Vector2 sample in samples) mean += sample;
                    mean /= samples.Count;

                    float cxx = 0f, czz = 0f, cxz = 0f;
                    foreach (Vector2 sample in samples)
                    {
                        float dx = sample.x - mean.x;
                        float dz = sample.y - mean.y;
                        cxx += dx * dx;
                        czz += dz * dz;
                        cxz += dx * dz;
                    }

                    float theta = 0.5f * Mathf.Atan2(2f * cxz, cxx - czz);
                    var axis = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));

                    float min = float.MaxValue, max = float.MinValue;
                    foreach (Vector2 sample in samples)
                    {
                        float proj = Vector2.Dot(sample - mean, axis);
                        if (proj < min) min = proj;
                        if (proj > max) max = proj;
                    }

                    if (max > min)
                    {
                        float range = max - min;
                        ranges.Add(range);
                        Debug.Log($"  {feet[f].name}: step range={range:F4}m (axis {Mathf.Rad2Deg * theta:F1}deg)");
                    }
                }

                if (ranges.Count == 0)
                {
                    Debug.LogError("[GroundSpeed] 无有效步幅测量");
                    return;
                }

                ranges.Sort();
                float stepLength = ranges[ranges.Count / 2];
                float nativeSpeed = clipLength > 0f ? 2f * stepLength / clipLength : 0f;

                Debug.Log($"[GroundSpeed] ==============================");
                Debug.Log($"[GroundSpeed] clip='{moveClip.name}' length={clipLength:F4}s");
                Debug.Log($"[GroundSpeed] stepLength(median)={stepLength:F4}m");
                Debug.Log($"[GroundSpeed] nativeGroundSpeed = 2 * {stepLength:F4} / {clipLength:F4} = {nativeSpeed:F4} m/s");
                Debug.Log($"[GroundSpeed] *** moveAnimationGroundSpeed = {nativeSpeed:F2} ***");
                Debug.Log($"[GroundSpeed] ==============================");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static Transform FindBone(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name) return child;
            }
            return null;
        }

        private static Transform FindBodyReference(Transform root)
        {
            // 与烘焙器 ResolveBodyReference 相同的逻辑
            string[] candidates = { "pelvis", "hips", "hip", "cog", "root" };
            Transform best = null;
            float bestScore = float.MaxValue;

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string lower = t.name.ToLowerInvariant();
                bool match = false;
                foreach (string c in candidates)
                {
                    if (lower.Contains(c)) { match = true; break; }
                }
                if (!match) continue;

                // 优先选 Hips/Cog（靠近模型中心）
                float score = Mathf.Abs(t.localPosition.x) + Mathf.Abs(t.localPosition.z);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = t;
                }
            }
            return best ?? root;
        }
    }
}
