using UnityEngine;
using Core.TweenSystem;

namespace Core.DayNightSystem
{
    /// <summary>
    /// Fondu entre deux skybox (jour/nuit) via le shader "Skybox/Blended" fait
    /// maison, piloté par les changements de phase du DayNightCycle. Le
    /// material de la scène doit utiliser ce shader avec ses deux Cubemaps
    /// (Skybox 1 = jour, Skybox 2 = nuit) déjà assignées dans l'Inspector.
    /// </summary>
    public sealed class SkyboxSwitcher : MonoBehaviour
    {
        [SerializeField] private DayNightCycle dayNightCycle;
        [Tooltip("Le material de skybox de la scène, utilisant le shader Skybox/Blended.")]
        [SerializeField] private Material skyboxMaterial;

        [SerializeField, Min(0.1f)] private float fadeDuration = 3f;

        private static readonly int BlendId = Shader.PropertyToID("_Blend");

        private void OnEnable()
        {
            if (dayNightCycle != null)
                dayNightCycle.OnPhaseChanged += HandlePhaseChanged;
        }

        private void OnDisable()
        {
            if (dayNightCycle != null)
                dayNightCycle.OnPhaseChanged -= HandlePhaseChanged;
        }

        private void HandlePhaseChanged(DayPhase phase)
        {
            if (skyboxMaterial == null) return;

            // Nuit/Crépuscule → skybox 2 (nuit). Jour/Aube → skybox 1 (jour).
            float targetBlend = (phase == DayPhase.Night || phase == DayPhase.Dusk) ? 1f : 0f;

            float current = skyboxMaterial.GetFloat(BlendId);
            Tweener.Value(current, targetBlend, fadeDuration, v => skyboxMaterial.SetFloat(BlendId, v));
        }
    }
}