# 01 — Rôle de chaque script

Une responsabilité par fichier.

---

## `IPoolable.cs` — les hooks de recyclage
**Rôle :** deux méthodes qu'un composant implémente pour réagir au recyclage.

```csharp
void OnSpawn();    // l'objet sort du pool → réinitialise l'état
void OnDespawn();  // l'objet retourne au pool → nettoie
```

Sans ça, un objet réutilisé garderait l'état de sa vie précédente (PV à 0,
vélocité résiduelle, effet encore actif). Facultatif : un prefab sans logique
d'état n'a pas besoin d'implémenter l'interface.

---

## `PooledObject.cs` — le lien instance ↔ pool
**Rôle :** ajouté automatiquement à chaque instance. Il porte trois choses :

1. **Le retour à son pool** :
```csharp
public void Initialize(Action<GameObject> returnToPool) { _returnToPool = returnToPool; ... }
public void Release() => _returnToPool?.Invoke(gameObject);
```
2. **Le retour automatique après délai** :
```csharp
public void ReleaseAfter(float seconds) => _autoReleaseAt = Time.time + seconds;
private void Update() { if (Time.time >= _autoReleaseAt) Release(); }
```
3. **Le cache des IPoolable** (perf) :
```csharp
_poolables = GetComponentsInChildren<IPoolable>(true); // fait UNE fois à la création
```
→ `InvokeSpawn`/`InvokeDespawn` bouclent sur ce cache sans re-scanner l'objet.

---

## `ObjectPool.cs` — le pool d'un prefab
**Rôle :** la réserve d'un seul prefab. Une `Stack<GameObject>` d'instances
inactives.

```csharp
public GameObject Get(pos, rot)
{
    GameObject go = _inactive.Count > 0 ? _inactive.Pop()
                  : _autoExpand         ? CreateInstance()
                  : null;                                 // épuisé et non extensible
    ...
    go.SetActive(true);
    po.InvokeSpawn();
    return go;
}

public void Release(GameObject go)
{
    po.InvokeDespawn();
    go.SetActive(false);
    _inactive.Push(go);
}
```

**Prewarm :** crée N instances désactivées à l'avance → aucun `Instantiate`
pendant l'action. **Auto-expand :** si la réserve est vide, on crée une instance
de plus (le pool grandit au pic de demande).

`CreateInstance` attache et configure le `PooledObject` (capture de `Release`
→ l'instance saura revenir à CE pool précis).

---

## `PoolManager.cs` — le point d'accès
**Rôle :** router les demandes vers le bon pool. Un `Dictionary<prefab, pool>`.

```csharp
public GameObject Spawn(prefab, pos, rot) => GetOrCreatePool(prefab).Get(pos, rot);
public T Spawn<T>(prefab, pos, rot) => Spawn(prefab, pos, rot)?.GetComponent<T>();
public void Despawn(instance) => instance.GetComponent<PooledObject>()?.Release();
```

Précharge des pools via l'Inspector (prefab + prewarm + auto-expand), ou crée un
pool à la volée pour un prefab jamais vu.

---

## `Examples/`
- **`Projectile.cs`** — implémente `IPoolable`, avance, et se renvoie au pool
  après `lifetime` via `PooledObject.ReleaseAfter`.
- **`ProjectileSpawner.cs`** — tire un projectile du pool à chaque appui.

Lis ensuite `02_flux_spawn_despawn.md`.
