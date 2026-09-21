using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Core.PoolingSystem;

namespace Core.WaveSystem
{
    /// <summary>
    /// Déroule un WaveSet en déclenchement MANUEL, façon Dungeon Defenders :
    /// plusieurs zones de spawn actives EN MÊME TEMPS, chacune crachant ses
    /// ennemis par SALVES plutôt qu'un par un à la file. Spawn via le Pooling.
    /// </summary>
    public sealed class WaveSpawner : MonoBehaviour
    {
        [SerializeField] private WaveSetDefinition waveSet;

        [Tooltip("Optionnel : si assigné, les ennemis sont poolés. Sinon, Instantiate.")]
        [SerializeField] private PoolManager poolManager;

        [Tooltip("Les zones de spawn de la scène. Chaque SpawnEntry en cible une par son Id.")]
        [SerializeField] private List<SpawnZone> zones = new();

        private Dictionary<string, SpawnZone> _zonesById;

        private int _waveIndex;
        private int _loop;
        private int _aliveCount;
        private bool _isWaveActive;

        public int Loop => _loop;
        public int AliveCount => _aliveCount;
        public bool IsWaveActive => _isWaveActive;

        public bool HasMoreWaves =>
            waveSet != null && waveSet.Waves.Count > 0 &&
            (waveSet.LoopEndless || _waveIndex < waveSet.Waves.Count);

        public bool CanStartWave => !_isWaveActive && HasMoreWaves;

        public int NextWaveNumber =>
            waveSet != null ? _loop * waveSet.Waves.Count + _waveIndex + 1 : 0;

        public event Action<int> OnWaveStarted;
        public event Action<int> OnWaveCompleted;
        public event Action OnAllWavesCompleted;
        public event Action<GameObject> OnEnemySpawned;

        private void Awake()
        {
            _zonesById = new Dictionary<string, SpawnZone>();
            foreach (SpawnZone zone in zones)
            {
                if (zone == null) continue;
                if (!_zonesById.TryAdd(zone.Id, zone))
                    Debug.LogWarning($"[WaveSpawner] Id de zone en double ignoré : '{zone.Id}'.");
            }
        }

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

            // Toutes les lignes de spawn tournent EN PARALLÈLE (zones simultanées).
            var running = new List<Coroutine>();
            foreach (SpawnEntry entry in wave.Spawns)
                running.Add(StartCoroutine(RunEntry(entry)));

            foreach (Coroutine c in running)
                yield return c;

            // Attend que tous les ennemis de la vague soient vaincus.
            while (_aliveCount > 0)
                yield return null;

            OnWaveCompleted?.Invoke(globalNumber);

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

            _isWaveActive = false;
        }

        // Fait apparaître une ligne de spawn par SALVES dans sa zone.
        private IEnumerator RunEntry(SpawnEntry entry)
        {
            if (entry.prefab == null) yield break;

            if (entry.startDelay > 0f)
                yield return new WaitForSeconds(entry.startDelay);

            float scale = 1f + _loop * waveSet.CountScalePerLoop;
            int totalCount = Mathf.Max(1, Mathf.RoundToInt(entry.count * scale));

            SpawnZone zone = ResolveZone(entry.zoneId);

            int spawned = 0;
            while (spawned < totalCount)
            {
                int thisBurst = Mathf.Min(entry.burstSize, totalCount - spawned);

                for (int i = 0; i < thisBurst; i++)
                    SpawnOne(entry.prefab, zone);

                spawned += thisBurst;

                if (spawned < totalCount && entry.burstInterval > 0f)
                    yield return new WaitForSeconds(entry.burstInterval);
            }
        }

        private SpawnZone ResolveZone(string zoneId)
        {
            if (!string.IsNullOrEmpty(zoneId) && _zonesById.TryGetValue(zoneId, out SpawnZone zone))
                return zone;

            Debug.LogWarning($"[WaveSpawner] Zone '{zoneId}' introuvable, spawn à la position du spawner.");
            return null;
        }

        private void SpawnOne(GameObject prefab, SpawnZone zone)
        {
            Vector3 pos = zone != null ? zone.GetRandomPoint() : transform.position;

            GameObject instance = poolManager != null
                ? poolManager.Spawn(prefab, pos, Quaternion.identity)
                : Instantiate(prefab, pos, Quaternion.identity);

            if (instance == null) return;

            _aliveCount++;
            OnEnemySpawned?.Invoke(instance);

            if (instance.TryGetComponent(out IWaveEnemy enemy))
                enemy.Defeated += HandleDefeated;
            else
                Debug.LogWarning($"[WaveSpawner] {instance.name} n'implémente pas IWaveEnemy.");
        }

        private void HandleDefeated(IWaveEnemy enemy)
        {
            enemy.Defeated -= HandleDefeated;
            _aliveCount = Mathf.Max(0, _aliveCount - 1);
        }
    }
}