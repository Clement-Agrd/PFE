using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace Maxence.SOTable.Editor
{
    /// <summary>
    /// Converts serialized values to numbers or text for sorting, filtering and CSV export.
    /// Never used in the per-frame drawing path (some getters allocate).
    /// </summary>
    internal static class PropertyValueReader
    {
        private const string DoubleTypeName = "double";
        private const string TrueText = "true";
        private const string FalseText = "false";
        private const string RoundTripFormat = "R";

        /// <summary>Numeric view of the value. Bools give 0/1, enums their value, collections their size.</summary>
        public static bool TryGetNumber(SerializedProperty property, out double value)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    value = property.longValue;
                    return true;

                case SerializedPropertyType.Float:
                    value = property.doubleValue;
                    return true;

                case SerializedPropertyType.Boolean:
                    value = property.boolValue ? 1d : 0d;
                    return true;

                case SerializedPropertyType.Enum:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.Character:
                    value = property.intValue;
                    return true;

                default:
                    if (PropertyClassifier.IsCollection(property))
                    {
                        value = property.arraySize;
                        return true;
                    }

                    value = 0d;
                    return false;
            }
        }

        /// <summary>Human readable, culture-invariant text of the value.</summary>
        public static string GetDisplayString(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return property.longValue.ToString(CultureInfo.InvariantCulture);

                case SerializedPropertyType.Float:
                    return FormatFloat(property);

                case SerializedPropertyType.Boolean:
                    return property.boolValue ? TrueText : FalseText;

                case SerializedPropertyType.String:
                    return property.stringValue;

                case SerializedPropertyType.Enum:
                    return FormatEnum(property);

                case SerializedPropertyType.ObjectReference:
                    return property.objectReferenceValue != null ? property.objectReferenceValue.name : string.Empty;

                case SerializedPropertyType.Color:
                    return "#" + ColorUtility.ToHtmlStringRGBA(property.colorValue);

                case SerializedPropertyType.Vector2:
                    return FormatVector(property.vector2Value.x, property.vector2Value.y);

                case SerializedPropertyType.Vector3:
                    return FormatVector(property.vector3Value.x, property.vector3Value.y, property.vector3Value.z);

                case SerializedPropertyType.Vector4:
                    Vector4 vector4 = property.vector4Value;
                    return FormatVector(vector4.x, vector4.y, vector4.z, vector4.w);

                case SerializedPropertyType.Vector2Int:
                    return FormatVector(property.vector2IntValue.x, property.vector2IntValue.y);

                case SerializedPropertyType.Vector3Int:
                    Vector3Int vector3Int = property.vector3IntValue;
                    return FormatVector(vector3Int.x, vector3Int.y, vector3Int.z);

                case SerializedPropertyType.Quaternion:
                    Vector3 euler = property.quaternionValue.eulerAngles;
                    return FormatVector(euler.x, euler.y, euler.z);

                case SerializedPropertyType.Rect:
                    Rect rect = property.rectValue;
                    return FormatVector(rect.x, rect.y, rect.width, rect.height);

                case SerializedPropertyType.LayerMask:
                    return property.intValue.ToString(CultureInfo.InvariantCulture);

                case SerializedPropertyType.Character:
                    return ((char)property.intValue).ToString();

                default:
                    return PropertyClassifier.IsCollection(property)
                        ? "[" + property.arraySize.ToString(CultureInfo.InvariantCulture) + "]"
                        : property.type;
            }
        }

        private static string FormatFloat(SerializedProperty property)
        {
            // floatValue avoids the "0.100000001490116" noise that doubleValue shows for float fields.
            return property.type == DoubleTypeName
                ? property.doubleValue.ToString(RoundTripFormat, CultureInfo.InvariantCulture)
                : property.floatValue.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatEnum(SerializedProperty property)
        {
            int index = property.enumValueIndex;
            string[] names = property.enumDisplayNames;
            return index >= 0 && index < names.Length
                ? names[index]
                : property.intValue.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatVector(float x, float y)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0}; {1})", x, y);
        }

        private static string FormatVector(float x, float y, float z)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0}; {1}; {2})", x, y, z);
        }

        private static string FormatVector(float x, float y, float z, float w)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0}; {1}; {2}; {3})", x, y, z, w);
        }
    }
}
