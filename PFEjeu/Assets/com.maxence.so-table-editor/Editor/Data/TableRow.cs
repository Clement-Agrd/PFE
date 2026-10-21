using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Maxence.SOTable.Editor
{
    /// <summary>
    /// One asset displayed as a table row. Owns its SerializedObject and caches
    /// one SerializedProperty per column so drawing never calls FindProperty.
    /// </summary>
    internal sealed class TableRow : IDisposable
    {
        private readonly SerializedProperty[] _properties;

        public ScriptableObject Asset { get; }
        public SerializedObject SerializedObject { get; }

        /// <summary>Cached because Object.name allocates a new string on every call.</summary>
        public string DisplayName { get; }

        public bool IsValid => Asset != null;

        public TableRow(ScriptableObject asset, IReadOnlyList<ColumnDefinition> columns)
        {
            Asset = asset;
            DisplayName = asset.name;
            SerializedObject = new SerializedObject(asset);

            _properties = new SerializedProperty[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                // Null when a derived asset does not expose this field (drawn as an empty cell).
                _properties[i] = SerializedObject.FindProperty(columns[i].PropertyPath);
            }
        }

        /// <summary>Returns the cached property of a data column, or null if the row has no such field.</summary>
        public SerializedProperty GetProperty(int dataColumnIndex)
        {
            return (uint)dataColumnIndex < (uint)_properties.Length ? _properties[dataColumnIndex] : null;
        }

        public void Dispose()
        {
            SerializedObject.Dispose();
        }
    }
}
