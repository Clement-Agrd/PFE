# Bilan de la zone de défense — scène `Assets/Scenes/GA/SceneJeuxGA.unity`

Unity 6000.6.0f1. Coordonnées en mètres, repère monde Unity : X = est, Z = nord, Y = hauteur.
À lire avec un screenshot de la zone. Valeurs mesurées en direct dans l'Editor, sauf mention contraire.

## 1. Vue d'ensemble

Tower defense (jeu de type Valheim). Les ennemis partent du nord-est et suivent des splines vers la base du joueur au sud-ouest. Le joueur pose des tours sur des emplacements placés à ~10 m des chemins.

```
 Z (nord)
  ^   Spawn_03 (134,43)   Spawn_04 (182,67)
  |   Spawn_01 (210,27)   Spawn_02 (221,-21)      <- spawns ennemis (est/nord-est)
  |
  |        zone de tours : x 95..199, z -74..46    <- 19 TowerSlots + 30 torches
  |
  |   herse / base (81,-88)   Barracks (107,-76)   <- point d'arrivée des 4 chemins
  |   Joueur (93,-107)   Capitaine (92,-112)
  |   muraille (spline sud-ouest, z -177..-86)
  +-----------------------------------------> X (est)
```

Terrain : 1000 x 600 x 1000 m, origine (-500, 0, -500). Hauteur de jeu ~ Y = 5. NavMesh : 2730 sommets, 1222 triangles (`DefenseNavigation`, un `NavMeshSurface`).

## 2. Objectif à défendre : la herse (`herse`)

- Position (80.7, 5.2, -88.4). Composants : `PlayerBase` (maxHealth = 20), `GateController`, `NavTarget`, `NavMeshObstacle`, `VillageEntrance`, `MinimapMarker`.
- Les 4 chemins ennemis finissent entre 6.6 et 10 m de la herse.
- Autour : `Prefabs_Barracks` (106.6, 5, -76.2, avec un `SpawnPoint` à z = -74), `NPC_Capitaine` (92, 6, -112), le joueur (92.6, 5, -106.9).

## 3. Chemins ennemis (`EnemyPath`, 4 splines `EnemySplinePath`)

| Route | Spawn (x, z) | Fin (x, z) | Longueur | Points | Fin → herse |
|---|---|---|---|---|---|
| Spline (01) | (210, 27) | (75.0, -94.0) | 184.8 m | 7 | 8.0 m |
| Spline (1) (02) | (221, -21) | (76.0, -93.0) | 180.7 m | 10 | 6.6 m |
| Spline (2) (03) | (134, 43) | (74.3, -96.0) | 159.0 m | 6 | 10.0 m |
| Spline (3) (04) | (182, 67) | (75.0, -95.0) | 200.1 m | 7 | 8.7 m |

Les 4 routes convergent à ~(75, -95), juste à côté de la herse. Elles ont des longueurs proches (159 à 200 m).

## 4. Système de vagues (`WaveSystem`)

- `WaveSpawner` : 4 `SpawnZone` (`Spawn_Route_01` à `04`, aux positions ci-dessus), `waveSet`, `poolManager`, `villageBetweenWaves` = false, `minPreviewDuration` = 2.5 s.
- `TowerDefenseLevel` : références vers le spawner, l'objectif de défense (la herse), son collider, le chemin par défaut, `wallet` et `playerInventory`.

## 5. Emplacements de tours (`TowerSlots_Temporary`)

19 slots. Chacun a un enfant `TemporaryTowerCube` (cube provisoire, `BoxCollider`). Les slots sont à **9.4 à 11.7 m d'un chemin** (10 m en général), donc des deux côtés des routes.

| Groupe | Slots | Zone approximative |
|---|---|---|
| `01_xx` | 6 | x 96..187, z -56..24 (route 01) |
| `02_xx` | 6 | x 113..199, z -74..-5 (route 02) |
| `03_xx` | 4 | x 102..124, z -33..30 (route 03) |
| `04_xx` | 3 | x 135..162, z -16..46 (route 04) |

Détail (x, y, z) :

- 01_01 (186.5, 5.1, 24.4), 01_02 (174.6, 5.1, -1.7), 01_03 (145.0, 6.1, -4.7), 01_04 (143.6, 5.5, -35.2), 01_05 (127.4, 5.2, -50.3), 01_06 (95.6, 6.0, -55.6)
- 02_01 (198.9, 5.9, -5.1), 02_02 (189.9, 5.1, -28.8), 02_03 (160.2, 5.2, -36.9), 02_04 (157.4, 5.3, -67.2), 02_05 (130.6, 5.3, -74.0), 02_06 (113.2, 5.1, -71.5)
- 03_01 (114.2, 5.0, 30.4), 03_02 (124.0, 5.3, 4.5), 03_03 (102.0, 5.3, -11.7), 03_04 (104.2, 5.9, -32.9)
- 04_01 (162.4, 5.0, 45.8), 04_02 (158.9, 5.1, 19.6), 04_03 (135.3, 5.4, -15.7)

Le groupe 02 est celui qui reste le plus à l'est, le long de la route issue de (221, -21).

## 6. Éclairage : torches

- 30 torches (prefab `Torch_Realistic_Flamee_V2`, flamme) rangées sous un objet racine `Torch` (30 enfants).
- Elles sont à ~2 à 4 m des slots : décalage local ~(±3 à 4, +0.5, 0) par rapport au slot, rotation 0.
- Les 8 slots `01_04`, `01_05` et `02_01` à `02_06` en manquaient. Elles ont été ajoutées à (-4, +0.55, 0) en local (numérotées (22) à (29)), scène sauvegardée.
- Les torches (4) à (5), (1) à (3) et (0) sont plus au sud (z ≈ -96..-106, y ≈ 10.5). Elles éclairent la zone de la muraille ou de la base, pas un slot.

## 7. Muraille (`muraille`)

- 2 splines de mur construites par `SplineWallBuilder` (objets `Muraille` et `Muraille (1)`, à (105, 5, -157)) :
  - mur A : (88, -177) → (83, -92), 99.6 m, 3 points
  - mur B : (78, -86) → (-27, -19), 125 m, 4 points
- Emprise du groupe : environ 186 x 160 m, hauteur ~8 m.

## 8. Points d'attention

- Les 19 slots sont tous « temporaires » (cube provisoire). Les vraies tours ne sont pas encore placées.
- `PlayerBase.maxHealth` = 20 : très faible face à 4 routes qui convergent au même point.
- Les 4 routes finissent ensemble à ~(75, -95). Les ennemis s'y concentrent, donc ce lieu est le point critique. Seul le slot `02_06` (113, -71) et le `01_06` (96, -56) sont proches de la fin des chemins.
- Aucun slot n'est à moins de ~36 m de la herse (`01_06` ≈ 36 m, `02_06` ≈ 37 m). La dernière ligne de défense est donc faible.
- Tous les slots sont à une distance quasi identique des chemins (~10 m), ce qui donne une zone très régulière.
- Non vérifié : le prefab des ennemis et le contenu de `waveSet` (nombre de vagues, types d'ennemis, PV) n'ont pas été inspectés.
