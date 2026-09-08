# Loot / Drop Table System

Tables de butin pondérées en ScriptableObjects : entrées garanties, tirages
pondérés, sous-tables imbriquées, quantités aléatoires. Namespace :
`Core.LootSystem`.

## ⚠️ Dépendance
Ce package référence l'**InventorySystem** (`Core.InventorySystem.ItemDefinition`).
Importe-le d'abord.

## Installation
Dépose le dossier `LootSystem/` dans `Assets/` (après l'InventorySystem).

## Mise en place
1. Crée une table : `Assets > Create > Loot > Loot Table`.
2. Ajoute des entrées. Pour chacune :
   - un **item** (ItemDefinition) OU une **sous-table** ;
   - **garanti** ? (tombe toujours, avec sa `dropChance`) ;
   - sinon un **poids** (pour le pool pondéré) ;
   - une **quantité** min-max.
3. Règle `rolls` (nombre de tirages pondérés) et `nothingWeight` (poids du « rien »).
4. Roule :
   ```csharp
   List<LootResult> loot = lootTable.Roll();
   foreach (var r in loot) inventory.Add(r.Item, r.Amount);
   ```

## Les deux modèles combinés
- **Garanti** : `guaranteed = true`. L'entrée tombe (si son `dropChance` réussit).
  Ex. « toujours 5-10 pièces ».
- **Pondéré** : `guaranteed = false`. Entre dans le pool. La table fait `rolls`
  tirages ; chacun choisit une entrée selon son poids (ou « rien » via
  `nothingWeight`). Ex. « 1 objet parmi : épée (poids 10), potion (60), rien (30) ».

## Sous-tables (composition)
Une entrée peut pointer vers une autre `LootTable` au lieu d'un item : cette
sous-table est roulée récursivement. Utile pour une table « drops rares »
partagée par plusieurs ennemis. (Garde-fou : profondeur max 8, ne fais pas de
table qui se référence elle-même.)

## Composant pratique
`LootDropper` porte une table et la roule via `Drop()`, en diffusant le résultat :
```csharp
lootDropper.OnLootDropped += results => {
    foreach (var r in results) SpawnPickup(r.Item, r.Amount); // ou add inventaire
};
lootDropper.Drop();
```

## API
- `LootTable.Roll()` → `List<LootResult>`.
- `LootResult` : `Item` (ItemDefinition) + `Amount`.
- `LootDropper.Drop()` + event `OnLootDropped`.

## Fichiers
- `LootResult.cs` — un objet obtenu (item + quantité)
- `LootEntry.cs` — une entrée (item/sous-table + poids + quantité + garanti)
- `LootTable.cs` — la table (SO) + logique de roll
- `LootDropper.cs` — composant pratique (Drop + event)
- `Examples/LootDemo.cs` — roll au clavier, ajout à l'inventaire
- `Explications/` — documentation détaillée

## 🔗 La chaîne complète : ennemi meurt → butin → inventaire
```csharp
public sealed class Enemy : MonoBehaviour
{
    [SerializeField] private LootTable lootTable;

    private void OnEnable() => GetComponent<Health>().OnDeath += DropLoot;

    private void DropLoot()
    {
        var loot = lootTable.Roll();
        foreach (var r in loot)
            playerInventory.Add(r.Item, r.Amount);   // ou spawn des pickups
    }
}
```
→ Health (mort) + Loot (roll) + Inventory (dépôt), reliés en quelques lignes.

## Tester la démo
Crée quelques ItemDefinition, une LootTable (mélange garanti/pondéré). Sur un
GameObject : `LootDemo` (assigne la table, et un InventoryHolder pour voir le
dépôt). Espace = roll. La Console montre le butin obtenu.
