# 02 — Le flux d'un chiffre, étape par étape

---

## De la frappe à l'affichage

```
Ennemi frappé
      │
      ▼
Health.OnDamaged(info)  →  spawner.Spawn(enemy.position, info.Amount, couleur)
      │
      ▼
DamageNumberSpawner.Spawn
      │
      ├─ 1. monde → écran :
      │        screenPoint = camera.WorldToScreenPoint(position + worldOffset)
      │        (worldOffset place le chiffre au-dessus de la tête)
      │        si screenPoint.z < 0 → ennemi derrière la caméra → on ne montre rien
      │
      ├─ 2. écran → canvas :
      │        ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null → overlay)
      │        → position "anchored" dans le canvas
      │
      ├─ 3. sortir un chiffre :
      │        poolManager.Spawn(prefab)  (ou Instantiate)
      │        le parenter au canvas
      │
      └─ 4. number.Play(anchored, texte, couleur)
```

---

## La vie du chiffre (dans DamageNumber)

```
Play → position de départ (+ jitter horizontal), texte, couleur, elapsed = 0

chaque frame (Update) :
      t = elapsed / lifetime   (0 → 1)
      │
      ├─ position = départ + haut × (floatDistance × t)   → il MONTE
      ├─ label.alpha = alphaOverLife.Evaluate(t)          → il S'EFFACE
      └─ scale = scaleOverLife.Evaluate(t)                → il grossit/rebondit

quand t atteint 1 :
      Despawn() → PooledObject.Release()  (retour au pool)
                  ou Destroy si pas poolé
```

---

## Pourquoi le jitter horizontal ?

Sans lui, plusieurs coups au même endroit empilent des chiffres exactement l'un
sur l'autre → illisible. Un petit décalage horizontal aléatoire à l'apparition
les éparpille, comme dans les vrais jeux.

```
Sans jitter :   25        Avec jitter :   25
                25                       12
                12                          38
```

---

## La projection en image

```
        Monde 3D                         Écran 2D (canvas overlay)
   ┌───────────────┐                    ┌───────────────────────┐
   │      💀       │  WorldToScreen     │            25 ↑       │
   │  (position +  │  ───────────────►  │         (anchored)     │
   │   worldOffset)│                    │                        │
   └───────────────┘                    └───────────────────────┘
```

Le chiffre suit la position de l'ennemi **au moment du spawn** (il ne le suit pas
ensuite ; il monte tout seul). Si tu veux qu'il colle à un ennemi qui bouge vite,
il faudrait mettre à jour sa position chaque frame — dis-moi si tu en as besoin.

Lis ensuite `03_choix_de_conception.md`.
