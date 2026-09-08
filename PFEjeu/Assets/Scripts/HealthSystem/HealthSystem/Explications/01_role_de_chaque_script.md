# 01 — Rôle de chaque script

---

## `DamageType.cs` — la catégorie de dégâts (SO)
**Rôle :** un asset par type (Physique, Feu, Glace, Poison...). Sert de **clé**
pour les résistances. Un id + un nom d'affichage.

---

## `DamageInfo.cs` — un coup
**Rôle :** décrire un coup de façon riche.

```csharp
public readonly int Amount;      // combien
public readonly DamageType Type; // de quel type (null = brut)
public readonly GameObject Source; // qui frappe
```

`readonly struct` → immuable, sans allocation. La **source** est ce qui te
permettra, plus tard, de savoir qui a tué (pour l'XP) ou d'ignorer les dégâts
alliés (friendly fire).

---

## `Resistance.cs` — un multiplicateur par type
**Rôle :** une entrée `type → multiplier`.

```
1   = normal        0.5 = -50 %
0   = immunisé      2   = ×2 (faiblesse)
```

Une entité liste ses résistances ; le Health applique le bon multiplicateur au
type du coup reçu.

---

## `IDamageable.cs` — les contrats
**Rôle :** `IDamageable` (`TakeDamage`, `IsDead`) et `IHealable` (`Heal`).
Les systèmes offensifs dépendent de ces interfaces, pas de la classe `Health` →
ils frappent tout ce qui les implémente.

---

## `Health.cs` — le composant de vie
**Rôle :** toute la logique.

**Recevoir un coup :**
```csharp
public void TakeDamage(in DamageInfo info)
{
    if (IsDead || info.Amount <= 0) return;
    if (IsInvulnerable || Time.time < _invulnUntil) return;   // i-frames

    int final = Mathf.RoundToInt(info.Amount * GetMultiplier(info.Type)); // résistance
    if (final <= 0) return;

    CurrentHealth = Mathf.Max(0, CurrentHealth - final);
    OnDamaged?.Invoke(info);
    OnHealthChanged?.Invoke(CurrentHealth, maxHealth);

    if (invulnAfterHitDuration > 0f) _invulnUntil = Time.time + invulnAfterHitDuration;
    if (CurrentHealth == 0) Die();
}
```

**Autres méthodes :** `TakeDamage(int)` (dégâts bruts), `Heal`, `SetMaxHealth`,
`Revive`, `Kill`. **Events :** `OnDamaged`, `OnHealed`, `OnHealthChanged`,
`OnDeath`. **Save :** `Capture`/`Restore`.

**Résistance :**
```csharp
private float GetMultiplier(DamageType type)
{
    if (type == null || resistances == null) return 1f;
    foreach (résistance) if (type correspond) return multiplier;
    return 1f;
}
```

---

## `Examples/HealthDemo.cs`
Dégâts (1), soin (2), réanime (R). Logge PV, coups, mort. Montre l'abonnement aux
events.

Lis ensuite `02_flux_d_un_coup.md`.
