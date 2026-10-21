using UnityEditor;
using UnityEngine;

namespace Maxence.SOTable.Editor
{
    /// <summary>Unity menu entries that open the SO Table Editor.</summary>
    internal static class SOTableMenuItems
    {
        private const string WindowMenuPath = "Tools/SO Table Editor";
        private const string AssetMenuPath = "Assets/Open in SO Table Editor";
        private const int AssetMenuPriority = 2000;

        [MenuItem(WindowMenuPath)]
        private static void OpenWindow()
        {
            SOTableWindow.Open();
        }

        [MenuItem(AssetMenuPath, false, AssetMenuPriority)]
        private static void OpenFromSelectedAsset()
        {
            SOTableWindow.OpenForType(Selection.activeObject.GetType());
        }

        [MenuItem(AssetMenuPath, true)]
        private static bool ValidateOpenFromSelectedAsset()
        {
            return Selection.activeObject is ScriptableObject asset
                && ScriptableObjectAssetDatabase.IsEditableType(asset.GetType());
        }
    }
}
