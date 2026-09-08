# Object Pooling System (recyclage d'objets)

Pool d'objets réutilisables pour éviter les `Instantiate`/`Destroy` répétés
(coûteux en CPU et générateurs de GC/micro-freezes). Namespace :
`Core.PoolingSystem`.

## Installation
Dépose le dossier `PoolingSystem/` dans `Assets/` de ton projet Unity.

## Pourquoi pooler ?
`Instantiate` alloue de la mémoire et `Destroy` la libère plus tard → à haute
fréquence (balles, ennemis, particules), ça cause du **garbage collection** et
des saccades. Le pool garde des instances désactivées et les **recycle** :
zéro allocation en régime établi.

## Mise en place
1. Sur un GameObject "Pooling" : ajoute `PoolManager`. Optionnel : renseigne des
   pools à précharger (prefab + prewarm + auto-expand).
2. Ajoute (ou laisse le pool ajouter) un `PooledObject` sur tes prefabs poolés.
3. Fais apparaître / disparaître via le manager :
   ```csharp
   GameObject go = poolManager.Spawn(prefab, position, rotation);
   poolManager.Despawn(go);            // retour immédiat
   poolManager.Despawn(go, 3f);        // retour dans 3 s
   ```

## Réagir au recyclage
Implémente `IPoolable` sur un composant du prefab :
```csharp
public sealed class Enemy : MonoBehaviour, IPoolable
{
    public void OnSpawn()   { /* reset PV, IA, position... */ }
    public void OnDespawn() { /* couper effets, arrêter coroutines... */ }
}
```
`OnSpawn`/`OnDespawn` sont appelés automatiquement à chaque sortie/retour.

## Retour automatique
Depuis un composant du prefab :
```csharp
GetComponent<PooledObject>().ReleaseAfter(2f); // revient au pool dans 2 s
GetComponent<PooledObject>().Release();        // revient tout de suite
```

## Spawn typé
```csharp
Projectile p = poolManager.Spawn<Projectile>(projectilePrefab, pos, rot);
```

## API essentielle
- `PoolManager.Spawn(prefab, pos, rot)` / `Spawn<T>(...)`.
- `PoolManager.Despawn(instance)` / `Despawn(instance, delay)`.
- `PooledObject.Release()` / `ReleaseAfter(seconds)`.
- `ObjectPool` : `Get`, `Release`, `Prewarm`, `Clear`, `CountActive/Inactive/All`.
- `IPoolable` : `OnSpawn`, `OnDespawn`.

## Fichiers
- `IPoolable.cs` — hooks de recyclage
- `PooledObject.cs` — lien instance ↔ pool + retour auto + cache des IPoolable
- `ObjectPool.cs` — pool d'un prefab (prewarm, auto-expand) — C# pur
- `PoolManager.cs` — pont MonoBehaviour, un pool par prefab, Spawn/Despawn
- `Examples/Projectile.cs`, `ProjectileSpawner.cs` — démo tir + retour auto
- `Explications/` — documentation détaillée du code

## Tester la démo
Crée un prefab de projectile (avec `Projectile`). Sur un GameObject : `PoolManager`
+ `ProjectileSpawner` (assigne le prefab). Espace tire ; les projectiles
reviennent seuls au pool après leur durée de vie — inspecte la hiérarchie pour
voir les instances se réactiver au lieu d'être recréées.
