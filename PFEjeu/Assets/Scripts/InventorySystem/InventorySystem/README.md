# Inventory System (inventaire modulaire)

Système d'inventaire à base de ScriptableObjects, avec stacking, capacité fixe,
events pour l'UI et (dé)sérialisation prête pour la sauvegarde. Namespace :
`Core.InventorySystem`.

## Installation
Dépose le dossier `InventorySystem/` dans `Assets/` de ton projet Unity.

## Concept en une phrase
Un item a deux facettes : sa **définition** (asset SO, données statiques
partagées) et ses **piles** en jeu (`ItemStack` = définition + quantité, dans
un slot). L'`Inventory` gère les slots ; l'`InventoryHolder` fait le pont Unity.

## Mise en place
1. `Assets > Create > Inventory > Item Definition` → crée un ou plusieurs items.
   Renseigne id (auto = nom de l'asset), nom, icône, catégorie, taille de pile max.
2. `Assets > Create > Inventory > Item Database` → glisses-y **tous** tes items
   (nécessaire pour le chargement des sauvegardes).
3. Sur un GameObject (ex. "Player") : ajoute `InventoryHolder`, règle la capacité
   et éventuellement des items de départ.
4. Accède à l'inventaire via `holder.Inventory` depuis ton code.

## API essentielle (`Inventory`)
- `Add(definition, amount)` → renvoie le **débordement** (ce qui n'a pas tenu).
- `Remove(definition, amount)` → renvoie la quantité **réellement retirée**.
- `Count(definition)` / `Contains(definition, amount)` / `HasFreeSlot()`.
- `GetSlot(index)` → l'`ItemStack` du slot (ou `null` si vide).
- `Clear()`.
- Events : `OnChanged` (global) et `OnSlotChanged(index)` (par case).
- Save : `Capture()` → `InventorySaveData` ; `Restore(data, database)`.

## Créer un type d'item spécialisé
Hérite de `ItemDefinition` et surcharge `Use()` :
```csharp
[CreateAssetMenu(menuName = "Inventory/Items/Consumable")]
public sealed class ConsumableItemDefinition : ItemDefinition
{
    [SerializeField] private int healAmount = 25;
    public override void Use(GameObject user)
        => user.GetComponent<Health>()?.Heal(healAmount);
}
```

## Brancher sur le Save System
`Inventory.Capture()` renvoie un objet sérialisable en JSON. Dans un
`SaveableBehaviour` (package SaveSystem) :
```csharp
public override string CaptureState()
    => JsonUtility.ToJson(holder.Inventory.Capture());

public override void RestoreState(string json)
    => holder.Inventory.Restore(JsonUtility.FromJson<InventorySaveData>(json), database);
```

## Brancher sur l'Event Bus
Dans `Inventory.OnChanged`, publie un `InventoryChangedEvent : IEvent` pour
notifier l'UI globalement, si tu utilises le package EventBus.

## Fichiers
- `ItemCategory.cs` — enum de catégories
- `ItemDefinition.cs` — données statiques d'un item (SO, base)
- `ItemStack.cs` — pile runtime (definition + quantité)
- `Inventory.cs` — moteur (slots, stacking, requêtes, save) — C# pur
- `InventoryHolder.cs` — pont MonoBehaviour + items de départ
- `ItemDatabase.cs` — registre id → définition (pour la save)
- `InventorySaveData.cs` — instantané sérialisable
- `Examples/` — item consommable, item équipement, démo commentée
- `Explications/` — documentation détaillée du code

## Tester la démo
Sur un GameObject : `InventoryHolder` + `InventoryDemo`, assigne un item à
tester, lance. La Console montre l'ajout (avec débordement), le retrait, les
events par slot et l'effet de `Use()`.
