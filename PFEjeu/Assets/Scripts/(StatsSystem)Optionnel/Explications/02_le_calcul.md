# 02 — Le calcul d'une valeur, en détail

C'est le cœur du système. Prends le temps de lire l'exemple chiffré.

---

## La formule

```
Final = ( Base + Σ Flat ) × ( 1 + Σ PercentAdd ) × Π ( 1 + PercentMult )
```

- On part de la **base**.
- On ajoute tous les **Flat**.
- On multiplie par (1 + somme des **PercentAdd**) — les pourcentages additifs
  sont d'abord **additionnés entre eux**, puis appliqués une seule fois.
- On multiplie par chaque **(1 + PercentMult)** — les multiplicatifs s'appliquent
  **séparément**, l'un après l'autre.

---

## Pourquoi PercentAdd et PercentMult sont différents

C'est la subtilité qui compte dans un vrai RPG.

```
Deux bonus de +50%.

En PercentAdd : 50% + 50% = 100% → ×(1 + 1.0) = ×2      (linéaire)
En PercentMult : ×1.5 puis ×1.5 = ×2.25                 (composé)
```

- **PercentAdd** pour des bonus « du même pot » (plusieurs buffs de force qui
  s'additionnent sans s'emballer).
- **PercentMult** pour des bonus « rares et forts » qu'on veut voir se composer
  (un ×2 dégâts qui se cumule multiplicativement avec le reste).

Choisir le bon type, c'est doser l'équilibrage.

---

## L'algorithme (dans `Stat.Calculate`)

Les modificateurs sont **triés par ordre** (Flat < PercentAdd < PercentMult).

```
final = base
sommePercentAdd = 0

pour chaque modificateur (dans l'ordre) :
    Flat        → final += valeur
    PercentAdd  → sommePercentAdd += valeur
                  si c'est le DERNIER PercentAdd d'affilée :
                      final *= (1 + sommePercentAdd)
                      sommePercentAdd = 0
    PercentMult → final *= (1 + valeur)

arrondir final à 4 décimales
```

Le tri garantit que tous les Flat passent avant, puis les PercentAdd groupés,
puis les PercentMult. On accumule les PercentAdd et on les applique en bloc dès
qu'on quitte la série.

---

## Exemple chiffré complet

```
Base : 100
Modificateurs :
   +20   (Flat)         source = épée
   +0.5  (PercentAdd)   source = potion  → +50%
   +0.1  (PercentAdd)   source = aura    → +10%
   +0.1  (PercentMult)  source = bénédiction → ×1.1

Calcul (dans l'ordre trié) :
   final = 100
   Flat +20            → final = 120
   PercentAdd +0.5     → somme = 0.5
   PercentAdd +0.1     → somme = 0.6 ; dernier de la série → final = 120 × 1.6 = 192
   PercentMult +0.1    → final = 192 × 1.1 = 211.2

Valeur finale = 211.2
```

---

## Le cache : quand ça recalcule

```
Lecture de Value :
   si "dirty" → recalcule + mémorise, dirty = false
   sinon → renvoie la valeur mémorisée

Ce qui met "dirty" (donc recalcule à la prochaine lecture) :
   - changer BaseValue
   - AddModifier / RemoveModifier / RemoveAllFromSource
```

Donc lire `Value` 1000 fois entre deux changements ne coûte qu'un accès mémoire.
Le recalcul n'arrive qu'aux vrais changements, qui émettent aussi `Changed`.

---

## Retirer par source (le confort)

```
Équiper une épée :
   AddModifier(force, +5 Flat, source = epee)
   AddModifier(crit,  +10% PercentAdd, source = epee)

Déséquiper l'épée :
   RemoveAllModifiersFromSource(epee)
   → retire les DEUX bonus, sur les deux stats, d'un coup.
```

Pas besoin de mémoriser quels modificateurs l'épée avait posés : la source suffit.

Lis ensuite `03_choix_de_conception.md`.
