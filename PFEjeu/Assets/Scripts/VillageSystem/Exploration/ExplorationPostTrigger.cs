using Core.Village.Exploration.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Core.Village.Exploration
{
    /// <summary>
    /// Ouvre le panneau du poste d'expédition en marchant dessus et en appuyant
    /// sur E (même pattern que <see cref="VillageTableTrigger"/>) : le prompt
    /// suit _playerInRange à chaque frame, indépendamment de tout le reste.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class ExplorationPostTrigger : MonoBehaviour
    {
        [SerializeField] private ExplorationPanelUI panel;
        [Tooltip("Texte affiché quand le joueur est dans la zone.")]
        [SerializeField] private GameObject promptUI;
        [SerializeField] private string promptMessage = "[E] Poste d'expédition";

        private bool _playerInRange;
        private TMP_Text _promptText;

        private void Awake()
        {
            if (promptUI != null) _promptText = promptUI.GetComponentInChildren<TMP_Text>(true);
        }

        private void Update()
        {
            bool shouldShowPrompt = _playerInRange && panel != null && !panel.IsShown;
            if (promptUI != null && promptUI.activeSelf != shouldShowPrompt)
            {
                if (shouldShowPrompt && _promptText != null) _promptText.text = promptMessage;
                promptUI.SetActive(shouldShowPrompt);
            }

            if (!_playerInRange || panel == null || panel.IsShown) return;

            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                panel.Show();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _playerInRange = true;
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _playerInRange = false;
        }
    }
}
