# 02 — Le flux d'une transaction, étape par étape

---

## Achat (Buy)

```
shop.Buy(potion, 2)
      │
      ├─ le marchand vend-il "potion" ? sinon → Fail("non vendu")
      ├─ inventaire présent ? sinon → Fail("aucun inventaire")
      │
      ├─ STOCK : remaining >= 2 ? (illimité = toujours)  sinon → Fail("stock insuffisant")
      │
      ├─ ARGENT : Wallet.CanAfford(buyPrice × 2) ?  sinon → Fail("pas assez d'argent")
      │
      ├─ PLACE : leftover = Inventory.Add(potion, 2)
      │           added = 2 - leftover
      │           added == 0 ? → Fail("inventaire plein")   (rien facturé)
      │
      ├─ Wallet.Spend(buyPrice × added)     ← on ne paie QUE ce qui est entré
      ├─ stock -= added
      └─ OnPurchased(entrée, added)
```

**L'ordre des vérifications est important :** on teste tout **avant** de toucher
au Wallet. On n'ajoute à l'inventaire qu'ensuite, et on ne facture que la
quantité réellement ajoutée. Impossible de payer sans recevoir.

---

## Exemple chiffré

```
Wallet 100, potion à 20 (buyPrice), stock 5, inventaire vide (place OK)

Buy(potion, 2)
   stock 5 >= 2 ✔
   afford 40 ✔
   Add → leftover 0, added 2
   Spend 40 → Wallet 60
   stock 5 → 3
   OnPurchased(potion, 2)

Buy(potion, 10)
   stock 3 >= 10 ? NON → Fail("stock insuffisant")   (rien ne bouge)
```

---

## Achat partiel (inventaire presque plein)

```
Wallet 100, potion à 20, on demande 3, mais 1 seule tient dans l'inventaire

Buy(potion, 3)
   afford 60 ✔
   Add(potion, 3) → leftover 2, added 1
   Spend 20 (pour 1 seule) → Wallet 80
   OnPurchased(potion, 1)
```

On n'achète et ne paie que ce qui rentre → jamais d'or perdu pour un objet non
reçu.

---

## Vente (Sell)

```
shop.Sell(minerai, 3)
      │
      ├─ vente autorisée ici ? le marchand liste-t-il "minerai" ?
      ├─ l'inventaire a-t-il 3 minerais ?  sinon → Fail
      │
      ├─ Inventory.Remove(minerai, 3)
      ├─ Wallet.Add(sellPrice × 3)
      ├─ le stock du marchand remonte (+3)   ← il pourra les revendre
      └─ OnSold(minerai, 3, or)
```

---

## Pourquoi le stock remonte à la vente ?

Quand tu vends 3 minerais au marchand, il les récupère et pourra les revendre.
C'est cohérent avec une économie « fermée » et évite de créer des objets à partir
de rien côté marchand. (Si tu préfères un stock d'achat et de vente séparés, ça
se distingue facilement — dis-le-moi.)

Lis ensuite `03_choix_de_conception.md`.
