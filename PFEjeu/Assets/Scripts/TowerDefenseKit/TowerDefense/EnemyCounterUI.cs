using UnityEngine;
using TMPro;
using Core.WaveSystem;

namespace Core.TowerDefense
{
    /// <summary>
    /// Affiche en overlay le nombre d'ennemis actuellement en vie. Visible
    /// pendant TOUTE la phase de défense (du lancement de la première vague
    /// jusqu'à la fin du set), pas juste pendant chaque vague individuelle.
    /// </summary>
    public sealed class EnemyCounterUI : MonoBehaviour
    {
        [SerializeField] private WaveSpawner waveSpawner;

        [Header("UI")]
        [SerializeField] private GameObject panel;
        [SerializeField] private TMP_Text counterLabel;
        [Tooltip("Format d'affichage. {0} = nombre d'ennemis restants.")]
        [SerializeField] private string format = "Ennemis restants : {0}";

        private bool _inDefensePhase;
        private int _lastShownCount = -1;

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

        private void Start()
        {
            if (panel != null) panel.SetActive(false);
        }

        private void HandleWaveStarted(int waveNumber)
        {
            // Entre en phase de défense dès la 1ère vague du set.
            if (waveNumber != 1) return;

            _inDefensePhase = true;
            if (panel != null) panel.SetActive(true);
        }

        private void HandleAllWavesCompleted()
        {
            _inDefensePhase = false;
            if (panel != null) panel.SetActive(false);
        }

        private void Update()
        {
            if (!_inDefensePhase || waveSpawner == null) return;

            int count = waveSpawner.AliveCount;
            if (count == _lastShownCount) return;

            _lastShownCount = count;
            if (counterLabel != null)
                counterLabel.text = string.Format(format, count);
        }
    }
}