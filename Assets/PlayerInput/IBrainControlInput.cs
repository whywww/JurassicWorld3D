using UnityEngine;

namespace JurassicWorld.PlayerInputs
{
    // Device modules depend on this marker, never on each other's assemblies.
    public interface IBrainControlInput { }

    public static class BrainInputSelection
    {
        public static bool CanStart(MonoBehaviour input)
        {
            if (!Application.isPlaying || !input.isActiveAndEnabled) return false;
            int selected = 0;
            foreach (var component in input.GetComponents<MonoBehaviour>())
                if (component is IBrainControlInput && component.enabled) selected++;
            if (selected == 1) return true;
            Debug.LogError("[BCI] 同一玩家只能启用一个脑控组件。请停止 Play，在 Inspector 中选择 EMOTIV、MindWave 或 BrainLink；键盘仍可用。", input);
            return false;
        }
    }
}
