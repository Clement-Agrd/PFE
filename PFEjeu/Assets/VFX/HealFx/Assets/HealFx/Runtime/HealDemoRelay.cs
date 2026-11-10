using UnityEngine;

namespace HealFx
{
    /// <summary>
    /// Relais de démo : chaque impulsion de la zone déclenche l'impulsion de la cible.
    /// En jeu, remplace-le par ton système de soin : quand un soin est appliqué à une unité, appelle target.Pulse().
    /// </summary>
    public sealed class HealDemoRelay : MonoBehaviour
    {
        [SerializeField] private HealZoneFx zone;
        [SerializeField] private HealTargetFx target;

        public void Bind(HealZoneFx healZone, HealTargetFx healTarget)
        {
            zone = healZone;
            target = healTarget;
        }

        private void OnEnable()
        {
            if (zone != null) zone.Pulsed += OnZonePulsed;
        }

        private void OnDisable()
        {
            if (zone != null) zone.Pulsed -= OnZonePulsed;
        }

        private void OnZonePulsed()
        {
            if (target != null) target.Pulse();
        }
    }
}
