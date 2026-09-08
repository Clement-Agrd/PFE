# 00 — Vue d'ensemble

Comment le système est assemblé et pourquoi. Les fichiers `01`, `02`, `03`
détaillent chaque script, le déroulé d'un roll et les choix de design.

---

## Le problème qu'on résout

« Quand cet ennemi meurt, il lâche : toujours quelques pièces, et 1 objet parmi
une liste, chacun avec sa rareté. » Coder ça à la main (des `Random.value`
éparpillés, des seuils en dur) devient vite illisible et impossible à équilibrer.

On veut décrire le butin comme une **donnée** (une table) qu'un designer règle,
et un moteur qui la **roule** selon des poids.

---

## Schéma global

```
        ┌──────────────────────────────┐
        │         LootDropper          │  (MonoBehaviour, optionnel)
        │  Drop() → roule + event      │
        └───────────────┬──────────────┘
                        │ roule
                        ▼
        ┌──────────────────────────────┐
        │        LootTable (SO)        │
        │  entrées + rolls + nothing   │
        │  Roll() → List<LootResult>   │
        └───────────────┬──────────────┘
                        │ liste de
                        ▼
        ┌──────────────────────────────┐
        │          LootEntry           │
        │  item OU sous-table          │
        │  poids, quantité, garanti    │
        └───────────────┬──────────────┘
                        │ produit
                        ▼
        ┌──────────────────────────────┐
        │         LootResult           │  item + quantité
        └──────────────────────────────┘
```

---

## Les 3 idées de design (le "pourquoi")

### 1. Deux modèles de drop, combinés
- **Garanti** : l'entrée tombe toujours (avec une probabilité propre si voulu).
  Pour les récompenses sûres (pièces, XP en objet...).
- **Pondéré** : un pool où chaque entrée a un **poids** ; la table fait N tirages,
  chacun tombant sur une entrée proportionnellement à son poids (ou sur « rien »).
  Pour la rareté (commun / rare / épique).

Une même table peut mélanger les deux : « toujours des pièces + 1 objet aléatoire ».

### 2. Sous-tables imbriquées
Une entrée peut pointer vers une **autre table**. On compose : une table
« drops communs » et une table « drops rares » réutilisées par plusieurs
ennemis, référencées depuis une table principale. Réutilisation maximale.

### 3. Découplé du « quoi en faire »
La table **produit** une liste d'objets ; elle ne les distribue pas. `LootDropper`
diffuse le résultat via un event. À toi de choisir : ajouter à l'inventaire,
faire apparaître des pickups au sol, afficher un écran de récompense.

---

## En une phrase

> La table roule ses entrées garanties (chacune selon sa probabilité) puis fait
> N tirages pondérés dans le reste (rareté), en résolvant les sous-tables au
> passage, et renvoie la liste des objets obtenus.

Lis ensuite `01_role_de_chaque_script.md`.
