# 00 — Vue d'ensemble de l'architecture

Comment le système est assemblé, et pourquoi. Les fichiers `01`, `02`, `03`
détaillent ensuite chaque script, le flux d'exécution et les choix de design.

---

## Le problème qu'on résout

Un comportement de jeu passe souvent par des **modes** mutuellement exclusifs :
un ennemi patrouille, poursuit, attaque ; une porte est ouverte ou fermée ;
un joueur est au sol, en saut, en dash. Gérer ça avec des booléens et des `if`
dans un seul `Update` devient vite illisible :

```csharp
// L'anti-pattern qu'on veut éviter :
void Update()
{
    if (isChasing) { /* ... */ }
    else if (isPatrolling && seesPlayer) { isChasing = true; isPatrolling = false; }
    else if (isAttacking && !inRange) { /* ... */ }
    // 200 lignes plus tard, plus personne ne comprend
}
```

Une **machine à états** remplace ce nœud de booléens par des **états explicites**
et des **transitions explicites**. On peut lire à voix haute ce que fait l'objet.

---

## Schéma global

```
        ┌────────────────────────────┐
        │     StateMachineRunner     │  (MonoBehaviour abstrait)
        │  Update()  → Machine.Tick()│
        │  FixedUpdate → FixedTick() │
        └──────────────┬─────────────┘
                       │ possède
                       ▼
        ┌────────────────────────────┐
        │        StateMachine        │  (pur C#, sans Unity)
        │  - CurrentState            │
        │  - transitions par état    │
        │  - transitions "any"       │
        └───────┬────────────┬───────┘
                │            │
        gère des│            │déclenche
                ▼            ▼
        ┌──────────────┐  ┌───────────────┐
        │    IState    │  │  Transition   │
        │ Enter/Tick/  │  │ To + Condition│
        │ FixedTick/   │  │ (Func<bool>)  │
        │ Exit         │  └───────────────┘
        └──────┬───────┘
               │ implémenté via
               ▼
        ┌──────────────┐
        │  StateBase   │  (méthodes virtuelles vides)
        └──────┬───────┘
               │ héritée par
               ▼
   PatrolState, ChaseState, ... (tes états concrets)
```

---

## Les 3 idées de design (le "pourquoi")

### 1. Un état = une classe (pas un `enum`)
Chaque état encapsule **sa** logique dans `Enter/Tick/Exit`. Ajouter un
comportement = ajouter une classe, **sans modifier** les états existants ni un
gros `switch`.

> Principe SOLID : **Open/Closed**.

### 2. La transition connaît la cible, l'état ne connaît personne
Un état ne dit jamais « ensuite, va vers X ». C'est la **machine** qui détient
les transitions (`from → to` si `condition`). Résultat : les états sont
réutilisables et ne dépendent pas les uns des autres.

> Principe SOLID : **Dependency Inversion** — les états dépendent d'abstractions
> (le contexte), pas d'autres états.

### 3. La machine est du C# pur, séparée de Unity
`StateMachine` n'hérite pas de `MonoBehaviour`. Elle est **testable seule** en
dehors d'une scène. Le `StateMachineRunner` est le seul point de contact avec
Unity : il se contente d'appeler `Tick()`/`FixedTick()`.

---

## En une phrase

> Le runner appelle `Tick()` chaque frame → la machine regarde si une transition
> est vraie, bascule si besoin (`Exit` puis `Enter`), puis fait vivre l'état
> courant (`Tick`).

Lis ensuite `01_role_de_chaque_script.md`.
