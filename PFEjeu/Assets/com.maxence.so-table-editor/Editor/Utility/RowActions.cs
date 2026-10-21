using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maxence.SOTable.Editor
{
    /// <summary>Asset operations triggered from the table: ping, context menu, duplicate, create.</summary>
    internal static class RowActions
    {
        private const string DefaultFolder = "Assets";
        private const string AssetExtension = ".asset";

        public static void PingAndSelect(Object asset)
        {
            if (asset == null)
            {
                return;
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        public static void ShowContextMenu(Object[] assets)
        {
            if (assets == null || assets.Length == 0)
            {
                return;
            }

            Object firstAsset = assets[0];
            var menu = new GenericMenu();

            menu.AddItem(SOTableStyles.PingMenuContent, false, () => PingAndSelect(firstAsset));
            menu.AddItem(SOTableStyles.RevealMenuContent, false, () => EditorUtility.RevealInFinder(AssetDatabase.GetAssetPath(firstAsset)));
            menu.AddSeparator(string.Empty);

            GUIContent duplicateContent = assets.Length > 1
                ? new GUIContent("Duplicate " + assets.Length + " Assets")
                : SOTableStyles.DuplicateMenuContent;
            menu.AddItem(duplicateContent, false, () => DuplicateAssets(assets));

            menu.ShowAsContext();
        }

        public static void DuplicateAssets(Object[] assets)
        {
            for (int i = 0; i < assets.Length; i++)
            {
                string path = AssetDatabase.GetAssetPath(assets[i]);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                string newPath = AssetDatabase.GenerateUniqueAssetPath(path);
                AssetDatabase.CopyAsset(path, newPath);
            }
        }

        /// <summary>Creates "New {Type}.asset" in <paramref name="folder"/> (or Assets/) and selects it.</summary>
        public static ScriptableObject CreateAsset(Type type, string folder)
        {
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
            {
                folder = DefaultFolder;
            }

            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/New " + type.Name + AssetExtension);
            ScriptableObject instance = ScriptableObject.CreateInstance(type);
            AssetDatabase.CreateAsset(instance, path);
            PingAndSelect(instance);
            return instance;
        }

        /// <summary>Folder of an asset with forward slashes, e.g. "Assets/Data/Enemies".</summary>
        public static string GetAssetFolder(Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path))
            {
                return DefaultFolder;
            }

            string folder = Path.GetDirectoryName(path);
            return string.IsNullOrEmpty(folder) ? DefaultFolder : folder.Replace('\\', '/');
        }
    }
}
