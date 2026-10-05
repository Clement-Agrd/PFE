using UnityEngine;

namespace Core.DayNightSystem
{
    /// <summary>
    /// Fondu entre deux skybox (jour/nuit) via le shader "Skybox/Blended" fait
    /// maison, suivant en continu DayNightCycle.NightBlend01. Le material de
    /// la scène doit utiliser ce shader avec ses deux Cubemaps (Skybox 1 =
    /// jour, Skybox 2 = nuit) déjà assignées dans l'Inspector.
    /// </summary>
    public sealed class SkyboxSwitcher : MonoBehaviour
    {
        [SerializeField] private DayNightCycle dayNightCycle;
        [Tooltip("Le material de skybox de la scène, utilisant le shader Skybox/Blended.")]
        [SerializeField] private Material skyboxMaterial;

        [Tooltip("Durée (s) pour rattraper une cible de blend, ex. lors d'un saut d'heure instantané.")]
        [SerializeField, Min(0.01f)] private float fadeDuration = 3f;

        private static readonly int BlendId = Shader.PropertyToID("_Blend");

        private void OnEnable()
        {
            if (dayNightCycle != null && skyboxMaterial != null)
                skyboxMaterial.SetFloat(BlendId, dayNightCycle.NightBlend01);
        }

        private void Update()
        {
            if (dayNightCycle == null || skyboxMaterial == null) return;

            float current = skyboxMaterial.GetFloat(BlendId);
            float target = dayNightCycle.NightBlend01;
            float next = Mathf.MoveTowards(current, target, Time.deltaTime / fadeDuration);
            skyboxMaterial.SetFloat(BlendId, next);
        }
    }
}