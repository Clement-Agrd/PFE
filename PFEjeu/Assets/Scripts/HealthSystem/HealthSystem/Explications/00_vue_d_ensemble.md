# 00 — Vue d'ensemble

Comment le système est assemblé et pourquoi. Les fichiers `01`, `02`, `03`
détaillent chaque script, le flux d'un coup et les choix de design.

---

## Le rôle particulier de ce système

Health n'est pas un système « à part » : c'est le **point de convergence** de
tout ce qui blesse ou soigne. Un sort, un DoT, un piège, une chute — tous
finissent par appeler `TakeDamage` sur un `Health`. Et `OnDeath` est le
déclencheur de l'XP, du loot, des succès, du game over.

```
   Ability (DamageEffect) ──┐
   Status (DoT) ────────────┤
   Piège / projectile ──────┼──► Health.TakeDamage(DamageInfo) ──► OnDeath ──► XP / Loot / VFX
   Chute / zone ────────────┘
```

C'est pour ça qu'il vaut le coup de le faire propre : tout le reste s'y branche.

---

## Schéma des pièces

```
        ┌──────────────────────────────┐
        │            Health            │  (MonoBehaviour, IDamageable/IHealable)
        │  CurrentHealth / MaxHealth   │
        │  TakeDamage / Heal / Die     │
        │  i-frames, résistances       │
        │  events                      │
        └───────────────┬──────────────┘
                        │ reçoit
                        ▼
        ┌──────────────────────────────┐
        │      DamageInfo (struct)     │  un coup
        │  Amount + Type + Source      │
        └───────────────┬──────────────┘
                        │ Type →
                        ▼
        ┌──────────────────────────────┐
        │       DamageType (SO)        │  physique, feu, glace...
        └──────────────────────────────┘
                        ▲
                        │ recherché dans
        ┌──────────────────────────────┐
        │  Resistance[] (sur le Health)│  type → multiplicateur
        └──────────────────────────────┘
```

---

## Les 3 idées de design (le "pourquoi")

### 1. Un coup est une donnée riche (DamageInfo), pas juste un int
`DamageInfo` porte le **montant**, le **type** (pour les résistances) et la
**source** (qui frappe). La source est précieuse : elle permet de créditer l'XP
au bon tueur, de gérer l'aggro, d'attribuer les stats.

### 2. Types de dégâts + résistances en données
Un `DamageType` est un asset ; chaque `Health` liste ses résistances (type →
multiplicateur). « Immunisé au feu, faible à la glace » se règle dans
l'Inspector, sans code.

### 3. Cible universelle via interfaces
`Health` implémente `IDamageable`/`IHealable`. Les systèmes offensifs cherchent
l'interface, pas la classe → ils frappent aussi bien un joueur, un ennemi, un
mur destructible, un bouclier... tout ce qui implémente l'interface.

---

## En une phrase

> Un coup (`DamageInfo`) arrive ; s'il passe l'invulnérabilité, il est réduit par
> la résistance au type, retiré des PV ; à 0, l'entité meurt et `OnDeath` prévient
> le reste du jeu (XP, loot, UI).

Lis ensuite `01_role_de_chaque_script.md`.
