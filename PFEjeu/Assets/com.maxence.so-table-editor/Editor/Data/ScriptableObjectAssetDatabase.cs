using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Maxence.SOTable.Editor
{
    /// <summary>
    /// All AssetDatabase queries of the tool: which ScriptableObject types exist in the project
    /// and which assets belong to a given type. Only the Assets folder is searched.
    /// </summary>
    internal static class ScriptableObjectAssetDatabase
    {
        private const string ScriptableObjectFilter = "t:ScriptableObject";
        private const string TypeFilterPrefix = "t:";
        private const string UnityEngineAssemblyPrefix = "UnityEngine";
        private const string UnityEditorAssemblyPrefix = "UnityEditor";

        private static readonly string[] SearchFolders = { "Assets" };

        /// <summary>Concrete, non-generic, non built-in ScriptableObject types.</summary>
        public static bool IsEditableType(Type type)
        {
            return type != null
                && typeof(ScriptableObject).IsAssignableFrom(type)
                && !type.IsAbstract
                && !type.IsGenericType
                && !IsBuiltInUnityType(type);
        }

        /// <summary>Types that have at least one asset under Assets/, sorted by name.</summary>
        public static List<Type> FindTypesWithAssets()
        {
            // GetMainAssetTypeAtPath reads the type without loading the asset.
            var foundTypes = new HashSet<Type>();
            string[] guids = AssetDatabase.FindAssets(ScriptableObjectFilter, SearchFolders);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                Type mainType = AssetDatabase.GetMainAssetTypeAtPath(path);
                if (mainType != null)
                {
                    foundTypes.Add(mainType);
                }
            }

            var result = new List<Type>(foundTypes.Count);
            foreach (Type type in foundTypes)
            {
                if (IsEditableType(type))
                {
                    result.Add(type);
                }
            }

            result.Sort(CompareTypesByName);
            return result;
        }

        /// <summary>Loads every asset of <paramref name="type"/> (and optionally its subclasses), sorted by name.</summary>
        public static List<ScriptableObject> LoadAssets(Type type, bool includeDerivedTypes)
        {
            var assets = new List<ScriptableObject>();
            if (type == null)
            {
                return assets;
            }

            string[] guids = AssetDatabase.FindAssets(TypeFilterPrefix + type.Name, SearchFolders);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                Type mainType = AssetDatabase.GetMainAssetTypeAtPath(path);

                // The "t:" filter matches by short name, so same-named types from other namespaces are rejected here.
                bool isMatch = includeDerivedTypes ? type.IsAssignableFrom(mainType) : mainType == type;
                if (mainType == null || !isMatch)
                {
                    continue;
                }

                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset != null)
                {
                    assets.Add(asset);
                }
            }

            assets.Sort(CompareAssetsByName);
            return assets;
        }

        private static bool IsBuiltInUnityType(Type type)
        {
            string assemblyName = type.Assembly.GetName().Name;
            return assemblyName.StartsWith(UnityEngineAssemblyPrefix, StringComparison.Ordinal)
                || assemblyName.StartsWith(UnityEditorAssemblyPrefix, StringComparison.Ordinal);
        }

        private static int CompareTypesByName(Type a, Type b)
        {
            return string.CompareOrdinal(a.Name, b.Name);
        }

        private static int CompareAssetsByName(ScriptableObject a, ScriptableObject b)
        {
            return EditorUtility.NaturalCompare(a.name, b.name);
        }
    }
}
