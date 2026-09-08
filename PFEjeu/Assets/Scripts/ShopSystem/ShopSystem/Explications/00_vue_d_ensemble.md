# 00 — Vue d'ensemble

Comment le système est assemblé et pourquoi. Les fichiers `01`, `02`, `03`
détaillent chaque script, le flux d'une transaction et les choix de design.

---

## Le problème qu'on résout

Une économie de jeu, c'est trois choses qui doivent rester cohérentes :
- une **monnaie** (le solde du joueur) ;
- un **stock** côté marchand (limité ou infini) ;
- l'**inventaire** du joueur (où arrivent/partent les objets).

Un achat doit vérifier les trois (assez d'argent ? en stock ? de la place ?) et
les mettre à jour **ensemble**, sans jamais créer d'incohérence (dépenser sans
recevoir l'objet, ou l'inverse).

---

## Schéma global

```
        ┌──────────────────────────────┐
        │             Shop             │  (le marchand)
        │  Buy / Sell                  │
        │  stock runtime               │
        └───────┬──────────────┬───────┘
                │ dépense/gagne │ ajoute/retire
                ▼               ▼
        ┌──────────────┐  ┌──────────────────┐
        │    Wallet    │  │    Inventory     │  (InventorySystem)
        │  (monnaie)   │  │ (objets du joueur)│
        └──────────────┘  └──────────────────┘
                ▲
                │ prix & stock initiaux
        ┌──────────────────────┐
        │  ShopDefinition (SO) │
        │   └ ShopEntry        │  item + prix achat/vente + stock
        └──────────────────────┘
```

---

## Les 3 idées de design (le "pourquoi")

### 1. Articles data-driven (SO)
Une boutique est un asset : la liste des articles avec leurs prix et stocks. On
monte l'échoppe dans l'Inspector, réutilisable et éditable sans code.

### 2. Définition (SO) vs stock runtime
Les prix et stocks **initiaux** sont dans l'asset (immuable). Le stock **restant**
change en jeu → il vit dans le composant `Shop`, pas dans l'asset (sinon vendre
en Play modifierait ton asset). Même séparation template/instance que partout.

### 3. Transaction atomique et vérifiée
`Buy` vérifie stock + argent + place, puis met tout à jour de façon cohérente. En
cas d'impossibilité, rien n'est modifié et `OnTransactionFailed` explique
pourquoi. Le Wallet et l'Inventory ne divergent jamais.

---

## En une phrase

> Le Shop lit ses prix/stocks dans un asset, et orchestre chaque transaction
> entre le Wallet (monnaie) et l'Inventory (objets), en vérifiant tout et en
> prévenant en cas d'échec.

Lis ensuite `01_role_de_chaque_script.md`.
