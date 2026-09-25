using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

using Core.PoolingSystem;

namespace Core.WaveSystem
{
    /// <summary>
    /// Gestionnaire des vagues.
    ///
    /// Les différentes SpawnEntry sont lancées en parallèle
    /// et peuvent utiliser différentes SpawnZone.
    ///
    /// Chaque SpawnZone peut posséder sa propre spline.
    /// </summary>
    public sealed class WaveSpawner : MonoBehaviour
    {
        // ============================================================
        // REFERENCES
        // ============================================================

        [Header("Waves")]

        [SerializeField]
        private WaveSetDefinition waveSet;


        [Header("Pooling")]

        [Tooltip(
            "Optionnel. Si assigné, les ennemis sont récupérés depuis le pool."
        )]
        [SerializeField]
        private PoolManager poolManager;


        [Header("Spawn Zones")]

        [Tooltip(
            "Toutes les zones utilisables par les SpawnEntry."
        )]
        [SerializeField]
        private List<SpawnZone> zones =
            new();


        // ============================================================
        // RUNTIME
        // ============================================================

        private Dictionary<string, SpawnZone>
            _zonesById;


        private int _waveIndex;

        private int _loop;

        private int _aliveCount;

        private bool _isWaveActive;


        // ============================================================
        // PUBLIC
        // ============================================================

        public int Loop =>
            _loop;


        public int AliveCount =>
            _aliveCount;


        public bool IsWaveActive =>
            _isWaveActive;


        public bool HasMoreWaves =>
            waveSet != null &&
            waveSet.Waves.Count > 0 &&
            (
                waveSet.LoopEndless ||
                _waveIndex <
                waveSet.Waves.Count
            );


        public bool CanStartWave =>
            !_isWaveActive &&
            HasMoreWaves;


        public int NextWaveNumber =>
            waveSet != null
                ? _loop *
                  waveSet.Waves.Count +
                  _waveIndex +
                  1
                : 0;


        // ============================================================
        // EVENTS
        // ============================================================

        public event Action<int>
            OnWaveStarted;


        public event Action<int>
            OnWaveCompleted;


        public event Action
            OnAllWavesCompleted;


        /// <summary>
        /// Ennemi créé + zone depuis laquelle il vient.
        ///
        /// TowerDefenseLevel utilise cette information
        /// pour lui attribuer la bonne spline.
        /// </summary>
        public event Action<
            GameObject,
            SpawnZone
        > OnEnemySpawned;


        // ============================================================
        // UNITY
        // ============================================================

        private void Awake()
        {
            BuildZoneLookup();
        }


        private void BuildZoneLookup()
        {
            _zonesById =
                new Dictionary<
                    string,
                    SpawnZone
                >();


            foreach (SpawnZone zone in zones)
            {
                if (zone == null)
                    continue;


                if (string.IsNullOrWhiteSpace(
                        zone.Id))
                {
                    Debug.LogWarning(
                        $"[WaveSpawner] " +
                        $"La zone {zone.name} n'a pas d'Id.",
                        zone
                    );

                    continue;
                }


                if (!_zonesById.TryAdd(
                        zone.Id,
                        zone))
                {
                    Debug.LogWarning(
                        $"[WaveSpawner] " +
                        $"Id de zone en double : '{zone.Id}'.",
                        zone
                    );
                }
            }
        }


        // ============================================================
        // WAVE
        // ============================================================

        /// <summary>
        /// À brancher sur le bouton "Vague suivante".
        /// </summary>
        public void StartNextWave()
        {
            if (!CanStartWave)
            {
                Debug.Log(
                    "[WaveSpawner] Impossible de lancer une vague."
                );

                return;
            }


            StartCoroutine(
                RunWave()
            );
        }


        private IEnumerator RunWave()
        {
            _isWaveActive =
                true;


            WaveDefinition wave =
                waveSet.Waves[
                    _waveIndex
                ];


            int globalNumber =
                NextWaveNumber;


            OnWaveStarted?.Invoke(
                globalNumber
            );


            if (wave.StartDelay > 0f)
            {
                yield return new WaitForSeconds(
                    wave.StartDelay
                );
            }


            // Toutes les lignes de spawn
            // fonctionnent simultanément.
            List<Coroutine> running =
                new List<Coroutine>();


            foreach (
                SpawnEntry entry
                in wave.Spawns)
            {
                running.Add(
                    StartCoroutine(
                        RunEntry(entry)
                    )
                );
            }


            foreach (
                Coroutine coroutine
                in running)
            {
                yield return coroutine;
            }


            // Attend que tous les ennemis soient :
            // - morts
            // OU
            // - arrivés au bout de leur spline.
            while (_aliveCount > 0)
            {
                yield return null;
            }


            OnWaveCompleted?.Invoke(
                globalNumber
            );


            _waveIndex++;


            if (_waveIndex >=
                waveSet.Waves.Count)
            {
                if (waveSet.LoopEndless)
                {
                    _waveIndex = 0;

                    _loop++;
                }
                else
                {
                    _isWaveActive =
                        false;


                    OnAllWavesCompleted
                        ?.Invoke();


                    yield break;
                }
            }


            _isWaveActive =
                false;
        }


        // ============================================================
        // SPAWN ENTRY
        // ============================================================

        private IEnumerator RunEntry(
            SpawnEntry entry)
        {
            if (entry.prefab == null)
                yield break;


            if (entry.startDelay > 0f)
            {
                yield return new WaitForSeconds(
                    entry.startDelay
                );
            }


            float scale =
                1f +
                _loop *
                waveSet.CountScalePerLoop;


            int totalCount =
                Mathf.Max(
                    1,
                    Mathf.RoundToInt(
                        entry.count *
                        scale
                    )
                );


            SpawnZone zone =
                ResolveZone(
                    entry.zoneId
                );


            int spawned = 0;


            while (spawned <
                totalCount)
            {
                int thisBurst =
                    Mathf.Min(
                        entry.burstSize,
                        totalCount -
                        spawned
                    );


                for (int i = 0;
                     i < thisBurst;
                     i++)
                {
                    SpawnOne(
                        entry.prefab,
                        zone
                    );
                }


                spawned +=
                    thisBurst;


                if (spawned <
                        totalCount &&
                    entry.burstInterval > 0f)
                {
                    yield return
                        new WaitForSeconds(
                            entry.burstInterval
                        );
                }
            }
        }


        // ============================================================
        // ZONES
        // ============================================================

        private SpawnZone ResolveZone(
            string zoneId)
        {
            if (!string.IsNullOrEmpty(
                    zoneId) &&
                _zonesById.TryGetValue(
                    zoneId,
                    out SpawnZone zone))
            {
                return zone;
            }


            Debug.LogWarning(
                $"[WaveSpawner] Zone '{zoneId}' introuvable. " +
                $"L'ennemi apparaîtra à la position du WaveSpawner."
            );


            return null;
        }


        // ============================================================
        // SPAWN
        // ============================================================

        private void SpawnOne(
            GameObject prefab,
            SpawnZone zone)
        {
            Vector3 position;

            if (zone != null)
            {
                if (!zone.TryGetRandomPoint(
                        out position))
                {
                    Debug.LogError(
                        $"[WaveSpawner] Spawn annulé pour {prefab.name} : " +
                        $"aucun NavMesh valide dans la zone '{zone.name}'.",
                        zone
                    );

                    return;
                }
            }
            else
            {
                if (!NavMesh.SamplePosition(
                        transform.position,
                        out NavMeshHit fallbackHit,
                        5f,
                        NavMesh.AllAreas))
                {
                    Debug.LogError(
                        $"[WaveSpawner] Spawn annulé pour {prefab.name} : " +
                        "aucun NavMesh valide près du WaveSpawner.",
                        this
                    );

                    return;
                }

                position =
                    fallbackHit.position;
            }


            GameObject instance =
                poolManager != null
                    ? poolManager.Spawn(
                        prefab,
                        position,
                        Quaternion.identity
                    )
                    : Instantiate(
                        prefab,
                        position,
                        Quaternion.identity
                    );


            if (instance == null)
                return;


            if (!instance.TryGetComponent(
                    out IWaveEnemy enemy))
            {
                Debug.LogWarning(
                    $"[WaveSpawner] {instance.name} " +
                    $"n'implémente pas IWaveEnemy.",
                    instance
                );

                return;
            }


            // Important :
            // on compte et on écoute AVANT l'initialisation.
            //
            // Comme ça, si l'ennemi est immédiatement
            // retiré pour une raison quelconque,
            // le compteur reste correct.
            _aliveCount++;


            enemy.Defeated +=
                HandleDefeated;


            OnEnemySpawned?.Invoke(
                instance,
                zone
            );
        }


        // ============================================================
        // DEFEAT
        // ============================================================

        private void HandleDefeated(
            IWaveEnemy enemy)
        {
            if (enemy != null)
            {
                enemy.Defeated -=
                    HandleDefeated;
            }


            _aliveCount =
                Mathf.Max(
                    0,
                    _aliveCount - 1
                );
        }
    }
}