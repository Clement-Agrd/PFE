using UnityEngine;

namespace Core.InventorySystem.UI
{
    /// <summary>
    /// Façade de l'inventaire pour les systèmes qui veulent l'ouvrir/fermer ou connaître son état
    /// (minimap...). Toute la logique (touches, pause, curseur) vit dans InventoryUI : avoir deux
    /// propriétaires de pause sur le même panneau figeait le jeu quand l'un le fermait sans l'autre.
    /// </summary>
    public class InventoryToggle : MonoBehaviour
    {
        [Tooltip("Laisser vide pour le retrouver automatiquement dans la scène.")]
        [SerializeField] private InventoryUI inventoryUI;

        public bool IsOpen => Resolve() != null && inventoryUI.IsOpen;

        public void SetInventoryState(bool open)
        {
            if (Resolve() != null) inventoryUI.SetOpen(open);
        }

        private InventoryUI Resolve()
        {
            if (inventoryUI == null) inventoryUI = FindFirstObjectByType<InventoryUI>();
            return inventoryUI;
        }
    }
}
