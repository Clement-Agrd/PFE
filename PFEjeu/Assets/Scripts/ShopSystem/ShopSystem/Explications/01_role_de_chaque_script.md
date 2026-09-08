# 01 — Rôle de chaque script

---

## `Wallet.cs` — la monnaie
```csharp
int Balance;
bool CanAfford(amount);
void Add(amount);           // gagner
bool Spend(amount);         // dépenser (false si insuffisant)
event OnBalanceChanged;
```
Une réserve d'or simple et sûre : `Spend` échoue proprement si le solde est
insuffisant (jamais de solde négatif). `SetBalance` sert à la sauvegarde.

---

## `ShopEntry.cs` — un article
```csharp
ItemDefinition item;
int buyPrice;      // prix pour le joueur
int sellPrice;     // ce que le marchand paie au rachat
int initialStock;  // -1 = illimité
```
Décrit ce qui est en vente et à quel prix. `sellPrice < buyPrice` en général
(le marchand fait sa marge).

---

## `ShopDefinition.cs` — la boutique (SO)
```csharp
string shopName;
List<ShopEntry> entries;
bool canSellHere;  // le joueur peut-il vendre ici ?
```
Un asset = une échoppe. Immuable : c'est le **modèle**, pas l'état runtime.

---

## `Shop.cs` — le marchand (runtime)
**Rôle :** exécuter les transactions et suivre le stock restant.

**Construction :**
```csharp
Awake : pour chaque entrée, l'indexe par item ; si stock fini, le suit dans _stock
```

**Acheter :**
```csharp
Buy(item, amount)
   article existe ? sinon échec "non vendu"
   stock suffisant ? sinon échec
   argent suffisant ? sinon échec
   Add(item, amount) → leftover ; added = amount - leftover
   added == 0 ? → échec "inventaire plein"
   Spend(buyPrice × added) ; stock -= added
   OnPurchased(entrée, added)
```

**Vendre :**
```csharp
Sell(item, amount)
   vente autorisée ? le marchand liste-t-il cet item ?
   l'inventaire en a-t-il assez ?
   Remove(item, amount) ; Wallet.Add(sellPrice × amount)
   le stock du marchand remonte
   OnSold(item, amount, or)
```

**Requêtes :** `GetStock`, `GetBuyPrice`, `GetSellPrice`.
**Échecs :** tous via `OnTransactionFailed(raison)`.

---

## `Examples/ShopDemo.cs`
B = acheter, S = vendre l'item de test. Logge solde, stock, échecs. Montre les
abonnements aux events du Wallet et du Shop.

Lis ensuite `02_flux_d_une_transaction.md`.
