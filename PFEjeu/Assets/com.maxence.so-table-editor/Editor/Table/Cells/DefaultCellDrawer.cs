using UnityEditor;
using UnityEngine;

namespace Maxence.SOTable.Editor
{
    /// <summary>
    /// Built-in drawer for single-line values.
    /// Numbers and text use delayed fields: the value is committed on Enter or focus loss,
    /// so editing does not re-apply to every selected row on each keystroke.
    /// </summary>
    internal sealed class DefaultCellDrawer : ICellDrawer
    {
        /// <summary>Below this, the difference is rounding noise, not a decorator.</summary>
        private const float DecoratorHeightThreshold = 0.5f;

        public bool CanDraw(SerializedProperty property)
        {
            return true;
        }

        public void Draw(Rect rect, SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    EditorGUI.DelayedIntField(rect, property, GUIContent.none);
                    break;

                case SerializedPropertyType.Float:
                    EditorGUI.DelayedFloatField(rect, property, GUIContent.none);
                    break;

                case SerializedPropertyType.String:
                    EditorGUI.DelayedTextField(rect, property, GUIContent.none);
                    break;

                case SerializedPropertyType.ObjectReference:
                    EditorGUI.ObjectField(rect, property, GUIContent.none);
                    break;

                case SerializedPropertyType.Boolean:
                    DrawToggle(rect, property);
                    break;

                default:
                    DrawPropertyFieldWithoutDecorators(rect, property);
                    break;
            }
        }

        private static void DrawToggle(Rect rect, SerializedProperty property)
        {
            EditorGUI.BeginProperty(rect, GUIContent.none, property);
            EditorGUI.BeginChangeCheck();

            bool value = EditorGUI.Toggle(rect, property.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                property.boolValue = value;
            }

            EditorGUI.EndProperty();
        }

        /// <summary>
        /// EditorGUI.PropertyField also draws [Header] and [Space] decorators, which would squash
        /// the field inside a one-line cell. The decorators are pushed above the cell and clipped,
        /// so only the field line stays visible (enums, colors, vectors, curves, gradients...).
        /// </summary>
        private static void DrawPropertyFieldWithoutDecorators(Rect rect, SerializedProperty property)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float fullHeight = EditorGUI.GetPropertyHeight(property, GUIContent.none, false);
            float decoratorHeight = fullHeight - lineHeight;

            if (decoratorHeight <= DecoratorHeightThreshold)
            {
                EditorGUI.PropertyField(rect, property, GUIContent.none, false);
                return;
            }

            GUI.BeginClip(rect);
            float fieldTop = (rect.height - lineHeight) * 0.5f;
            var shiftedRect = new Rect(0f, fieldTop - decoratorHeight, rect.width, fullHeight);
            EditorGUI.PropertyField(shiftedRect, property, GUIContent.none, false);
            GUI.EndClip();
        }
    }
}
