using System;
using UnityEngine;

namespace Core.InventorySystem
{
    public sealed class Inventory
    {
        private readonly ItemStack[] _slots;

        public int Capacity => _slots.Length;

        public event Action OnChanged;
        public event Action<int> OnSlotChanged;

        public Inventory(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentException("La capacité doit être > 0.", nameof(capacity));

            _slots = new ItemStack[capacity];
        }

        public ItemStack GetSlot(int index) => _slots[index];

        #region Ajout / Retrait

        public int Add(ItemDefinition definition, int amount)
        {
            if (definition == null || amount <= 0) return amount;

            int remaining = amount;

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

        /// <summary>Retire jusqu'à 'amount' items d'un type donné (utilisé par le Shop).</summary>
        public int Remove(ItemDefinition definition, int amount)
        {
            if (definition == null || amount <= 0) return 0;

            int removed = 0;

            for (int i = _slots.Length - 1; i >= 0 && removed < amount; i--)
            {
                ItemStack slot = _slots[i];
                if (slot == null || !slot.Matches(definition)) continue;

                int justRemoved = slot.Remove(amount - removed);
                removed += justRemoved;

                if (slot.IsEmpty) _slots[i] = null;
                if (justRemoved > 0) RaiseSlot(i);
            }

            if (removed > 0) OnChanged?.Invoke();
            return removed;
        }

        /// <summary>Retire des objets d'une case précise (utilisé pour le Drop au sol).</summary>
        public int RemoveAt(int index, int amount = -1)
        {
            if (index < 0 || index >= _slots.Length) return 0;
            ItemStack slot = _slots[index];
            if (slot == null || slot.IsEmpty) return 0;

            int toRemove = (amount <= 0) ? slot.Quantity : Mathf.Min(amount, slot.Quantity);
            int removed = slot.Remove(toRemove);

            if (slot.IsEmpty) _slots[index] = null;

            RaiseSlot(index);
            OnChanged?.Invoke();
            return removed;
        }

        #endregion

        #region Échange et Déplacement de Slots

        public void SwapSlots(int indexA, int indexB)
        {
            if (indexA == indexB || indexA < 0 || indexA >= _slots.Length || indexB < 0 || indexB >= _slots.Length) return;

            ItemStack slotA = _slots[indexA];
            ItemStack slotB = _slots[indexB];

            if (slotA == null || slotA.IsEmpty) return;

            if (slotB != null && !slotB.IsEmpty && slotB.Matches(slotA.Definition) && slotA.Definition.IsStackable)
            {
                int remaining = slotB.Add(slotA.Quantity);
                if (remaining <= 0)
                {
                    _slots[indexA] = null;
                }
                else
                {
                    slotA.Remove(slotA.Quantity - remaining);
                }
            }
            else
            {
                _slots[indexA] = slotB;
                _slots[indexB] = slotA;
            }

            RaiseSlot(indexA);
            RaiseSlot(indexB);
            OnChanged?.Invoke();
        }

        #endregion

        #region Requêtes (Shop & Tests)

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

        private void RaiseSlot(int index) => OnSlotChanged?.Invoke(index);
    }
}