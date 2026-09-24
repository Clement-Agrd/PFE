using UnityEngine;
using Core.WaveSystem;
using Core.Village;

namespace Core.Village
{
    /// <summary>
    /// Téléporte le joueur sur la zone de défense au lancement d'une vague.
    /// Se déclenche uniquement au TOUT PREMIER OnWaveStarted d'une session
    /// (les vagues suivantes du même set ne re-téléportent pas le joueur,
    /// qui est censé défendre où il est).
    /// </summary>
    public sealed class PlayerTeleportOnDefense : MonoBehaviour
    {
        [Header("Références")]
        [SerializeField] private WaveSpawner waveSpawner;
        [Tooltip("Le joueur à téléporter (son Transform racine).")]
        [SerializeField] private Transform player;
        [Tooltip("Si le joueur utilise un CharacterController, il doit être désactivé le temps de la téléportation (sinon il ignore le déplacement direct).")]
        [SerializeField] private CharacterController playerController;
        [Tooltip("Optionnel : si le joueur est en vue village, on en sort d'abord proprement.")]
        [SerializeField] private VillageViewController villageView;

        [Header("Destination")]
        [Tooltip("Le point où le joueur apparaît pour défendre.")]
        [SerializeField] private Transform defenseSpawnPoint;

        private void OnEnable()
        {
            if (waveSpawner != null)
                waveSpawner.OnWaveStarted += HandleWaveStarted;
        }

        private void OnDisable()
        {
            if (waveSpawner != null)
                waveSpawner.OnWaveStarted -= HandleWaveStarted;
        }

        private void HandleWaveStarted(int waveNumber)
        {
            // Ne téléporte qu'au début d'une session (première vague du set).
            if (waveNumber != 1) return;

            if (villageView != null && villageView.IsInVillageView)
                villageView.ExitVillageView();

            Teleport();
        }

        private void Teleport()
        {
            if (player == null || defenseSpawnPoint == null) return;

            // Un CharacterController bloque toute modification directe de la
            // position s'il reste actif : on le coupe le temps du saut.
            if (playerController != null) playerController.enabled = false;

            player.SetPositionAndRotation(defenseSpawnPoint.position, defenseSpawnPoint.rotation);

            if (playerController != null) playerController.enabled = true;
        }
    }
}