using System;
using UnityEngine;

namespace JurassicWorld.PlayerInputs
{
    // Optional modules register on the main thread before scene OnEnable callbacks.
    // Keeping this contract independent lets the project compile after a module is removed.
    public static class PlayerMovementInput
    {
        private static UnityEngine.Object owner;
        private static Func<Vector2> read;

        public static bool HasProvider => owner != null && read != null;

        public static void Register(UnityEngine.Object provider, Func<Vector2> reader)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            if (HasProvider && owner != provider)
                throw new InvalidOperationException("A movement input provider is already registered.");
            owner = provider;
            read = reader;
        }

        public static void Unregister(UnityEngine.Object provider)
        {
            if (!ReferenceEquals(owner, provider)) return;
            owner = null;
            read = null;
        }

        public static Vector2 ReadMovement() => HasProvider ? read() : Vector2.zero;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            owner = null;
            read = null;
        }
    }
}
