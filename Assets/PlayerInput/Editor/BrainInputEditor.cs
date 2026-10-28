using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace JurassicWorld.PlayerInputs.Editor
{
    // The standard component header checkbox remains the source selector.
    public class BrainInputEditor : UnityEditor.Editor
    {
        public static bool CanEdit => !EditorApplication.isPlayingOrWillChangePlaymode;

        public static void SelectOnly(Behaviour selected)
        {
            if (!CanEdit) return;
            Undo.SetCurrentGroupName("选择脑控设备");
            foreach (var input in selected.GetComponents<MonoBehaviour>())
            {
                if (!(input is IBrainControlInput) || input == selected || !input.enabled) continue;
                Undo.RecordObject(input, "选择脑控设备");
                input.enabled = false;
                EditorUtility.SetDirty(input);
                PrefabUtility.RecordPrefabInstancePropertyModifications(input);
            }
            BrainInputInspectorGuard.Watch(selected.gameObject);
        }

        protected virtual void OnEnable()
        {
            if (target is Behaviour input) BrainInputInspectorGuard.Watch(input.gameObject);
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("勾选此组件会关闭另外两个脑控组件；全部关闭则仅用键盘。停止 Play 后才能切换和修改参数。", MessageType.Info);
            using (new EditorGUI.DisabledScope(!CanEdit)) DrawDefaultInspector();
        }
    }

    // Component titlebars are drawn by Unity's EditorElement, not Editor.OnHeaderGUI.
    // Observe the native m_Enabled property and disable its actual UI Toolkit header in Play.
    [InitializeOnLoad]
    public static class BrainInputInspectorGuard
    {
        private static readonly Dictionary<int, bool> enabled = new Dictionary<int, bool>();
        private static double nextHeaders;

        static BrainInputInspectorGuard()
        {
            ObjectChangeEvents.changesPublished += Changed;
            Undo.undoRedoPerformed += Snapshot;
            EditorApplication.update += UpdateHeaders;
        }

        public static void Watch(GameObject player)
        {
            foreach (var input in player.GetComponents<MonoBehaviour>())
                if (input is IBrainControlInput) enabled[input.GetInstanceID()] = input.enabled;
        }

        private static void Snapshot()
        {
            foreach (var inspector in Resources.FindObjectsOfTypeAll<BrainInputEditor>())
                if (inspector.target is Behaviour input) Watch(input.gameObject);
        }

        private static void Changed(ref ObjectChangeEventStream stream)
        {
            if (!BrainInputEditor.CanEdit) return;
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeGameObjectOrComponentProperties) continue;
                stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var change);
                var input = EditorUtility.InstanceIDToObject(change.instanceId) as MonoBehaviour;
                if (input == null || !(input is IBrainControlInput)) continue;
                if (enabled.TryGetValue(change.instanceId, out bool previous) && !previous && input.enabled)
                    BrainInputEditor.SelectOnly(input);
                else enabled[change.instanceId] = input.enabled;
            }
        }

        public static void UpdateHeaders()
        {
            if (EditorApplication.timeSinceStartup < nextHeaders) return;
            nextHeaders = EditorApplication.timeSinceStartup + .2;
            var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            foreach (var inspector in Resources.FindObjectsOfTypeAll<BrainInputEditor>())
            {
                if (inspector.target == null) continue;
                string headerName = ObjectNames.GetInspectorTitle(inspector.target) + "Header";
                foreach (var window in windows)
                {
                    if (window.GetType().FullName != "UnityEditor.InspectorWindow") continue;
                    window.rootVisualElement.Query<IMGUIContainer>(headerName).ForEach(header => header.SetEnabled(BrainInputEditor.CanEdit));
                }
            }
        }
    }
}
