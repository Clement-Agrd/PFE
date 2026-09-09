# 01 — Rôle de chaque script

---

## `ModifierType.cs` — les trois types (+ l'ordre)
```csharp
Flat = 100,        // +5 brut
PercentAdd = 200,  // +10% additionnés
PercentMult = 300  // ×1.1 séparé
```

Astuce : les **valeurs numériques encodent l'ordre**. En triant les
modificateurs par cet ordre, on garantit que tous les Flat passent avant les
PercentAdd, qui passent avant les PercentMult — exactement ce que la formule
exige.

---

## `StatModifier.cs` — un modificateur
```csharp
public readonly float Value;       // 5, 0.2, ...
public readonly ModifierType Type; // Flat / PercentAdd / PercentMult
public readonly int Order;         // par défaut = (int)Type
public readonly object Source;     // qui l'a posé (item, buff...)
```

`Source` est la clé de la gestion propre : on retire un bonus par son objet
source, pas en cherchant à retrouver le bon modificateur à la main.

---

## `Stat.cs` — une stat
**Rôle :** base + modificateurs, avec calcul et cache.

```csharp
public float Value
{
    get { if (_dirty) { _cachedValue = Calculate(); _dirty = false; } return _cachedValue; }
}
```

**Ajout/retrait** marquent la stat « dirty » (à recalculer) et émettent `Changed` :
```csharp
public void AddModifier(mod) { _modifiers.Add(mod); tri par Order; MarkDirty(); }
public bool RemoveAllFromSource(source) { RemoveAll(m => m.Source == source); ... }
```

Le calcul suit la formule (détaillé dans `02`). Le résultat est arrondi à 4
décimales pour éviter la dérive flottante.

---

## `StatDefinition.cs` — l'identité d'une stat (SO)
**Rôle :** un asset par stat (Force, PVMax, Vitesse...). Sert de **clé** : les
autres systèmes référencent une stat par cet asset. Porte un nom d'affichage et
une valeur de base par défaut.

---

## `StatsComponent.cs` — les stats d'une entité
**Rôle :** créer et exposer les stats.

```csharp
public Stat GetStat(def)
{
    if (pas encore créée) Register(def, def.DefaultBaseValue); // auto-création
    return _stats[def];
}
public float GetValue(def) => GetStat(def)?.Value ?? 0f;
public void AddModifier(def, mod) => GetStat(def)?.AddModifier(mod);
public void RemoveAllModifiersFromSource(source) { sur toutes les stats }
```

Chaque stat relaie son `Changed` vers `OnStatChanged(def)` → un seul point
d'abonnement pour l'UI et les systèmes dépendants (Health, etc.).

---

## `Examples/StatsDemo.cs`
Applique +10 (Flat), +20% (PercentAdd), puis retire le buff par sa source, au
clavier. Logge la valeur recalculée. Modèle d'usage.

Lis ensuite `02_le_calcul.md`.
