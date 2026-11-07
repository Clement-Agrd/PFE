using System;
using UnityEditor;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// P5 : harmonisation. Reconstruit les materiaux froids (pack et kit) ; les instances de scene referencent
    /// les memes assets, elles sont donc mises a jour sans regenerer les rochers.
    /// </summary>
    public static class MountainP5Menu
    {
        [MenuItem("Tools/Defense/Mountain/P5 - Harmoniser les materiaux de roche")]
        public static void RebuildMaterials()
        {
            try
            {
                MountainRockProfile profile = MountainRockProfile.LoadOrCreate();
                MountainRockMaterials.Set set = MountainRockMaterials.Build(profile, MountainRockCatalog.Build(profile));
                AssetDatabase.SaveAssets();
                Debug.Log("[Mountain P5] Materiaux de roche harmonises : " + set.byOriginal.Count + " materiaux d'origine.");
            }
            catch (Exception exception)
            {
                Debug.LogError("[Mountain P5] interrompu : " + exception.Message);
            }
        }
    }
}
