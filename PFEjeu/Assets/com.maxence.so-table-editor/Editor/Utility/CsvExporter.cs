using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;

namespace Maxence.SOTable.Editor
{
    /// <summary>
    /// Writes the visible rows and columns to a CSV file (RFC 4180, UTF-8 with BOM so Excel
    /// detects accents). Values are culture-invariant: decimals always use a dot.
    /// </summary>
    internal static class CsvExporter
    {
        private const char Separator = ',';
        private const char Quote = '"';
        private const int InitialCapacity = 4096;

        private static readonly char[] CharactersRequiringQuotes = { Separator, Quote, '\n', '\r' };

        /// <param name="dataColumns">Columns to export, in display order. <see cref="TableModel.NameColumnIndex"/> is the asset name.</param>
        public static void Export(string filePath, TableModel model, IReadOnlyList<int> dataColumns)
        {
            var builder = new StringBuilder(InitialCapacity);

            AppendHeader(builder, model, dataColumns);

            IReadOnlyList<TableRow> rows = model.VisibleRows;
            for (int r = 0; r < rows.Count; r++)
            {
                TableRow row = rows[r];
                if (!row.IsValid)
                {
                    continue;
                }

                row.SerializedObject.UpdateIfRequiredOrScript();

                for (int c = 0; c < dataColumns.Count; c++)
                {
                    if (c > 0)
                    {
                        builder.Append(Separator);
                    }

                    AppendField(builder, GetCellText(row, dataColumns[c]));
                }

                builder.AppendLine();
            }

            File.WriteAllText(filePath, builder.ToString(), new UTF8Encoding(true));
        }

        private static void AppendHeader(StringBuilder builder, TableModel model, IReadOnlyList<int> dataColumns)
        {
            for (int c = 0; c < dataColumns.Count; c++)
            {
                if (c > 0)
                {
                    builder.Append(Separator);
                }

                int dataColumn = dataColumns[c];
                string title = dataColumn == TableModel.NameColumnIndex
                    ? SOTableStyles.NameColumnTitle
                    : model.Columns[dataColumn].DisplayName;

                AppendField(builder, title);
            }

            builder.AppendLine();
        }

        private static string GetCellText(TableRow row, int dataColumn)
        {
            if (dataColumn == TableModel.NameColumnIndex)
            {
                return row.DisplayName;
            }

            SerializedProperty property = row.GetProperty(dataColumn);
            return property != null ? PropertyValueReader.GetDisplayString(property) : string.Empty;
        }

        private static void AppendField(StringBuilder builder, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            if (value.IndexOfAny(CharactersRequiringQuotes) < 0)
            {
                builder.Append(value);
                return;
            }

            builder.Append(Quote)
                .Append(value.Replace("\"", "\"\""))
                .Append(Quote);
        }
    }
}
