# 00 — Vue d'ensemble

Comment le système est assemblé et pourquoi. Les fichiers `01`, `02`, `03`
détaillent chaque script, le déroulé d'une partie et les choix de design.

---

## Le problème qu'on résout

Un tower defense (ou un mode survie de roguelike) enchaîne des **vagues**
d'ennemis, de plus en plus dures, chacune avec sa composition (5 gobelins, puis
3 gobelins + 1 troll...). On veut :
- décrire ces vagues **sans coder** (un designer les compose) ;
- **recycler** les ennemis (pooling) au lieu d'en créer/détruire des centaines ;
- savoir **quand une vague est finie** pour lancer la suivante.

---

## Schéma global

```
        ┌──────────────────────────────┐
        │         WaveSpawner          │  (MonoBehaviour)
        │  enchaîne les vagues         │
        │  spawn via PoolManager       │
        │  compte les vivants          │
        └───────────────┬──────────────┘
                        │ déroule
                        ▼
        ┌──────────────────────────────┐
        │     WaveSetDefinition (SO)   │  la séquence du niveau
        │  waves[] + endless           │
        └───────────────┬──────────────┘
                        │ liste de
                        ▼
        ┌──────────────────────────────┐
        │      WaveDefinition (SO)     │  une vague
        │  startDelay + spawns[]       │
        └───────────────┬──────────────┘
                        │ liste de
                        ▼
        ┌──────────────────────────────┐
        │        SpawnEntry            │  prefab + nombre + intervalle
        └──────────────────────────────┘

   IWaveEnemy : l'ennemi crie "Defeated" en mourant → le spawner décompte
   PoolManager : fournit/recycle les instances
```

---

## Les 3 idées de design (le "pourquoi")

### 1. Vagues en données (SO), à trois niveaux
`SpawnEntry` (une ligne) → `WaveDefinition` (une vague) → `WaveSetDefinition`
(le niveau entier). On compose tout dans l'Inspector, réutilisable et éditable
sans programmer.

### 2. Spawn via le Pooling
Le spawner appelle `poolManager.Spawn(...)` → les ennemis sont recyclés. Sur des
vagues de centaines d'ennemis, ça évite le garbage et les saccades. Fallback
`Instantiate` si aucun pool n'est assigné.

### 3. Fin de vague par comptage découplé
Le spawner ne connaît pas ta classe d'ennemi. Il compte : +1 au spawn, −1 quand
l'ennemi crie `Defeated` (via l'interface `IWaveEnemy`). À 0 → vague nettoyée.
Ton ennemi branche `Defeated` sur sa mort (Health, sortie d'écran...).

---

## En une phrase

> Le spawner déroule les vagues d'un WaveSet : il fait apparaître (via le pool)
> les ennemis de chaque vague, attend qu'ils soient tous vaincus, passe à la
> suivante, et reboucle en plus dur si le mode endless est activé.

Lis ensuite `01_role_de_chaque_script.md`.
