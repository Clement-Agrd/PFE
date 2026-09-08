using UnityEngine;
using Core.InventorySystem;

namespace Core.ShopSystem.Examples
{
    /// <summary>
    /// Démo : B = acheter, S = vendre l'item de test. Logge solde, stock,
    /// et les échecs. Assigne le Shop, le Wallet et un item vendu par la boutique.
    /// </summary>
    public sealed class ShopDemo : MonoBehaviour
    {
        [SerializeField] private Shop shop;
        [SerializeField] private Wallet wallet;
        [SerializeField] private ItemDefinition testItem;

        private void OnEnable()
        {
            if (wallet != null) wallet.OnBalanceChanged += HandleBalance;
            if (shop != null)
            {
                shop.OnPurchased += HandlePurchased;
                shop.OnSold += HandleSold;
                shop.OnTransactionFailed += HandleFailed;
            }
        }

        private void OnDisable()
        {
            if (wallet != null) wallet.OnBalanceChanged -= HandleBalance;
            if (shop != null)
            {
                shop.OnPurchased -= HandlePurchased;
                shop.OnSold -= HandleSold;
                shop.OnTransactionFailed -= HandleFailed;
            }
        }

        private void Update()
        {
            if (testItem == null || shop == null) return;

            if (Input.GetKeyDown(KeyCode.B)) shop.Buy(testItem);
            if (Input.GetKeyDown(KeyCode.S)) shop.Sell(testItem);
        }

        private void HandleBalance(int balance) => Debug.Log($"[Shop] Solde : {balance}");

        private void HandlePurchased(ShopEntry entry, int amount)
            => Debug.Log($"[Shop] Acheté {amount} × {entry.Item.DisplayName} (stock restant : {shop.GetStock(entry.Item)}).");

        private void HandleSold(ItemDefinition item, int amount, int gold)
            => Debug.Log($"[Shop] Vendu {amount} × {item.DisplayName} → +{gold} or.");

        private void HandleFailed(string reason) => Debug.Log($"[Shop] ✘ {reason}");
    }
}
