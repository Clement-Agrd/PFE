using System;
using System.Collections;
using UnityEngine;
using Core.PoolingSystem;

namespace Core.WaveSystem
{
    /// <summary>
    /// Déroule un WaveSet en DÉCLENCHEMENT MANUEL : chaque vague est lancée par
    /// StartNextWave() (ton bouton). Quand la vague est nettoyée (tous les ennemis
    /// vaincus — tués OU arrivés à la base), le spawner redevient prêt pour la suivante.
    /// Spawn via le Pooling. Gère le mode endless avec difficulté croissante.
    ///
    /// NOTE : remplace le WaveSpawner d'origine (auto-enchaîné). L'ancien 'autoStart'
    /// et 'delayBetweenWaves' ont été retirés (inutiles en manuel).
    /// </summary>
    public sealed class WaveSpawner : MonoBehaviour
    {
        [SerializeField] private WaveSetDefinition waveSet;

        [Tooltip("Optionnel : si assigné, les ennemis sont poolés. Sinon, Instantiate.")]
        [SerializeField] private PoolManager poolManager;

        [Tooltip("Points d'apparition. Vide = position du spawner. (En TD, l'ennemi se repositionne sur le chemin.)")]
        [SerializeField] private Transform[] spawnPoints;

        private int _waveIndex;
        private int _loop;
        private int _aliveCount;
        private bool _isWaveActive;

        public int Loop => _loop;
        public int AliveCount => _aliveCount;
        public bool IsWaveActive => _isWaveActive;

        /// <summary>Reste-t-il des vagues à lancer ? (toujours vrai en endless)</summary>
        public bool HasMoreWaves =>
            waveSet != null && waveSet.Waves.Count > 0 &&
            (waveSet.LoopEndless || _waveIndex < waveSet.Waves.Count);

        /// <summary>Peut-on lancer la prochaine vague maintenant ? (bouton actif ?)</summary>
        public bool CanStartWave => !_isWaveActive && HasMoreWaves;

        /// <summary>Numéro global de la prochaine vague (croît en endless).</summary>
        public int NextWaveNumber =>
            waveSet != null ? _loop * waveSet.Waves.Count + _waveIndex + 1 : 0;

        public event Action<int> OnWaveStarted;
        public event Action<int> OnWaveCompleted;
        public event Action OnAllWavesCompleted;
        public event Action<GameObject> OnEnemySpawned;

        /// <summary>À brancher sur ton bouton « Vague suivante ». Ignoré si une vague est en cours.</summary>
        public void StartNextWave()
        {
            if (!CanStartWave)
            {
                Debug.Log("[WaveSpawner] Impossible de lancer une vague (déjà en cours ou terminées).");
                return;
            }
            StartCoroutine(RunWave());
        }

        private IEnumerator RunWave()
        {
            _isWaveActive = true;

            WaveDefinition wave = waveSet.Waves[_waveIndex];
            int globalNumber = NextWaveNumber;
            OnWaveStarted?.Invoke(globalNumber);

            if (wave.StartDelay > 0f)
                yield return new WaitForSeconds(wave.StartDelay);

            yield return SpawnWave(wave);

            // Attendre que tous les ennemis de la vague soient vaincus (tués ou arrivés à la base).
            while (_aliveCount > 0)
                yield return null;

            OnWaveCompleted?.Invoke(globalNumber);

            // Avancer (ou reboucler en endless, ou terminer).
            _waveIndex++;
            if (_waveIndex >= waveSet.Waves.Count)
            {
                if (waveSet.LoopEndless)
                {
                    _waveIndex = 0;
                    _loop++;
                }
                else
                {
                    _isWaveActive = false;
                    OnAllWavesCompleted?.Invoke();
                    yield break;
                }
            }

            _isWaveActive = false; // prêt pour la vague suivante → le bouton se réactive
        }

        private IEnumerator SpawnWave(WaveDefinition wave)
        {
            float scale = 1f + _loop * waveSet.CountScalePerLoop;

            foreach (SpawnEntry entry in wave.Spawns)
            {
                if (entry.prefab == null) continue;

                int count = Mathf.Max(1, Mathf.RoundToInt(entry.count * scale));
                for (int i = 0; i < count; i++)
                {
                    SpawnOne(entry.prefab);
                    if (entry.interval > 0f)
                        yield return new WaitForSeconds(entry.interval);
                }
            }
        }

        private void SpawnOne(GameObject prefab)
        {
            Transform point = GetSpawnPoint();
            Vector3 pos = point != null ? point.position : transform.position;
            Quaternion rot = point != null ? point.rotation : Quaternion.identity;

            GameObject instance = poolManager != null
                ? poolManager.Spawn(prefab, pos, rot)
                : Instantiate(prefab, pos, rot);

            if (instance == null) return;

            _aliveCount++;
            OnEnemySpawned?.Invoke(instance);

            if (instance.TryGetComponent(out IWaveEnemy enemy))
                enemy.Defeated += HandleDefeated;
            else
                Debug.LogWarning($"[WaveSpawner] {instance.name} n'implémente pas IWaveEnemy → " +
                                 "la vague ne saura pas quand il est vaincu.");
        }

        private void HandleDefeated(IWaveEnemy enemy)
        {
            enemy.Defeated -= HandleDefeated;
            _aliveCount = Mathf.Max(0, _aliveCount - 1);
        }

        private Transform GetSpawnPoint()
        {
            if (spawnPoints == null || spawnPoints.Length == 0) return null;
            return spawnPoints[UnityEngine.Random.Range(0, spawnPoints.Length)];
        }
    }
}
