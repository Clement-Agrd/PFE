using UnityEngine;
using Core.WaveSystem;
using Core.DayNightSystem;

namespace Core.TowerDefense
{
    /// <summary>
    /// Bascule le cycle jour/nuit sur la nuit au lancement d'une session de
    /// défense (première vague), et le fige (optionnel) le temps de la bataille
    /// pour que l'ambiance ne redérive pas vers le jour pendant que tu combats.
    /// Remet le cycle en marche normale (et de retour au jour) une fois toutes
    /// les vagues terminées.
    /// </summary>
    public sealed class DefenseNightTrigger : MonoBehaviour
    {
        [SerializeField] private WaveSpawner waveSpawner;
        [SerializeField] private DayNightCycle dayNightCycle;

        [Header("Nuit de défense")]
        [Tooltip("Heure (0-24) vers laquelle basculer au début d'une défense.")]
        [SerializeField, Range(0f, 24f)] private float nightHour = 21f;
        [Tooltip("Fige le cycle pendant toute la défense (pas d'aube surprise en pleine vague).")]
        [SerializeField] private bool pauseCycleDuringDefense = true;

        [Header("Retour au jour")]
        [Tooltip("Heure vers laquelle revenir une fois toutes les vagues terminées.")]
        [SerializeField, Range(0f, 24f)] private float dayHour = 9f;

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
            // Ne bascule qu'au tout début d'une session (première vague).
            if (waveNumber != 1 || dayNightCycle == null) return;

            dayNightCycle.SetTime(nightHour);
            if (pauseCycleDuringDefense) dayNightCycle.Paused = true;
        }

        private void HandleAllWavesCompleted()
        {
            if (dayNightCycle == null) return;

            dayNightCycle.Paused = false;
            dayNightCycle.SetTime(dayHour);
        }
    }
}