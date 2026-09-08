# 00 — Vue d'ensemble de l'architecture

Comment le système est assemblé et pourquoi. Les fichiers `01`, `02`, `03`
détaillent chaque script, le flux spawn/despawn et les choix de design.

---

## Le problème qu'on résout

`Instantiate` et `Destroy` sont **chers** : allocation mémoire, réveil de tous
les composants, puis plus tard un passage du **garbage collector** qui provoque
des micro-freezes visibles. À haute fréquence (balles, ennemis de vague,
impacts, particules), c'est un tueur de framerate.

Le **pooling** inverse la logique : au lieu de créer/détruire, on garde un stock
d'instances **désactivées** et on les **réactive** au besoin. En régime établi,
zéro allocation, zéro GC.

```
        SANS pool                         AVEC pool
   tir → Instantiate (alloc)         tir → Get (réactive une instance)
   fin → Destroy (→ GC plus tard)    fin → Release (désactive, remet en réserve)
   ... saccades au GC                ... fluide, mémoire stable
```

---

## Schéma global

```
        ┌──────────────────────────────┐
        │         PoolManager          │  (MonoBehaviour)
        │  Dictionary<prefab, pool>    │
        │  Spawn / Despawn (typés)     │
        └───────────────┬──────────────┘
                        │ un par prefab
                        ▼
        ┌──────────────────────────────┐
        │          ObjectPool          │  (C# pur)
        │  Stack<GameObject> inactifs  │
        │  Get / Release / Prewarm     │
        └───────────────┬──────────────┘
                        │ attache à chaque instance
                        ▼
        ┌──────────────────────────────┐
        │         PooledObject         │  (component auto-ajouté)
        │  - sait revenir à son pool   │
        │  - retour auto après délai   │
        │  - cache les IPoolable       │
        └───────────────┬──────────────┘
                        │ notifie
                        ▼
        ┌──────────────────────────────┐
        │          IPoolable           │  (sur tes composants)
        │     OnSpawn / OnDespawn      │
        └──────────────────────────────┘
```

---

## Les 3 idées de design (le "pourquoi")

### 1. Un pool par prefab, orchestré par un manager
Chaque prefab a sa propre réserve (`ObjectPool`). Le `PoolManager` route les
demandes vers le bon pool et en crée un à la volée si le prefab est inconnu.

### 2. `PooledObject` : l'instance sait rentrer chez elle
Plutôt que de demander « à quel pool appartient cet objet ? » à chaque despawn,
chaque instance porte un `PooledObject` configuré **une fois** avec un callback
de retour. Despawn = `instance.GetComponent<PooledObject>().Release()`.

### 3. Hooks `IPoolable` pour un état propre
Un objet recyclé n'est pas « neuf » : il faut réinitialiser ses PV, sa vélocité,
couper ses effets. `OnSpawn`/`OnDespawn` donnent ces points d'accroche, appelés
automatiquement. Les `IPoolable` sont mis en cache → pas de `GetComponent` répété.

---

## En une phrase

> `PoolManager.Spawn` réactive une instance du pool du prefab (ou en crée une),
> la place, appelle `OnSpawn` ; `Despawn` la désactive, appelle `OnDespawn`, et
> la remet en réserve pour la prochaine fois.

Lis ensuite `01_role_de_chaque_script.md`.
