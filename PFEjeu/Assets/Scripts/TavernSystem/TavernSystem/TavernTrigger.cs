using TMPro;
using UnityEngine;
using Core.Village;
using Core.TavernSystem.UI;

namespace Core.TavernSystem
{
    /// <summary>
    /// Zone d'interaction de la taverne : approche-toi, appuie sur E, la carte
    /// s'ouvre (même pattern que VillageTableTrigger / ExplorationPostTrigger).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(Tavern))]
    public sealed class TavernTrigger : MonoBehaviour
    {
        [SerializeField] private TavernPanelUI panel;
        [SerializeField] private VillageInputReader input;
        [Tooltip("Texte affiché quand le joueur est dans la zone.")]
        [SerializeField] private GameObject promptUI;
        [SerializeField] private string promptMessage = "[E] Taverne";

        private Tavern _tavern;
        private bool _playerInRange;
        private TMP_Text _promptText;

        private void Awake()
        {
            _tavern = GetComponent<Tavern>();
            if (promptUI != null) _promptText = promptUI.GetComponentInChildren<TMP_Text>(true);
        }

        private void OnEnable()
        {
            if (input != null) input.InteractPressed += HandleInteractPressed;
        }

        private void OnDisable()
        {
            if (input != null) input.InteractPressed -= HandleInteractPressed;
        }

        private void Update()
        {
            bool shouldShowPrompt = _playerInRange && panel != null && !panel.IsShown;
            if (promptUI != null && promptUI.activeSelf != shouldShowPrompt)
            {
                if (shouldShowPrompt && _promptText != null) _promptText.text = promptMessage;
                promptUI.SetActive(shouldShowPrompt);
            }
        }

        private void HandleInteractPressed()
        {
            if (!_playerInRange || panel == null || panel.IsShown) return;
            panel.Show(_tavern);
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
