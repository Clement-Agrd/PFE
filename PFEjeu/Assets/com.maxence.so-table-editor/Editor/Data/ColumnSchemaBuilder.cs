using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maxence.SOTable.Editor
{
    /// <summary>
    /// Builds the column list of a ScriptableObject type by walking its serialized fields.
    /// Nested serializable structs/classes are flattened into one column per field.
    /// </summary>
    internal static class ColumnSchemaBuilder
    {
        private const string ScriptPropertyPath = "m_Script";
        private const string NestedNameSeparator = " / ";

        /// <summary>How many nested levels are flattened. Deeper objects become a complex cell.</summary>
        private const int MaxFlattenDepth = 2;

        public static List<ColumnDefinition> Build(Type scriptableObjectType)
        {
            var columns = new List<ColumnDefinition>();
            if (scriptableObjectType == null || scriptableObjectType.IsAbstract)
            {
                return columns;
            }

            // A temporary instance gives the exact field layout of the type itself,
            // so derived assets never add columns that other rows would lack.
            ScriptableObject template = null;
            try
            {
                template = ScriptableObject.CreateInstance(scriptableObjectType);
                if (template == null)
                {
                    return columns;
                }

                template.hideFlags = HideFlags.HideAndDontSave;

                using (var serializedObject = new SerializedObject(template))
                {
                    SerializedProperty iterator = serializedObject.GetIterator();
                    bool enterChildren = true;

                    while (iterator.NextVisible(enterChildren))
                    {
                        enterChildren = false;

                        if (iterator.propertyPath == ScriptPropertyPath)
                        {
                            continue;
                        }

                        CollectColumns(iterator, 0, string.Empty, columns);
                    }
                }
            }
            finally
            {
                if (template != null)
                {
                    Object.DestroyImmediate(template);
                }
            }

            return columns;
        }

        private static void CollectColumns(SerializedProperty property, int depth, string displayPrefix, List<ColumnDefinition> output)
        {
            string displayName = displayPrefix + property.displayName;

            if (CanFlatten(property, depth))
            {
                SerializedProperty child = property.Copy();
                SerializedProperty end = property.GetEndProperty();
                string childPrefix = displayName + NestedNameSeparator;

                if (child.NextVisible(true))
                {
                    do
                    {
                        if (SerializedProperty.EqualContents(child, end))
                        {
                            break;
                        }

                        CollectColumns(child, depth + 1, childPrefix, output);
                    }
                    while (child.NextVisible(false));
                }

                return;
            }

            output.Add(new ColumnDefinition(
                property.propertyPath,
                displayName,
                property.propertyType,
                PropertyClassifier.IsComplex(property)));
        }

        private static bool CanFlatten(SerializedProperty property, int depth)
        {
            return depth < MaxFlattenDepth
                && property.propertyType == SerializedPropertyType.Generic
                && !property.isArray
                && property.hasVisibleChildren;
        }
    }
}
