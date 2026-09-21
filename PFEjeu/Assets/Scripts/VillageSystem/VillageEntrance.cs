using UnityEngine;
using Core.InventorySystem;

namespace Core.Village
{
    /// <summary>Zone d'entrée du village : transfère les ressources du joueur vers l'HDV.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class VillageEntrance : MonoBehaviour
    {
        [SerializeField] private VillageManager villageManager;

        private void OnTriggerEnter(Collider other)
        {
            InventoryHolder holder = other.GetComponentInParent<InventoryHolder>();
            if (holder != null && villageManager != null)
                villageManager.TransferPlayerResourcesToHDV(holder);
        }
    }
}