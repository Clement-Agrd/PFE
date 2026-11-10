// OPTIONNEL : à n'utiliser que si tu construis le VFX Graph décrit dans VFX_HealZone_Recipe.md.
// Dossier "Optional~" ignoré par Unity : déplace ce fichier dans Assets/HealFx/Runtime pour l'activer
// (nécessite le package Visual Effect Graph).
using UnityEngine;
using UnityEngine.VFX;

namespace HealFx
{
    /// <summary>
    /// Pilote un VisualEffect (VFX Graph) avec la même palette / le même rayon que HealZoneFx.
    /// Propriétés exposées attendues dans le graph : Radius (float), PrimaryColor (Color), SecondaryColor (Color), Intensity (float).
    /// Événements attendus : "OnPulse" (rafale de croix au centre).
    /// </summary>
    [RequireComponent(typeof(VisualEffect))]
    public sealed class HealZoneVfxGraphDriver : MonoBehaviour
    {
        private static readonly int RadiusId = Shader.PropertyToID("Radius");
        private static readonly int PrimaryId = Shader.PropertyToID("PrimaryColor");
        private static readonly int SecondaryId = Shader.PropertyToID("SecondaryColor");
        private static readonly int IntensityId = Shader.PropertyToID("Intensity");
        private static readonly int PulseEventId = Shader.PropertyToID("OnPulse");

        [SerializeField] private HealFxPalette palette;
        [SerializeField] private HealZoneFx zone;          // optionnel : s'abonne à Pulsed et lit le rayon
        [SerializeField, Min(0.5f)] private float radius = 3.4f;
        [SerializeField, Min(0f)] private float intensity = 1.2f;

        private VisualEffect effect;

        private void Awake()
        {
            effect = GetComponent<VisualEffect>();
        }

        private void OnEnable()
        {
            if (zone != null) zone.Pulsed += Pulse;
            Apply();
        }

        private void OnDisable()
        {
            if (zone != null) zone.Pulsed -= Pulse;
        }

        private void OnValidate()
        {
            if (effect != null) Apply();
        }

        public void Pulse()
        {
            effect.SendEvent(PulseEventId);
        }

        private void Apply()
        {
            float r = zone != null ? zone.Radius : radius;
            effect.SetFloat(RadiusId, r);
            effect.SetFloat(IntensityId, intensity);
            if (palette == null) return;
            effect.SetVector4(PrimaryId, palette.Primary);
            effect.SetVector4(SecondaryId, palette.Secondary);
        }
    }
}
