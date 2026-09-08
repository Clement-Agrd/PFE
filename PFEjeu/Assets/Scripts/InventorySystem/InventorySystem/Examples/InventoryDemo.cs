using UnityEngine;

namespace Core.InventorySystem.Examples
{
    /// <summary>
    /// Démo : s'abonne aux events de l'inventaire et enchaîne quelques opérations
    /// au démarrage. Place ce composant sur le MÊME GameObject qu'un InventoryHolder.
    /// </summary>
    [RequireComponent(typeof(InventoryHolder))]
    public sealed class InventoryDemo : MonoBehaviour
    {
        [SerializeField] private ItemDefinition itemToTest;
        [SerializeField] private int amountToAdd = 150;
        [SerializeField] private int amountToRemove = 60;

        private InventoryHolder _holder;

        private void Awake() => _holder = GetComponent<InventoryHolder>();

        private void OnEnable()
        {
            // Tous les Awake tournent avant tout OnEnable → l'Inventory existe déjà.
            _holder.Inventory.OnChanged += HandleChanged;
            _holder.Inventory.OnSlotChanged += HandleSlotChanged;
        }

        private void OnDisable()
        {
            if (_holder == null || _holder.Inventory == null) return;
            _holder.Inventory.OnChanged -= HandleChanged;
            _holder.Inventory.OnSlotChanged -= HandleSlotChanged;
        }

        private void Start()
        {
            if (itemToTest == null)
            {
                Debug.LogWarning("[Demo] Assigne un item à tester dans l'Inspector.");
                return;
            }

            Inventory inv = _holder.Inventory;

            // Ex : ajouter 150 d'un item à maxStack 99 → 99 + 51 sur 2 slots.
            int leftover = inv.Add(itemToTest, amountToAdd);
            Debug.Log($"[Demo] Ajout {amountToAdd} {itemToTest.DisplayName} → non rentré : {leftover}. Total : {inv.Count(itemToTest)}.");

            // Puis retirer une partie.
            int removed = inv.Remove(itemToTest, amountToRemove);
            Debug.Log($"[Demo] Retiré {removed}. Total restant : {inv.Count(itemToTest)}.");

            // Enfin, utiliser l'item (polymorphisme : effet selon le type dérivé).
            itemToTest.Use(gameObject);
        }

        private void HandleChanged() => Debug.Log("[Demo] Inventaire modifié.");
        private void HandleSlotChanged(int index) => Debug.Log($"[Demo] Slot {index} mis à jour.");
    }
}
