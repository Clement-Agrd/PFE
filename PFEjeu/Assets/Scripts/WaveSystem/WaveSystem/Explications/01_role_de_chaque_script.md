# 01 — Rôle de chaque script

---

## `SpawnEntry.cs` — une ligne de spawn
```csharp
public GameObject prefab;   // quel ennemi
public int count;           // combien
public float interval;      // délai entre chaque (0 = tous d'un coup)
```
La brique de base. Une vague en contient plusieurs (ex. « 5 gobelins toutes les
0.5 s » + « 1 troll »).

---

## `WaveDefinition.cs` — une vague (SO)
```csharp
private string waveName;
private float startDelay;              // avant de commencer
private List<SpawnEntry> spawns;
public int TotalCount { ... }          // somme des count (avant scaling)
```
Un asset = une vague. Composée de lignes de spawn.

---

## `WaveSetDefinition.cs` — l'enchaînement (SO)
```csharp
private List<WaveDefinition> waves;    // l'ordre des vagues
private bool loopEndless;              // reboucler à l'infini ?
private float countScalePerLoop;       // +X d'ennemis par boucle
```
Le déroulé complet d'un niveau. C'est ce que tu assignes au spawner.

---

## `IWaveEnemy.cs` — le contrat de l'ennemi
```csharp
public interface IWaveEnemy { event Action<IWaveEnemy> Defeated; }
```
L'ennemi **crie sa défaite**. Le spawner s'y abonne pour décompter les vivants,
sans jamais connaître la classe concrète de l'ennemi.

---

## `WaveSpawner.cs` — le moteur
**Rôle :** dérouler le WaveSet.

**La boucle principale (coroutine) :**
```csharp
tant que (vrai) :
    wave = waves[_waveIndex]
    attendre wave.StartDelay
    OnWaveStarted(numéro)
    SpawnWave(wave)                    ← fait apparaître les ennemis
    tant que _aliveCount > 0 : attendre  ← attend que tout meure
    OnWaveCompleted(numéro)
    passer à la vague suivante (ou reboucler endless, ou finir)
    attendre delayBetweenWaves
```

**Le spawn d'un ennemi :**
```csharp
instance = poolManager != null ? poolManager.Spawn(...) : Instantiate(...)
_aliveCount++
OnEnemySpawned(instance)
si instance est IWaveEnemy → s'abonner à Defeated
sinon → warning (le spawner ne saura pas quand il meurt)
```

**Quand un ennemi meurt :**
```csharp
HandleDefeated : se désabonne, _aliveCount--
```

Expose `StartWaves/StopWaves`, `Loop`, `AliveCount`, et les events.

---

## `Examples/`
- **`WaveEnemyExample`** — meurt après un délai, crie `Defeated`, retourne au
  pool. (En vrai jeu : `health.OnDeath += Die`.)
- **`WaveLogger`** — logge les events du spawner.

Lis ensuite `02_flux_d_une_partie.md`.
