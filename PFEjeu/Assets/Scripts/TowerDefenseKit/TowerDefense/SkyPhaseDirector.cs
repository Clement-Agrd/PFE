using UnityEngine;
using Core.WaveSystem;
using LayeredSky;

namespace Core.TowerDefense
{
    /// <summary>
    /// Fait le lien entre le gameplay (WaveSpawner) et le ciel (SkyController, Layered Sky v3).
    ///
    ///   WaveSpawner ── OnWaveStarted ────────▶ SkyPhaseDirector ──▶ SkyController.TransitionTo(...)
    ///               ── OnDefenseCompleted ──────▶
    ///
    ///   Hub / exploration (jour)        -> preset Jour        (état de départ du SkyController)
    ///   Le joueur lance la 1re vague    -> preset Nuit        (fondu continu, traverse le Crépuscule)
    ///   Retour en phase village     -> preset Jour        (traverse le Crépuscule : aube/soir)
    ///   Option : phase de préparation   -> StartPreparationPhase() -> preset Crépuscule (fixe)
    ///
    /// Aucun Singleton, aucun Find : toutes les références sont branchées dans l'Inspector.
    /// Ne modifie ni DayNightCycle ni DefenseNightTrigger.
    /// </summary>
    public sealed class SkyPhaseDirector : MonoBehaviour
    {
        #region Références
        [Header("Références")]
        [SerializeField] private WaveSpawner waveSpawner;
        [SerializeField] private SkyController skyController;
        #endregion

        #region Presets
        [Header("Presets")]
        [Tooltip("SkyPreset_Jour : hub et exploration.")]
        [SerializeField] private SkyPreset dayPreset;
        [Tooltip("SkyPreset_Nuit : défense.")]
        [SerializeField] private SkyPreset nightPreset;
        [Tooltip("SkyPreset_Crepuscule : optionnel, phase de préparation à ambiance fixe.")]
        [SerializeField] private SkyPreset duskPreset;
        #endregion

        #region Durées (secondes)
        [Header("Durées de transition (s)")]
        [SerializeField, Min(0f)] private float toNightSeconds = 8f;
        [SerializeField, Min(0f)] private float toDaySeconds = 8f;
        [SerializeField, Min(0f)] private float toDuskSeconds = 6f;
        #endregion

        private bool _defenseActive;

        #region Cycle de vie
        private void OnEnable()
        {
            if (waveSpawner == null) return;
            waveSpawner.OnWaveStarted += HandleWaveStarted;
            waveSpawner.OnDefenseCompleted += HandleDefenseCompleted;
        }

        private void OnDisable()
        {
            if (waveSpawner == null) return;
            waveSpawner.OnWaveStarted -= HandleWaveStarted;
            waveSpawner.OnDefenseCompleted -= HandleDefenseCompleted;
        }
        #endregion

        #region Handlers
        private void HandleWaveStarted(int waveNumber)
        {
            // La nuit tombe une seule fois par défense, pas à chaque vague.
            if (_defenseActive) return;
            _defenseActive = true;
            Go(nightPreset, toNightSeconds);
        }

        private void HandleDefenseCompleted()
        {
            _defenseActive = false;
            Go(dayPreset, toDaySeconds);
        }
        #endregion

        #region API publique
        /// <summary>Ambiance de transition fixe (ex. phase de préparation). À brancher sur un bouton/UnityEvent si besoin.</summary>
        public void StartPreparationPhase()
        {
            if (_defenseActive) return;
            Go(duskPreset, toDuskSeconds);
        }
        #endregion

        #region Utilitaires
        private void Go(SkyPreset preset, float seconds)
        {
            if (skyController == null || preset == null) return;
            skyController.TransitionTo(preset, seconds);
        }
        #endregion
    }
}
