# 02 — Le flux Ajout / Retrait, étape par étape

Le cœur du système : comment `Add` empile intelligemment et comment `Remove`
libère les slots. Avec un exemple chiffré.

---

## L'algorithme d'AJOUT (`Add`)

Objectif : ajouter `amount` items en gaspillant le moins de slots possible.

```
Add(potion, 150)   avec maxStackSize = 99, inventaire = [potion×80, vide, vide, ...]
      │
      ▼
ÉTAPE 1 — compléter les piles existantes du même type (si empilable)
      │
      ├─ slot 0 : potion×80, SpaceLeft = 19
      │     → on y verse 19 → potion×99 (plein). reste = 150 - 19 = 131
      │     → RaiseSlot(0)
      │
      ▼
ÉTAPE 2 — placer le reste dans les slots vides (nouvelles piles)
      │
      ├─ slot 1 : vide → new ItemStack(potion, min(131, 99)) = potion×99
      │     reste = 131 - 99 = 32 → RaiseSlot(1)
      │
      ├─ slot 2 : vide → new ItemStack(potion, min(32, 99)) = potion×32
      │     reste = 32 - 32 = 0 → RaiseSlot(2)
      │
      ▼
reste = 0 → OnChanged.Invoke()
retourne 0  (tout est rentré)
```

**Si l'inventaire est plein** avant d'avoir tout casé, `Add` s'arrête et
**renvoie le reste** (ex. `12`). À l'appelant de décider : refuser le ramassage,
laisser l'objet au sol, afficher « inventaire plein », etc.

```csharp
int leftover = inventory.Add(potion, 150);
if (leftover > 0) Debug.Log($"{leftover} n'ont pas tenu.");
```

**Cas non empilable** (`maxStackSize = 1`) : l'étape 1 est sautée
(`IsStackable == false`), et l'étape 2 place 1 item par slot vide. 3 épées =
3 slots occupés.

---

## L'algorithme de RETRAIT (`Remove`)

Objectif : retirer `amount` items du type donné, en libérant les slots vidés.

```
Remove(potion, 60)   avec inventaire = [potion×99, potion×99, potion×32]
      │
      ▼
On parcourt les slots EN SENS INVERSE (dernières piles d'abord) :
      │
      ├─ slot 2 : potion×32 → on retire min(60, 32) = 32
      │     removed = 32. slot vidé → _slots[2] = null → RaiseSlot(2)
      │
      ├─ slot 1 : potion×99 → on retire min(60-32, 99) = 28
      │     removed = 60. slot 1 → potion×71 → RaiseSlot(1)
      │
      └─ removed (60) == amount (60) → on s'arrête
      │
      ▼
OnChanged.Invoke()
retourne 60  (quantité réellement retirée)
```

**Si tu demandes plus que disponible**, `Remove` retire tout ce qu'il peut et
renvoie ce qu'il a effectivement retiré (< amount). Vérifie d'abord avec
`Contains(def, amount)` si tu veux un retrait « tout ou rien ».

---

## Le rôle des deux events

```
Add / Remove
   │
   ├─ pour CHAQUE slot touché : OnSlotChanged(index)
   │     → l'UI rafraîchit UNE case (perf : pas toute la grille)
   │
   └─ à la fin, si quelque chose a changé : OnChanged()
         → utile pour un total, un poids, un « inventaire plein », etc.
```

Exemple côté UI :
```csharp
inventory.OnSlotChanged += i => slotViews[i].Refresh(inventory.GetSlot(i));
inventory.OnChanged     += () => weightLabel.text = ComputeWeight().ToString();
```

---

## Le flux SAUVEGARDE (résumé, détaillé dans 03)

```
Capture()  →  parcourt les slots occupés  →  InventorySaveData { (index, id, qty)... }
                                                     │ JsonUtility.ToJson
                                                     ▼
                                                fichier de save

Restore(data, database)  →  Clear()  →  pour chaque entrée :
        database.GetById(id) → ItemDefinition → _slots[index] = new ItemStack(def, qty)
```

Lis ensuite `03_choix_de_conception.md`.
