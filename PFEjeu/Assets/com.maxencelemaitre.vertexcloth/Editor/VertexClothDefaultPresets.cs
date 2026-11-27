using System;
using UnityEditor;
using UnityEngine;

namespace VertexCloth.EditorTools
{
    /// <summary>Presets prêts à l'emploi : Robe, Cape, Hair (cheveux / mèches), Banner (drapeau / bannière).</summary>
    public static class VertexClothDefaultPresets
    {
        public const string Folder = "Assets/VertexCloth/Presets";

        [MenuItem("Tools/Vertex Cloth/Create Default Presets")]
        public static void CreateAll()
        {
            VertexClothEditorUtility.EnsureFolder(Folder);

            EnsurePreset("Robe", null, null);

            EnsurePreset("Cape",
                s =>
                {
                    s.gravityScale = 0.6f;
                    s.windInfluence = 2.5f;
                    s.bendStiffness = 0.05f;
                    s.shapeStiffnessAtPins = 0.15f;
                    s.shapeStiffnessAtFree = 0.01f;
                    s.maxTravel = 0.8f;
                },
                p =>
                {
                    p.pinnedUntil = 0.15f;
                    p.freeFrom = 0.7f;
                    p.curve = 1.2f;
                });

            EnsurePreset("Hair",
                s =>
                {
                    s.damping = 4f;
                    s.gravityScale = 0.8f;
                    s.stretchStiffness = 0.98f;
                    s.bendStiffness = 0.35f;
                    s.shapeStiffnessAtPins = 0.4f;
                    s.shapeStiffnessAtFree = 0.08f;
                    s.maxTravel = 0.15f;
                    s.windInfluence = 1.2f;
                },
                p =>
                {
                    p.pinnedUntil = 0.15f;
                    p.freeFrom = 0.6f;
                });

            EnsurePreset("Banner",
                s =>
                {
                    s.damping = 1.5f;
                    s.gravityScale = 0.3f;
                    s.windInfluence = 3.5f;
                    s.gustAmount = 0.6f;
                    s.shapeStiffnessAtPins = 0.1f;
                    s.shapeStiffnessAtFree = 0f;
                    s.maxTravel = 1.2f;
                },
                p =>
                {
                    p.direction = GradientDirection.PositiveX;
                    p.pinnedUntil = 0.05f;
                    p.freeFrom = 0.5f;
                });

            AssetDatabase.SaveAssets();
        }

        /// <summary>Charge un preset par défaut (Robe, Cape, Hair, Banner) en le créant si besoin.</summary>
        public static VertexClothPreset LoadOrCreate(string name)
        {
            CreateAll();
            return AssetDatabase.LoadAssetAtPath<VertexClothPreset>(PresetPath(name));
        }

        private static string PresetPath(string name)
        {
            return Folder + "/Preset_" + name + ".asset";
        }

        private static void EnsurePreset(string name, Action<VertexClothSettings> tuneSettings, Action<VertexClothPreset> tunePreset)
        {
            if (AssetDatabase.LoadAssetAtPath<VertexClothPreset>(PresetPath(name)) != null)
                return;

            string settingsPath = Folder + "/Settings_" + name + ".asset";
            var settings = AssetDatabase.LoadAssetAtPath<VertexClothSettings>(settingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<VertexClothSettings>();
                if (tuneSettings != null)
                    tuneSettings(settings);
                AssetDatabase.CreateAsset(settings, settingsPath);
            }

            var preset = ScriptableObject.CreateInstance<VertexClothPreset>();
            preset.settings = settings;
            if (tunePreset != null)
                tunePreset(preset);
            AssetDatabase.CreateAsset(preset, PresetPath(name));
        }
    }
}
