using UnityEngine;

using Core.WaveSystem;
using Core.ShopSystem;
using Core.InventorySystem;

namespace Core.TowerDefense
{
    /// <summary>
    /// Coordinateur du niveau.
    ///
    /// Il relie :
    /// - WaveSpawner
    /// - SpawnZone
    /// - EnemySplinePath
    /// - Wallet
    /// - Inventory
    ///
    /// Il n'y a plus de PlayerBase ni de NavTarget.
    /// </summary>
    public sealed class TowerDefenseLevel :
        MonoBehaviour
    {
        // ============================================================
        // WAVES
        // ============================================================

        [Header("Waves")]

        [SerializeField]
        private WaveSpawner spawner;


        // ============================================================
        // PATH
        // ============================================================

        [Header("Chemin de secours")]

        [Tooltip(
            "Optionnel. Utilisé si une SpawnZone n'a pas d'EnemySplinePath."
        )]
        [SerializeField]
        private EnemySplinePath defaultPath;


        // ============================================================
        // REWARDS
        // ============================================================

        [Header("Récompenses")]

        [Tooltip(
            "Optionnel : porte-monnaie recevant l'or des ennemis tués."
        )]
        [SerializeField]
        private Wallet wallet;


        [Tooltip(
            "Optionnel : inventaire recevant les drops des ennemis tués."
        )]
        [SerializeField]
        private InventoryHolder playerInventory;


        // ============================================================
        // PUBLIC
        // ============================================================

        public bool CanStartWave =>
            spawner != null &&
            spawner.CanStartWave;


        // ============================================================
        // UNITY
        // ============================================================

        private void OnEnable()
        {
            if (spawner == null)
                return;


            spawner.OnEnemySpawned +=
                HandleEnemySpawned;


            spawner.OnWaveStarted +=
                HandleWaveStarted;


            spawner.OnWaveCompleted +=
                HandleWaveCompleted;


            spawner.OnAllWavesCompleted +=
                HandleAllCompleted;
        }


        private void OnDisable()
        {
            if (spawner == null)
                return;


            spawner.OnEnemySpawned -=
                HandleEnemySpawned;


            spawner.OnWaveStarted -=
                HandleWaveStarted;


            spawner.OnWaveCompleted -=
                HandleWaveCompleted;


            spawner.OnAllWavesCompleted -=
                HandleAllCompleted;
        }


        // ============================================================
        // WAVE
        // ============================================================

        /// <summary>
        /// À brancher sur le bouton "Vague suivante".
        /// </summary>
        public void StartNextWave()
        {
            if (spawner != null)
            {
                spawner.StartNextWave();
            }
        }


        // ============================================================
        // ENEMY
        // ============================================================

        private void HandleEnemySpawned(
            GameObject enemy,
            SpawnZone spawnZone)
        {
            if (enemy == null)
                return;


            if (!enemy.TryGetComponent(
                    out NavEnemy navEnemy))
            {
                Debug.LogWarning(
                    $"[TowerDefenseLevel] " +
                    $"{enemy.name} possède IWaveEnemy " +
                    $"mais pas NavEnemy.",
                    enemy
                );

                return;
            }


            // ========================================================
            // PATH
            // ========================================================

            EnemySplinePath selectedPath =
                null;


            if (spawnZone != null)
            {
                selectedPath =
                    spawnZone.EnemyPath;
            }


            if (selectedPath == null)
            {
                selectedPath =
                    defaultPath;
            }


            if (selectedPath == null)
            {
                Debug.LogError(
                    $"[TowerDefenseLevel] " +
                    $"Aucune spline n'est configurée pour {enemy.name}. " +
                    $"SpawnZone = " +
                    $"{(spawnZone != null ? spawnZone.name : "None")}.",
                    enemy
                );
            }
            else
            {
                navEnemy.SetPath(
                    selectedPath
                );
            }


            // ========================================================
            // INITIALIZATION
            // ========================================================

            navEnemy.Initialize(
                wallet,
                playerInventory
            );
        }


        // ============================================================
        // EVENTS
        // ============================================================

        private void HandleWaveStarted(
            int wave)
        {
            Debug.Log(
                $"[TD] Vague {wave} lancée."
            );
        }


        private void HandleWaveCompleted(
            int wave)
        {
            Debug.Log(
                $"[TD] Vague {wave} nettoyée !"
            );
        }


        private void HandleAllCompleted()
        {
            Debug.Log(
                "[TD] Toutes les vagues terminées — victoire !"
            );
        }
    }
}