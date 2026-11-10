// FICHIER GÉNÉRÉ par gen/build_presets.py depuis presets.json : ne pas éditer à la main.
using UnityEngine;

namespace WaterFx.Editor
{
    public static class WaterFxPresets
    {
        public static readonly WaterPresetSpec[] All =
        {
            new WaterPresetSpec
            {
                Id = "Ocean", PrefabName = "Water_Ocean", Label = "Océan", Size = new Vector2(200f, 200f),
                Floats = new[] { new FloatProp("_Turbidity", 1f), new FloatProp("_WaveScale", 1f), new FloatProp("_WaveSpeed", 1f), new FloatProp("_NormalTilingA", 9f), new FloatProp("_NormalTilingB", 3.4f), new FloatProp("_NormalStrength", 0.55f), new FloatProp("_FlowSpeed", 0.02f), new FloatProp("_FlowPeriod", 3f), new FloatProp("_ReflectionStrength", 1f), new FloatProp("_Roughness", 0.04f), new FloatProp("_SpecularStrength", 1f), new FloatProp("_ReflectionDistortion", 0.03f), new FloatProp("_SssPower", 4f), new FloatProp("_SssDistortion", 0.6f), new FloatProp("_RefractionStrength", 0.04f), new FloatProp("_FoamTiling", 7f), new FloatProp("_FoamShoreWidth", 0.9f), new FloatProp("_FoamCrest", 0.8f), new FloatProp("_FoamAmount", 0.9f), new FloatProp("_EdgeFade", 0.5f) },
                Colors = new[] { new ColorProp("_DeepColor", new Color(0.01f, 0.07f, 0.13f, 1f)), new ColorProp("_SssColor", new Color(0.06f, 0.45f, 0.42f, 1f)), new ColorProp("_FoamColor", new Color(0.95f, 0.97f, 1f, 1f)) },
                Vectors = new[] { new VectorProp("_Absorption", new Vector4(0.3f, 0.07f, 0.035f, 0f)), new VectorProp("_WaveA", new Vector4(1f, 0.25f, 0.16f, 58f)), new VectorProp("_WaveB", new Vector4(0.6f, 0.8f, 0.12f, 31f)), new VectorProp("_WaveC", new Vector4(-0.3f, 1f, 0.09f, 17f)), new VectorProp("_WaveD", new Vector4(0.9f, -0.4f, 0.06f, 8.5f)), new VectorProp("_ScrollA", new Vector4(0.02f, 0.01f, 0f, 0f)), new VectorProp("_ScrollB", new Vector4(-0.015f, 0.02f, 0f, 0f)), new VectorProp("_FlowDirection", new Vector4(1f, 0.3f, 0f, 0f)) }
            },
            new WaterPresetSpec
            {
                Id = "Lagoon", PrefabName = "Water_Lagoon", Label = "Lagon", Size = new Vector2(60f, 60f),
                Floats = new[] { new FloatProp("_Turbidity", 1f), new FloatProp("_WaveScale", 1f), new FloatProp("_WaveSpeed", 1f), new FloatProp("_NormalTilingA", 9f), new FloatProp("_NormalTilingB", 3.4f), new FloatProp("_NormalStrength", 0.5f), new FloatProp("_FlowSpeed", 0.02f), new FloatProp("_FlowPeriod", 3f), new FloatProp("_ReflectionStrength", 1f), new FloatProp("_Roughness", 0.04f), new FloatProp("_SpecularStrength", 1f), new FloatProp("_ReflectionDistortion", 0.03f), new FloatProp("_SssPower", 4f), new FloatProp("_SssDistortion", 0.6f), new FloatProp("_RefractionStrength", 0.06f), new FloatProp("_FoamTiling", 7f), new FloatProp("_FoamShoreWidth", 1.1f), new FloatProp("_FoamCrest", 0.97f), new FloatProp("_FoamAmount", 0.9f), new FloatProp("_EdgeFade", 0.7f) },
                Colors = new[] { new ColorProp("_DeepColor", new Color(0f, 0.3f, 0.32f, 1f)), new ColorProp("_SssColor", new Color(0.1f, 0.55f, 0.5f, 1f)), new ColorProp("_FoamColor", new Color(0.95f, 0.97f, 1f, 1f)) },
                Vectors = new[] { new VectorProp("_Absorption", new Vector4(0.55f, 0.12f, 0.06f, 0f)), new VectorProp("_WaveA", new Vector4(1f, 0.1f, 0.07f, 14f)), new VectorProp("_WaveB", new Vector4(0.5f, 1f, 0.05f, 8f)), new VectorProp("_WaveC", new Vector4(-0.7f, 0.3f, 0.04f, 4.5f)), new VectorProp("_WaveD", new Vector4(0.2f, -1f, 0.03f, 2.6f)), new VectorProp("_ScrollA", new Vector4(0.02f, 0.01f, 0f, 0f)), new VectorProp("_ScrollB", new Vector4(-0.015f, 0.02f, 0f, 0f)), new VectorProp("_FlowDirection", new Vector4(1f, 0.3f, 0f, 0f)) }
            },
            new WaterPresetSpec
            {
                Id = "Lake", PrefabName = "Water_Lake", Label = "Lac", Size = new Vector2(80f, 80f),
                Floats = new[] { new FloatProp("_Turbidity", 1f), new FloatProp("_WaveScale", 1f), new FloatProp("_WaveSpeed", 1f), new FloatProp("_NormalTilingA", 5f), new FloatProp("_NormalTilingB", 2.2f), new FloatProp("_NormalStrength", 0.35f), new FloatProp("_FlowSpeed", 0.02f), new FloatProp("_FlowPeriod", 3f), new FloatProp("_ReflectionStrength", 1f), new FloatProp("_Roughness", 0.02f), new FloatProp("_SpecularStrength", 1f), new FloatProp("_ReflectionDistortion", 0.03f), new FloatProp("_SssPower", 4f), new FloatProp("_SssDistortion", 0.6f), new FloatProp("_RefractionStrength", 0.04f), new FloatProp("_FoamTiling", 7f), new FloatProp("_FoamShoreWidth", 0.35f), new FloatProp("_FoamCrest", 1f), new FloatProp("_FoamAmount", 0.6f), new FloatProp("_EdgeFade", 0.5f) },
                Colors = new[] { new ColorProp("_DeepColor", new Color(0.02f, 0.07f, 0.08f, 1f)), new ColorProp("_SssColor", new Color(0.05f, 0.25f, 0.2f, 1f)), new ColorProp("_FoamColor", new Color(0.95f, 0.97f, 1f, 1f)) },
                Vectors = new[] { new VectorProp("_Absorption", new Vector4(0.4f, 0.14f, 0.1f, 0f)), new VectorProp("_WaveA", new Vector4(1f, 0.3f, 0.04f, 9f)), new VectorProp("_WaveB", new Vector4(0.4f, 1f, 0.03f, 5f)), new VectorProp("_WaveC", new Vector4(-0.8f, 0.2f, 0.02f, 3f)), new VectorProp("_WaveD", new Vector4(0.1f, -1f, 0.015f, 1.8f)), new VectorProp("_ScrollA", new Vector4(0.02f, 0.01f, 0f, 0f)), new VectorProp("_ScrollB", new Vector4(-0.015f, 0.02f, 0f, 0f)), new VectorProp("_FlowDirection", new Vector4(1f, 0.3f, 0f, 0f)) }
            },
            new WaterPresetSpec
            {
                Id = "River", PrefabName = "Water_River", Label = "Rivière", Size = new Vector2(20f, 60f),
                Floats = new[] { new FloatProp("_Turbidity", 1f), new FloatProp("_WaveScale", 1f), new FloatProp("_WaveSpeed", 1f), new FloatProp("_NormalTilingA", 3.2f), new FloatProp("_NormalTilingB", 1.8f), new FloatProp("_NormalStrength", 0.9f), new FloatProp("_FlowSpeed", 0.9f), new FloatProp("_FlowPeriod", 1.6f), new FloatProp("_ReflectionStrength", 1f), new FloatProp("_Roughness", 0.05f), new FloatProp("_SpecularStrength", 1f), new FloatProp("_ReflectionDistortion", 0.03f), new FloatProp("_SssPower", 4f), new FloatProp("_SssDistortion", 0.6f), new FloatProp("_RefractionStrength", 0.05f), new FloatProp("_FoamTiling", 3f), new FloatProp("_FoamShoreWidth", 0.6f), new FloatProp("_FoamCrest", 0.97f), new FloatProp("_FoamAmount", 1f), new FloatProp("_EdgeFade", 0.3f) },
                Colors = new[] { new ColorProp("_DeepColor", new Color(0.05f, 0.1f, 0.08f, 1f)), new ColorProp("_SssColor", new Color(0.06f, 0.45f, 0.42f, 1f)), new ColorProp("_FoamColor", new Color(0.95f, 0.97f, 1f, 1f)) },
                Vectors = new[] { new VectorProp("_Absorption", new Vector4(0.55f, 0.25f, 0.2f, 0f)), new VectorProp("_WaveA", new Vector4(0f, -1f, 0.03f, 2.5f)), new VectorProp("_WaveB", new Vector4(0.2f, -0.9f, 0.02f, 1.4f)), new VectorProp("_WaveC", new Vector4(-0.2f, -1f, 0.015f, 0.9f)), new VectorProp("_WaveD", new Vector4(0.5f, -0.8f, 0.01f, 0.6f)), new VectorProp("_ScrollA", new Vector4(0f, 0f, 0f, 0f)), new VectorProp("_ScrollB", new Vector4(0f, 0f, 0f, 0f)), new VectorProp("_FlowDirection", new Vector4(0f, -1f, 0f, 0f)) }
            },
            new WaterPresetSpec
            {
                Id = "Swamp", PrefabName = "Water_Swamp", Label = "Marais", Size = new Vector2(40f, 40f),
                Floats = new[] { new FloatProp("_Turbidity", 1.6f), new FloatProp("_WaveScale", 1f), new FloatProp("_WaveSpeed", 1f), new FloatProp("_NormalTilingA", 4f), new FloatProp("_NormalTilingB", 1.6f), new FloatProp("_NormalStrength", 0.25f), new FloatProp("_FlowSpeed", 0.02f), new FloatProp("_FlowPeriod", 3f), new FloatProp("_ReflectionStrength", 0.55f), new FloatProp("_Roughness", 0.12f), new FloatProp("_SpecularStrength", 0.4f), new FloatProp("_ReflectionDistortion", 0.03f), new FloatProp("_SssPower", 4f), new FloatProp("_SssDistortion", 0.6f), new FloatProp("_RefractionStrength", 0.02f), new FloatProp("_FoamTiling", 7f), new FloatProp("_FoamShoreWidth", 0.35f), new FloatProp("_FoamCrest", 1f), new FloatProp("_FoamAmount", 0.3f), new FloatProp("_EdgeFade", 0.25f) },
                Colors = new[] { new ColorProp("_DeepColor", new Color(0.07f, 0.1f, 0.035f, 1f)), new ColorProp("_SssColor", new Color(0.15f, 0.25f, 0.05f, 1f)), new ColorProp("_FoamColor", new Color(0.62f, 0.66f, 0.45f, 1f)) },
                Vectors = new[] { new VectorProp("_Absorption", new Vector4(0.9f, 0.45f, 0.75f, 0f)), new VectorProp("_WaveA", new Vector4(1f, 0.4f, 0.015f, 6f)), new VectorProp("_WaveB", new Vector4(0.3f, 1f, 0.01f, 3.2f)), new VectorProp("_WaveC", new Vector4(-0.5f, 0.5f, 0.008f, 1.8f)), new VectorProp("_WaveD", new Vector4(0f, -1f, 0.006f, 1.1f)), new VectorProp("_ScrollA", new Vector4(0.02f, 0.01f, 0f, 0f)), new VectorProp("_ScrollB", new Vector4(-0.015f, 0.02f, 0f, 0f)), new VectorProp("_FlowDirection", new Vector4(1f, 0.3f, 0f, 0f)) }
            },
            new WaterPresetSpec
            {
                Id = "Puddle", PrefabName = "Water_Puddle", Label = "Flaque", Size = new Vector2(6f, 6f),
                Floats = new[] { new FloatProp("_Turbidity", 1f), new FloatProp("_WaveScale", 0f), new FloatProp("_WaveSpeed", 1f), new FloatProp("_NormalTilingA", 1.5f), new FloatProp("_NormalTilingB", 0.8f), new FloatProp("_NormalStrength", 0.12f), new FloatProp("_FlowSpeed", 0f), new FloatProp("_FlowPeriod", 3f), new FloatProp("_ReflectionStrength", 1.8f), new FloatProp("_Roughness", 0.01f), new FloatProp("_SpecularStrength", 1f), new FloatProp("_ReflectionDistortion", 0.012f), new FloatProp("_SssPower", 4f), new FloatProp("_SssDistortion", 0.6f), new FloatProp("_RefractionStrength", 0.012f), new FloatProp("_FoamTiling", 7f), new FloatProp("_FoamShoreWidth", 0.05f), new FloatProp("_FoamCrest", 0.62f), new FloatProp("_FoamAmount", 0f), new FloatProp("_EdgeFade", 0.02f) },
                Colors = new[] { new ColorProp("_DeepColor", new Color(0.015f, 0.02f, 0.025f, 1f)), new ColorProp("_SssColor", new Color(0.06f, 0.45f, 0.42f, 1f)), new ColorProp("_FoamColor", new Color(0.95f, 0.97f, 1f, 1f)) },
                Vectors = new[] { new VectorProp("_Absorption", new Vector4(0.25f, 0.22f, 0.2f, 0f)), new VectorProp("_WaveA", new Vector4(1f, 0.25f, 0.16f, 58f)), new VectorProp("_WaveB", new Vector4(0.6f, 0.8f, 0.12f, 31f)), new VectorProp("_WaveC", new Vector4(-0.3f, 1f, 0.09f, 17f)), new VectorProp("_WaveD", new Vector4(0.9f, -0.4f, 0.06f, 8.5f)), new VectorProp("_ScrollA", new Vector4(0.004f, 0.002f, 0f, 0f)), new VectorProp("_ScrollB", new Vector4(-0.003f, 0.004f, 0f, 0f)), new VectorProp("_FlowDirection", new Vector4(1f, 0.3f, 0f, 0f)) }
            },
        };
    }
}
