using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;

namespace Maxence.SOTable.Editor
{
    /// <summary>
    /// Parses and evaluates the search query. Terms are separated by spaces and must all match:
    /// "goblin" matches the asset name, "hp>50" / "rank=boss" / "isFlying=true" test a field.
    /// Supported operators: = == != &gt; &gt;= &lt; &lt;=
    /// </summary>
    internal sealed class RowFilter
    {
        private enum ComparisonOperator
        {
            Equal,
            NotEqual,
            Greater,
            GreaterOrEqual,
            Less,
            LessOrEqual
        }

        private readonly struct Condition
        {
            public readonly int ColumnIndex;
            public readonly ComparisonOperator Operator;
            public readonly string Text;
            public readonly double Number;
            public readonly bool IsNumeric;

            public Condition(int columnIndex, ComparisonOperator comparisonOperator, string text, double number, bool isNumeric)
            {
                ColumnIndex = columnIndex;
                Operator = comparisonOperator;
                Text = text;
                Number = number;
                IsNumeric = isNumeric;
            }
        }

        /// <summary>Tolerance for "=" on floats, so "speed=0.1" matches a float stored as 0.100000001.</summary>
        private const double NumberTolerance = 1e-4;

        private static readonly char[] OperatorCharacters = { '>', '<', '=', '!' };
        private static readonly char[] TermSeparators = { ' ', '\t' };

        private readonly List<string> _nameTerms = new List<string>();
        private readonly List<Condition> _conditions = new List<Condition>();

        public bool IsEmpty => _nameTerms.Count == 0 && _conditions.Count == 0;

        #region Parsing

        public void Parse(string query, IReadOnlyList<ColumnDefinition> columns)
        {
            _nameTerms.Clear();
            _conditions.Clear();

            if (string.IsNullOrWhiteSpace(query))
            {
                return;
            }

            string[] tokens = query.Split(TermSeparators, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (TryParseCondition(token, columns, out Condition condition))
                {
                    _conditions.Add(condition);
                }
                else
                {
                    // Anything that is not a valid "field op value" is searched in the asset name.
                    _nameTerms.Add(token);
                }
            }
        }

        private static bool TryParseCondition(string token, IReadOnlyList<ColumnDefinition> columns, out Condition condition)
        {
            condition = default;

            int operatorIndex = token.IndexOfAny(OperatorCharacters);
            if (operatorIndex <= 0)
            {
                return false;
            }

            bool isTwoCharOperator = operatorIndex + 1 < token.Length && token[operatorIndex + 1] == '=';
            int operatorLength = isTwoCharOperator ? 2 : 1;

            if (!TryParseOperator(token.Substring(operatorIndex, operatorLength), out ComparisonOperator comparisonOperator))
            {
                return false;
            }

            string value = token.Substring(operatorIndex + operatorLength);
            if (value.Length == 0)
            {
                return false;
            }

            int columnIndex = FindColumnIndex(token.Substring(0, operatorIndex), columns);
            if (columnIndex < 0)
            {
                return false;
            }

            bool isNumeric = TryParseNumber(value, out double number);
            condition = new Condition(columnIndex, comparisonOperator, value, number, isNumeric);
            return true;
        }

        private static bool TryParseOperator(string text, out ComparisonOperator comparisonOperator)
        {
            switch (text)
            {
                case "=":
                case "==":
                    comparisonOperator = ComparisonOperator.Equal;
                    return true;
                case "!=":
                    comparisonOperator = ComparisonOperator.NotEqual;
                    return true;
                case ">":
                    comparisonOperator = ComparisonOperator.Greater;
                    return true;
                case ">=":
                    comparisonOperator = ComparisonOperator.GreaterOrEqual;
                    return true;
                case "<":
                    comparisonOperator = ComparisonOperator.Less;
                    return true;
                case "<=":
                    comparisonOperator = ComparisonOperator.LessOrEqual;
                    return true;
                default:
                    comparisonOperator = ComparisonOperator.Equal;
                    return false;
            }
        }

        private static int FindColumnIndex(string field, IReadOnlyList<ColumnDefinition> columns)
        {
            string searchKey = field.ToLowerInvariant();
            for (int i = 0; i < columns.Count; i++)
            {
                ColumnDefinition column = columns[i];
                if (column.SearchKey == searchKey
                    || string.Equals(column.PropertyPath, field, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool TryParseNumber(string text, out double number)
        {
            if (bool.TryParse(text, out bool boolean))
            {
                number = boolean ? 1d : 0d;
                return true;
            }

            // Accept "2,5" as well as "2.5" (French keyboards).
            return double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }

        #endregion

        #region Evaluation

        public bool Matches(TableRow row)
        {
            if (!row.IsValid)
            {
                return false;
            }

            for (int i = 0; i < _nameTerms.Count; i++)
            {
                if (row.DisplayName.IndexOf(_nameTerms[i], StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }

            if (_conditions.Count == 0)
            {
                return true;
            }

            row.SerializedObject.UpdateIfRequiredOrScript();
            for (int i = 0; i < _conditions.Count; i++)
            {
                if (!Evaluate(row, _conditions[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Evaluate(TableRow row, Condition condition)
        {
            SerializedProperty property = row.GetProperty(condition.ColumnIndex);
            if (property == null)
            {
                return false;
            }

            if (condition.IsNumeric && PropertyValueReader.TryGetNumber(property, out double value))
            {
                return CompareNumbers(value, condition.Number, condition.Operator);
            }

            return CompareText(PropertyValueReader.GetDisplayString(property), condition.Text, condition.Operator);
        }

        private static bool CompareNumbers(double value, double target, ComparisonOperator comparisonOperator)
        {
            switch (comparisonOperator)
            {
                case ComparisonOperator.Equal: return Math.Abs(value - target) <= NumberTolerance;
                case ComparisonOperator.NotEqual: return Math.Abs(value - target) > NumberTolerance;
                case ComparisonOperator.Greater: return value > target;
                case ComparisonOperator.GreaterOrEqual: return value >= target;
                case ComparisonOperator.Less: return value < target;
                case ComparisonOperator.LessOrEqual: return value <= target;
                default: return false;
            }
        }

        private static bool CompareText(string value, string target, ComparisonOperator comparisonOperator)
        {
            value = value ?? string.Empty;

            switch (comparisonOperator)
            {
                case ComparisonOperator.Equal:
                    return value.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0;
                case ComparisonOperator.NotEqual:
                    return value.IndexOf(target, StringComparison.OrdinalIgnoreCase) < 0;
            }

            int comparison = string.Compare(value, target, StringComparison.OrdinalIgnoreCase);
            switch (comparisonOperator)
            {
                case ComparisonOperator.Greater: return comparison > 0;
                case ComparisonOperator.GreaterOrEqual: return comparison >= 0;
                case ComparisonOperator.Less: return comparison < 0;
                case ComparisonOperator.LessOrEqual: return comparison <= 0;
                default: return false;
            }
        }

        #endregion
    }
}
