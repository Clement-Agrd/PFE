# 00 — Vue d'ensemble

Comment le système est assemblé et pourquoi. Les fichiers `01`, `02`, `03`
détaillent chaque script, le flux d'une mise à jour et les choix de design.

---

## Le rôle de ce système

Tu as des dizaines de systèmes qui produisent des **valeurs** (PV, XP, or,
stamina...). Il te manque de quoi les **montrer**. Une barre de remplissage est
la réponse universelle : vie, XP, mana, chargement, réputation... tout se
représente par « une valeur entre 0 et un max ».

L'idée : une barre **générique** qui prend `(current, max)` de n'importe quelle
source, et l'affiche joliment (animée, colorée, avec un effet de dégâts).

---

## Schéma global

```
   n'importe quelle source ──SetValue(cur, max)──┐
   (Health, XP, Shop, Stats)                     ▼
        ┌──────────────────────────────┐
        │            UIBar             │
        │  remplissage animé (fill)    │
        │  traînée retardée (dégâts)   │
        │  label + couleur (option)    │
        └───────────────┬──────────────┘
                        │ (optionnel)
                        ▼
        ┌──────────────────────────────┐
        │        WorldSpaceBar         │
        │  suit un ennemi + billboard  │
        │  se cache quand plein        │
        └──────────────────────────────┘
```

---

## Les 3 idées de design (le "pourquoi")

### 1. Interface universelle : SetValue(current, max)
La barre ne connaît ni Health, ni XP. Elle prend deux nombres. **Toute** source
de valeur s'y branche en une ligne : `source.OnChanged += (c, m) => bar.SetValue(c, m)`.
C'est ce qui la rend réutilisable partout.

### 2. Remplissage animé + traînée de dégâts
Un remplissage qui « saute » d'un coup est moche. On l'anime en douceur. Et une
seconde barre **retardée** derrière montre les dégâts encaissés (le flash rouge
qui rattrape) — un effet qui rend le combat lisible et satisfaisant.

### 3. HUD ou world-space, même barre
La même `UIBar` sert en HUD (coin de l'écran) ou au-dessus d'un ennemi. Le
`WorldSpaceBar` ajoute juste le suivi et le billboard, sans dupliquer la logique.

---

## En une phrase

> N'importe quelle source appelle `SetValue(current, max)` ; la barre anime son
> remplissage, montre les dégâts via une traînée retardée, et peut suivre un
> ennemi en world-space.

Lis ensuite `01_role_de_chaque_script.md`.
