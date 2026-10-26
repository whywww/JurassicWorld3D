using System;
using System.Collections.Generic;
using UnityEngine;

namespace JurassicWorld
{
    public enum EmotivMovementAction
    {
        [InspectorName("前进")] Forward,
        [InspectorName("后退")] Backward,
        [InspectorName("前进并左转")] ForwardLeft,
        [InspectorName("前进并右转")] ForwardRight,
        [InspectorName("停止")] Stop
    }

    [CreateAssetMenu(menuName = "Jurassic World/Emotiv Control Settings")]
    public sealed class EmotivControlSettings : ScriptableObject
    {
        [Serializable]
        public sealed class CommandSensitivity
        {
            public bool useProjectValue;
            [Range(1, 10)] public int value = 5;
        }

        public EmotivMovementAction push = EmotivMovementAction.Forward;
        public EmotivMovementAction pull = EmotivMovementAction.Backward;
        public EmotivMovementAction left = EmotivMovementAction.ForwardLeft;
        public EmotivMovementAction right = EmotivMovementAction.ForwardRight;
        [Min(0f)] public float moveSpeed = 4f;
        [Min(0f)] public float turnSpeed = 60f;
        [Range(0f, 1f)] public float threshold = 0.3f;
        [Min(0.1f)] public float commandTimeout = 1f;
        [Range(0f, 1f)] public float fallbackForward = 0.5f;
        public CommandSensitivity pushSensitivity = new CommandSensitivity();
        public CommandSensitivity pullSensitivity = new CommandSensitivity();
        public CommandSensitivity leftSensitivity = new CommandSensitivity();
        public CommandSensitivity rightSensitivity = new CommandSensitivity();

        public void Validate()
        {
            moveSpeed = Mathf.Max(0f, FiniteOr(moveSpeed, 4f));
            turnSpeed = Mathf.Max(0f, FiniteOr(turnSpeed, 60f));
            threshold = Mathf.Clamp01(FiniteOr(threshold, 0.3f));
            commandTimeout = Mathf.Max(0.1f, FiniteOr(commandTimeout, 1f));
            fallbackForward = Mathf.Clamp01(FiniteOr(fallbackForward, 0.5f));
            ValidateSensitivity(ref pushSensitivity);
            ValidateSensitivity(ref pullSensitivity);
            ValidateSensitivity(ref leftSensitivity);
            ValidateSensitivity(ref rightSensitivity);
        }

        private static void ValidateSensitivity(ref CommandSensitivity sensitivity)
        {
            if (sensitivity == null) sensitivity = new CommandSensitivity();
            sensitivity.value = Mathf.Clamp(sensitivity.value, 1, 10);
        }

        public CommandSensitivity GetSensitivity(string command)
        {
            switch (command)
            {
                case "push": return pushSensitivity;
                case "pull": return pullSensitivity;
                case "left": return leftSensitivity;
                case "right": return rightSensitivity;
                default: return null;
            }
        }

        public List<int> BuildSensitivityValues(IList<string> activeActions, IList<int> currentValues)
        {
            if (activeActions == null || currentValues == null || currentValues.Count != 4)
                throw new ArgumentException("Cortex must return four sensitivity values.");
            var values = new List<int>(currentValues);
            foreach (int value in values)
                if (value < 1 || value > 10) throw new ArgumentException("Invalid Cortex sensitivity value.");
            var seen = new HashSet<string>();
            int index = 0;
            foreach (string command in activeActions)
            {
                if (command == "neutral") continue;
                if (string.IsNullOrEmpty(command) || !seen.Add(command) || index >= 4)
                    throw new ArgumentException("Invalid Cortex active action list.");
                var sensitivity = GetSensitivity(command);
                if (sensitivity != null && sensitivity.useProjectValue)
                    values[index] = Mathf.Clamp(sensitivity.value, 1, 10);
                index++;
            }
            return values;
        }

        private static float FiniteOr(float value, float fallback)
            => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;

        private void OnValidate() => Validate();

        // X is turn, Y is forward. Turning actions always advance as well.
        public Vector2 ResolveCommand(string command, double power, double age)
        {
            var fallback = new Vector2(0f, fallbackForward);
            if (double.IsNaN(power) || double.IsInfinity(power) || double.IsNaN(age)
                || age >= commandTimeout) return fallback;
            float strength = Mathf.Clamp01((float)power);
            if (strength < threshold) return fallback;
            EmotivMovementAction mapped;
            switch (command)
            {
                case "push": mapped = push; break;
                case "pull": mapped = pull; break;
                case "left": mapped = left; break;
                case "right": mapped = right; break;
                default: return fallback;
            }
            switch (mapped)
            {
                case EmotivMovementAction.Forward: return new Vector2(0f, strength);
                case EmotivMovementAction.Backward: return new Vector2(0f, -strength);
                case EmotivMovementAction.ForwardLeft: return new Vector2(-strength, strength);
                case EmotivMovementAction.ForwardRight: return new Vector2(strength, strength);
                default: return Vector2.zero;
            }
        }
    }
}
