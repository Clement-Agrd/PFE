using UnityEngine;
using Core.WaveSystem;
using Core.ShopSystem;
using Core.InventorySystem;

namespace Core.TowerDefense
{
    /// <summary>
    /// Coordinateur d'un niveau de tower defense : câble le WaveSpawner, le chemin,
    /// la base, le porte-monnaie et l'inventaire du joueur. Chaque ennemi qui
    /// apparaît reçoit ses références (chemin + base + wallet + inventaire pour les drops).
    /// Expose StartNextWave() à brancher sur ton bouton « Vague suivante ».
    /// </summary>
    public sealed class TowerDefenseLevel : MonoBehaviour
    {
        [SerializeField] private WaveSpawner spawner;
        [SerializeField] private WaypointPath path;
        [SerializeField] private PlayerBase playerBase;
        [Tooltip("Optionnel : porte-monnaie du joueur (or gagné aux kills).")]
        [SerializeField] private Wallet wallet;
        [Tooltip("Optionnel : inventaire du joueur (où atterrissent les drops).")]
        [SerializeField] private InventoryHolder playerInventory;

        private void OnEnable()
        {
            if (spawner != null)
            {
                spawner.OnEnemySpawned += HandleEnemySpawned;
                spawner.OnWaveStarted += HandleWaveStarted;
                spawner.OnWaveCompleted += HandleWaveCompleted;
                spawner.OnAllWavesCompleted += HandleAllCompleted;
            }
            if (playerBase != null)
            {
                playerBase.OnHealthChanged += HandleBaseHealth;
                playerBase.OnDestroyed += HandleDefeat;
            }
        }

        private void OnDisable()
        {
            if (spawner != null)
            {
                spawner.OnEnemySpawned -= HandleEnemySpawned;
                spawner.OnWaveStarted -= HandleWaveStarted;
                spawner.OnWaveCompleted -= HandleWaveCompleted;
                spawner.OnAllWavesCompleted -= HandleAllCompleted;
            }
            if (playerBase != null)
            {
                playerBase.OnHealthChanged -= HandleBaseHealth;
                playerBase.OnDestroyed -= HandleDefeat;
            }
        }

        /// <summary>À brancher sur le bouton « Vague suivante » (OnClick).</summary>
        public void StartNextWave()
        {
            if (spawner != null) spawner.StartNextWave();
        }

        /// <summary>Le bouton doit-il être cliquable ? (à lire dans ton UI)</summary>
        public bool CanStartWave => spawner != null && spawner.CanStartWave;

        private void HandleEnemySpawned(GameObject enemy)
        {
            if (enemy.TryGetComponent(out TowerDefenseEnemy tdEnemy))
                tdEnemy.Initialize(path, playerBase, wallet, playerInventory);
        }

        private void HandleWaveStarted(int wave) => Debug.Log($"[TD] Vague {wave} lancée.");
        private void HandleWaveCompleted(int wave) => Debug.Log($"[TD] Vague {wave} nettoyée ! (clique pour la suivante)");
        private void HandleAllCompleted() => Debug.Log("[TD] Toutes les vagues terminées — victoire !");
        private void HandleBaseHealth(int cur, int max) => Debug.Log($"[TD] Base : {cur}/{max} PV.");
        private void HandleDefeat() => Debug.Log("[TD] Base détruite — défaite.");
    }
}
