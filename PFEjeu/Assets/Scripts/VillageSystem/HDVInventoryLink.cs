using UnityEngine;
using Core.Village;
using Core.InventorySystem.UI;

namespace Core.Village
{
    /// <summary>
    /// Ouvre/ferme l'inventaire de l'HDV en même temps que la vue village.
    /// </summary>
    public sealed class HDVInventoryLink : MonoBehaviour
    {
        [SerializeField] private VillageViewController villageView;
        [SerializeField] private InventoryUI hdvInventoryUI;
        [Tooltip("Résumé des ressources de l'HDV (optionnel), affiché avec la vue village.")]
        [SerializeField] private GameObject resourceBarUI;

        private void OnEnable()
        {
            if (villageView == null) return;
            villageView.OnEnteredVillageView += Show;
            villageView.OnExitedVillageView += Hide;
        }

        private void OnDisable()
        {
            if (villageView == null) return;
            villageView.OnEnteredVillageView -= Show;
            villageView.OnExitedVillageView -= Hide;
        }

        private void Show()
        {
            hdvInventoryUI?.SetOpen(true);
            if (resourceBarUI != null) resourceBarUI.SetActive(true);
        }

        private void Hide()
        {
            hdvInventoryUI?.SetOpen(false);
            if (resourceBarUI != null) resourceBarUI.SetActive(false);
        }
    }
}