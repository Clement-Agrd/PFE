using UnityEditor;

namespace Defense.Relief.EditorTools
{
    /// <summary>Selection du preset d'amplitude (Doux / Actuel) dans la config.</summary>
    public static class DefenseReliefPresetMenu
    {
        private const string PresetProperty = "preset";

        [MenuItem("Tools/Defense/Relief/Preset - Doux")]
        public static void SelectSoft() => Select(DefenseReliefConfig.ReliefPreset.Soft);

        [MenuItem("Tools/Defense/Relief/Preset - Doux modifie (plaine plate, base en hauteur)")]
        public static void SelectSoftInverted() => Select(DefenseReliefConfig.ReliefPreset.SoftInverted);

        [MenuItem("Tools/Defense/Relief/Preset - Actuel")]
        public static void SelectCurrent() => Select(DefenseReliefConfig.ReliefPreset.Current);

        private static void Select(DefenseReliefConfig.ReliefPreset preset)
        {
            DefenseReliefConfig config = DefenseReliefConfig.LoadOrCreate();
            var so = new SerializedObject(config);
            so.FindProperty(PresetProperty).enumValueIndex = (int)preset;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }
    }
}
