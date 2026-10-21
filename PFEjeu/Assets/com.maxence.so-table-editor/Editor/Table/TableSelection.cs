using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maxence.SOTable.Editor
{
    /// <summary>
    /// Row selection of the table: click, Ctrl/Cmd+click toggle, Shift+click range.
    /// Mirrors the selected assets in Unity's Selection so the Inspector follows the table.
    /// </summary>
    internal sealed class TableSelection
    {
        private readonly TableModel _model;
        private readonly HashSet<TableRow> _rows = new HashSet<TableRow>();
        private readonly List<TableRow> _scratch = new List<TableRow>();

        /// <summary>Index in VisibleRows used as the start of a Shift+click range, -1 when unknown.</summary>
        private int _anchorIndex = -1;

        public event Action Changed;

        public int Count => _rows.Count;

        /// <summary>Allocation-free enumeration (HashSet enumerator is a struct).</summary>
        public HashSet<TableRow>.Enumerator GetEnumerator() => _rows.GetEnumerator();

        public TableSelection(TableModel model)
        {
            _model = model;
        }

        public bool Contains(TableRow row)
        {
            return _rows.Contains(row);
        }

        /// <summary>Call when row order or visibility changes.</summary>
        public void ResetAnchor()
        {
            _anchorIndex = -1;
        }

        #region Click Handling

        public void HandleLeftClick(TableRow row, int rowIndex, Event evt)
        {
            if (evt.shift && _anchorIndex >= 0)
            {
                SelectRange(_anchorIndex, rowIndex);
            }
            else if (EditorGUI.actionKey)
            {
                if (!_rows.Remove(row))
                {
                    _rows.Add(row);
                }

                _anchorIndex = rowIndex;
            }
            else
            {
                SelectSingle(row, rowIndex);
            }

            NotifyChanged();
        }

        /// <summary>Right-click keeps a multi-selection if the clicked row is part of it.</summary>
        public void HandleRightClick(TableRow row, int rowIndex)
        {
            if (_rows.Contains(row))
            {
                return;
            }

            SelectSingle(row, rowIndex);
            NotifyChanged();
        }

        private void SelectSingle(TableRow row, int rowIndex)
        {
            _rows.Clear();
            _rows.Add(row);
            _anchorIndex = rowIndex;
        }

        private void SelectRange(int fromIndex, int toIndex)
        {
            IReadOnlyList<TableRow> visibleRows = _model.VisibleRows;
            if (visibleRows.Count == 0)
            {
                return;
            }

            int lastIndex = visibleRows.Count - 1;
            int start = Mathf.Clamp(Mathf.Min(fromIndex, toIndex), 0, lastIndex);
            int end = Mathf.Clamp(Mathf.Max(fromIndex, toIndex), 0, lastIndex);

            _rows.Clear();
            for (int i = start; i <= end; i++)
            {
                _rows.Add(visibleRows[i]);
            }
        }

        #endregion

        #region Assets

        public Object[] GetSelectedAssets()
        {
            var assets = new List<Object>(_rows.Count);
            foreach (TableRow row in _rows)
            {
                if (row.IsValid)
                {
                    assets.Add(row.Asset);
                }
            }

            return assets.ToArray();
        }

        /// <summary>Re-selects rows by asset after the model reloaded (row objects are recreated).</summary>
        public void SelectAssets(HashSet<Object> assets)
        {
            _rows.Clear();
            _anchorIndex = -1;

            if (assets == null || assets.Count == 0)
            {
                return;
            }

            IReadOnlyList<TableRow> allRows = _model.AllRows;
            for (int i = 0; i < allRows.Count; i++)
            {
                TableRow row = allRows[i];
                if (row.IsValid && assets.Contains(row.Asset))
                {
                    _rows.Add(row);
                }
            }
        }

        /// <summary>Drops selected rows hidden by the filter, so edits never reach invisible rows.</summary>
        public void RemoveHiddenRows()
        {
            _anchorIndex = -1;
            if (_rows.Count == 0)
            {
                return;
            }

            _scratch.Clear();
            IReadOnlyList<TableRow> visibleRows = _model.VisibleRows;
            for (int i = 0; i < visibleRows.Count; i++)
            {
                if (_rows.Contains(visibleRows[i]))
                {
                    _scratch.Add(visibleRows[i]);
                }
            }

            if (_scratch.Count == _rows.Count)
            {
                return;
            }

            _rows.Clear();
            _rows.UnionWith(_scratch);
            NotifyChanged();
        }

        #endregion

        private void NotifyChanged()
        {
            Selection.objects = GetSelectedAssets();
            Changed?.Invoke();
        }
    }
}
