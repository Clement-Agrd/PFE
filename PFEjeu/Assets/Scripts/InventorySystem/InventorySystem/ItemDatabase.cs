using System.Collections.Generic;
using UnityEngine;

namespace Core.InventorySystem
{
    /// <summary>
    /// Registre de tous les ItemDefinition du jeu. Résout un id (string) → définition.
    /// Indispensable au chargement d'une sauvegarde : la save stocke des ids, pas
    /// des références d'assets. Le dictionnaire est construit à la 1re demande.
    /// Création : Assets > Create > Inventory > Item Database
    /// </summary>
    [CreateAssetMenu(fileName = "ItemDatabase", menuName = "Inventory/Item Database")]
    public sealed class ItemDatabase : ScriptableObject
    {
        [SerializeField] private List<ItemDefinition> items = new();

        private Dictionary<string, ItemDefinition> _lookup;

        public ItemDefinition GetById(string id)
        {
            EnsureLookup();
            return _lookup.TryGetValue(id, out ItemDefinition def) ? def : null;
        }

        private void EnsureLookup()
        {
            if (_lookup != null) return;

            _lookup = new Dictionary<string, ItemDefinition>(items.Count);
            foreach (ItemDefinition item in items)
            {
                if (item == null || string.IsNullOrEmpty(item.Id)) continue;

                if (!_lookup.TryAdd(item.Id, item))
                    Debug.LogWarning($"[ItemDatabase] Id en double ignoré : '{item.Id}'.");
            }
        }

        // Force la reconstruction du cache au (re)chargement de l'asset.
        private void OnEnable() => _lookup = null;
    }
}
