using System;
using UnityEngine;

namespace Core.ShopSystem
{
    /// <summary>
    /// Porte-monnaie : une réserve de monnaie (or, pièces...). Add pour gagner,
    /// Spend pour dépenser (échoue si insuffisant), CanAfford pour tester.
    /// </summary>
    public sealed class Wallet : MonoBehaviour
    {
        [SerializeField, Min(0)] private int balance;

        public int Balance => balance;

        public event Action<int> OnBalanceChanged;

        public bool CanAfford(int amount) => amount <= 0 || balance >= amount;

        public void Add(int amount)
        {
            if (amount <= 0) return;
            balance += amount;
            OnBalanceChanged?.Invoke(balance);
        }

        /// <summary>Dépense 'amount' si possible. Renvoie false si le solde est insuffisant.</summary>
        public bool Spend(int amount)
        {
            if (amount < 0 || balance < amount) return false;

            balance -= amount;
            OnBalanceChanged?.Invoke(balance);
            return true;
        }

        // --- Sauvegarde ---
        public void SetBalance(int value)
        {
            balance = Mathf.Max(0, value);
            OnBalanceChanged?.Invoke(balance);
        }
    }
}
