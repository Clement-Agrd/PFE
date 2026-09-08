# 01 — Rôle de chaque script

---

## `LootResult.cs` — un objet obtenu
```csharp
public readonly ItemDefinition Item;
public readonly int Amount;
```
Le produit d'un roll : un item (de ton InventorySystem) et une quantité. Struct
immuable, sans allocation.

---

## `LootEntry.cs` — une entrée de table
```csharp
ItemDefinition item;   // OU
LootTable subTable;    // une sous-table à rouler à la place

float weight;          // poids dans le pool pondéré (si non garanti)
int minAmount, maxAmount; // quantité tirée au hasard

bool guaranteed;       // tombe toujours ?
float dropChance;      // (garanti) probabilité de tomber, 1 = toujours
```

Une entrée décrit **quoi** peut tomber et **selon quelles règles**. Item OU
sous-table (pas les deux). Garanti OU pondéré.

---

## `LootTable.cs` — la table (SO) + le roll
**Rôle :** contenir les entrées et savoir les rouler.

```csharp
List<LootEntry> entries;
int rolls;             // nombre de tirages pondérés
float nothingWeight;   // poids d'un tirage « rien »
```

**Roll :**
```csharp
public List<LootResult> Roll() { var r = new List<>(); RollInto(r, 0); return r; }

void RollInto(results, depth)
{
    // 1) entrées garanties : chacune, si Random.value <= dropChance → Resolve
    // 2) rolls tirages pondérés dans le pool des non-garanties → Resolve
}
```

**Resolve** transforme une entrée en résultat :
```csharp
if (entry.SubTable != null) subTable.RollInto(results, depth + 1);  // récursion
else amount = Random.Range(min, max + 1) ; results.Add(item, amount);
```

**PickWeighted** choisit dans le pool selon les poids (ou « rien ») :
```csharp
total = nothingWeight + Σ poids
roll ∈ [0, total)
si roll < nothingWeight → null (rien)
sinon on soustrait les poids un par un jusqu'à tomber sur l'entrée
```

Un garde-fou `MaxDepth = 8` empêche une boucle infinie si des sous-tables se
référencent en cercle.

---

## `LootDropper.cs` — le composant pratique
```csharp
public IReadOnlyList<LootResult> Drop()
{
    var results = table.Roll();
    OnLootDropped?.Invoke(results);
    return results;
}
```
Porte une table, la roule, diffuse le résultat. Ne décide pas quoi en faire →
tu t'abonnes à `OnLootDropped`.

---

## `Examples/LootDemo.cs`
Roule la table à l'Espace, logge le butin, et l'ajoute à un `InventoryHolder`
si assigné. Montre la chaîne loot → inventaire.

Lis ensuite `02_flux_d_un_roll.md`.
