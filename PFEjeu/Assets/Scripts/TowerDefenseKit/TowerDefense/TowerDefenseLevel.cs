using UnityEngine;
using Core.WaveSystem;
using Core.ShopSystem;
using Core.InventorySystem;

namespace Core.TowerDefense
{
    /// <summary>
    /// Coordinateur d'un niveau de tower defense (version NavMesh) : câble le
    /// WaveSpawner, l'objectif à défendre, la base, le porte-monnaie et l'inventaire.
    /// Chaque ennemi qui apparaît reçoit sa cible et ses références.
    /// </summary>
    public sealed class TowerDefenseLevel : MonoBehaviour
    {
        [SerializeField] private WaveSpawner spawner;
        [Tooltip("L'objectif vers lequel les ennemis courent (base, cristal...).")]
        [SerializeField] private Transform navTarget;
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

        public bool CanStartWave => spawner != null && spawner.CanStartWave;

        private void HandleEnemySpawned(GameObject enemy)
        {
            if (enemy.TryGetComponent(out NavEnemy nav))
                nav.Initialize(navTarget, playerBase, wallet, playerInventory);
        }

        private void HandleWaveStarted(int wave) => Debug.Log($"[TD] Vague {wave} lancée.");
        private void HandleWaveCompleted(int wave) => Debug.Log($"[TD] Vague {wave} nettoyée !");
        private void HandleAllCompleted() => Debug.Log("[TD] Toutes les vagues terminées — victoire !");
        private void HandleBaseHealth(int cur, int max) => Debug.Log($"[TD] Base : {cur}/{max} PV.");
        private void HandleDefeat() => Debug.Log("[TD] Base détruite — défaite.");
    }
}