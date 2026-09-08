# Health / Damage System

Points de vie, dégâts typés avec résistances, invulnérabilité, mort/réanimation,
events. C'est la **cible commune** des systèmes offensifs. Namespace :
`Core.HealthSystem`.

## Installation
Dépose le dossier `HealthSystem/` dans `Assets/`.

## Mise en place
1. (Optionnel) Crée des types de dégâts : `Assets > Create > Combat > Damage Type`
   (Physical, Fire, Ice...).
2. Sur une entité vivante : ajoute `Health`. Règle `maxHealth`, les i-frames, et
   des résistances (type → multiplicateur).
3. Inflige / soigne :
   ```csharp
   health.TakeDamage(new DamageInfo(20, fireType, attacker));
   health.TakeDamage(15);          // dégâts bruts
   health.Heal(10);
   ```

## Résistances
Ajoute des entrées `Resistance` sur le Health :
```
type = Fire,  multiplier = 0    → immunisé au feu
type = Ice,   multiplier = 2    → faible à la glace (×2)
type = Phys,  multiplier = 0.75 → -25 % aux dégâts physiques
```
Un type absent = multiplicateur 1 (normal). Dégâts sans type = 1 aussi.

## Invulnérabilité
- `invulnAfterHitDuration` : i-frames automatiques après chaque coup.
- `IsInvulnerable` : à activer/désactiver toi-même (dash, cinématique).

## Events (pour l'UI et le gameplay)
```csharp
health.OnHealthChanged += (cur, max) => healthBar.fillAmount = (float)cur / max;
health.OnDamaged       += info => FlashRed();
health.OnDeath         += () => PlayDeathAnim();
```

## API essentielle
- `TakeDamage(DamageInfo)` / `TakeDamage(int)` / `Heal(int)`.
- `Revive(health?)`, `Kill()`, `SetMaxHealth(value, healToFull)`.
- `CurrentHealth`, `MaxHealth`, `Normalized`, `IsDead`, `IsInvulnerable`.
- Events : `OnDamaged`, `OnHealed`, `OnHealthChanged`, `OnDeath`.
- Save : `Capture(out cur, out max)` / `Restore(cur, max)`.

---

## 🔗 Câblage avec tes autres systèmes

### Ability System — un DamageEffect qui frappe le Health
```csharp
public override void Execute(in AbilityContext ctx)
{
    if (ctx.Target != null && ctx.Target.TryGetComponent(out Health health))
        health.TakeDamage(new DamageInfo(damage, damageType, ctx.Caster));
}
```

### Status Effects — un DoT qui frappe le Health
```csharp
public override void OnTick(StatusEffectContext ctx)
{
    if (ctx.Target.TryGetComponent(out Health health))
        health.TakeDamage(new DamageInfo(damagePerTick * ctx.Instance.Stacks, poisonType));
}
```
(Remplace l'interface `IEffectDamageable` des exemples du Status System par un
accès direct à `Health`, ou fais implémenter `IEffectDamageable` à `Health`.)

### XP — donner de l'expérience au tueur à la mort
```csharp
health.OnDeath += () =>
{
    // 'lastAttacker' mémorisé depuis OnDamaged (info.Source)
    if (lastAttacker != null && lastAttacker.TryGetComponent(out ExperienceComponent xp))
        xp.AddExperience(xpReward);
};
```

### Save System — persister les PV
```csharp
public override string CaptureState()
{
    health.Capture(out int cur, out int max);
    return JsonUtility.ToJson(new HealthState { cur = cur, max = max });
}
```

### Event Bus — annoncer une mort sans couplage
```csharp
health.OnDeath += () => EventBus.Publish(new EntityDiedEvent(gameObject));
```

## Fichiers
- `Health.cs` — les PV et toute la logique (MonoBehaviour)
- `DamageInfo.cs` — un coup (montant, type, source)
- `DamageType.cs` — catégorie de dégâts (SO)
- `Resistance.cs` — type → multiplicateur
- `IDamageable.cs` — IDamageable + IHealable
- `Examples/HealthDemo.cs` — démo clavier
- `Explications/` — documentation détaillée

## Tester la démo
Sur un GameObject : `Health` + `HealthDemo`. Lance : 1 = dégâts, 2 = soin,
R = réanimer. La Console montre les PV, les coups et la mort.
