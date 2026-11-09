using UnityEngine;

namespace LayeredSky
{
    /// <summary>
    /// Envoie le SkyState au shader Skybox/LayeredFantasySky.
    /// Les propriétés sont des Vector (pas des Color) : aucune conversion de couleur automatique d'Unity,
    /// on passe des valeurs de radiance LINÉAIRE (conversion explicite via Color.linear).
    /// </summary>
    public sealed class SkyMaterialApplier : ISkyStateApplier
    {
        #region Property IDs (calculés une seule fois)
        private static readonly int ZenithId = Shader.PropertyToID("_ZenithColor");
        private static readonly int MidId = Shader.PropertyToID("_MidColor");
        private static readonly int HorizonId = Shader.PropertyToID("_HorizonColor");
        private static readonly int GroundId = Shader.PropertyToID("_GroundColor");
        private static readonly int SunDirId = Shader.PropertyToID("_SunDirection");
        private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        private static readonly int SunRadianceId = Shader.PropertyToID("_SunRadiance");
        private static readonly int SunSizeId = Shader.PropertyToID("_SunSize");
        private static readonly int SunGlowId = Shader.PropertyToID("_SunGlow");
        private static readonly int StarIntensityId = Shader.PropertyToID("_StarIntensity");
        private static readonly int TwinkleId = Shader.PropertyToID("_Twinkle");
        private static readonly int RotationId = Shader.PropertyToID("_Rotation");
        private static readonly int MoonVisId = Shader.PropertyToID("_MoonVisibility");
        private static readonly int MoonRadianceId = Shader.PropertyToID("_MoonRadiance");
        private static readonly int Moon1DirId = Shader.PropertyToID("_Moon1Dir");
        private static readonly int Moon1ColorId = Shader.PropertyToID("_Moon1Color");
        private static readonly int Moon1ParamsId = Shader.PropertyToID("_Moon1Params");
        private static readonly int CloudCoverageId = Shader.PropertyToID("_CloudCoverage");
        private static readonly int CloudSpeedId = Shader.PropertyToID("_CloudSpeed");
        private static readonly int CloudShadowId = Shader.PropertyToID("_CloudShadow");
        private static readonly int CloudLitId = Shader.PropertyToID("_CloudLit");
        private static readonly int CloudLitGainId = Shader.PropertyToID("_CloudLitGain");
        private static readonly int CloudLightDirId = Shader.PropertyToID("_CloudLightDir");
        private static readonly int AuroraIntensityId = Shader.PropertyToID("_AuroraIntensity");
        private static readonly int AuroraAId = Shader.PropertyToID("_AuroraColorA");
        private static readonly int AuroraBId = Shader.PropertyToID("_AuroraColorB");
        private static readonly int AuroraSpeedId = Shader.PropertyToID("_AuroraSpeed");
        private static readonly int MeteorId = Shader.PropertyToID("_MeteorIntensity");
        private static readonly int ExposureId = Shader.PropertyToID("_Exposure");
        #endregion

        // Graines des lunes (relief différent par lune)
        private const float Moon1Seed = 11f;

        private readonly Material _material;

        public SkyMaterialApplier(Material material)
        {
            _material = material;
        }

        public void Apply(in SkyState s)
        {
            Material m = _material;

            m.SetVector(ZenithId, ToLinear(s.zenithColor));
            m.SetVector(MidId, ToLinear(s.midColor));
            m.SetVector(HorizonId, ToLinear(s.horizonColor));
            m.SetVector(GroundId, ToLinear(s.groundColor));

            m.SetVector(SunDirId, s.SunDirection);
            m.SetVector(SunColorId, ToLinear(s.sunColor));
            m.SetFloat(SunRadianceId, s.sunRadiance);
            m.SetFloat(SunSizeId, s.sunSize);
            m.SetFloat(SunGlowId, s.sunGlow);

            m.SetFloat(StarIntensityId, s.starIntensity);
            m.SetFloat(TwinkleId, s.twinkle);
            m.SetFloat(RotationId, s.starRotation);

            m.SetFloat(MoonVisId, s.moonVisibility);
            m.SetFloat(MoonRadianceId, s.moonRadiance);
            m.SetVector(Moon1DirId, s.Moon1Direction);
            m.SetVector(Moon1ColorId, ToLinear(s.moon1Color));
            m.SetVector(Moon1ParamsId, new Vector4(s.moon1Radius, Moon1Seed, 0f, 0f));

            m.SetFloat(CloudCoverageId, s.cloudCoverage);
            m.SetFloat(CloudSpeedId, s.cloudSpeed);
            m.SetVector(CloudShadowId, ToLinear(s.cloudShadowColor));
            m.SetVector(CloudLitId, ToLinear(s.cloudLitColor));
            m.SetFloat(CloudLitGainId, s.cloudLitGain);
            m.SetVector(CloudLightDirId, s.LightDirection);   // les nuages sont éclairés par la lumière principale

            m.SetFloat(AuroraIntensityId, s.auroraIntensity);
            m.SetVector(AuroraAId, ToLinear(s.auroraColorA));
            m.SetVector(AuroraBId, ToLinear(s.auroraColorB));
            m.SetFloat(AuroraSpeedId, s.auroraSpeed);

            m.SetFloat(MeteorId, s.meteorIntensity);

            m.SetFloat(ExposureId, s.exposure);
        }

        public void Finish() { }

        private static Vector4 ToLinear(Color c)
        {
            Color l = c.linear;
            return new Vector4(l.r, l.g, l.b, 1f);
        }
    }
}
