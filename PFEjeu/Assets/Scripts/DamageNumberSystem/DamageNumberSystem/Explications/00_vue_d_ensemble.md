# 00 — Vue d'ensemble

Comment le système est assemblé et pourquoi. Les fichiers `01`, `02`, `03`
détaillent chaque script, le flux d'un chiffre et les choix de design.

---

## Ce qu'on fait

Le « floating combat text » : quand un ennemi prend un coup, un chiffre pop
au-dessus de lui, monte et s'efface. C'est du **feedback** (juice) : ça rend les
coups lisibles et satisfaisants, sans changer la logique de combat.

Deux difficultés à régler proprement :
1. **Positionner** un élément d'UI au-dessus d'un objet du monde 3D (projection
   monde → écran).
2. **Ne pas exploser en performances** quand il y a beaucoup de chiffres
   (recyclage via pooling).

---

## Schéma global

```
   Health.OnDamaged ──► spawner.Spawn(worldPos, dégâts)
                                    │
        ┌───────────────────────────┴──────────────┐
        │        DamageNumberSpawner                │
        │  monde → écran (WorldToScreenPoint)       │
        │  → position ancrée dans le canvas overlay │
        │  → sort un chiffre du PoolManager         │
        └───────────────────────────┬──────────────┘
                                    │
                                    ▼
        ┌──────────────────────────────────────────┐
        │              DamageNumber                 │  (prefab UI + TMP)
        │  monte + fondu + scale (courbes)          │
        │  fin de vie → retour au pool              │
        └──────────────────────────────────────────┘
```

---

## Les 3 idées de design (le "pourquoi")

### 1. Prefab piloté par toi
Le chiffre est un **prefab UI** que tu stylises (police TMP, contour, couleur).
Le système ne fait qu'**animer** et **positionner**. Tu changes le look sans
toucher au code.

### 2. Projection monde → écran
L'ennemi est dans le monde 3D ; le chiffre est de l'UI 2D. Le spawner projette la
position monde en position écran (`WorldToScreenPoint`), puis en position ancrée
dans le canvas. Le chiffre apparaît pile au bon endroit à l'écran.

### 3. Recyclage via le Pooling
Un combat génère beaucoup de chiffres très vite. Les créer/détruire ferait du
garbage et des saccades. On les **recycle** via ton PoolManager → fluide même
sous une pluie de coups.

---

## En une phrase

> À chaque coup, le spawner projette la position de l'ennemi à l'écran et sort un
> chiffre du pool ; le chiffre monte en s'effaçant selon des courbes, puis
> retourne au pool.

Lis ensuite `01_role_de_chaque_script.md`.
