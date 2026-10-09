using UnityEngine;
using Core.InventorySystem.UI;

namespace Core.Minimap
{
    /// <summary>
    /// Ouvre la fenêtre d'inventaire directement sur l'onglet Carte
    /// (clic sur la minimap, ou touche M). Refermer avec la même touche.
    /// </summary>
    public sealed class MinimapOpener : MonoBehaviour
    {
        [SerializeField] private InventoryToggle inventoryToggle;
        [SerializeField] private InventoryTabController tabController;
        [SerializeField] private KeyCode toggleKey = KeyCode.M;

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey)) Toggle();
        }

        public void Toggle()
        {
            if (inventoryToggle == null || tabController == null) return;
            if (inventoryToggle.IsOpen && tabController.IsMapShown)
            {
                inventoryToggle.SetInventoryState(false);
                return;
            }
            if (!inventoryToggle.IsOpen) inventoryToggle.SetInventoryState(true);
            tabController.ShowMapTab();
        }

        public void Open()
        {
            if (inventoryToggle == null || tabController == null) return;
            if (!inventoryToggle.IsOpen) inventoryToggle.SetInventoryState(true);
            tabController.ShowMapTab();
        }
    }
}
