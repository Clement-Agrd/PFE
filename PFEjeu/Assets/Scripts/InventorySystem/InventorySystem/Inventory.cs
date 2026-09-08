using System;
using UnityEngine;

namespace Core.InventorySystem
{
    /// <summary>
    /// Moteur d'inventaire : tableau de slots à capacité fixe. Gère le stacking,
    /// l'ajout avec débordement, le retrait, les requêtes et la (dé)sérialisation.
    /// Pur C# (aucun héritage MonoBehaviour) → testable hors Unity.
    /// </summary>
    public sealed class Inventory
    {
        private readonly ItemStack[] _slots; // null = slot vide

        public int Capacity => _slots.Length;

        /// <summary>Émis une fois après toute opération qui modifie l'inventaire.</summary>
        public event Action OnChanged;

        /// <summary>Émis pour chaque slot modifié (index). Idéal pour rafraîchir une seule case d'UI.</summary>
        public event Action<int> OnSlotChanged;

        public Inventory(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentException("La capacité doit être > 0.", nameof(capacity));

            _slots = new ItemStack[capacity];
        }

        public ItemStack GetSlot(int index) => _slots[index];

        #region Ajout / Retrait

        /// <summary>
        /// Ajoute 'amount' items. Remplit d'abord les piles existantes du même type,
        /// puis les slots vides. Renvoie la quantité qui n'a PAS pu être ajoutée
        /// (0 = tout est rentré, >0 = inventaire plein).
        /// </summary>
        public int Add(ItemDefinition definition, int amount)
        {
            if (definition == null || amount <= 0) return amount;

            int remaining = amount;

            // 1) Compléter les piles existantes du même type (si empilable).
            if (definition.IsStackable)
            {
                for (int i = 0; i < _slots.Length && remaining > 0; i++)
                {
                    ItemStack slot = _slots[i];
                    if (slot != null && slot.Matches(definition) && !slot.IsFull)
                    {
                        int before = remaining;
                        remaining = slot.Add(remaining);
                        if (remaining != before) RaiseSlot(i);
                    }
                }
            }

            // 2) Placer le reste dans des slots vides (nouvelles piles).
            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                if (_slots[i] == null)
                {
                    int toPlace = Mathf.Min(remaining, definition.MaxStackSize);
                    _slots[i] = new ItemStack(definition, toPlace);
                    remaining -= toPlace;
                    RaiseSlot(i);
                }
            }

            if (remaining != amount) OnChanged?.Invoke();
            return remaining;
        }

        /// <summary>Retire jusqu'à 'amount' items du type donné. Renvoie la quantité réellement retirée.</summary>
        public int Remove(ItemDefinition definition, int amount)
        {
            if (definition == null || amount <= 0) return 0;

            int removed = 0;

            // On vide les dernières piles d'abord (ordre stable et prévisible).
            for (int i = _slots.Length - 1; i >= 0 && removed < amount; i--)
            {
                ItemStack slot = _slots[i];
                if (slot == null || !slot.Matches(definition)) continue;

                int justRemoved = slot.Remove(amount - removed);
                removed += justRemoved;

                if (slot.IsEmpty) _slots[i] = null; // libère le slot
                if (justRemoved > 0) RaiseSlot(i);
            }

            if (removed > 0) OnChanged?.Invoke();
            return removed;
        }

        #endregion

        #region Requêtes

        public int Count(ItemDefinition definition)
        {
            if (definition == null) return 0;

            int total = 0;
            for (int i = 0; i < _slots.Length; i++)
            {
                ItemStack slot = _slots[i];
                if (slot != null && slot.Matches(definition)) total += slot.Quantity;
            }
            return total;
        }

        public bool Contains(ItemDefinition definition, int amount = 1)
            => Count(definition) >= amount;

        public bool HasFreeSlot()
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i] == null) return true;
            return false;
        }

        public void Clear()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null)
                {
                    _slots[i] = null;
                    RaiseSlot(i);
                }
            }
            OnChanged?.Invoke();
        }

        #endregion

        #region Sauvegarde

        /// <summary>Produit un instantané sérialisable (par id d'item).</summary>
        public InventorySaveData Capture()
        {
            var data = new InventorySaveData();
            for (int i = 0; i < _slots.Length; i++)
            {
                ItemStack slot = _slots[i];
                if (slot == null || slot.IsEmpty) continue;

                data.slots.Add(new InventorySaveData.SlotData
                {
                    index = i,
                    itemId = slot.Definition.Id,
                    quantity = slot.Quantity
                });
            }
            return data;
        }

        /// <summary>Reconstruit l'inventaire depuis un instantané, en résolvant les ids via la database.</summary>
        public void Restore(InventorySaveData data, ItemDatabase database)
        {
            Clear();
            if (data == null || database == null) return;

            foreach (InventorySaveData.SlotData s in data.slots)
            {
                if (s.index < 0 || s.index >= _slots.Length) continue;

                ItemDefinition def = database.GetById(s.itemId);
                if (def == null)
                {
                    Debug.LogWarning($"[Inventory] Item introuvable dans la database : '{s.itemId}'.");
                    continue;
                }

                _slots[s.index] = new ItemStack(def, s.quantity);
                RaiseSlot(s.index);
            }

            OnChanged?.Invoke();
        }

        #endregion

        private void RaiseSlot(int index) => OnSlotChanged?.Invoke(index);
    }
}
