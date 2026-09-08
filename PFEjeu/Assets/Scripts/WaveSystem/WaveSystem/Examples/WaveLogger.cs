using UnityEngine;

namespace Core.WaveSystem.Examples
{
    /// <summary>
    /// Démo : logge les events du spawner (vague démarrée/finie, ennemi spawné,
    /// tout terminé). Pose ce composant à côté d'un WaveSpawner.
    /// </summary>
    [RequireComponent(typeof(WaveSpawner))]
    public sealed class WaveLogger : MonoBehaviour
    {
        private WaveSpawner _spawner;

        private void Awake() => _spawner = GetComponent<WaveSpawner>();

        private void OnEnable()
        {
            _spawner.OnWaveStarted += HandleWaveStarted;
            _spawner.OnWaveCompleted += HandleWaveCompleted;
            _spawner.OnAllWavesCompleted += HandleAllCompleted;
            _spawner.OnEnemySpawned += HandleEnemySpawned;
        }

        private void OnDisable()
        {
            _spawner.OnWaveStarted -= HandleWaveStarted;
            _spawner.OnWaveCompleted -= HandleWaveCompleted;
            _spawner.OnAllWavesCompleted -= HandleAllCompleted;
            _spawner.OnEnemySpawned -= HandleEnemySpawned;
        }

        private void HandleWaveStarted(int wave)
            => Debug.Log($"[Wave] ▶ Vague {wave} démarrée (boucle {_spawner.Loop}).");

        private void HandleWaveCompleted(int wave)
            => Debug.Log($"[Wave] ✔ Vague {wave} nettoyée.");

        private void HandleAllCompleted()
            => Debug.Log("[Wave] 🏁 Toutes les vagues terminées !");

        private void HandleEnemySpawned(GameObject enemy)
            => Debug.Log($"[Wave] Spawn : {enemy.name} (vivants : {_spawner.AliveCount}).");
    }
}
