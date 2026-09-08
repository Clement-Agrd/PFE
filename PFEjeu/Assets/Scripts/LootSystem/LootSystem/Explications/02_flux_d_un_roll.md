# 02 — Le flux d'un roll, étape par étape

---

## Le déroulé

```
table.Roll()
      │
      ▼
RollInto(results, depth=0)
      │
      ├─ 1) ENTRÉES GARANTIES
      │      pour chaque entrée guaranteed :
      │          si Random.value <= dropChance → Resolve(entrée)
      │
      └─ 2) TIRAGES PONDÉRÉS
             pool = entrées non garanties
             répéter 'rolls' fois :
                 entrée = PickWeighted(pool)   ← selon les poids, ou « rien »
                 si entrée != null → Resolve(entrée)

Resolve(entrée) :
      si sous-table → RollInto(results, depth+1)   (récursion)
      sinon → quantité = Random(min, max) → ajoute LootResult(item, quantité)
```

---

## PickWeighted : comment le poids devient une probabilité

```
Pool : épée (poids 10), potion (poids 60)   +   nothingWeight 30
Total = 10 + 60 + 30 = 100

roll = Random ∈ [0, 100)

[0 ────── 30)   → rien        (30 %)
[30 ───── 40)   → épée        (10 %)
[40 ──── 100)   → potion      (60 %)
```

On tire un point sur la « roue », on regarde dans quelle tranche il tombe. Plus
le poids est grand, plus la tranche est large, plus l'entrée sort souvent.

---

## Exemple complet

Table « ennemi commun » :
```
Entrées :
  [garanti]  Pièces  ×5-10   dropChance 1
  [garanti]  Gemme   ×1      dropChance 0.1   (10 % de bonus)
  [pondéré]  Épée    poids 10
  [pondéré]  Potion  poids 60
rolls = 1 ,  nothingWeight = 30
```

Un roll possible :
```
1) Garanties :
   Pièces → Random.value(0.4) <= 1 → OUI → quantité 5-10 → 7 pièces
   Gemme  → Random.value(0.83) <= 0.1 → NON → pas de gemme
2) Tirage pondéré (1 fois) :
   roll = 52 → tranche [40,100) → Potion → quantité 1 → 1 potion

Résultat : { Pièces ×7, Potion ×1 }
```

Un autre roll pourrait donner `{ Pièces ×9, Gemme ×1, Épée ×1 }`, ou
`{ Pièces ×6 }` (le tirage pondéré est tombé sur « rien »).

---

## Les sous-tables (récursion)

```
Table "Boss" :
  [garanti] SousTable "Drops garantis boss"   (pièces, clé...)
  [pondéré] SousTable "Loot rare"  poids 100  (l'une des reliques)

Roll de "Boss" :
   Resolve(entrée garantie) → RollInto("Drops garantis boss")  → ses résultats
   Resolve(tirage pondéré)  → RollInto("Loot rare")            → ses résultats
```

La sous-table est roulée **avec ses propres règles** (garanties + poids) et ses
résultats sont ajoutés à la liste. `depth` augmente à chaque niveau ; au-delà de
8, on s'arrête (sécurité).

Lis ensuite `03_choix_de_conception.md`.
