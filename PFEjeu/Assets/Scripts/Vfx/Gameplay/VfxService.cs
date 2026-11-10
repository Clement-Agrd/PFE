using System.Collections;
using System.Collections.Generic;
using Core.TowerDefense;
using Core.BarracksSystem;
using Core.Village;
using SpiritVFX;
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
        [SerializeField] private SpiritGate spiritPortalPrefab;

        [Header("Bâtiments")]
        [SerializeField] private DefenseConstructionUI construction;
        [SerializeField] private VillageManager village;
        [SerializeField] private BuildingEffects buildingEffectsPrefab;
        [SerializeField] private GameObject enemyBindingPrefab;
        [SerializeField] private GameObject hitPrefab;
        [SerializeField] private GameObject deathPrefab;

        private readonly Dictionary<string, Transform> _zoneById = new Dictionary<string, Transform>();
        private readonly Dictionary<Barracks, SpiritGate> _portals = new();
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
            if (waveSpawner != null)
            {
                waveSpawner.OnZoneSpawnStarted += HandleZonePreview;
                waveSpawner.OnEnemySpawned += HandleEnemySpawned;
            }
            Barracks.SoldierSpawned += HandleSoldierSpawned;
            if (construction != null) construction.OnBuildingConstructed += HandleConstruction;
            if (village != null) village.OnBuildingUpgraded += HandleUpgrade;
        }

        private void OnDisable()
        {
            if (waveSpawner != null)
            {
                waveSpawner.OnZoneSpawnStarted -= HandleZonePreview;
                waveSpawner.OnEnemySpawned -= HandleEnemySpawned;
            }
            Barracks.SoldierSpawned -= HandleSoldierSpawned;
            if (construction != null) construction.OnBuildingConstructed -= HandleConstruction;
            if (village != null) village.OnBuildingUpgraded -= HandleUpgrade;
            StopAllCoroutines();
            foreach (var portal in _portals.Values)
                if (portal != null) Destroy(portal.gameObject);
            _portals.Clear();
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
            if (!_zoneById.TryGetValue(zoneId, out Transform zone)) return;
            if (spawnPortalPrefab == null) return;
            GameObject fx = _pool.Spawn(spawnPortalPrefab, zone.position, Quaternion.identity);
            if (fx != null && fx.TryGetComponent(out PooledParticleFx player)) player.SetLifetime(profile.SpawnPortalDuration);
        }

        private void HandleSoldierSpawned(Barracks barracks, Vector3 position)
        {
            if (spiritPortalPrefab == null || barracks.gameObject.scene != gameObject.scene ||
                _portals.ContainsKey(barracks)) return;

            var rotation = Quaternion.Euler(0f, barracks.transform.eulerAngles.y, 0f);
            var portal = Instantiate(spiritPortalPrefab, position, rotation, transform);
            _portals.Add(barracks, portal);
            StartCoroutine(PlayPortal(barracks, portal));
        }

        private IEnumerator PlayPortal(Barracks barracks, SpiritGate portal)
        {
            portal.Open();
            yield return new WaitForSecondsRealtime(1.1f);
            if (portal != null)
            {
                portal.Close();
                yield return new WaitUntil(() => portal == null || portal.State == SpiritState.Closed);
                if (portal != null) Destroy(portal.gameObject);
            }
            _portals.Remove(barracks);
        }

        private BuildingEffects GetBuildingEffects(Building building)
        {
            if (building == null || buildingEffectsPrefab == null) return null;
            var effects = building.GetComponentInChildren<BuildingEffects>();
            return effects != null ? effects : Instantiate(buildingEffectsPrefab, building.transform);
        }

        private void HandleConstruction(Building building) => GetBuildingEffects(building)?.PlayBuild();
        private void HandleUpgrade(Building building, int level) => GetBuildingEffects(building)?.PlayUpgrade(level);

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
