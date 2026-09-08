# 03 — Choix de conception (et leurs limites)

---

## Pourquoi un `PooledObject` sur chaque instance ?

Le vrai problème du pooling : au moment du despawn, savoir **à quel pool** rendre
l'objet. Deux mauvaises réponses classiques :
- un `Dictionary<instance, pool>` global → lookup + entretien à chaque
  spawn/despawn ;
- chercher le pool par le nom/prefab → fragile.

La bonne réponse : l'instance **porte** son moyen de retour. Le `PooledObject`
capture un callback `Release` pointant vers son pool précis. Despawn devient
O(1) et robuste, même avec des dizaines de pools.

**Bonus :** il met aussi en cache les `IPoolable` (un seul `GetComponentsInChildren`
à la création) et gère le retour automatique.

---

## Pourquoi désactiver (SetActive) plutôt que détruire ?

C'est le principe même : un objet désactivé ne coûte quasiment rien (pas
d'Update, pas de rendu) mais reste **prêt à resservir instantanément**. Le
détruire annulerait tout le bénéfice.

**Conséquence à connaître :** `OnDisable`/`OnEnable` de tes composants se
déclenchent à chaque despawn/spawn. Si tu t'abonnes à des events dans
`OnEnable`, tu te désabonnes dans `OnDisable` → ça reste cohérent avec le
pattern habituel.

---

## Prewarm vs Auto-expand : à quoi ça sert ?

- **Prewarm** : payer le coût de création **au chargement** (écran de load),
  pas pendant l'action. Dimensionne-le sur ton usage typique.
- **Auto-expand** : filet de sécurité si la demande dépasse le prewarm. Le pool
  grandit au pic et garde ensuite ces instances. Désactive-le
  (`autoExpand = false`) si tu veux plafonner strictement (ex. 20 balles max) —
  `Get` renverra alors `null` quand c'est plein, à toi de gérer.

---

## Pourquoi un `ObjectPool` non générique (GameObject) ?

Le manager doit indexer des pools **hétérogènes** dans un seul dictionnaire
(balles, ennemis, impacts...). Un pool générique `ObjectPool<T>` donnerait un
type différent par prefab, impossible à ranger ensemble simplement. On raisonne
donc en `GameObject`, et `Spawn<T>` fait le `GetComponent<T>` final pour le
confort typé côté appelant.

---

## Le piège du reset d'état

Un objet recyclé **n'est pas neuf**. Si tu oublies de réinitialiser dans
`OnSpawn`, tu auras des bugs vicieux : un ennemi qui réapparaît avec 0 PV, un
projectile qui garde sa vélocité précédente, une coroutine encore en cours.

**Règle :** tout état qui change pendant la vie de l'objet doit être remis à sa
valeur de départ dans `OnSpawn` (ou coupé dans `OnDespawn`).

---

## Autre piège : les références vers une instance rendue

Après un `Despawn`, l'instance peut être **réutilisée** ailleurs. Si tu gardes
une référence vers elle (ex. « ma cible »), tu pourrais pointer vers un objet
devenu autre chose. Vérifie `activeInHierarchy` ou invalide tes références au
despawn.

---

## Ce que le système NE fait PAS (volontairement)

- Pas de **limite mémoire globale** ni de rétrécissement automatique du pool
  (on ne détruit les inactifs que sur `Clear()`).
- Pas de **pooling d'objets non-GameObject** (structures pures) — c'est un autre
  besoin, couvert par `UnityEngine.Pool.ObjectPool<T>` intégré.
- Pas de **persistance entre scènes** (le manager est un MonoBehaviour de scène ;
  ajoute `DontDestroyOnLoad` si tu veux qu'il survive).

---

## Intégration avec les autres packages

- **Ability System** : un `SpawnProjectileEffect` peut appeler
  `poolManager.Spawn(...)` au lieu d'`Instantiate`.
- **Event Bus** : publie un `EnemySpawnedEvent`/`EnemyDespawnedEvent` dans
  `OnSpawn`/`OnDespawn` pour tenir un compteur de vague sans couplage.

---

## Récap des principes appliqués

| Principe                    | Où dans le système                                   |
|-----------------------------|------------------------------------------------------|
| **Perf / zéro GC**          | recyclage au lieu d'Instantiate/Destroy              |
| **Single Responsibility**   | manager (routage) ≠ pool (réserve) ≠ PooledObject (retour) |
| **O(1) despawn**            | l'instance porte son callback de retour              |
| **Open/Closed**             | tes objets réagissent via IPoolable, pool inchangé   |
| **Cache > lookup répété**   | IPoolable mis en cache à la création                 |
