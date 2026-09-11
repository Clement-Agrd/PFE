using UnityEngine;

namespace Core.InventorySystem
{
    [RequireComponent(typeof(Collider))]
    public class ItemPickup : MonoBehaviour
    {
        public ItemDefinition Definition;
        public int Quantity = 1;

        public void Setup(ItemDefinition item, int count)
        {
            Definition = item;
            Quantity = count;
        }

        /// <summary>Génère le texte dynamique (ex: "[E] Ramasser Bois (x5)" ou "[E] Boire Potion")</summary>
        public string GetPromptText(KeyCode interactKey)
        {
            if (Definition == null) return string.Empty;

            string verb = Definition.InteractionVerb; // Ou juste "Ramasser" si tu n'as pas modifié ItemDefinition
            string quantityText = Quantity > 1 ? $" (x{Quantity})" : "";

            return $"[{interactKey}] {verb} {Definition.DisplayName}{quantityText}";
        }
    }
}