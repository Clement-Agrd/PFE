// FICHIER GÉNÉRÉ par gen/build_presets.py depuis presets.json : ne pas éditer à la main.
using UnityEngine;

namespace ZoneFx
{
    public enum ZoneFxPreset
    {
        Blue,
        Gold,
        Purple,
        Cyan,
        Broken,
        Green
    }

    /// <summary>Couleur de l'anneau, couleur du trait (plus claire), tirets (0 = anneau continu).</summary>
    public readonly struct ZonePresetData
    {
        public readonly Color Color; public readonly Color Core; public readonly float DashCount, DashRatio, DashSpeed;
        public ZonePresetData(Color color, Color core, float dashCount, float dashRatio, float dashSpeed) { Color = color; Core = core; DashCount = dashCount; DashRatio = dashRatio; DashSpeed = dashSpeed; }
    }

    public static class ZoneFxPresets
    {
        private static readonly ZonePresetData[] Table =
        {
            new ZonePresetData(new Color(0.2f, 0.55f, 1f, 1f), new Color(0.64f, 0.798f, 1f, 1f), 0f, 0.8f, 0f),
            new ZonePresetData(new Color(1f, 0.72f, 0.18f, 1f), new Color(1f, 0.874f, 0.631f, 1f), 0f, 0.8f, 0f),
            new ZonePresetData(new Color(0.62f, 0.32f, 1f, 1f), new Color(0.829f, 0.694f, 1f, 1f), 0f, 0.8f, 0f),
            new ZonePresetData(new Color(0.15f, 0.9f, 1f, 1f), new Color(0.618f, 0.955f, 1f, 1f), 0f, 0.8f, 0f),
            new ZonePresetData(new Color(0.5f, 0.45f, 1f, 1f), new Color(0.775f, 0.753f, 1f, 1f), 7f, 0.78f, 0.04f),
            new ZonePresetData(new Color(0.4f, 1f, 0.25f, 1f), new Color(0.73f, 1f, 0.663f, 1f), 0f, 0.8f, 0f),
        };

        public static ZonePresetData Get(ZoneFxPreset preset) { return Table[(int)preset]; }
    }
}
