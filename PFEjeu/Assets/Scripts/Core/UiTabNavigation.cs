using System;
using UnityEngine;

namespace Core
{
    /// <summary>Shoulder-button tabs belonging to one UI screen, never its nested confirmations.</summary>
    [DisallowMultipleComponent]
    public sealed class UiTabNavigation : MonoBehaviour
    {
        private Action<int> _change;
        public static void Register(Transform root, Action<int> change)
        {
            if (root == null) return;
            if (!root.TryGetComponent<UiTabNavigation>(out var tabs)) tabs = root.gameObject.AddComponent<UiTabNavigation>();
            tabs._change = change;
        }
        internal void Change(int direction) => _change?.Invoke(direction);
    }
}
