using System;
using UnityEngine;

namespace LayeredSky
{
    /// <summary>
    /// État complet du ciel à un instant donné (couleurs, soleil, lune, nuages, aurores, lumière, brouillard).
    /// Structure de DONNÉES pure : interpolable (Lerp), sans dépendance à la scène.
    /// Les champs sont publics car c'est un conteneur de données sérialisé (initialisation d'objet + Inspector).
    /// Couleurs : saisies dans l'Inspector, converties en linéaire par SkyMaterialApplier.
    /// </summary>
    [Serializable]
    public struct SkyState
    {
        [Header("Dégradé du ciel")]
        [ColorUsage(false, true)] public Color zenithColor;
        [ColorUsage(false, true)] public Color midColor;
        [ColorUsage(false, true)] public Color horizonColor;
        [ColorUsage(false, true)] public Color groundColor;

        [Header("Soleil")]
        [Range(-180f, 180f)] public float sunAzimuth;
        [Range(-90f, 90f)] public float sunElevation;
        [ColorUsage(false, true)] public Color sunColor;
        [Min(0f)] public float sunRadiance;
        [Min(0.05f)] public float sunSize;
        [Min(0f)] public float sunGlow;

        [Header("Étoiles / nébuleuse")]
        [Min(0f)] public float starIntensity;
        [Range(0f, 1f)] public float twinkle;
        [Range(0f, 360f)] public float starRotation;

        [Header("Lune")]
        [Range(0f, 1f)] public float moonVisibility;
        [Min(0f)] public float moonRadiance;
        [Range(-180f, 180f)] public float moon1Azimuth;
        [Range(-90f, 90f)] public float moon1Elevation;
        [Min(0.1f)] public float moon1Radius;
        [ColorUsage(false, true)] public Color moon1Color;

        [Header("Étoiles filantes")]
        [Min(0f)] public float meteorIntensity;

        [Header("Nuages")]
        [Range(0f, 1f)] public float cloudCoverage;
        [Min(0f)] public float cloudSpeed;
        [ColorUsage(false, true)] public Color cloudShadowColor;
        [ColorUsage(false, true)] public Color cloudLitColor;
        [Min(0f)] public float cloudLitGain;

        [Header("Aurores")]
        [Min(0f)] public float auroraIntensity;
        [ColorUsage(false, true)] public Color auroraColorA;
        [ColorUsage(false, true)] public Color auroraColorB;
        [Min(0f)] public float auroraSpeed;

        [Header("Lumière principale (Directional Light)")]
        [Range(-180f, 360f)] public float lightAzimuth;
        [Range(-90f, 90f)] public float lightElevation;
        public Color lightColor;
        [Min(0f)] public float lightIntensity;

        [Header("Environnement")]
        [Min(0f)] public float ambientIntensity;
        public Color fogColor;
        [Min(0f)] public float fogDensity;
        [Min(0f)] public float exposure;

        /// <summary>Convention Skybox/Panoramic : (cosEl·cosAz, sinEl, −cosEl·sinAz).</summary>
        public static Vector3 DirectionFromAngles(float azimuthDeg, float elevationDeg)
        {
            float az = azimuthDeg * Mathf.Deg2Rad;
            float el = elevationDeg * Mathf.Deg2Rad;
            float cosEl = Mathf.Cos(el);
            return new Vector3(cosEl * Mathf.Cos(az), Mathf.Sin(el), -cosEl * Mathf.Sin(az));
        }

        public Vector3 SunDirection => DirectionFromAngles(sunAzimuth, sunElevation);
        public Vector3 LightDirection => DirectionFromAngles(lightAzimuth, lightElevation);
        public Vector3 Moon1Direction => DirectionFromAngles(moon1Azimuth, moon1Elevation);

        /// <summary>Interpolation complète (angles via LerpAngle pour prendre le chemin le plus court).</summary>
        public static SkyState Lerp(in SkyState a, in SkyState b, float t)
        {
            t = Mathf.Clamp01(t);
            return new SkyState
            {
                zenithColor = Color.Lerp(a.zenithColor, b.zenithColor, t),
                midColor = Color.Lerp(a.midColor, b.midColor, t),
                horizonColor = Color.Lerp(a.horizonColor, b.horizonColor, t),
                groundColor = Color.Lerp(a.groundColor, b.groundColor, t),

                sunAzimuth = Mathf.LerpAngle(a.sunAzimuth, b.sunAzimuth, t),
                sunElevation = Mathf.Lerp(a.sunElevation, b.sunElevation, t),
                sunColor = Color.Lerp(a.sunColor, b.sunColor, t),
                sunRadiance = Mathf.Lerp(a.sunRadiance, b.sunRadiance, t),
                sunSize = Mathf.Lerp(a.sunSize, b.sunSize, t),
                sunGlow = Mathf.Lerp(a.sunGlow, b.sunGlow, t),

                starIntensity = Mathf.Lerp(a.starIntensity, b.starIntensity, t),
                twinkle = Mathf.Lerp(a.twinkle, b.twinkle, t),
                starRotation = Mathf.LerpAngle(a.starRotation, b.starRotation, t),

                moonVisibility = Mathf.Lerp(a.moonVisibility, b.moonVisibility, t),
                moonRadiance = Mathf.Lerp(a.moonRadiance, b.moonRadiance, t),
                moon1Azimuth = Mathf.LerpAngle(a.moon1Azimuth, b.moon1Azimuth, t),
                moon1Elevation = Mathf.Lerp(a.moon1Elevation, b.moon1Elevation, t),
                moon1Radius = Mathf.Lerp(a.moon1Radius, b.moon1Radius, t),
                moon1Color = Color.Lerp(a.moon1Color, b.moon1Color, t),

                meteorIntensity = Mathf.Lerp(a.meteorIntensity, b.meteorIntensity, t),

                cloudCoverage = Mathf.Lerp(a.cloudCoverage, b.cloudCoverage, t),
                cloudSpeed = Mathf.Lerp(a.cloudSpeed, b.cloudSpeed, t),
                cloudShadowColor = Color.Lerp(a.cloudShadowColor, b.cloudShadowColor, t),
                cloudLitColor = Color.Lerp(a.cloudLitColor, b.cloudLitColor, t),
                cloudLitGain = Mathf.Lerp(a.cloudLitGain, b.cloudLitGain, t),

                auroraIntensity = Mathf.Lerp(a.auroraIntensity, b.auroraIntensity, t),
                auroraColorA = Color.Lerp(a.auroraColorA, b.auroraColorA, t),
                auroraColorB = Color.Lerp(a.auroraColorB, b.auroraColorB, t),
                auroraSpeed = Mathf.Lerp(a.auroraSpeed, b.auroraSpeed, t),

                lightAzimuth = Mathf.LerpAngle(a.lightAzimuth, b.lightAzimuth, t),
                lightElevation = Mathf.Lerp(a.lightElevation, b.lightElevation, t),
                lightColor = Color.Lerp(a.lightColor, b.lightColor, t),
                lightIntensity = Mathf.Lerp(a.lightIntensity, b.lightIntensity, t),

                ambientIntensity = Mathf.Lerp(a.ambientIntensity, b.ambientIntensity, t),
                fogColor = Color.Lerp(a.fogColor, b.fogColor, t),
                fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, t),
                exposure = Mathf.Lerp(a.exposure, b.exposure, t)
            };
        }
    }
}
