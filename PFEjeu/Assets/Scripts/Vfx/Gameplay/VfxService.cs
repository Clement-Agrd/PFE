using System.Collections.Generic;
using Core.HealthSystem;
using Core.PoolingSystem;
using Core.WaveSystem;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Relie les evenements de jeu aux effets, via le PoolManager existant (aucun polling, aucun Find) :
    ///
    ///   WaveSpawner.OnZoneSpawnStarted --> portail rouge-orange sur la SpawnZone (duree = profil)
    ///   WaveSpawner.OnEnemySpawned ------> EnemyVfxBinding (aura + poussiere) sur l'ennemi
    ///   Health.OnDamaged / OnDeath ------> impact / mort (via IEnemyVfxSink)
    ///
    /// Les prefabs d'effets sont nouveaux : aucun prefab existant n'est modifie.
    /// </summary>
    public sealed class VfxService : MonoBehaviour, IEnemyVfxSink
    {
        [SerializeField] private VfxProfile profile;
        [SerializeField] private WaveSpawner waveSpawner;
        [SerializeField] private SpawnZone[] zones = System.Array.Empty<SpawnZone>();

        [Header("Prefabs d'effets (nouveaux, poolables)")]
        [SerializeField] private GameObject spawnPortalPrefab;
        [SerializeField] private GameObject enemyBindingPrefab;
        [SerializeField] private GameObject hitPrefab;
        [SerializeField] private GameObject deathPrefab;

        private readonly Dictionary<string, Transform> _zoneById = new Dictionary<string, Transform>();
        private int _activeBindings;
        private PoolManager _pool;
        private System.Action _onBindingReleased;

        #region Cycle de vie

        private void Awake()
        {
            _onBindingReleased = OnBindingReleased;
            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i] != null) _zoneById[zones[i].Id] = zones[i].transform;
            }
        }

        private void Start()
        {
            _pool = PoolManager.Instance;
            Warmup(spawnPortalPrefab);
            Warmup(enemyBindingPrefab);
            Warmup(hitPrefab);
            Warmup(deathPrefab);
        }

        private void OnEnable()
        {
            if (waveSpawner == null) return;
            waveSpawner.OnZoneSpawnStarted += HandleZonePreview;
            waveSpawner.OnEnemySpawned += HandleEnemySpawned;
        }

        private void OnDisable()
        {
            if (waveSpawner == null) return;
            waveSpawner.OnZoneSpawnStarted -= HandleZonePreview;
            waveSpawner.OnEnemySpawned -= HandleEnemySpawned;
        }

        /// <summary>Prechauffe le pool (instances creees au chargement, pas pendant la vague).</summary>
        private void Warmup(GameObject prefab)
        {
            if (prefab == null || profile == null) return;
            int n = profile.WarmupPerPrefab;
            var tmp = new GameObject[n];
            for (int i = 0; i < n; i++) tmp[i] = _pool.Spawn(prefab, Vector3.zero, Quaternion.identity);
            for (int i = 0; i < n; i++) _pool.Despawn(tmp[i]);
        }

        #endregion

        #region Evenements de vague

        private void HandleZonePreview(string zoneId, GameObject enemyPrefab, int count)
        {
            if (spawnPortalPrefab == null || !_zoneById.TryGetValue(zoneId, out Transform zone)) return;

            GameObject fx = _pool.Spawn(spawnPortalPrefab, zone.position, Quaternion.identity);
            if (fx != null && fx.TryGetComponent(out PooledParticleFx player)) player.SetLifetime(profile.SpawnPortalDuration);
        }

        private void HandleEnemySpawned(GameObject enemy, SpawnZone zone)
        {
            if (enemyBindingPrefab == null || enemy == null) return;
            if (_activeBindings >= profile.MaxEnemyBindings) return;
            if (!enemy.TryGetComponent(out Health health)) return;

            GameObject go = _pool.Spawn(enemyBindingPrefab, enemy.transform.position, Quaternion.identity);
            if (go == null || !go.TryGetComponent(out EnemyVfxBinding binding)) return;

            _activeBindings++;
            binding.Bind(enemy.transform, health, this, profile.EnemyGlowHeight, _onBindingReleased);
        }

        private void OnBindingReleased() => _activeBindings = Mathf.Max(0, _activeBindings - 1);

        #endregion

        #region IEnemyVfxSink

        public void PlayHit(Vector3 position)
        {
            if (hitPrefab != null) _pool.Spawn(hitPrefab, position, Quaternion.identity);
        }

        public void PlayDeath(Vector3 position)
        {
            if (deathPrefab != null) _pool.Spawn(deathPrefab, position, Quaternion.identity);
        }

        #endregion
    }
}
