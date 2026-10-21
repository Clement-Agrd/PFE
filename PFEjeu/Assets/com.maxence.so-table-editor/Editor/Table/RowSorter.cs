using System;
using System.Collections.Generic;
using UnityEditor;

namespace Maxence.SOTable.Editor
{
    /// <summary>
    /// Stable sort of rows by one column. Each row value is read once into a key,
    /// instead of reading SerializedProperties inside every comparison (n log n reads).
    /// </summary>
    internal static class RowSorter
    {
        private struct SortKey
        {
            public double Number;
            public string Text;
            public bool IsNumeric;
            public bool IsMissing;
            public int OriginalIndex;
        }

        private sealed class SortKeyComparer : IComparer<SortKey>
        {
            private readonly int _direction;

            public SortKeyComparer(bool ascending)
            {
                _direction = ascending ? 1 : -1;
            }

            public int Compare(SortKey a, SortKey b)
            {
                // Rows without the field always go last, whatever the direction.
                if (a.IsMissing != b.IsMissing)
                {
                    return a.IsMissing ? 1 : -1;
                }

                int result = 0;
                if (!a.IsMissing)
                {
                    result = a.IsNumeric && b.IsNumeric
                        ? a.Number.CompareTo(b.Number)
                        : EditorUtility.NaturalCompare(a.Text ?? string.Empty, b.Text ?? string.Empty);
                }

                // Original order breaks ties, which makes Array.Sort stable.
                return result != 0 ? result * _direction : a.OriginalIndex.CompareTo(b.OriginalIndex);
            }
        }

        private static readonly SortKeyComparer AscendingComparer = new SortKeyComparer(true);
        private static readonly SortKeyComparer DescendingComparer = new SortKeyComparer(false);

        /// <param name="dataColumnIndex">Column to sort by, or <see cref="TableModel.NameColumnIndex"/>.</param>
        public static void Sort(List<TableRow> rows, int dataColumnIndex, bool ascending)
        {
            int count = rows.Count;
            if (count < 2)
            {
                return;
            }

            var keys = new SortKey[count];
            TableRow[] items = rows.ToArray();

            for (int i = 0; i < count; i++)
            {
                keys[i] = BuildKey(items[i], dataColumnIndex, i);
            }

            Array.Sort(keys, items, ascending ? AscendingComparer : DescendingComparer);

            rows.Clear();
            rows.AddRange(items);
        }

        private static SortKey BuildKey(TableRow row, int dataColumnIndex, int originalIndex)
        {
            var key = new SortKey { OriginalIndex = originalIndex };

            if (dataColumnIndex == TableModel.NameColumnIndex)
            {
                key.Text = row.DisplayName;
                return key;
            }

            if (!row.IsValid)
            {
                key.IsMissing = true;
                return key;
            }

            row.SerializedObject.UpdateIfRequiredOrScript();
            SerializedProperty property = row.GetProperty(dataColumnIndex);
            if (property == null)
            {
                key.IsMissing = true;
                return key;
            }

            if (PropertyValueReader.TryGetNumber(property, out double number))
            {
                key.IsNumeric = true;
                key.Number = number;
            }
            else
            {
                key.Text = PropertyValueReader.GetDisplayString(property);
            }

            return key;
        }
    }
}
