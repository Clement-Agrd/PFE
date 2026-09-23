using UnityEngine;
using UnityEngine.InputSystem;

namespace Core.Village
{
    /// <summary>
    /// Zone d'interaction pour la table de l'HDV : approche-toi, appuie sur E,
    /// la vue village s'active.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class VillageTableTrigger : MonoBehaviour
    {
        [SerializeField] private VillageViewController villageView;
        [Tooltip("Texte affiché quand le joueur est dans la zone (optionnel).")]
        [SerializeField] private GameObject promptUI;

        private bool _playerInRange;

        private void Update()
        {
            if (!_playerInRange) return;
            if (villageView != null && villageView.IsInVillageView) return;

            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                villageView?.EnterVillageView();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;

            _playerInRange = true;
            if (promptUI != null) promptUI.SetActive(true);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;

            _playerInRange = false;
            if (promptUI != null) promptUI.SetActive(false);
        }
    }
}