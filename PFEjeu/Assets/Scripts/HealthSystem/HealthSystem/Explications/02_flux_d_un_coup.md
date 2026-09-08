# 02 — Le flux d'un coup, étape par étape

---

## Recevoir des dégâts

```
attaquant → health.TakeDamage(new DamageInfo(20, Fire, attaquant))
      │
      ├─ déjà mort ? ou montant <= 0 ? → on ignore
      ├─ invulnérable ? (IsInvulnerable ou i-frames actives) → on ignore
      │
      ├─ multiplicateur = résistance au type Fire   (ex. 0.5)
      ├─ dégâts finaux = round(20 × 0.5) = 10
      │     └─ si <= 0 (immunisé/absorbé) → on ignore
      │
      ├─ CurrentHealth -= 10
      ├─ OnDamaged(info)                → VFX, son, "lastAttacker = info.Source"
      ├─ OnHealthChanged(cur, max)      → barre de vie
      │
      ├─ i-frames : _invulnUntil = Time.time + durée   (si > 0)
      │
      └─ CurrentHealth == 0 ? → Die() → IsDead = true → OnDeath()
                                              └─ XP au tueur, loot, game over...
```

---

## Exemple chiffré avec résistance

```
maxHealth 100, résistance { Fire : 0.5 }, i-frames 0.5 s

TakeDamage(20, Fire)  → 20 × 0.5 = 10 → PV 90, i-frames jusqu'à t+0.5
TakeDamage(20, Fire)  à t+0.2 → invulnérable → IGNORÉ
TakeDamage(20, Fire)  à t+0.6 → 10 → PV 80
TakeDamage(30, Ice)   (pas de résistance Ice = ×1) → 30 → PV 50
TakeDamage(20)        (brut, pas de type = ×1) → PV 30
```

Les i-frames évitent qu'un objet touché plusieurs fois dans la même frame (ou
très vite) ne se fasse « multi-toucher » — classique sur les zones de dégâts.

---

## Soigner

```
health.Heal(15)
      │
      ├─ mort ? → on ignore (un mort ne se soigne pas ; passe par Revive)
      ├─ CurrentHealth = min(max, CurrentHealth + 15)   (jamais au-dessus du max)
      ├─ OnHealed(15)
      └─ OnHealthChanged(cur, max)
```

---

## Mourir et réanimer

```
Die()   → IsDead = true ; OnDeath()
Revive(health?) → IsDead = false ; PV remis (à max, ou à la valeur donnée)
Kill()  → force PV = 0 puis Die()   (exécution scriptée, zone de mort instantanée)
```

---

## Créditer l'XP au tueur : le pattern « dernier attaquant »

```csharp
private GameObject _lastAttacker;

void OnEnable()
{
    health.OnDamaged += info => _lastAttacker = info.Source;   // mémorise QUI frappe
    health.OnDeath   += GiveXpToKiller;
}

void GiveXpToKiller()
{
    if (_lastAttacker != null && _lastAttacker.TryGetComponent(out ExperienceComponent xp))
        xp.AddExperience(xpReward);
}
```

C'est pour ça que `DamageInfo` porte la **source** : sans elle, impossible de
savoir à qui donner l'XP.

Lis ensuite `03_choix_de_conception.md`.
