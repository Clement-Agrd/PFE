# 00 — Vue d'ensemble de l'architecture

Comment le système est assemblé et pourquoi. Les fichiers `01`, `02`, `03`
détaillent chaque script, le flux d'ajout/retrait et les choix de design.

---

## Le problème qu'on résout

Un inventaire mélange facilement deux choses très différentes :
- les **données figées** d'un item (son nom, son icône, sa taille de pile) —
  identiques pour toutes les potions du jeu ;
- l'**état vivant** d'un item en jeu (quelle pile, combien, dans quel slot) —
  différent pour chaque instance.

Si on met tout dans une seule classe, on **duplique** les données figées à
chaque item ramassé (10 000 potions = 10 000 copies du nom, de l'icône...).
Et sauvegarder devient lourd.

La solution classique et robuste : **séparer template et instance**.

```
        ItemDefinition (asset SO)          ItemStack (runtime)
        ┌───────────────────────┐          ┌──────────────────┐
        │ id: "potion_hp"        │◄─────────│ definition ──────┼──► (pointe vers l'asset)
        │ nom: "Potion de vie"   │          │ quantity: 34     │
        │ icône, maxStack: 99    │          └──────────────────┘
        └───────────────────────┘          (léger, dupliqué sans coût)
         UN seul asset partagé
```

C'est le même principe que les **Prefabs** : un asset partagé, des instances
légères.

---

## Schéma global

```
        ┌──────────────────────────────┐
        │       InventoryHolder        │  (MonoBehaviour)
        │  - capacity, items de départ │
        │  - expose .Inventory         │
        └───────────────┬──────────────┘
                        │ possède
                        ▼
        ┌──────────────────────────────┐
        │          Inventory           │  (C# pur, testable)
        │  ItemStack[] _slots          │
        │  Add / Remove / Count ...    │
        │  events: OnChanged, OnSlot   │
        │  Capture / Restore           │
        └───┬───────────────────┬──────┘
            │ contient           │ utilise pour la save
            ▼                    ▼
    ┌──────────────┐    ┌──────────────────┐
    │  ItemStack   │    │  InventorySaveData│  (id, quantité, slot)
    │ def+quantity │    └────────┬─────────┘
    └──────┬───────┘             │ résolu par
           │ pointe vers         ▼
           ▼             ┌──────────────────┐
    ┌──────────────┐     │   ItemDatabase   │  (id → ItemDefinition)
    │ItemDefinition│◄────┤   registre SO    │
    │  (SO, base)  │     └──────────────────┘
    └──────┬───────┘
           │ héritée par
           ▼
   ConsumableItemDefinition, EquipmentItemDefinition, ...
```

---

## Les 4 idées de design (le "pourquoi")

### 1. Template (SO) vs instance (ItemStack)
Données figées dans un asset partagé, état vivant dans une pile légère.
Zéro duplication, sauvegarde minuscule.

### 2. Moteur séparé de Unity
`Inventory` est du C# pur : on peut le tester (ajouter/retirer, vérifier le
stacking) sans lancer de scène. `InventoryHolder` est le seul lien MonoBehaviour.

### 3. Extensibilité par héritage de SO
Un nouveau type d'item (arme, sort, clé) = une classe qui hérite de
`ItemDefinition` et surcharge `Use()`. Le moteur n'a **rien** à savoir de ces
types : il manipule des `ItemDefinition` de façon polymorphe.

### 4. Save & UII par les extrémités
- `Capture()`/`Restore()` + `ItemDatabase` → sérialisation par id, propre à
  brancher sur le Save System.
- `OnSlotChanged(index)` → l'UI ne redessine qu'une case, pas toute la grille.

---

## En une phrase

> `InventoryHolder` crée un `Inventory` de N slots ; on y `Add`/`Remove` des
> `ItemDefinition` ; le moteur gère le stacking et prévient l'UI via des events ;
> pour la save, il exporte des ids résolus par l'`ItemDatabase`.

Lis ensuite `01_role_de_chaque_script.md`.
