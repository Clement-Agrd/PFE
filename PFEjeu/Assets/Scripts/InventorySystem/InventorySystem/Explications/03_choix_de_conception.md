# 03 — Choix de conception (et leurs limites)

Le « pourquoi » des décisions, et les pièges à connaître.

---

## Pourquoi séparer `ItemDefinition` (SO) et `ItemStack` ?

C'est LE choix structurant. Les données figées d'un item (nom, icône, stack max)
sont identiques pour toutes ses instances. Les mettre dans un ScriptableObject
**partagé** évite de les dupliquer des milliers de fois en mémoire, et rend la
sauvegarde minuscule (on stocke juste un id + une quantité).

**Analogie :** `ItemDefinition` = le Prefab, `ItemStack` = l'instance.

**Limite :** si tu veux des items avec un **état par instance** (durabilité
d'une arme, enchantement unique), `ItemStack` devient le bon endroit pour
ajouter ces champs. Dis-le-moi, on l'étend proprement.

---

## Pourquoi un moteur C# pur (`Inventory`) + un `InventoryHolder` séparé ?

Même logique que la State Machine du package précédent :
1. **Testabilité** : on instancie un `Inventory`, on ajoute/retire, on vérifie
   le résultat — sans lancer Unity.
2. **Réutilisabilité** : le moteur peut servir ailleurs qu'un GameObject (un
   coffre, un marchand, un stockage partagé).

Le `InventoryHolder` ne fait que créer le moteur et le pré-remplir.

---

## Pourquoi `Add` renvoie le débordement au lieu de tout accepter ?

Retourner « ce qui n'a pas tenu » laisse **l'appelant décider** de la règle
métier : refuser le ramassage, laisser l'objet au sol, afficher un message...
Le moteur reste neutre — il ne connaît pas ta logique de jeu.

**Alternative écartée :** un `bool TryAdd` (tout ou rien). Moins souple : on
perd l'info de la quantité partiellement acceptée. On pourra ajouter un `TryAdd`
en surcouche si tu préfères ce style pour certains cas.

---

## Pourquoi un tableau `ItemStack[]` avec des `null`, et pas une `List` ?

Un inventaire de jeu a des **slots à position fixe** (l'objet du slot 5 reste au
slot 5 pour l'UI). Le tableau indexé modélise ça directement, et `null` = case
vide est simple et sans allocation. Une `List` compacterait les trous et ferait
« sauter » les items dans la grille à chaque retrait.

**Limite :** pas de tri/compactage automatique. Si tu veux un bouton « ranger »,
c'est une méthode `Sort()`/`Compact()` à ajouter (facile).

---

## Pourquoi une `ItemDatabase` séparée pour la save ?

Un fichier JSON ne peut pas référencer un asset Unity directement. On sauvegarde
donc des `id` (string), et il faut un moyen de refaire `id → asset` au
chargement. La database centralise ça.

**Piège :** un item **absent** de la database au chargement ne peut pas être
restauré (log d'avertissement, slot ignoré). Pense à y ajouter chaque nouvel
item. Une amélioration possible : peupler la database automatiquement en éditeur
(scan des assets). Dis-moi si tu veux ce confort.

---

## L'ordre Awake / OnEnable dans la démo

`InventoryDemo.OnEnable` s'abonne à `_holder.Inventory`. Ça marche parce que
Unity exécute **tous les `Awake` avant tous les `OnEnable`** : quand la démo
s'abonne, `InventoryHolder.Awake` a déjà créé l'`Inventory`.

**Piège si tu changes ce pattern :** si tu accédais à `Inventory` depuis un
`Awake` d'un autre composant, l'ordre entre `Awake` n'est pas garanti → possible
`null`. Reste sur `OnEnable`/`Start` pour consommer l'inventaire d'un holder.

---

## Ce que le système NE fait PAS (volontairement)

- Pas de **déplacement/drag&drop entre slots** (`MoveSlot(from, to)`) — ajout
  simple à faire.
- Pas de **poids/encombrement** — un `float weight` sur `ItemDefinition` + un
  total dans `Inventory` suffiraient.
- Pas d'**équipement** (slots d'armure/arme dédiés) — c'est un système au-dessus
  qui consomme cet inventaire.
- Pas de **split de pile** (couper 99 en 50+49) — méthode à ajouter.
- Pas d'**UI** fournie — le système expose les events qu'il faut pour la brancher.

L'architecture accueille tout ça sans réécriture.

---

## Intégration avec les autres packages

- **Save System** : `InventoryHolder` peut hériter de `SaveableBehaviour` et
  implémenter `CaptureState`/`RestoreState` avec `Inventory.Capture()/Restore()`.
- **Event Bus** : dans `OnChanged`, publie un `InventoryChangedEvent : IEvent`
  pour notifier globalement (UI, quêtes, succès) sans couplage direct.

---

## Récap des principes appliqués

| Principe                     | Où dans le système                                    |
|------------------------------|-------------------------------------------------------|
| **Template / instance**      | ItemDefinition (SO) vs ItemStack                      |
| **Single Responsibility**    | moteur ≠ holder ≠ database ≠ save data                 |
| **Open/Closed**              | nouveaux items = SO dérivés, moteur inchangé          |
| **Testabilité**              | Inventory sans Unity                                  |
| **Encapsulation**            | quantity muté seulement via Add/Remove bornés         |
| **UI/Save par les bords**    | events par slot + Capture/Restore par id              |
