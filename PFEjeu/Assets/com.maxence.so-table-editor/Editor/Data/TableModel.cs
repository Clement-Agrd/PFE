using System;
using System.Collections.Generic;
using UnityEngine;

namespace Maxence.SOTable.Editor
{
    /// <summary>
    /// Data side of the table: columns, all rows, and the sorted + filtered rows the view displays.
    /// Knows nothing about drawing.
    /// </summary>
    internal sealed class TableModel : IDisposable
    {
        /// <summary>Data column index used for the asset name column.</summary>
        public const int NameColumnIndex = -1;

        #region Fields

        private readonly List<TableRow> _allRows = new List<TableRow>();
        private readonly List<TableRow> _visibleRows = new List<TableRow>();
        private readonly RowFilter _filter = new RowFilter();

        private List<ColumnDefinition> _columns = new List<ColumnDefinition>();
        private string _filterQuery = string.Empty;
        private bool _hasSort;
        private int _sortColumnIndex = NameColumnIndex;
        private bool _sortAscending = true;

        #endregion

        #region Properties

        public Type RowType { get; private set; }
        public IReadOnlyList<ColumnDefinition> Columns => _columns;
        public IReadOnlyList<TableRow> AllRows => _allRows;
        public IReadOnlyList<TableRow> VisibleRows => _visibleRows;

        #endregion

        #region Loading

        /// <summary>Rebuilds columns and rows for <paramref name="type"/>. Keeps the current filter and sort.</summary>
        public void Load(Type type, bool includeDerivedTypes)
        {
            DisposeRows();

            RowType = type;
            _columns = ColumnSchemaBuilder.Build(type);

            List<ScriptableObject> assets = ScriptableObjectAssetDatabase.LoadAssets(type, includeDerivedTypes);
            for (int i = 0; i < assets.Count; i++)
            {
                _allRows.Add(new TableRow(assets[i], _columns));
            }

            if (_hasSort && _sortColumnIndex >= _columns.Count)
            {
                ClearSort();
            }

            _filter.Parse(_filterQuery, _columns);
            ApplySortAndFilter();
        }

        #endregion

        #region Filter & Sort

        public void SetFilter(string query)
        {
            _filterQuery = query ?? string.Empty;
            _filter.Parse(_filterQuery, _columns);
            RebuildVisibleRows();
        }

        public void SetSort(int dataColumnIndex, bool ascending)
        {
            _hasSort = true;
            _sortColumnIndex = dataColumnIndex;
            _sortAscending = ascending;
            ApplySortAndFilter();
        }

        public void ClearSort()
        {
            _hasSort = false;
            _sortColumnIndex = NameColumnIndex;
            _sortAscending = true;
        }

        private void ApplySortAndFilter()
        {
            if (_hasSort)
            {
                RowSorter.Sort(_allRows, _sortColumnIndex, _sortAscending);
            }

            RebuildVisibleRows();
        }

        private void RebuildVisibleRows()
        {
            _visibleRows.Clear();
            bool acceptAll = _filter.IsEmpty;

            for (int i = 0; i < _allRows.Count; i++)
            {
                TableRow row = _allRows[i];
                if (row.IsValid && (acceptAll || _filter.Matches(row)))
                {
                    _visibleRows.Add(row);
                }
            }
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            DisposeRows();
            _columns = new List<ColumnDefinition>();
            RowType = null;
        }

        private void DisposeRows()
        {
            for (int i = 0; i < _allRows.Count; i++)
            {
                _allRows[i].Dispose();
            }

            _allRows.Clear();
            _visibleRows.Clear();
        }

        #endregion
    }
}
