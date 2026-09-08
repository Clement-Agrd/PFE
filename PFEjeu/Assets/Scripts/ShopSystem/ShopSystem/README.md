# Shop / Economy System

Achat, vente et monnaie, branchés sur ton Inventaire. Namespace :
`Core.ShopSystem`.

## ⚠️ Dépendance
Ce package référence l'**InventorySystem** (`ItemDefinition`, `Inventory`,
`InventoryHolder`). Importe-le d'abord.

## Installation
Dépose le dossier `ShopSystem/` dans `Assets/` (après l'InventorySystem).

## Mise en place
1. Sur le **joueur** : ajoute `Wallet` (règle le solde de départ).
2. Crée une boutique : `Assets > Create > Shop > Shop Definition`. Ajoute des
   articles (item + prix d'achat + prix de vente + stock initial, -1 = illimité).
3. Sur un **marchand** : ajoute `Shop`, assigne la ShopDefinition, le `Wallet` du
   joueur et son `InventoryHolder`.
4. Achète / vends :
   ```csharp
   shop.Buy(potionItem);   // acheter 1
   shop.Sell(oreItem, 3);  // vendre 3
   ```

## API essentielle
- `Wallet` : `Balance`, `Add(n)`, `Spend(n)` → bool, `CanAfford(n)`, event
  `OnBalanceChanged`.
- `Shop.Buy(item, amount = 1)` / `Sell(item, amount = 1)` → bool.
- `Shop.GetStock(item)` / `GetBuyPrice(item)` / `GetSellPrice(item)`.
- Events : `OnPurchased(entry, amount)`, `OnSold(item, amount, gold)`,
  `OnTransactionFailed(reason)`.

## Brancher une UI de boutique
```csharp
foreach (var entry in shopDefinition.Entries)
    CreateRow(entry.Item, entry.BuyPrice, shop.GetStock(entry.Item),
              onBuy: () => shop.Buy(entry.Item));

shop.OnTransactionFailed += reason => ShowToast(reason);
wallet.OnBalanceChanged  += b => goldLabel.text = b.ToString();
```

## Comportements clés
- **Achat partiel** : si l'inventaire ne peut prendre qu'une partie, seule cette
  partie est achetée et facturée (pas de perte d'or ni d'objet).
- **Stock** : suivi au runtime (l'asset reste intact). -1 = illimité. Vendre au
  marchand fait remonter son stock (il revend).
- **Échecs explicites** : chaque refus passe par `OnTransactionFailed(raison)`
  (« Pas assez d'argent », « Inventaire plein », « Stock insuffisant »...).

## Fichiers
- `Wallet.cs` — la monnaie (MonoBehaviour)
- `ShopEntry.cs` — un article (item + prix + stock)
- `ShopDefinition.cs` — la boutique (SO)
- `Shop.cs` — achat/vente + stock runtime (MonoBehaviour)
- `Examples/ShopDemo.cs` — acheter/vendre au clavier
- `Explications/` — documentation détaillée

## Brancher sur les autres systèmes
- **Inventory** : socle des transactions (items entrent/sortent).
- **Interaction** : marchand → `SimpleInteractable` → ouvrir l'UI de boutique.
- **Loot** : le butin remplit l'inventaire → matière à vendre.
- **Save System** : sauvegarde le `Wallet.Balance` (et éventuellement le stock).
- **Quest** : « dépense 100 or » via `OnPurchased`.

## Notes
- **Une seule monnaie** (int). Pour plusieurs (or + gemmes), duplique le Wallet
  par devise, ou passe à un Wallet multi-devises — dis-moi si tu veux la variante.
- Un marchand n'**achète** que les objets qu'il **liste** (il a un prix de vente
  pour eux). Pour racheter n'importe quel objet, il faudrait une valeur de base
  sur l'ItemDefinition — extension possible.

## Tester la démo
Crée un ItemDefinition (Potion), une ShopDefinition (Potion : achat 20 / vente 8,
stock 5). Sur le joueur : `InventoryHolder` + `Wallet` (solde 100). Sur un
marchand : `Shop` (assigne tout) + `ShopDemo` (assigne Shop, Wallet, Potion).
B = acheter, S = vendre. La Console montre solde, stock et échecs.
