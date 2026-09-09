# Stats & Modifiers System

Stats de base + modificateurs empilables (buffs, debuffs, équipement) avec le
bon ordre de calcul (Flat / PercentAdd / PercentMult). Namespace :
`Core.StatsSystem`.

## Installation
Dépose le dossier `StatsSystem/` dans `Assets/`.

## Mise en place
1. Crée des stats : `Assets > Create > Stats > Stat Definition` (Force, PVMax,
   Vitesse, Défense...). Règle une valeur de base par défaut.
2. Sur l'entité : ajoute `StatsComponent`, liste les stats voulues avec leur
   valeur de base.
3. Lis / modifie :
   ```csharp
   float atk = stats.GetValue(attackStat);
   stats.AddModifier(attackStat, new StatModifier(10f, ModifierType.Flat, buffSource));
   ```

## Les trois types de modificateur
```
Valeur finale = ( Base + Σ Flat ) × ( 1 + Σ PercentAdd ) × Π ( 1 + PercentMult )
```
- **Flat** : additif brut (`+5`).
- **PercentAdd** : pourcentages **additionnés entre eux** (`+10%` et `+10%` = `+20%`).
- **PercentMult** : pourcentage **multiplicatif séparé** (`×1.1`, appliqué à part).

Exemple : base 100, +20 Flat, +50% PercentAdd, +10% PercentMult
`(100 + 20) × 1.5 × 1.1 = 198`.

## La source d'un modificateur (important)
Chaque modificateur porte une **source** (un objet identifiant qui l'a posé).
Ça permet de tout retirer d'un coup :
```csharp
var sword = new object();                 // ou l'ItemDefinition, le buff...
stats.AddModifier(strengthStat, new StatModifier(5f, ModifierType.Flat, sword));
stats.AddModifier(critStat,     new StatModifier(0.1f, ModifierType.PercentAdd, sword));
// déséquiper l'épée → retire TOUS ses bonus, sur toutes les stats :
stats.RemoveAllModifiersFromSource(sword);
```

## Réagir aux changements
```csharp
stats.OnStatChanged += def => {
    if (def == maxHealthStat) health.SetMaxHealth(Mathf.RoundToInt(stats.GetValue(def)));
    RefreshStatUI(def);
};
```

## API essentielle
- `GetValue(def)` / `GetStat(def)`.
- `AddModifier(def, modifier)`.
- `RemoveModifiersFromSource(def, source)` / `RemoveAllModifiersFromSource(source)`.
- Sur `Stat` : `BaseValue`, `Value`, `Modifiers`, `RemoveModifier`, `Changed`.
- Event : `OnStatChanged(def)`.

## Fichiers
- `ModifierType.cs` — Flat / PercentAdd / PercentMult (encode aussi l'ordre)
- `StatModifier.cs` — valeur + type + source
- `Stat.cs` — base + modificateurs, calcul + cache (C# pur)
- `StatDefinition.cs` — identité d'une stat (SO)
- `StatsComponent.cs` — porte les stats d'une entité (MonoBehaviour)
- `Examples/StatsDemo.cs` — modifs au clavier
- `Explications/` — documentation détaillée

---

## 🔗 Câblage avec tes autres systèmes

- **Health** : PVMax devient une stat. `OnStatChanged(maxHealth)` →
  `health.SetMaxHealth(value)`.
- **XP / Leveling** : `OnLevelUp` → augmente les `BaseValue` des stats.
- **Status Effects** : un ralenti = un `StatModifier` PercentMult négatif sur la
  stat Vitesse, avec l'effet comme source ; à la fin, `RemoveAllFromSource`.
- **Inventory / Equipment** : équiper un objet ajoute ses modificateurs (source =
  l'item) ; le déséquiper appelle `RemoveAllModifiersFromSource(item)`.

## Tester la démo
Crée une stat "Attack" (base 20). Sur un GameObject : `StatsComponent` (ajoute
Attack) + `StatsDemo` (assigne Attack). Lance : 1 = +10, 2 = +20%, 3 = retire.
La Console montre la valeur recalculée à chaque changement.
