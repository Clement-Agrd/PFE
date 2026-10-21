using UnityEditor;

namespace Maxence.SOTable.Editor
{
    /// <summary>
    /// Decides how a serialized field fits in a table cell.
    /// Shared by the column builder, the cell drawers and the value reader so they always agree.
    /// </summary>
    internal static class PropertyClassifier
    {
        /// <summary>Arrays and lists. Strings are arrays of chars internally, so they are excluded.</summary>
        public static bool IsCollection(SerializedProperty property)
        {
            return property.isArray && property.propertyType != SerializedPropertyType.String;
        }

        /// <summary>
        /// True when the value cannot be edited on a single line: collections, nested objects,
        /// [SerializeReference] fields and multi-line Unity structs (Rect, Bounds).
        /// Those cells show a button that opens the asset in the Inspector instead.
        /// </summary>
        public static bool IsComplex(SerializedProperty property)
        {
            if (IsCollection(property))
            {
                return true;
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.Generic:
                case SerializedPropertyType.ManagedReference:
                case SerializedPropertyType.Rect:
                case SerializedPropertyType.RectInt:
                case SerializedPropertyType.Bounds:
                case SerializedPropertyType.BoundsInt:
                    return true;
                default:
                    return false;
            }
        }
    }
}
