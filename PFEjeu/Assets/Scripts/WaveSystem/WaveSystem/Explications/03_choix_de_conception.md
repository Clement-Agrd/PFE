# 03 — Choix de conception (et leurs limites)

---

## Pourquoi trois niveaux de SO (Entry → Wave → WaveSet) ?

Chaque niveau a un rôle clair :
- `SpawnEntry` : « quoi, combien, à quel rythme » (une ligne).
- `WaveDefinition` : une vague (plusieurs lignes + un délai).
- `WaveSetDefinition` : le déroulé d'un niveau (des vagues + endless).

Ça permet de **réutiliser** : une même vague dans plusieurs niveaux, un même
prefab dans plusieurs vagues. Et un designer compose tout dans l'Inspector.

---

## Pourquoi une coroutine pour le déroulé ?

Une vague, c'est du temps : délais, intervalles de spawn, attente que tout meure.
Une coroutine exprime ça linéairement (`yield return WaitForSeconds`,
`yield return null` en attente) — bien plus lisible qu'une machine à états de
timers dans `Update`. `StopWaves` l'interrompt proprement.

> Optimisation possible : les `new WaitForSeconds(...)` allouent. Avec ton
> package Toolkit, remplace-les par `Wait.Seconds(...)` (mis en cache).

---

## Pourquoi la fin de vague par interface (IWaveEnemy) ?

Le spawner ne doit pas dépendre de ta classe d'ennemi (qui varie selon le jeu).
L'interface `Defeated` est le **minimum** de couplage : l'ennemi dit juste « je
suis vaincu ». Le spawner décompte. Ça marche que l'ennemi meure par PV, timer,
sortie de zone, ou script.

**Piège :** si un prefab spawné n'implémente PAS `IWaveEnemy`, le spawner ne peut
pas le décompter → `_aliveCount` ne redescend jamais → la vague ne se termine
pas. Le spawner émet un warning dans ce cas. Vérifie que tes prefabs
implémentent l'interface.

---

## Pourquoi le pool est-il optionnel (fallback Instantiate) ?

Pour que le spawner reste utilisable même sans pool assigné (prototypage), tout
en profitant du pooling dès qu'il est là. **Mais** le code référence le type
`PoolManager` → le package Pooling doit être présent pour compiler. En prod,
assigne toujours un PoolManager : sur des vagues nombreuses, l'`Instantiate`
répété fait chuter le framerate.

---

## Pourquoi la vague avance « quand tout est mort » ?

C'est le comportement tower-defense classique (nettoyer avant la suivante). Si tu
veux d'autres règles :
- **avancer après un timer** (peu importe s'il reste des ennemis) : remplace
  l'attente `while (_aliveCount > 0)` par une attente de durée ;
- **spawn continu** (pas de vagues distinctes) : une seule « vague » avec un
  gros intervalle en boucle.

Dis-moi si tu veux une de ces variantes.

---

## Ce que le système NE fait PAS (volontairement)

- Pas de **chemins / navigation** : le spawner place les ennemis, leur
  déplacement est leur affaire (NavMesh, waypoints, ton pathfinding).
- Pas de **scaling des PV** intégré (seulement le nombre) : l'ennemi lit
  `spawner.Loop` s'il veut gonfler sa vie.
- Pas de **récompense de fin de vague** : branche-toi sur `OnWaveCompleted`
  (donner de l'or, ouvrir une boutique...).
- Pas de **UI** (compteur de vague, timer) : le spawner expose les events, à toi
  de dessiner.
- Pas de **spawn pondéré aléatoire** dans une entrée : chaque entrée est un
  prefab fixe. Pour du « choisis au hasard parmi ces ennemis », combine avec
  `WeightedRandom` (Toolkit).

---

## Intégration avec les autres packages

| Système | Lien |
|---|---|
| **Object Pooling** | spawn/despawn recyclés (déjà utilisé) |
| **Health** | `OnDeath` → `Defeated?.Invoke(this)` + retour au pool |
| **XP / Loot** | à la mort de l'ennemi (via son Health), donner XP/butin |
| **Event Bus** | republier `OnWaveCompleted` en `WaveClearedEvent` |
| **Toolkit** | `Wait.Seconds` (moins d'alloc), `WeightedRandom` (spawn varié) |

---

## Récap des principes appliqués

| Principe                | Où dans le système                                   |
|-------------------------|------------------------------------------------------|
| **Data-driven**         | vagues composées en SO à trois niveaux                |
| **Découplage**          | fin de vague via IWaveEnemy, pas de dépendance ennemi |
| **Perf**                | spawn via pool (recyclage, zéro GC)                   |
| **Lisibilité**          | déroulé en coroutine linéaire                         |
| **Extensible**          | endless + scaling, events pour brancher le reste      |
