# 01 — Rôle de chaque script

Une responsabilité par fichier. Voici ce que fait chacun et la ligne clé.

---

## `IState.cs` — le contrat d'un état
**Rôle :** définir les 4 moments de vie d'un état.

```csharp
void Enter();      // une fois, à l'entrée
void Tick();       // chaque frame (depuis Update)
void FixedTick();  // chaque pas physique (depuis FixedUpdate)
void Exit();       // une fois, à la sortie
```

C'est le vocabulaire commun : la machine ne sait rien de tes états concrets,
elle sait juste leur dire « entre », « vis », « sors ».

---

## `StateBase.cs` — la base pratique
**Rôle :** implémenter `IState` avec des corps **vides**, pour que tes états
ne surchargent que ce dont ils ont besoin.

```csharp
public virtual void Enter() { }   // à surcharger si utile
public virtual void Tick()  { }
```

Un état « porte fermée » n'a peut-être besoin que de `Enter`/`Exit` :
sans cette base, tu devrais quand même écrire `Tick` et `FixedTick` vides.

---

## `Transition.cs` — une transition
**Rôle :** associer un **état cible** à une **condition**.

```csharp
public IState To { get; }              // vers où aller
public Func<bool> Condition { get; }   // quand y aller
```

`Func<bool>` est une fonction qui renvoie vrai/faux. On y met n'importe quelle
condition : `() => distance < 5f`, `() => health <= 0`, `() => Input.GetKey(...)`.

---

## `StateMachine.cs` — le moteur
**Rôle :** le cœur. Il garde l'état courant, stocke les transitions, et à chaque
`Tick()` décide s'il faut changer d'état.

**Structures internes :**
```csharp
Dictionary<IState, List<Transition>> _transitions;  // sorties propres à chaque état
List<Transition> _anyTransitions;                   // sorties globales (depuis partout)
List<Transition> _currentTransitions;               // cache des sorties de l'état courant
```

**Le changement d'état :**
```csharp
public void SetState(IState state)
{
    if (state == CurrentState) return;   // pas de re-entrée inutile
    CurrentState?.Exit();                // on quitte proprement l'ancien
    CurrentState = state;
    _transitions.TryGetValue(CurrentState, out _currentTransitions);
    _currentTransitions ??= EmptyTransitions;   // met à jour le cache
    CurrentState.Enter();                // on entre dans le nouveau
    OnStateChanged?.Invoke(CurrentState);// on prévient l'extérieur
}
```

**L'event `OnStateChanged` :** permet à ton UI, ton audio ou tes logs de réagir
sans que la machine les connaisse (« l'ennemi passe en alerte → joue un son »).

---

## `StateMachineRunner.cs` — le pont Unity
**Rôle :** relier la machine (C# pur) au cycle de vie Unity.

```csharp
protected virtual void Awake()  { Machine = new StateMachine(); Build(Machine); }
protected abstract void Build(StateMachine machine);   // TU câbles ici
protected virtual void Update()      => Machine.Tick();
protected virtual void FixedUpdate() => Machine.FixedTick();
```

Tu hérites cette classe et tu implémentes `Build` : c'est le seul endroit où
tu crées tes états et déclares tes transitions.

---

## `Examples/EnemyContext.cs` — les données partagées
**Rôle :** un objet simple que **tous** les états de l'ennemi partagent
(transform, cible, vitesse, portée). C'est le pattern **composition** : les
états reçoivent ce contexte au constructeur.

```csharp
public PatrolState(EnemyContext ctx) => _ctx = ctx;
```

Ça évite que chaque état aille chercher ses dépendances tout seul
(pas de `GetComponent`, pas de `Find`, pas de singleton).

---

## `Examples/PatrolState.cs` & `ChaseState.cs` — deux états concrets
**Rôle :** montrer le pattern complet. Chacun n'implémente que ce qui le
concerne, en lisant le contexte.

---

## `Examples/EnemyStateMachine.cs` — le câblage
**Rôle :** l'exemple de `Build`. C'est le modèle à copier.

```csharp
machine.AddTransition(patrol, chase, () => ctx.DistanceToTarget() <= chaseRange);
machine.AddTransition(chase, patrol, () => ctx.DistanceToTarget() > chaseRange);
machine.SetState(patrol);  // ne jamais oublier l'état initial !
```

Lis ensuite `02_flux_d_execution.md`.
