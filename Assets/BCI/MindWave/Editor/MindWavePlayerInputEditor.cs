using UnityEditor;
using JurassicWorld.PlayerInputs.Editor;
namespace JurassicWorld.BCI.MindWave.Editor
{
    [CustomEditor(typeof(MindWavePlayerInput))]
    public sealed class MindWavePlayerInputEditor : BrainInputEditor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("使用组件标题的勾选框选择 MindWave。Connector / TcpSimulator 只决定该设备的后端，停止 Play 后可修改。", MessageType.Info);
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(!CanEdit))
            {
                var settings = serializedObject.FindProperty("settings");
                var source = settings.FindPropertyRelative("source");
                string[] backends = { "Connector", "TcpSimulator" };
                int index = System.Array.IndexOf(backends, source.stringValue);
                int chosen = EditorGUILayout.Popup("Backend", System.Math.Max(0, index), backends);
                if (CanEdit) source.stringValue = backends[chosen];
                var child = settings.Copy(); var end = settings.GetEndProperty();
                if (child.NextVisible(true))
                {
                    do
                    {
                        if (SerializedProperty.EqualContents(child, end)) break;
                        if (child.name != "source") EditorGUILayout.PropertyField(child, true);
                    } while (child.NextVisible(false));
                }
                if (CanEdit) serializedObject.ApplyModifiedProperties();
            }
        }
    }
}
