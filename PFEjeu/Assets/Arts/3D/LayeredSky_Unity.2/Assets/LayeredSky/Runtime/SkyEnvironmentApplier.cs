using UnityEngine;
using UnityEngine.Rendering;

namespace LayeredSky
{
    /// <summary>
    /// Ambiance (SH du skybox), réflexions et brouillard.
    /// DynamicGI.UpdateEnvironment() est coûteux : il est LIMITÉ à un appel toutes les N secondes pendant une transition,
    /// plus un appel final garanti à la fin.
    /// </summary>
    public sealed class SkyEnvironmentApplier : ISkyStateApplier
    {
        private readonly bool _driveFog;
        private readonly float _giInterval;
        private float _lastGiTime = float.NegativeInfinity;
        private bool _giDirty;

        public SkyEnvironmentApplier(bool driveFog, float giUpdateInterval)
        {
            _driveFog = driveFog;
            _giInterval = giUpdateInterval;
        }

        public void Apply(in SkyState s)
        {
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.ambientIntensity = s.ambientIntensity;

            if (_driveFog)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = s.fogColor;
                RenderSettings.fogDensity = s.fogDensity;
            }

            if (Time.unscaledTime - _lastGiTime >= _giInterval)
            {
                UpdateEnvironment();
            }
            else
            {
                _giDirty = true;
            }
        }

        public void Finish()
        {
            if (_giDirty) UpdateEnvironment();
        }

        private void UpdateEnvironment()
        {
            DynamicGI.UpdateEnvironment();
            _lastGiTime = Time.unscaledTime;
            _giDirty = false;
        }
    }
}
