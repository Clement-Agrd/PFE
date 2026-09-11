using UnityEngine;
using TMPro;

namespace Core.InventorySystem
{
    public class PlayerInteractor : MonoBehaviour
    {
        [Header("Références")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private InventoryHolder inventoryHolder;

        [Header("Interface UI")]
        [SerializeField] private GameObject interactUI;
        [SerializeField] private TextMeshProUGUI interactText;

        [Header("Paramètres")]
        [SerializeField] private float interactRange = 3f;
        [SerializeField] private KeyCode interactKey = KeyCode.E;

        private void Update()
        {
            if (playerCamera == null || inventoryHolder == null) return;

            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            bool isLookingAtItem = false;

            if (Physics.Raycast(ray, out RaycastHit hit, interactRange))
            {
                ItemPickup pickup = hit.collider.GetComponentInParent<ItemPickup>();

                if (pickup != null && pickup.Definition != null)
                {
                    isLookingAtItem = true;

                    // Le texte se met à jour automatiquement avec le nom, le verbe et la quantité
                    if (interactText != null)
                    {
                        interactText.text = pickup.GetPromptText(interactKey);
                    }

                    if (Input.GetKeyDown(interactKey))
                    {
                        int remaining = inventoryHolder.Inventory.Add(pickup.Definition, pickup.Quantity);

                        if (remaining <= 0)
                        {
                            Destroy(pickup.gameObject);
                            isLookingAtItem = false;
                        }
                        else
                        {
                            pickup.Quantity = remaining;
                        }
                    }
                }
            }

            if (interactUI != null)
            {
                interactUI.SetActive(isLookingAtItem);
            }
        }
    }
}