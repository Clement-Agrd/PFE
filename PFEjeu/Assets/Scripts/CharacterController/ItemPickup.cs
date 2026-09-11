using UnityEngine;

namespace Core.InventorySystem
{
    [RequireComponent(typeof(Collider))]
    public class ItemPickup : MonoBehaviour
    {
        [Header("Données")]
        [SerializeField] private ItemDefinition itemDefinition;
        [SerializeField] private int quantity = 1;

        public void Setup(ItemDefinition item, int count)
        {
            itemDefinition = item;
            quantity = count;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (itemDefinition == null) return;

            // Cherche le composant sur l'objet touché OU sur ses parents
            InventoryHolder holder = other.GetComponentInParent<InventoryHolder>();
            
            if (holder != null)
            {
                int remaining = holder.Inventory.Add(itemDefinition, quantity);

                if (remaining <= 0)
                {
                    Destroy(gameObject);
                }
                else
                {
                    quantity = remaining;
                }
            }
        }
    }
}