# 00 — Vue d'ensemble

Comment le système est assemblé et pourquoi. Les fichiers `01`, `02`, `03`
détaillent chaque script, le calcul d'une valeur et les choix de design.

---

## Le problème qu'on résout

Une stat en jeu n'est presque jamais une simple valeur. La « force » d'un
personnage, c'est :
```
force de base (10)
+ épée équipée (+5)
+ potion de force (+20%)
- malédiction (-10%)
+ bénédiction (×1.15)
```
Et tout ça doit pouvoir s'ajouter et se **retirer** proprement (déséquiper
l'épée, fin de la potion) sans se marcher dessus.

Coder ça à la main devient un cauchemar : où stocker les bonus ? dans quel
ordre les appliquer ? comment retirer juste ceux de l'épée ?

Ce système répond : une stat = **une base + une pile de modificateurs**, chacun
avec un type (comment il s'applique) et une source (qui l'a posé).

---

## Schéma global

```
        ┌──────────────────────────────┐
        │        StatsComponent        │  (sur l'entité)
        │  GetValue / AddModifier      │
        │  OnStatChanged               │
        └───────────────┬──────────────┘
                        │ un par StatDefinition
                        ▼
        ┌──────────────────────────────┐
        │            Stat              │  (C# pur)
        │  BaseValue + modificateurs   │
        │  Value (calculé + caché)     │
        └───────────────┬──────────────┘
                        │ liste de
                        ▼
        ┌──────────────────────────────┐
        │        StatModifier          │
        │  Value + Type + Source       │
        └──────────────────────────────┘

   StatDefinition (SO) : identité de la stat (Force, PVMax...) référencée partout
```

---

## Les 3 idées de design (le "pourquoi")

### 1. Trois types de modificateur, dans le bon ordre
```
Final = ( Base + Σ Flat ) × ( 1 + Σ PercentAdd ) × Π ( 1 + PercentMult )
```
C'est la formule standard des RPG. Elle distingue les `+X` bruts, les `+X%` qui
s'additionnent, et les `×` qui se multiplient à part. Sans cette distinction,
« +50% » et « +50% » donneraient à tort ×2.25 au lieu de ×2.

### 2. Chaque modificateur a une SOURCE
La source (l'item, le buff, l'aura) identifie qui a posé le bonus. On peut donc
retirer **tous** les modificateurs d'une source en un appel — c'est ce qui rend
le déséquipement et la fin des buffs triviaux et sans bug.

### 3. Valeur mise en cache
Recalculer à chaque lecture serait du gâchis. La valeur n'est recalculée que
quand un modificateur (ou la base) change. Entre-temps, on lit le cache.

---

## En une phrase

> Une stat combine sa base et ses modificateurs (Flat, puis PercentAdd, puis
> PercentMult) pour donner sa valeur finale, mise en cache ; on ajoute/retire des
> modificateurs par source, et un event prévient quand une valeur change.

Lis ensuite `01_role_de_chaque_script.md`.
