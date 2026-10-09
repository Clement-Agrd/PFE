using System.Collections;
using UnityEngine;
using Core.WaveSystem;
using Core.DayNightSystem;

namespace Core.TowerDefense
{
    /// <summary>
    /// Phase village = jour figé. Phase défense = nuit figée, qui ne se lance
    /// qu'UNE fois, au démarrage de la première vague, et dure jusqu'à la fin
    /// de toutes les vagues. Retour au jour (figé) ensuite.
    /// </summary>
    public sealed class DefenseNightTrigger : MonoBehaviour
    {
        [SerializeField] private WaveSpawner waveSpawner;
        [SerializeField] private DayNightCycle dayNightCycle;

        [Header("Phase village (jour)")]
        [Tooltip("Heure (0-24) tenue pendant la phase village.")]
        [SerializeField, Range(0f, 24f)] private float dayHour = 9f;
        [Tooltip("Fige le jour en phase village : la nuit ne tombe jamais toute seule.")]
        [SerializeField] private bool freezeDayInVillage = true;

        [Header("Phase défense (nuit)")]
        [Tooltip("Heure (0-24) vers laquelle basculer au début de la défense.")]
        [SerializeField, Range(0f, 24f)] private float nightHour = 21f;

        private bool _defenseActive;

        private IEnumerator Start()
        {
            // DayNightCycle applique son heure de départ dans son propre Start :
            // on attend une frame pour passer après lui.
            yield return null;
            if (!_defenseActive) EnterVillagePhase();
        }

        private void OnEnable()
        {
            if (waveSpawner == null) return;
            waveSpawner.OnWaveStarted += HandleWaveStarted;
            waveSpawner.OnAllWavesCompleted += HandleAllWavesCompleted;
        }

        private void OnDisable()
        {
            if (waveSpawner == null) return;
            waveSpawner.OnWaveStarted -= HandleWaveStarted;
            waveSpawner.OnAllWavesCompleted -= HandleAllWavesCompleted;
        }

        private void HandleWaveStarted(int waveNumber)
        {
            // La nuit ne se lance qu'une fois par défense, pas à chaque vague.
            if (_defenseActive || dayNightCycle == null) return;

            _defenseActive = true;
            dayNightCycle.SetTime(nightHour);
            dayNightCycle.Paused = true;
        }

        private void HandleAllWavesCompleted()
        {
            _defenseActive = false;
            EnterVillagePhase();
        }

        private void EnterVillagePhase()
        {
            if (dayNightCycle == null) return;

            dayNightCycle.SetTime(dayHour);
            dayNightCycle.Paused = freezeDayInVillage;
        }
    }
}
