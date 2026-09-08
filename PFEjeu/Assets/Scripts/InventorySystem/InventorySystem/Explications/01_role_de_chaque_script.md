# 01 — Rôle de chaque script

Une responsabilité par fichier. Voici le détail.

---

## `ItemCategory.cs` — les catégories
**Rôle :** un simple `enum` (Weapon, Consumable, Material...) pour classer les
items (filtres d'UI, règles de gameplay). Extensible.

> Si tu veux des catégories créables par un designer sans recompiler, remplace
> l'enum par des ScriptableObjects « tag ». Je peux te fournir cette variante.

---

## `ItemDefinition.cs` — le template d'un item (SO)
**Rôle :** les données **statiques** d'un type d'item, dans un asset partagé.

```csharp
public string Id => id;              // clé stable pour la save (défaut = nom d'asset)
public int MaxStackSize => maxStackSize;
public bool IsStackable => maxStackSize > 1;
public virtual void Use(GameObject user) { ... }   // surchargée par les dérivés
```

**Points clés :**
- `Use` est **virtuelle** → le comportement change selon le type dérivé
  (polymorphisme). Le moteur appelle `definition.Use(user)` sans savoir si c'est
  une potion ou une épée.
- `OnValidate` fixe l'`id` au nom de l'asset s'il est vide → jamais d'id oublié.

---

## `ItemStack.cs` — la pile runtime
**Rôle :** ce qui occupe réellement un slot = une `ItemDefinition` + une
`quantity`. C'est l'instance légère.

```csharp
public int Add(int amount)     // ajoute jusqu'à la limite, renvoie le débordement
public int Remove(int amount)  // retire, renvoie la quantité retirée
public int SpaceLeft => MaxStackSize - quantity;
public bool IsFull / IsEmpty;
```

**Pourquoi pas de setter public sur `quantity` ?** Pour préserver les invariants
(jamais au-dessus de `MaxStackSize`, jamais négatif). Toute mutation passe par
`Add`/`Remove` qui bornent la valeur.

---

## `Inventory.cs` — le moteur
**Rôle :** le cœur. Un tableau `ItemStack[] _slots` (un `null` = case vide) et
toute la logique. Découpé en régions : Ajout/Retrait, Requêtes, Sauvegarde.

**Ce qu'il expose :**
```csharp
int  Add(definition, amount)     // renvoie le débordement (0 = tout rentré)
int  Remove(definition, amount)  // renvoie la quantité retirée
int  Count(definition)
bool Contains(definition, amount)
bool HasFreeSlot()
void Clear()
event Action OnChanged;          // une fois par opération
event Action<int> OnSlotChanged; // par case modifiée
InventorySaveData Capture();     // snapshot pour la save
void Restore(data, database);    // reconstruction depuis un snapshot
```

Le détail de l'algorithme de stacking est dans `02_flux_add_remove.md`.

---

## `InventoryHolder.cs` — le pont Unity
**Rôle :** relier le moteur à Unity et pré-remplir l'inventaire.

```csharp
public Inventory Inventory { get; private set; }

private void Awake()
{
    Inventory = new Inventory(capacity);
    foreach (var entry in startingItems)         // items de départ (Inspector)
        Inventory.Add(entry.definition, entry.amount);
}
```

C'est le point d'accès pour le reste du jeu : `GetComponent<InventoryHolder>().Inventory`.

---

## `ItemDatabase.cs` — le registre id → définition
**Rôle :** au chargement d'une sauvegarde, on a des **ids** (string) mais on a
besoin des **assets**. La database fait la correspondance.

```csharp
public ItemDefinition GetById(string id)  // "potion_hp" → l'asset Potion
```

Elle construit un `Dictionary` à la première demande (et avertit en cas d'id en
double). Sans elle, impossible de recharger un inventaire depuis un fichier.

---

## `InventorySaveData.cs` — l'instantané sérialisable
**Rôle :** la forme qu'on écrit dans la sauvegarde. Une liste de
`(index, itemId, quantity)`. Compatible `JsonUtility`.

```csharp
public struct SlotData { public int index; public string itemId; public int quantity; }
```

On stocke des **ids**, jamais des références d'assets (qui ne se sérialisent pas
en JSON portable).

---

## `Examples/`
- **`ConsumableItemDefinition.cs`** — item dérivé : `Use()` soigne.
- **`EquipmentItemDefinition.cs`** — item dérivé non empilable avec stats.
- **`InventoryDemo.cs`** — s'abonne aux events, ajoute/retire/utilise un item,
  logge tout. Le modèle à copier pour brancher ton propre code.

Lis ensuite `02_flux_add_remove.md`.
