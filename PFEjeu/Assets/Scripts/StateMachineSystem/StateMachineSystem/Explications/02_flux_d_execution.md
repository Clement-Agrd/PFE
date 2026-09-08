# 02 — Le flux d'exécution, étape par étape

On suit ce qui se passe du démarrage jusqu'à un changement d'état.

---

## Étape 0 — Construction (au démarrage)

```
GameObject activé
      │
      ▼
StateMachineRunner.Awake()
      │
      ├─ Machine = new StateMachine()
      │
      └─ Build(Machine)          ← TON code : crée états + transitions
             │
             ├─ new PatrolState(ctx), new ChaseState(ctx)
             ├─ machine.AddTransition(patrol, chase, condition)
             ├─ machine.AddTransition(chase, patrol, condition)
             └─ machine.SetState(patrol)   ← 1er Enter() déclenché ici
```

À la fin de `Build`, la machine a un état courant (`patrol`) dont `Enter()` a
déjà tourné.

---

## Étape 1 — Chaque frame (Update)

```
StateMachineRunner.Update()
      │
      ▼
Machine.Tick()
      │
      ├─ 1. GetTransition()
      │        │
      │        ├─ parcourt les transitions "any"      (prioritaires)
      │        └─ parcourt les transitions de l'état courant
      │        │
      │        └─ renvoie la 1re dont Condition() == true, sinon null
      │
      ├─ 2. si transition trouvée → SetState(transition.To)
      │        │
      │        ├─ CurrentState.Exit()   (ancien état)
      │        ├─ CurrentState = nouveau
      │        ├─ recharge le cache des transitions
      │        └─ CurrentState.Enter()  (nouvel état) + OnStateChanged
      │
      └─ 3. CurrentState.Tick()         ← fait vivre l'état (nouveau ou inchangé)
```

**Ordre important :** on évalue la transition **avant** le `Tick`. Donc si on
vient de basculer, c'est le **nouvel** état qui reçoit le `Tick` de cette frame.

---

## Étape 2 — Chaque pas physique (FixedUpdate)

```
StateMachineRunner.FixedUpdate()  →  Machine.FixedTick()  →  CurrentState.FixedTick()
```

Utilise `FixedTick` pour tout ce qui touche au `Rigidbody` / à la physique
(déplacements avec forces, `MovePosition`, etc.). `Tick` (Update) pour le reste
(lecture d'inputs, timers, logique de décision).

---

## Exemple concret : l'ennemi

```
Frame N   : état = Patrol.  distance = 8 (> 5).  Aucune transition. Patrol.Tick()
Frame N+1 : la cible approche, distance = 4 (<= 5).
            → transition Patrol→Chase vraie
            → Patrol.Exit()  ("Quitte la Patrouille")
            → Chase.Enter()  ("Entre en Poursuite")
            → Chase.Tick()   (avance vers la cible)
...
Frame M   : la cible fuit, distance = 6 (> 5).
            → transition Chase→Patrol vraie
            → Chase.Exit() → Patrol.Enter() → Patrol.Tick()
```

Chaque bascule produit exactement un `Exit` et un `Enter` → parfait pour
brancher animations, sons, VFX au bon moment.

---

## Cycle de vie Unity concerné

| Méthode Unity | Dans le runner | Rôle                                          |
|---------------|----------------|-----------------------------------------------|
| `Awake`       | crée la machine + `Build` | prépare tout avant la 1re frame    |
| `Update`      | `Machine.Tick()`          | transitions + logique par frame    |
| `FixedUpdate barrier` | `Machine.FixedTick()` | logique physique de l'état     |

> `Build` tourne dans `Awake`. Si un état a besoin d'un autre composant, assure-toi
> qu'il existe déjà (sinon initialise dans `Start` — voir `03`).

Lis ensuite `03_choix_de_conception.md`.
