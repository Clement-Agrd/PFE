using UnityEngine;

namespace LayeredSky
{
    /// <summary>Oriente et règle la Directional Light principale (soleil le jour, lune la nuit).</summary>
    public sealed class SkyLightApplier : ISkyStateApplier
    {
        private readonly Light _light;
        private readonly Transform _transform;

        public SkyLightApplier(Light light)
        {
            _light = light;
            _transform = light.transform;
        }

        public void Apply(in SkyState s)
        {
            // La lumière pointe VERS le sol : son avant est l'opposé de la direction de la source.
            _transform.rotation = Quaternion.LookRotation(-s.LightDirection);
            _light.color = s.lightColor;
            _light.intensity = s.lightIntensity;
        }

        public void Finish() { }
    }
}
