# Tower Defense Kit (adaptation du Wave System)

Rend ton Wave System « parfait » pour un tower defense : vagues déclenchées
manuellement, ennemis qui suivent un chemin, comptés comme vaincus qu'ils soient
TUÉS ou ARRIVÉS À LA BASE.

## ⚠️ Dépendances (tu les as déjà)
- **WaveSystem** (on y remplace le WaveSpawner)
- **PoolingSystem** (spawn/recyclage des ennemis)
- **HealthSystem** (PV des ennemis, tués par les tours)
- **ShopSystem** (Wallet, l'or gagné aux kills)
- **LootSystem** (les drops des ennemis)
- **InventorySystem** (où atterrissent les drops)

## Définir un type d'ennemi (fiche)
Crée une fiche par ennemi : `Assets > Create > Tower Defense > Enemy Definition`.
Tu y règles : **PV** (maxHealth), **vitesse**, **dégâts à la base**, **or au kill**,
et la **table de drop** (une LootTable de ton LootSystem).
Assigne cette fiche sur le prefab (champ `definition` de `TowerDefenseEnemy`).
Sans fiche, l'ennemi utilise les champs réglés directement sur le prefab (fallback).

Ainsi, dans la **WaveDefinition**, tu ajoutes juste le **prefab + le nombre** ;
tout le reste (PV/dégâts/or/drop) vient de la fiche.

## Installation
1. **Remplace** `Assets/WaveSystem/WaveSpawner.cs` par le `WaveSpawner.cs` de ce
   kit (déclenchement manuel).
2. Dépose le dossier `TowerDefense/` dans `Assets/`.

## Ce qui change dans le WaveSpawner
- Plus d'auto-enchaînement : `StartNextWave()` lance la prochaine vague.
- `CanStartWave` dit si le bouton doit être actif (pas de vague en cours + il en
  reste). `HasMoreWaves`, `NextWaveNumber`, `IsWaveActive` exposés.
- Retirés : `autoStart` et `delayBetweenWaves` (inutiles en manuel).
- Le comptage des vivants (via `IWaveEnemy.Defeated`) est inchangé : c'est lui
  qui fait que « tué » ET « arrivé à la base » décomptent la vague.

## Montage de la scène
1. **Le chemin** : un GameObject "Path" avec `WaypointPath`. Crée des GameObjects
   enfants (des points vides) le long de la route et glisse-les dans la liste,
   dans l'ordre. Le **dernier point = la base**. Un gizmo jaune dessine le chemin.
2. **La base** : un GameObject "Base" avec `PlayerBase` (règle ses PV).
3. **Le porte-monnaie** : un `Wallet` sur le joueur (solde de départ).
4. **Le prefab d'ennemi** :
   - `TowerDefenseEnemy` (règle vitesse, dégâts à la base, or donné) ;
   - `PooledObject` (ajouté auto par RequireComponent) ;
   - un `Health` (PV, pour que tes tours puissent le tuer) ;
   - un collider si tes tours le détectent.
5. **Le spawner** : un GameObject "Spawner" avec `WaveSpawner`. Assigne le
   `WaveSet` et le `PoolManager`.
6. **Le coordinateur** : ajoute `TowerDefenseLevel` (sur le spawner ou un objet
   dédié). Assigne : spawner, path, playerBase, wallet, et l'`InventoryHolder` du
   joueur (pour recevoir les drops).

## Le bouton « Vague suivante »
- Crée un bouton UI. Dans son **OnClick**, appelle
  `TowerDefenseLevel.StartNextWave()`.
- Pour griser le bouton pendant une vague, lis `level.CanStartWave` chaque frame :
  ```csharp
  nextWaveButton.interactable = level.CanStartWave;
  ```

## Le flux complet
```
Clic "Vague suivante" → spawner spawn les ennemis (pool)
   → le coordinateur donne à chaque ennemi : chemin + base + wallet
   → l'ennemi lit sa fiche (PV/vitesse/dégâts/or/drop) et suit le chemin :
        tué par une tour (Health.OnDeath) → +or + drop dans l'inventaire → Defeated
        arrive à la base → dégâts à la base → Defeated
   → à 0 ennemi vivant → vague nettoyée → bouton réactivé
```

## Fichiers
- `WaveSpawner.cs` — REMPLACE celui de WaveSystem/ (déclenchement manuel)
- `TowerDefense/EnemyDefinition.cs` — fiche d'un ennemi (PV, dégâts, or, drop)
- `TowerDefense/WaypointPath.cs` — le chemin
- `TowerDefense/PlayerBase.cs` — les PV de la base
- `TowerDefense/TowerDefenseEnemy.cs` — l'ennemi (fiche + chemin + mort/base + drop + pool)
- `TowerDefense/TowerDefenseLevel.cs` — coordinateur + StartNextWave()

## Notes
- **2D** : dans `TowerDefenseEnemy.Update`, `transform.forward = dir` est du 3D.
  Pour du 2D, remplace par une rotation sur Z (dis-moi, je te la donne).
- Tes **tours** ne sont pas dans ce kit : elles doivent juste faire des dégâts au
  `Health` de l'ennemi (`health.TakeDamage(...)`). La mort est gérée ici.
- Afficher les PV de la base / l'or : branche ta `UIBar` sur `PlayerBase.OnHealthChanged`
  et un label sur `Wallet.OnBalanceChanged`.
