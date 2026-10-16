#if UNITY_EDITOR
using System;
using UnityEngine;

namespace JurassicWorld.Editor
{
    // Sample after animation, head compensation and foot IK, rather than between their updates.
    [DefaultExecutionOrder(2000)]
    public sealed class TRexSideHandoffProbe : MonoBehaviour
    {
        public Action observe;
        private void LateUpdate() => observe?.Invoke();
    }
}
#endif
