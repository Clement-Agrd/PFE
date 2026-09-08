# 02 — Le flux Spawn / Despawn, étape par étape

---

## Étape 0 — Préchargement (Awake du PoolManager)

```
PoolManager.Awake()
      │
      └─ pour chaque pool préconfiguré (Inspector) :
             new ObjectPool(prefab, parent, prewarm, autoExpand)
                   │
                   └─ Prewarm(n) : crée n instances, les désactive, les empile
                          (chaque création attache un PooledObject configuré)
```

Résultat : au démarrage, la réserve est prête. Aucun `Instantiate` pendant
l'action tant qu'on reste sous le pic préchargé.

---

## Flux SPAWN

```
poolManager.Spawn(projectilePrefab, pos, rot)
      │
      ▼
GetOrCreatePool(prefab)            ← trouve (ou crée) le pool de ce prefab
      │
      ▼
ObjectPool.Get(pos, rot)
      │
      ├─ réserve non vide ? → Pop une instance inactive
      ├─ sinon auto-expand ? → CreateInstance (le pool grandit)
      ├─ sinon               → return null (épuisé, non extensible)
      │
      ├─ place l'instance (position + rotation)
      ├─ SetActive(true)
      ├─ CountActive++
      └─ PooledObject.InvokeSpawn() → OnSpawn() sur chaque IPoolable
                                       (reset PV, vélocité, programme le retour...)
```

---

## Flux DESPAWN (retour au pool)

Trois façons de déclencher le retour, toutes convergent au même endroit :

```
 a) poolManager.Despawn(go)                → PooledObject.Release()
 b) go.GetComponent<PooledObject>().Release()
 c) ReleaseAfter(t) → Update() atteint le délai → Release()
                                   │
                                   ▼
                    _returnToPool(gameObject)   (callback capturé à la création)
                                   │
                                   ▼
                    ObjectPool.Release(go)
                          │
                          ├─ PooledObject.InvokeDespawn() → OnDespawn() sur IPoolable
                          ├─ SetActive(false)
                          ├─ reparent sous le pool (rangement)
                          ├─ Push dans la réserve
                          └─ CountActive--
```

L'instance n'est **pas détruite** : elle attend, désactivée, le prochain
`Spawn`. C'est tout l'intérêt.

---

## Le cycle complet illustré

```
Prewarm(3)        réserve = [A, B, C]           actifs = 0
Spawn             réserve = [A, B]   → C actif   actifs = 1
Spawn             réserve = [A]      → B actif    actifs = 2
Despawn(C)        réserve = [A, C]   (C désactivé) actifs = 1
Spawn             réserve = [A]      → C réactivé  actifs = 2   ← recyclage, pas de création
Spawn Spawn       réserve = []       (A sort, puis auto-expand D) actifs = 4
```

---

## Pourquoi le retour auto passe par un timer dans Update ?

`ReleaseAfter(t)` mémorise `Time.time + t`. Chaque frame, `PooledObject.Update`
compare et déclenche `Release` quand l'échéance est atteinte. Simple, sans
coroutine à gérer, et annulable (un nouveau `ReleaseAfter` ou un `Release`
manuel réinitialise l'échéance).

> Note : le timer utilise `Time.time` (temps du jeu). Si tu mets le jeu en pause
> avec `Time.timeScale = 0`, les retours auto se figent aussi.

Lis ensuite `03_choix_de_conception.md`.
