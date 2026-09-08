# State Machine System (FSM générique)

Machine à états finie, réutilisable, découplée de Unity. Namespace :
`Core.StateMachineSystem`.

## Installation
Dépose le dossier `StateMachineSystem/` dans `Assets/` de ton projet Unity.

## Concept en une phrase
Chaque **état** est une classe. La **machine** garde l'état courant et évalue
des **transitions** (une condition `Func<bool>` → un état cible). Quand une
condition devient vraie, la machine bascule.

## Mise en place rapide
1. Crée tes états en héritant de `StateBase` (surcharge seulement ce qu'il faut).
2. Crée un composant héritant de `StateMachineRunner`.
3. Dans `Build(machine)` : instancie tes états, déclare les transitions,
   fixe l'état initial avec `machine.SetState(...)`.
4. Ajoute ce composant sur un GameObject. C'est tout — l'`Update`/`FixedUpdate`
   sont gérés par le runner.

## Exemple minimal
```csharp
using Core.StateMachineSystem;

public sealed class DoorState : StateBase
{
    public override void Enter() => /* ouvrir la porte */;
    public override void Exit()  => /* fermer la porte */;
}

public sealed class DoorMachine : StateMachineRunner
{
    [SerializeField] private bool playerNear;

    protected override void Build(StateMachine machine)
    {
        var closed = new DoorState();
        var open   = new DoorState();

        machine.AddTransition(closed, open,   () => playerNear);
        machine.AddTransition(open,   closed, () => !playerNear);

        machine.SetState(closed);
    }
}
```

## API essentielle
- `machine.AddTransition(from, to, condition)` — transition ciblée.
- `machine.AddAnyTransition(to, condition)` — depuis n'importe quel état
  (ex. `→ Mort` si PV ≤ 0).
- `machine.SetState(state)` — force un état.
- `machine.CurrentState` — l'état actif.
- `machine.OnStateChanged` — event émis à chaque changement (UI, audio, logs).

## Fichiers
- `IState.cs` — contrat d'un état (Enter/Tick/FixedTick/Exit)
- `StateBase.cs` — base à hériter (méthodes virtuelles vides)
- `Transition.cs` — état cible + condition
- `StateMachine.cs` — le moteur (pur C#, testable seul)
- `StateMachineRunner.cs` — pont MonoBehaviour (pilote via Update/FixedUpdate)
- `Examples/` — ennemi Patrouille ↔ Poursuite (contexte + 2 états + runner)
- `Explications/` — documentation détaillée du code

## Exemple fourni
Le dossier `Examples/` montre un ennemi qui patrouille et poursuit une cible
selon la distance. Ajoute `EnemyStateMachine` sur un GameObject, glisse une
cible (`target`) dans l'Inspector, lance : les logs montrent les transitions.
