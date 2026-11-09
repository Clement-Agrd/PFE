#if UNITY_EDITOR
// GÉNÉRÉ par gen/build_defaults.py depuis presets.json — modifiez presets.json puis relancez le script,
// ou ajustez directement les assets SkyPreset dans l'Inspector.
using UnityEngine;

namespace LayeredSky.EditorTools
{
    /// <summary>Valeurs par défaut des 3 ambiances (couleurs écrites en LINÉAIRE puis converties pour l'Inspector).</summary>
    public static class LayeredSkyDefaults
    {
        private static Color Lin(float r, float g, float b) => new Color(r, g, b, 1f).gamma;

        public static SkyState Jour() => new SkyState
        {
                zenithColor = Lin(0.06f, 0.2f, 0.62f),
                midColor = Lin(0.14f, 0.34f, 0.8f),
                horizonColor = Lin(0.55f, 0.72f, 0.95f),
                groundColor = Lin(0.1f, 0.12f, 0.14f),
                sunAzimuth = 0f,
                sunElevation = 36f,
                sunColor = Lin(1.0f, 0.96f, 0.85f),
                sunRadiance = 60f,
                sunSize = 0.62f,
                sunGlow = 0.15f,
                starIntensity = 0.0f,
                twinkle = 0.3f,
                starRotation = 0f,
                moonVisibility = 0.8f,
                moonRadiance = 0.9f,
                moon1Azimuth = 190f,
                moon1Elevation = 24f,
                moon1Radius = 3.6f,
                moon1Color = Lin(0.95f, 0.95f, 0.91f),
                cloudCoverage = 0.5f,
                cloudSpeed = 0.02f,
                cloudShadowColor = Lin(0.3f, 0.36f, 0.5f),
                cloudLitColor = Lin(1.0f, 0.97f, 0.92f),
                cloudLitGain = 0.8f,
                auroraIntensity = 0.0f,
                auroraColorA = Lin(0.1f, 1.0f, 0.38f),
                auroraColorB = Lin(0.9f, 0.22f, 0.85f),
                auroraSpeed = 0.15f,
                lightAzimuth = 0f,
                lightElevation = 36f,
                lightColor = Lin(1.0f, 0.96f, 0.88f),
                lightIntensity = 1.2f,
                ambientIntensity = 1.1f,
                fogColor = Lin(0.55f, 0.7f, 0.9f),
                fogDensity = 0.002f,
                exposure = 1.0f,
                meteorIntensity = 0.0f
        };

        public static SkyState Crepuscule() => new SkyState
        {
                zenithColor = Lin(0.004f, 0.006f, 0.045f),
                midColor = Lin(0.06f, 0.025f, 0.165f),
                horizonColor = Lin(0.9f, 0.22f, 0.26f),
                groundColor = Lin(0.005f, 0.004f, 0.018f),
                sunAzimuth = 0f,
                sunElevation = 2f,
                sunColor = Lin(1.0f, 0.78f, 0.5f),
                sunRadiance = 60f,
                sunSize = 0.62f,
                sunGlow = 1.0f,
                starIntensity = 1.0f,
                twinkle = 0.35f,
                starRotation = 0f,
                moonVisibility = 1.0f,
                moonRadiance = 1.0f,
                moon1Azimuth = 185f,
                moon1Elevation = 16f,
                moon1Radius = 3.6f,
                moon1Color = Lin(0.95f, 0.95f, 0.91f),
                cloudCoverage = 0.55f,
                cloudSpeed = 0.015f,
                cloudShadowColor = Lin(0.055f, 0.032f, 0.14f),
                cloudLitColor = Lin(1.0f, 0.46f, 0.26f),
                cloudLitGain = 1.0f,
                auroraIntensity = 0.5f,
                auroraColorA = Lin(0.1f, 1.0f, 0.38f),
                auroraColorB = Lin(0.9f, 0.22f, 0.85f),
                auroraSpeed = 0.15f,
                lightAzimuth = 0f,
                lightElevation = 8f,
                lightColor = Lin(1.0f, 0.66f, 0.4f),
                lightIntensity = 1.4f,
                ambientIntensity = 1.0f,
                fogColor = Lin(0.28f, 0.09f, 0.16f),
                fogDensity = 0.004f,
                exposure = 1.0f,
                meteorIntensity = 0.4f
        };

        public static SkyState Nuit() => new SkyState
        {
                zenithColor = Lin(0.002f, 0.003f, 0.03f),
                midColor = Lin(0.02f, 0.015f, 0.09f),
                horizonColor = Lin(0.1f, 0.07f, 0.26f),
                groundColor = Lin(0.003f, 0.003f, 0.014f),
                sunAzimuth = 0f,
                sunElevation = -28f,
                sunColor = Lin(1.0f, 0.78f, 0.5f),
                sunRadiance = 60f,
                sunSize = 0.62f,
                sunGlow = 1.0f,
                starIntensity = 1.2f,
                twinkle = 0.5f,
                starRotation = 0f,
                moonVisibility = 1.0f,
                moonRadiance = 1.0f,
                moon1Azimuth = 170f,
                moon1Elevation = 40f,
                moon1Radius = 3.6f,
                moon1Color = Lin(0.95f, 0.95f, 0.91f),
                cloudCoverage = 0.35f,
                cloudSpeed = 0.012f,
                cloudShadowColor = Lin(0.02f, 0.022f, 0.07f),
                cloudLitColor = Lin(0.4f, 0.55f, 1.0f),
                cloudLitGain = 0.9f,
                auroraIntensity = 1.5f,
                auroraColorA = Lin(0.1f, 1.0f, 0.38f),
                auroraColorB = Lin(0.9f, 0.22f, 0.85f),
                auroraSpeed = 0.18f,
                lightAzimuth = 170f,
                lightElevation = 40f,
                lightColor = Lin(0.45f, 0.58f, 1.0f),
                lightIntensity = 0.45f,
                ambientIntensity = 0.8f,
                fogColor = Lin(0.03f, 0.04f, 0.11f),
                fogDensity = 0.006f,
                exposure = 1.0f,
                meteorIntensity = 1.0f
        };
    }
}
#endif
