using UnityEditor;
using UnityEngine;

namespace Maxence.SOTable.Editor
{
    /// <summary>
    /// Built-in drawer for values that do not fit on one line (lists, nested objects, Rect, Bounds...).
    /// Shows a small button that selects the asset so the value can be edited in the Inspector.
    /// </summary>
    internal sealed class ComplexCellDrawer : ICellDrawer
    {
        private const string ButtonTooltip = "Edit this value in the Inspector";

        // Reused to avoid allocating a GUIContent per cell per frame.
        private static readonly GUIContent ButtonContent = new GUIContent(string.Empty, ButtonTooltip);

        public bool CanDraw(SerializedProperty property)
        {
            return PropertyClassifier.IsComplex(property);
        }

        public void Draw(Rect rect, SerializedProperty property)
        {
            ButtonContent.text = PropertyClassifier.IsCollection(property)
                ? "List (" + property.arraySize + ")"
                : ObjectNames.NicifyVariableName(property.type);

            if (GUI.Button(rect, ButtonContent, EditorStyles.miniButton))
            {
                RowActions.PingAndSelect(property.serializedObject.targetObject);
            }
        }
    }
}
