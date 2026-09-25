using System.Collections.Generic;
using UnityEngine;
using Core.InventorySystem;

namespace Core.Village.UI
{
    /// <summary>
    /// Barre compacte des ressources stockées à l'HDV (icône + nombre par
    /// ressource, façon jeu de gestion). Se met à jour en temps réel, sans
    /// afficher une grille d'inventaire complète.
    /// </summary>
    public sealed class ResourceBarUI : MonoBehaviour
    {
        [Header("Données")]
        [SerializeField] private InventoryHolder hdvStorage;
        [Tooltip("Les ressources à afficher, dans l'ordre. Réutilise idéalement la même liste que VillageManager.")]
        [SerializeField] private List<ItemDefinition> trackedResources = new();

        [Header("UI")]
        [SerializeField] private Transform rowsParent;
        [SerializeField] private ResourceBarRowUI rowPrefab;

        private Inventory _inventory;
        private readonly Dictionary<ItemDefinition, ResourceBarRowUI> _rows = new();

        private void Start()
        {
            if (hdvStorage == null) return;

            _inventory = hdvStorage.Inventory;
            if (_inventory == null) return;

            BuildRows();
            RefreshAll();

            _inventory.OnChanged += RefreshAll;
        }

        private void OnDestroy()
        {
            if (_inventory != null) _inventory.OnChanged -= RefreshAll;
        }

        private void BuildRows()
        {
            for (int i = rowsParent.childCount - 1; i >= 0; i--)
                Destroy(rowsParent.GetChild(i).gameObject);
            _rows.Clear();

            foreach (ItemDefinition item in trackedResources)
            {
                if (item == null) continue;

                ResourceBarRowUI row = Instantiate(rowPrefab, rowsParent);
                row.SetItem(item);
                _rows[item] = row;
            }
        }

        private void RefreshAll()
        {
            // Tout ce qui est dans le coffre de l'HDV doit apparaître, même si
            // ce n'est pas dans la liste de départ (loot, nouvelle ressource...).
            for (int i = 0; i < _inventory.Capacity; i++)
            {
                ItemStack slot = _inventory.GetSlot(i);
                if (slot == null || slot.IsEmpty || _rows.ContainsKey(slot.Definition)) continue;

                ResourceBarRowUI row = Instantiate(rowPrefab, rowsParent);
                row.SetItem(slot.Definition);
                _rows[slot.Definition] = row;
            }

            foreach (var pair in _rows)
                pair.Value.SetValue(_inventory.Count(pair.Key));
        }
    }
}