using UnityEditor;
using UnityEngine;

namespace JurassicWorld.Editor
{
    [CustomEditor(typeof(EmotivControlSettings))]
    public sealed class EmotivControlSettingsEditor : UnityEditor.Editor
    {
        public const string DefaultAssetPath = "Assets/Settings/EmotivControlSettings.asset";
        internal static bool CanEditSettings => !EditorApplication.isPlayingOrWillChangePlaymode && !Application.isPlaying;

        public override void OnInspectorGUI()
        {
            DrawSettings((EmotivControlSettings)target, serializedObject);
        }

        internal static void DrawSettings(EmotivControlSettings settings, SerializedObject serialized,
            EmotivSensitivityController sensitivity = null)
        {
            serialized.Update();
            if (!CanEditSettings)
                EditorGUILayout.HelpBox("Play 期间参数只读。请停止 Play，在 Inspector 修改并保存，再点击 Play 应用。", MessageType.Info);
            using (new EditorGUI.DisabledScope(!CanEditSettings))
            {
                EditorGUILayout.LabelField("信号 → 动作", EditorStyles.boldLabel);
                Field(serialized, "push", "Push");
                Field(serialized, "pull", "Pull");
                Field(serialized, "left", "Left");
                Field(serialized, "right", "Right");
                EditorGUILayout.Space();
                Field(serialized, "moveSpeed", "移动速度（米/秒）");
                Field(serialized, "turnSpeed", "转向速度（度/秒）");
                Field(serialized, "threshold", "接受阈值（Power）");
                Field(serialized, "commandTimeout", "指令超时（秒）");
                Field(serialized, "fallbackForward", "无有效指令时前进比例");
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Cortex command 敏感度（1–10）", EditorStyles.boldLabel);
                SensitivityField(serialized, "push", "Push", sensitivity);
                SensitivityField(serialized, "pull", "Pull", sensitivity);
                SensitivityField(serialized, "left", "Left", sensitivity);
                SensitivityField(serialized, "right", "Right", sensitivity);
                if (CanEditSettings && serialized.ApplyModifiedProperties())
                {
                    settings.Validate();
                    EditorUtility.SetDirty(settings);
                }
                EditorGUILayout.LabelField("转向角度 = Power × 转向速度 × 持续秒数");
                if (CanEditSettings)
                {
                    if (GUILayout.Button("保存控制配置")) AssetDatabase.SaveAssetIfDirty(settings);
                }
            }
            if (sensitivity != null) EditorGUILayout.HelpBox(sensitivity.Status, MessageType.Info);
        }

        private static void SensitivityField(SerializedObject serialized, string command, string label,
            EmotivSensitivityController sensitivity)
        {
            var setting = serialized.FindProperty(command + "Sensitivity");
            var useProjectValue = setting.FindPropertyRelative("useProjectValue");
            EditorGUILayout.PropertyField(useProjectValue, new GUIContent(label + "：使用项目值"));
            using (new EditorGUI.IndentLevelScope())
            {
                if (useProjectValue.boolValue)
                    EditorGUILayout.PropertyField(setting.FindPropertyRelative("value"), new GUIContent("敏感度"));
                else EditorGUILayout.LabelField("敏感度", "沿用 profile");
                if (sensitivity != null) EditorGUILayout.LabelField(sensitivity.DescribeCommand(command));
            }
        }

        private static void Field(SerializedObject serialized, string property, string label)
            => EditorGUILayout.PropertyField(serialized.FindProperty(property), new GUIContent(label));
    }

    [CustomEditor(typeof(EmotivPlayerInput))]
    public sealed class EmotivPlayerInputEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var reference = serializedObject.FindProperty("controlSettings");
            using (new EditorGUI.DisabledScope(!EmotivControlSettingsEditor.CanEditSettings))
            {
                EditorGUILayout.PropertyField(reference, new GUIContent("控制配置资源"));
                if (reference.objectReferenceValue == null && GUILayout.Button("关联项目默认控制配置"))
                    reference.objectReferenceValue = AssetDatabase.LoadAssetAtPath<EmotivControlSettings>(
                        EmotivControlSettingsEditor.DefaultAssetPath);
            }
            if (EmotivControlSettingsEditor.CanEditSettings) serializedObject.ApplyModifiedProperties();
            if (!Application.isPlaying && reference.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("未关联配置时使用默认参数；请先关联配置资源以保存调节值。", MessageType.Warning);
                return;
            }
            var settings = ((EmotivPlayerInput)target).ControlSettings;
            EmotivControlSettingsEditor.DrawSettings(settings, new SerializedObject(settings),
                Application.isPlaying ? ((EmotivPlayerInput)target).Sensitivity : null);
        }
    }
}
