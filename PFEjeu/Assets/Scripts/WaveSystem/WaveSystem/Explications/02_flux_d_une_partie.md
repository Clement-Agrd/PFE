# 02 — Le déroulé d'une partie, étape par étape

---

## La boucle du spawner

```
StartWaves()
      │
      ▼
RunWaves() (coroutine)
      │
      └─ tant que vrai :
             wave = waves[_waveIndex]
             │
             ├─ attendre wave.StartDelay
             ├─ OnWaveStarted(numéro global)
             │
             ├─ SpawnWave(wave) :
             │     pour chaque SpawnEntry :
             │         count = round(entry.count × scale endless)
             │         répéter count fois :
             │             SpawnOne(prefab)   → _aliveCount++
             │             attendre entry.interval
             │
             ├─ attendre que _aliveCount == 0   ← les ennemis meurent un par un
             ├─ OnWaveCompleted(numéro)
             │
             ├─ _waveIndex++
             │     si dépasse la dernière vague :
             │         si pas endless → OnAllWavesCompleted ; STOP
             │         sinon → _waveIndex = 0 ; _loop++   (reboucle plus dur)
             │
             └─ attendre delayBetweenWaves
```

---

## Le comptage des vivants (le point clé)

```
SpawnOne → _aliveCount++   et   s'abonne à enemy.Defeated

... l'ennemi vit sa vie ...

L'ennemi meurt → il appelle Defeated?.Invoke(this)
      │
      ▼
WaveSpawner.HandleDefeated(enemy)
      ├─ se désabonne de enemy.Defeated
      └─ _aliveCount--

Quand _aliveCount atteint 0 → la vague est nettoyée → on continue.
```

**Pourquoi ça marche sans connaître l'ennemi :** le spawner ne voit qu'une
interface (`IWaveEnemy`). Peu importe que l'ennemi meure par des PV, un timer ou
en sortant de l'écran : dès qu'il crie `Defeated`, il est décompté.

---

## Exemple concret

```
Wave 1 : { gobelin ×3 (interval 0.5) }
Wave 2 : { gobelin ×2, troll ×1 }
WaveSet : [Wave1, Wave2], endless = false

t=0.0  StartWaves
t=1.0  (startDelay) OnWaveStarted(1)
t=1.0  spawn gobelin  (vivants 1)
t=1.5  spawn gobelin  (vivants 2)
t=2.0  spawn gobelin  (vivants 3)
...    les 3 gobelins meurent → vivants 0
       OnWaveCompleted(1)
t=X    attendre delayBetweenWaves (3s)
       OnWaveStarted(2)  spawn 2 gobelins + 1 troll (vivants 3)
       ... tout meurt → vivants 0 → OnWaveCompleted(2)
       dernière vague, pas endless → OnAllWavesCompleted ; STOP
```

---

## Le scaling endless

```
scale = 1 + _loop × countScalePerLoop

Boucle 0 : scale 1.0 → nombres normaux
Boucle 1 : scale 1.5 → +50 % d'ennemis (si countScalePerLoop = 0.5)
Boucle 2 : scale 2.0 → double
```

Le nombre de chaque `SpawnEntry` est multiplié par `scale` (arrondi). Pour
durcir aussi les PV, ton ennemi peut lire `spawner.Loop` à son spawn et gonfler
sa vie en conséquence.

Lis ensuite `03_choix_de_conception.md`.
