using System;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Maxence.SOTable.Editor
{
    /// <summary>Searchable dropdown listing ScriptableObject types (Unity's AdvancedDropdown).</summary>
    internal sealed class ScriptableObjectTypeDropdown : AdvancedDropdown
    {
        private const string RootTitle = "ScriptableObject Types";
        private static readonly Vector2 MinimumSize = new Vector2(260f, 320f);

        private readonly IReadOnlyList<Type> _types;
        private readonly Action<Type> _onTypeSelected;

        private sealed class TypeItem : AdvancedDropdownItem
        {
            public Type Type { get; }

            public TypeItem(Type type) : base(BuildLabel(type))
            {
                Type = type;
            }

            private static string BuildLabel(Type type)
            {
                // The namespace disambiguates two types with the same name.
                return string.IsNullOrEmpty(type.Namespace) ? type.Name : type.Name + "  (" + type.Namespace + ")";
            }
        }

        public ScriptableObjectTypeDropdown(AdvancedDropdownState state, IReadOnlyList<Type> types, Action<Type> onTypeSelected)
            : base(state)
        {
            _types = types;
            _onTypeSelected = onTypeSelected;
            minimumSize = MinimumSize;
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem(RootTitle);
            for (int i = 0; i < _types.Count; i++)
            {
                root.AddChild(new TypeItem(_types[i]));
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is TypeItem typeItem)
            {
                _onTypeSelected?.Invoke(typeItem.Type);
            }
        }
    }
}
