using UnityEditor;
using UnityEngine;

namespace Maxence.SOTable.Editor
{
    /// <summary>
    /// Draws the value of one table cell. Implement it and register it with
    /// <see cref="CellDrawerRegistry.Register"/> to customise how some fields look in the table.
    /// </summary>
    public interface ICellDrawer
    {
        /// <summary>Return true if this drawer handles <paramref name="property"/>.</summary>
        bool CanDraw(SerializedProperty property);

        /// <summary>
        /// Draw the value inside <paramref name="rect"/> (one line high). Modify the property through
        /// SerializedProperty APIs only: the table applies the changes, records Undo and propagates
        /// the value to the other selected rows.
        /// </summary>
        void Draw(Rect rect, SerializedProperty property);
    }
}
