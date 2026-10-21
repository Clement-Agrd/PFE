using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Maxence.SOTable.Editor
{
    /// <summary>
    /// Draws the table: sortable/resizable header and virtualized rows (only visible rows are drawn).
    /// Applies an edit to every selected row. Selection logic lives in <see cref="TableSelection"/>.
    /// Header column 0 is the asset name; header column N is data column N - 1.
    /// </summary>
    internal sealed class TableView
    {
        #region Constants

        private const int NameHeaderColumn = 0;
        private const float RowHeight = 20f;
        private const float CellHorizontalPadding = 3f;
        private const float CellVerticalPadding = 1f;
        private const float IconSize = 16f;
        private const float IconSpacing = 3f;
        private const float NameColumnWidth = 180f;
        private const float NameColumnMinWidth = 100f;
        private const float DataColumnMinWidth = 40f;
        private const float HeaderCharacterWidth = 7f;
        private const float HeaderExtraWidth = 24f;

        #endregion

        #region Fields

        private readonly TableModel _model;
        private readonly MultiColumnHeader _header;
        private Vector2 _scrollPosition;

        #endregion

        #region Events & Properties

        /// <summary>Raised when a header is clicked. Args: data column index (or NameColumnIndex), ascending.</summary>
        public event Action<int, bool> SortChanged;

        public TableSelection RowSelection { get; }

        public int HeaderColumnCount => _header.state.columns.Length;

        #endregion

        public TableView(TableModel model, MultiColumnHeaderState headerState)
        {
            _model = model;
            RowSelection = new TableSelection(model);

            _header = new MultiColumnHeader(headerState);
            _header.sortingChanged += OnHeaderSortingChanged;
        }

        #region Header State

        public static MultiColumnHeaderState CreateHeaderState(IReadOnlyList<ColumnDefinition> columns)
        {
            var headerColumns = new MultiColumnHeaderState.Column[columns.Count + 1];

            headerColumns[NameHeaderColumn] = new MultiColumnHeaderState.Column
            {
                headerContent = new GUIContent(SOTableStyles.NameColumnTitle),
                width = NameColumnWidth,
                minWidth = NameColumnMinWidth,
                autoResize = false,
                allowToggleVisibility = false,
                canSort = true,
                sortingArrowAlignment = TextAlignment.Right
            };

            for (int i = 0; i < columns.Count; i++)
            {
                ColumnDefinition column = columns[i];
                headerColumns[i + 1] = new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent(column.DisplayName, column.PropertyPath),
                    width = GetDefaultWidth(column),
                    minWidth = DataColumnMinWidth,
                    autoResize = false,
                    allowToggleVisibility = true,
                    canSort = true,
                    sortingArrowAlignment = TextAlignment.Right
                };
            }

            return new MultiColumnHeaderState(headerColumns);
        }

        private static float GetDefaultWidth(ColumnDefinition column)
        {
            float typeWidth;
            if (column.IsComplex)
            {
                typeWidth = 100f;
            }
            else
            {
                switch (column.PropertyType)
                {
                    case SerializedPropertyType.Boolean: typeWidth = 60f; break;
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.Float: typeWidth = 80f; break;
                    case SerializedPropertyType.Enum: typeWidth = 110f; break;
                    case SerializedPropertyType.String: typeWidth = 160f; break;
                    case SerializedPropertyType.ObjectReference: typeWidth = 170f; break;
                    case SerializedPropertyType.Vector2:
                    case SerializedPropertyType.Vector2Int: typeWidth = 140f; break;
                    case SerializedPropertyType.Vector3:
                    case SerializedPropertyType.Vector3Int:
                    case SerializedPropertyType.Quaternion: typeWidth = 200f; break;
                    case SerializedPropertyType.Vector4: typeWidth = 250f; break;
                    default: typeWidth = 120f; break;
                }
            }

            // EditorStyles may not be ready here, so the title width is estimated.
            float titleWidth = column.DisplayName.Length * HeaderCharacterWidth + HeaderExtraWidth;
            return Mathf.Max(typeWidth, titleWidth);
        }

        public bool TryGetSort(out int dataColumnIndex, out bool ascending)
        {
            int headerIndex = _header.sortedColumnIndex;
            if (headerIndex < 0)
            {
                dataColumnIndex = TableModel.NameColumnIndex;
                ascending = true;
                return false;
            }

            dataColumnIndex = ToDataColumnIndex(headerIndex);
            ascending = _header.IsSortedAscending(headerIndex);
            return true;
        }

        /// <summary>Visible columns in display order, as data column indices.</summary>
        public void GetVisibleDataColumns(List<int> output)
        {
            output.Clear();
            int[] visibleColumns = _header.state.visibleColumns;
            for (int i = 0; i < visibleColumns.Length; i++)
            {
                output.Add(ToDataColumnIndex(visibleColumns[i]));
            }
        }

        private static int ToDataColumnIndex(int headerColumnIndex)
        {
            return headerColumnIndex == NameHeaderColumn ? TableModel.NameColumnIndex : headerColumnIndex - 1;
        }

        private void OnHeaderSortingChanged(MultiColumnHeader header)
        {
            // Row indices change after a sort, so the Shift+click anchor is no longer meaningful.
            RowSelection.ResetAnchor();

            if (TryGetSort(out int dataColumnIndex, out bool ascending))
            {
                SortChanged?.Invoke(dataColumnIndex, ascending);
            }
        }

        #endregion

        #region Drawing

        public void OnGUI(Rect rect)
        {
            // Wide mode keeps vectors on one line whatever the column width.
            bool previousWideMode = EditorGUIUtility.wideMode;
            EditorGUIUtility.wideMode = true;

            try
            {
                var headerRect = new Rect(rect.x, rect.y, rect.width, _header.height);
                _header.OnGUI(headerRect, _scrollPosition.x);

                var bodyRect = new Rect(rect.x, headerRect.yMax, rect.width, rect.height - _header.height);
                DrawBody(bodyRect);
            }
            finally
            {
                EditorGUIUtility.wideMode = previousWideMode;
            }
        }

        private void DrawBody(Rect bodyRect)
        {
            IReadOnlyList<TableRow> rows = _model.VisibleRows;
            float contentWidth = _header.state.widthOfAllVisibleColumns;
            var viewRect = new Rect(0f, 0f, contentWidth, rows.Count * RowHeight);

            _scrollPosition = GUI.BeginScrollView(bodyRect, _scrollPosition, viewRect);

            if (rows.Count > 0)
            {
                int firstRow = Mathf.Max(0, Mathf.FloorToInt(_scrollPosition.y / RowHeight));
                int lastRow = Mathf.Min(rows.Count - 1, Mathf.CeilToInt((_scrollPosition.y + bodyRect.height) / RowHeight));

                for (int i = firstRow; i <= lastRow; i++)
                {
                    var rowRect = new Rect(0f, i * RowHeight, contentWidth, RowHeight);
                    DrawRow(rows[i], i, rowRect);
                }
            }

            GUI.EndScrollView();
        }

        private void DrawRow(TableRow row, int rowIndex, Rect rowRect)
        {
            if (!row.IsValid)
            {
                return;
            }

            if (Event.current.type == EventType.Repaint)
            {
                DrawRowBackground(rowRect, rowIndex, RowSelection.Contains(row));
            }

            // Cheap no-op unless the asset changed elsewhere (Inspector, Undo, script).
            row.SerializedObject.UpdateIfRequiredOrScript();

            int[] visibleColumns = _header.state.visibleColumns;
            for (int visibleIndex = 0; visibleIndex < visibleColumns.Length; visibleIndex++)
            {
                int headerColumn = visibleColumns[visibleIndex];
                Rect cellRect = GetPaddedCellRect(visibleIndex, rowRect);

                if (headerColumn == NameHeaderColumn)
                {
                    DrawNameCell(cellRect, row, rowIndex);
                }
                else
                {
                    DrawDataCell(cellRect, row, ToDataColumnIndex(headerColumn));
                }
            }
        }

        private Rect GetPaddedCellRect(int visibleColumnIndex, Rect rowRect)
        {
            Rect cellRect = _header.GetCellRect(visibleColumnIndex, rowRect);
            cellRect.x += CellHorizontalPadding;
            cellRect.width -= CellHorizontalPadding * 2f;
            cellRect.y += CellVerticalPadding;
            cellRect.height -= CellVerticalPadding * 2f;
            return cellRect;
        }

        private static void DrawRowBackground(Rect rowRect, int rowIndex, bool isSelected)
        {
            if (isSelected)
            {
                EditorGUI.DrawRect(rowRect, SOTableStyles.SelectedRowColor);
            }
            else if (rowIndex % 2 == 1)
            {
                EditorGUI.DrawRect(rowRect, SOTableStyles.AlternateRowColor);
            }
        }

        private void DrawNameCell(Rect cellRect, TableRow row, int rowIndex)
        {
            HandleNameCellInput(cellRect, row, rowIndex);

            var iconRect = new Rect(cellRect.x, cellRect.y + (cellRect.height - IconSize) * 0.5f, IconSize, IconSize);
            if (Event.current.type == EventType.Repaint)
            {
                Texture icon = AssetPreview.GetMiniThumbnail(row.Asset);
                if (icon != null)
                {
                    GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                }
            }

            var labelRect = new Rect(iconRect.xMax + IconSpacing, cellRect.y, cellRect.width - IconSize - IconSpacing, cellRect.height);
            EditorGUI.LabelField(labelRect, row.DisplayName);
        }

        private void HandleNameCellInput(Rect cellRect, TableRow row, int rowIndex)
        {
            Event evt = Event.current;
            if (evt.type != EventType.MouseDown || !cellRect.Contains(evt.mousePosition))
            {
                return;
            }

            if (evt.button == 0)
            {
                RowSelection.HandleLeftClick(row, rowIndex, evt);
                if (evt.clickCount == 2)
                {
                    RowActions.PingAndSelect(row.Asset);
                }

                evt.Use();
            }
            else if (evt.button == 1)
            {
                RowSelection.HandleRightClick(row, rowIndex);
                RowActions.ShowContextMenu(RowSelection.GetSelectedAssets());
                evt.Use();
            }
        }

        private void DrawDataCell(Rect cellRect, TableRow row, int dataColumnIndex)
        {
            SerializedProperty property = row.GetProperty(dataColumnIndex);
            if (property == null)
            {
                EditorGUI.LabelField(cellRect, SOTableStyles.MissingValueLabel, EditorStyles.centeredGreyMiniLabel);
                return;
            }

            ICellDrawer drawer = CellDrawerRegistry.Resolve(property);

            EditorGUI.BeginChangeCheck();
            drawer.Draw(cellRect, property);

            // ApplyModifiedProperties returns false when nothing was modified (e.g. a button click),
            // which prevents copying untouched values to the other selected rows.
            if (EditorGUI.EndChangeCheck() && row.SerializedObject.ApplyModifiedProperties())
            {
                PropagateToSelection(row, property, dataColumnIndex);
            }
        }

        #endregion

        #region Multi-row Editing

        private void PropagateToSelection(TableRow sourceRow, SerializedProperty sourceProperty, int dataColumnIndex)
        {
            if (RowSelection.Count < 2 || !RowSelection.Contains(sourceRow))
            {
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Edit " + RowSelection.Count + " assets");

            foreach (TableRow targetRow in RowSelection)
            {
                if (targetRow == sourceRow || !targetRow.IsValid || targetRow.GetProperty(dataColumnIndex) == null)
                {
                    continue;
                }

                targetRow.SerializedObject.UpdateIfRequiredOrScript();
                targetRow.SerializedObject.CopyFromSerializedProperty(sourceProperty);
                targetRow.SerializedObject.ApplyModifiedProperties();
            }

            // One Ctrl+Z reverts the whole multi-row edit.
            Undo.CollapseUndoOperations(undoGroup);
        }

        #endregion
    }
}
