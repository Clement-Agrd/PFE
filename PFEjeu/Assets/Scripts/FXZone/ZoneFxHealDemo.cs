using UnityEngine;
using UnityEngine.InputSystem;
using ZoneFx;

namespace Core.FXZone
{
    /// <summary>
    /// Démo de la zone de soin ZoneFx pour la baie Zone_Soin de ZONE_07_FX : simule le tick de soin d'une tour
    /// (Pulse à intervalle régulier) sans avoir besoin de la vraie tour.
    /// Z = afficher / masquer, X = preset suivant, C = rayon suivant. Aucune allocation par frame.
    /// </summary>
    public sealed class ZoneFxHealDemo : MonoBehaviour
    {
        [Header("Zone")]
        [SerializeField] private ZoneFxController zone;
        [SerializeField, Min(0.5f)] private float radius = 5f;
        [SerializeField] private float[] radiusSteps = { 3f, 5f, 8f };

        [Header("Tick de soin simulé")]
        [SerializeField, Min(0.2f)] private float tickInterval = 2f;

        [Header("Touches")]
        [SerializeField] private Key toggleKey = Key.Z;
        [SerializeField] private Key presetKey = Key.X;
        [SerializeField] private Key radiusKey = Key.C;

        private float nextTick;
        private int radiusIndex;
        private int presetIndex;
        private static readonly int PresetCount = System.Enum.GetValues(typeof(ZoneFxPreset)).Length;

        private void Reset()
        {
            zone = GetComponentInChildren<ZoneFxController>();
        }

        private void Start()
        {
            if (zone == null) zone = GetComponentInChildren<ZoneFxController>();
            if (zone == null)
            {
                Debug.LogWarning("[ZoneFxHealDemo] Aucun ZoneFxController assigné.", this);
                enabled = false;
                return;
            }
            zone.SetRadius(radius);
            presetIndex = (int)zone.Preset;
            nextTick = Time.time + tickInterval;
        }

        private void Update()
        {
            if (zone.IsShown && Time.time >= nextTick)
            {
                nextTick = Time.time + tickInterval;
                zone.Pulse();
            }

            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb[toggleKey].wasPressedThisFrame)
            {
                if (zone.IsShown) zone.Hide(); else zone.Show();
            }
            if (kb[presetKey].wasPressedThisFrame)
            {
                presetIndex = (presetIndex + 1) % PresetCount;
                zone.SetPreset((ZoneFxPreset)presetIndex);
            }
            if (kb[radiusKey].wasPressedThisFrame && radiusSteps != null && radiusSteps.Length > 0)
            {
                radiusIndex = (radiusIndex + 1) % radiusSteps.Length;
                radius = radiusSteps[radiusIndex];
                zone.SetRadius(radius);
            }
        }
    }
}
