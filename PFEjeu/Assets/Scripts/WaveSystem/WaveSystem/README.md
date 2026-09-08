# Wave Spawner System

Vagues d'ennemis définies en ScriptableObjects, difficulté croissante (endless),
spawn via ton Object Pooling. Namespace : `Core.WaveSystem`.

## ⚠️ Dépendance
Ce package référence le **PoolingSystem** (`Core.PoolingSystem`). Importe-le
d'abord. (Sans PoolManager assigné, le spawner retombe sur `Instantiate`, mais
le code référence toujours le type `PoolManager` — le package Pooling doit être
présent pour compiler.)

## Installation
Dépose le dossier `WaveSystem/` dans `Assets/` (après le PoolingSystem).

## Mise en place
1. Crée des vagues : `Assets > Create > Waves > Wave`. Ajoute des lignes de
   spawn (prefab + nombre + intervalle), et un délai de départ.
2. Crée un enchaînement : `Waves > Wave Set`, glisses-y tes vagues dans l'ordre.
   Coche `loopEndless` pour l'infini + règle `countScalePerLoop`.
3. Sur un GameObject "Spawner" : ajoute `WaveSpawner`, assigne le WaveSet, le
   `PoolManager`, et des points de spawn (optionnels).
4. Tes ennemis doivent implémenter `IWaveEnemy` (voir plus bas).

## L'ennemi doit signaler sa défaite
Le spawner sait qu'une vague est finie quand tous les ennemis ont crié `Defeated`.
Fais implémenter `IWaveEnemy` à ton ennemi et déclenche-le à la mort :
```csharp
public sealed class Enemy : MonoBehaviour, IWaveEnemy
{
    public event Action<IWaveEnemy> Defeated;

    // Branche sur ton Health :
    private void OnEnable() => GetComponent<Health>().OnDeath += Die;
    private void OnDisable() => GetComponent<Health>().OnDeath -= Die;

    private void Die()
    {
        Defeated?.Invoke(this);
        GetComponent<PooledObject>().Release(); // retour au pool
    }
}
```

## Difficulté croissante (endless)
En mode endless, à chaque boucle complète, le nombre d'ennemis est multiplié par
`1 + loop × countScalePerLoop`. Boucle 0 = normal, boucle 1 = +50 % (si 0.5), etc.
Pour scaler aussi les PV, lis `spawner.Loop` depuis ton ennemi et ajuste sa vie.

## API essentielle
- `StartWaves()` / `StopWaves()`.
- `Loop`, `AliveCount`, `IsRunning`.
- Events : `OnWaveStarted(n)`, `OnWaveCompleted(n)`, `OnAllWavesCompleted`,
  `OnEnemySpawned(go)`.

## Fichiers
- `SpawnEntry.cs` — une ligne de spawn (prefab + nombre + intervalle)
- `WaveDefinition.cs` — une vague (SO)
- `WaveSetDefinition.cs` — enchaînement de vagues + endless (SO)
- `IWaveEnemy.cs` — contrat de l'ennemi (event Defeated)
- `WaveSpawner.cs` — le moteur (MonoBehaviour)
- `Examples/WaveEnemyExample.cs` — ennemi de démo (meurt après un délai)
- `Examples/WaveLogger.cs` — logge les events du spawner
- `Explications/` — documentation détaillée

## Brancher sur tes autres systèmes
- **Pooling** : déjà utilisé pour le spawn/despawn.
- **Health** : `OnDeath` → `Defeated?.Invoke(this)` + retour au pool.
- **Event Bus** : republier `OnWaveCompleted` en `WaveClearedEvent`.
- **XP / Loot** : à la mort d'un ennemi, donner XP/butin (via son Health).

## Tester la démo
Crée un prefab ennemi avec `WaveEnemyExample` (+ `PooledObject`). Crée une Wave
(quelques spawns de ce prefab) et un Wave Set. Sur un GameObject : `PoolManager`
+ `WaveSpawner` (assigne WaveSet + PoolManager) + `WaveLogger`. Lance : la Console
montre les vagues démarrer, les ennemis mourir, la vague se nettoyer, la suivante
partir.
