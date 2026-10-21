using UnityEditor;

namespace Maxence.SOTable.Editor
{
    /// <summary>
    /// Immutable description of one data column: which serialized field it shows and how.
    /// The asset name column is not a ColumnDefinition, see <see cref="TableModel.NameColumnIndex"/>.
    /// </summary>
    internal sealed class ColumnDefinition
    {
        /// <summary>Path passed to SerializedObject.FindProperty, e.g. "_stats.health".</summary>
        public string PropertyPath { get; }

        /// <summary>Header text, e.g. "Stats / Health".</summary>
        public string DisplayName { get; }

        public SerializedPropertyType PropertyType { get; }

        /// <summary>True for lists, nested objects and other values edited in the Inspector.</summary>
        public bool IsComplex { get; }

        /// <summary>Lowercase display name without spaces ("stats.health"), matched by the filter.</summary>
        public string SearchKey { get; }

        public ColumnDefinition(string propertyPath, string displayName, SerializedPropertyType propertyType, bool isComplex)
        {
            PropertyPath = propertyPath;
            DisplayName = displayName;
            PropertyType = propertyType;
            IsComplex = isComplex;
            SearchKey = BuildSearchKey(displayName);
        }

        private static string BuildSearchKey(string displayName)
        {
            return displayName
                .Replace(" ", string.Empty)
                .Replace('/', '.')
                .ToLowerInvariant();
        }
    }
}
