# 03 — Choix de conception (et leurs limites)

Le « pourquoi » des décisions, et les pièges à connaître.

---

## Pourquoi des classes d'état et pas un `enum` + `switch` ?

Un `enum` paraît plus simple au début :
```csharp
enum State { Patrol, Chase, Attack }
switch (state) { case State.Patrol: ...; }
```
Mais chaque nouvel état gonfle le `switch`, et la logique de **toutes** les
transitions se mélange dans un seul bloc. Avec des classes :
- chaque état est isolé, lisible, testable ;
- ajouter un état ne touche à aucun état existant (**Open/Closed**) ;
- on peut réutiliser un état d'un projet à l'autre.

**Coût :** un peu plus de fichiers. Rentable dès 3 états ou dès qu'un état a une
vraie logique.

---

## Pourquoi les transitions dans la machine, pas dans les états ?

Si un état contenait ses propres sorties (`if (x) GoTo(Chase)`), il devrait
**connaître** les autres états → couplage. En externalisant les transitions
dans la machine, un état ignore totalement ce qui vient avant ou après lui.
C'est ce qui le rend réutilisable.

**Alternative connue :** les transitions internes à l'état (pattern « State »
strict). Plus orienté objet, mais recouple les états entre eux. Le choix ici
privilégie le découplage.

---

## Pourquoi `Func<bool>` pour les conditions ?

Une condition est juste « une fonction qui répond oui/non ». `Func<bool>` permet
d'écrire la condition **là où on déclare la transition**, avec accès au contexte :
```csharp
machine.AddTransition(patrol, chase, () => ctx.DistanceToTarget() <= range);
```
Pas besoin de créer une classe par condition. Lisible et flexible.

**Limite :** ces lambdas sont évaluées **chaque frame**. Garde-les légères (une
comparaison, un test de flag). Évite d'y mettre un `Physics.OverlapSphere` ou un
`GetComponent` — calcule ça ailleurs et stocke le résultat dans le contexte.

---

## Pourquoi séparer `StateMachine` (C# pur) et `StateMachineRunner` (MonoBehaviour) ?

Deux raisons :
1. **Testabilité** : on peut instancier une `StateMachine`, lui donner des états
   factices et appeler `Tick()` dans un test unitaire, **sans** lancer Unity.
2. **Réutilisabilité** : la même machine peut être pilotée autrement qu'en
   `Update` (ex. par un tour de jeu, un serveur, une coroutine).

Le runner ne fait qu'une chose : traduire le cycle Unity en appels `Tick`.

---

## Les transitions "any" sont prioritaires — pourquoi ?

Dans `GetTransition()`, on teste d'abord `_anyTransitions`. Logique : une
condition globale du type « PV ≤ 0 → Mort » doit pouvoir **interrompre**
n'importe quel état, même en pleine attaque. Si tu veux l'inverse pour un cas
précis, dis-le-moi, on ajustera l'ordre.

---

## Le piège de l'initialisation dans `Awake`

`Build` s'exécute dans `Awake`. Si un de tes états a besoin, au constructeur,
d'un composant ajouté par un **autre** script dans son propre `Awake`, l'ordre
n'est pas garanti.

**Solutions (dis-moi si besoin) :**
- 🥇 Passer les dépendances via le **contexte** construit dans `Build` (déjà le
  cas dans l'exemple) → pas de dépendance à l'ordre.
- 🥈 Déplacer la construction dans `Start` (surcharge `Awake` pour ne rien faire,
  et appelle `Build` depuis `Start`).
- 🥉 `[DefaultExecutionOrder]` sur le runner.

---

## Ce que le système NE fait PAS (volontairement)

- Pas d'**états hiérarchiques** (un état contenant une sous-machine). Ajoutable.
- Pas d'**historique** (revenir à l'état précédent). Ajoutable via une pile.
- Pas de **sérialisation** de l'état courant (à combiner avec un Save System si
  tu veux reprendre dans le bon état).
- Pas de **file d'événements** : les transitions sont évaluées par polling
  (chaque frame), pas déclenchées par events. Suffisant dans 95 % des cas et
  plus simple à débuguer.

L'architecture accueille ces extensions sans réécriture.

---

## Récap des principes appliqués

| Principe                    | Où dans le système                                   |
|-----------------------------|------------------------------------------------------|
| **S**ingle Responsibility   | machine ≠ runner ≠ état ≠ transition                 |
| **O**pen/Closed             | nouvel état = nouvelle classe, rien à modifier       |
| **L**iskov Substitution     | tout `IState` est interchangeable pour la machine    |
| **D**ependency Inversion    | états dépendent du contexte, pas les uns des autres  |
| **Composition > héritage**  | données partagées via `EnemyContext`, pas via une classe mère |
| **Testabilité**             | `StateMachine` sans Unity                            |
