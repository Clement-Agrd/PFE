using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    /// <summary>Defines the controls owned by one open screen or modal dialogue.</summary>
    [DisallowMultipleComponent]
    public sealed class UiNavigationScope : MonoBehaviour
    {
        internal static readonly List<UiNavigationScope> Scopes = new();
        private static int _order;
        internal Action Cancel;
        internal Func<bool> IsOpen;
        internal Component Owner;
        internal bool Modal;
        internal int Order, OpenedFrame;
        internal bool Available => isActiveAndEnabled && (IsOpen == null || IsOpen());

        public static UiNavigationScope Register(Transform root, Action cancel, Component owner = null,
            Func<bool> isOpen = null, bool modal = true)
        {
            if (root == null) return null;
            if (!root.TryGetComponent<UiNavigationScope>(out var scope)) scope = root.gameObject.AddComponent<UiNavigationScope>();
            scope.Cancel = cancel; scope.Owner = owner; scope.IsOpen = isOpen; scope.Modal = modal;
            scope.Order = ++_order; scope.OpenedFrame = Time.frameCount;
            UiNavigation.Refresh();
            return scope;
        }

        private void OnEnable()
        {
            if (!Scopes.Contains(this)) Scopes.Add(this);
            Order = ++_order; OpenedFrame = Time.frameCount;
            UiNavigation.Refresh();
        }
        private void OnDisable() { Scopes.Remove(this); UiNavigation.Refresh(); }
    }
}
