using UnityEngine;
using Core.InventorySystem;

namespace Core.LootSystem.Examples
{
    /// <summary>
    /// Démo : roule une table à chaque appui sur Espace, logge le butin, et
    /// l'ajoute à un inventaire si un InventoryHolder est assigné.
    /// </summary>
    public sealed class LootDemo : MonoBehaviour
    {
        [SerializeField] private LootTable table;
        [Tooltip("Optionnel : où déposer le butin.")]
        [SerializeField] private InventoryHolder inventory;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
                RollLoot();
        }

        private void RollLoot()
        {
            if (table == null)
            {
                Debug.LogWarning("[LootDemo] Assigne une LootTable.");
                return;
            }

            var results = table.Roll();
            if (results.Count == 0)
            {
                Debug.Log("[Loot] Rien n'est tombé.");
                return;
            }

            foreach (LootResult result in results)
            {
                Debug.Log($"[Loot] {result.Item.DisplayName} ×{result.Amount}");

                if (inventory != null && inventory.Inventory != null)
                    inventory.Inventory.Add(result.Item, result.Amount);
            }
        }
    }
}
