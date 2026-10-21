using UnityEditor;
using UnityEngine;

namespace Maxence.SOTable.Editor
{
    /// <summary>
    /// Texts, colors and GUIContents of the tool, created once.
    /// No GUIStyle here: EditorStyles is not always ready when static fields initialise.
    /// </summary>
    internal static class SOTableStyles
    {
        #region Texts

        public const string WindowTitle = "SO Table";
        public const string NameColumnTitle = "Asset";
        public const string MissingValueLabel = "—";
        public const string NoTypeSelectedMessage = "Pick a ScriptableObject type in the toolbar to list all its assets.";
        public const string NoAssetFoundFormat = "No asset of type {0} found under Assets/.";
        public const string StatusFormat = "{0} / {1} assets  ·  {2} selected";
        public const string ExportDialogTitle = "Export visible rows to CSV";

        #endregion

        #region Colors

        public static Color AlternateRowColor => EditorGUIUtility.isProSkin
            ? new Color(1f, 1f, 1f, 0.03f)
            : new Color(0f, 0f, 0f, 0.04f);

        public static Color SelectedRowColor => EditorGUIUtility.isProSkin
            ? new Color(0.17f, 0.36f, 0.53f, 0.85f)
            : new Color(0.23f, 0.45f, 0.69f, 0.45f);

        #endregion

        #region Contents

        public static readonly GUIContent SelectTypeContent = new GUIContent("Select Type…", "Choose which ScriptableObject type to display.");
        public static readonly GUIContent IncludeDerivedContent = new GUIContent("Derived", "Also list assets of types that inherit from the selected type.");
        public static readonly GUIContent CreateAssetContent = new GUIContent("+", "Create a new asset of this type next to the first one.");
        public static readonly GUIContent RefreshContent = new GUIContent("Refresh", "Reload assets from the project.");
        public static readonly GUIContent ExportCsvContent = new GUIContent("Export CSV", "Export the visible rows and columns.");
        public static readonly GUIContent HelpHintContent = new GUIContent(
            "Ctrl/Shift+click names to multi-select · edits apply to all selected rows",
            "Filter examples: goblin   hp>50   rank=boss   isFlying=true   stats.health>=100");

        public static readonly GUIContent PingMenuContent = new GUIContent("Ping in Project");
        public static readonly GUIContent RevealMenuContent = new GUIContent("Reveal in File Browser");
        public static readonly GUIContent DuplicateMenuContent = new GUIContent("Duplicate");

        #endregion
    }
}
