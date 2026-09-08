# 03 — Choix de conception (et leurs limites)

---

## Pourquoi séparer garanti et pondéré ?

Ce sont deux besoins narratifs distincts :
- **Garanti** répond à « ça tombe toujours » (récompense de base fiable).
- **Pondéré** répond à « 1 objet parmi une liste, selon la rareté » (le frisson
  du drop).

Les mélanger dans un seul mécanisme forcerait des bidouilles (un objet garanti
avec un poids énorme). Les séparer rend l'intention claire dans l'Inspector.

---

## Pourquoi le poids plutôt qu'un pourcentage ?

Les poids sont **relatifs** : `10 / 60 / 30` se lit « la potion sort 6× plus que
l'épée ». Ajouter une entrée ne t'oblige pas à recalculer tous les pourcentages
pour qu'ils fassent 100 % — tu ajoutes son poids, c'est tout. Le total se
normalise automatiquement.

Le `nothingWeight` est juste une « entrée invisible » qui représente l'échec du
tirage. Le mettre à 0 garantit qu'un objet sort à chaque tirage.

---

## Pourquoi des sous-tables ?

Pour **réutiliser** et **structurer**. Une table « loot rare » (avec ses propres
poids) peut être référencée par tous les boss ; une table « drops communs »
partagée par tous les gobelins. On change l'équilibrage à un seul endroit. C'est
la composition appliquée au butin.

**Piège :** ne fais pas une table qui se référence elle-même (directement ou en
cercle) → le garde-fou `MaxDepth = 8` évite le crash, mais le résultat serait
absurde. Structure tes tables en arbre, pas en boucle.

---

## Pourquoi la table ne distribue-t-elle pas le butin ?

Séparer « décider ce qui tombe » de « en faire quelque chose » garde la table
réutilisable : le même roll peut alimenter un inventaire, spawn des pickups au
sol, ou remplir un écran de récompense de fin de niveau. `LootDropper` diffuse le
résultat via un event ; la décision t'appartient.

---

## Déterminisme et seed

Le roll utilise `UnityEngine.Random`. Pour un résultat **reproductible** (seed
de run de roguelike, tests), initialise `Random.InitState(seed)` avant de rouler,
ou passe à un `System.Random` local — dis-moi si tu veux une version seedable.

---

## Ce que le système NE fait PAS (volontairement)

- Pas de **pickups au sol** fournis (objets à ramasser) : c'est une couche
  au-dessus (spawn un prefab par LootResult, souvent via ton Object Pooling).
- Pas de **loot lié au niveau/à la chance du joueur** (magic find) : multiplie
  les poids des entrées rares selon une stat avant de rouler, si besoin.
- Pas d'**anti-doublon** ni de **pity timer** (« garanti un rare tous les 10 »).
  Ajoutables au-dessus de la table.
- Pas de **quantité pondérée** (2 pièces plus probable que 10) : la quantité est
  uniforme entre min et max. Pour une distribution, utilise une sous-table ou une
  courbe.

---

## Intégration avec les autres packages

| Système | Lien |
|---|---|
| **Inventory** | `LootResult.Item` est un `ItemDefinition` → `inventory.Add(r.Item, r.Amount)` |
| **Health** | `OnDeath` → `lootTable.Roll()` → dépose le butin |
| **Wave / Pooling** | à la mort d'un ennemi poolé, roule et spawn des pickups poolés |
| **Toolkit** | `WeightedRandom` fait le même tirage pondéré si tu préfères l'externaliser |
| **Event Bus** | `OnLootDropped` → publier `LootDroppedEvent` pour l'UI |

---

## Récap des principes appliqués

| Principe                | Où dans le système                                   |
|-------------------------|------------------------------------------------------|
| **Data-driven**         | butin décrit en tables (SO), réglable sans code       |
| **Deux modèles**        | garanti + pondéré, intentions claires                 |
| **Composition**         | sous-tables réutilisables et imbriquées               |
| **Découplage**          | la table produit, le dropper diffuse, toi tu décides  |
| **Robustesse**          | garde-fou de profondeur contre les cycles             |
