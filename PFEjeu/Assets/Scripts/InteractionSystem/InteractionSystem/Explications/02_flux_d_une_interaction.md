# 02 — Le flux d'une interaction, étape par étape

---

## Chaque frame (dans Interactor.Update)

```
FindBest()
      │
      ├─ OverlapSphereNonAlloc(joueur, radius, buffer, mask)  → colliders à portée
      │
      ├─ pour chacun :
      │      interactable = GetComponentInParent<IInteractable>()
      │      si null ou !CanInteract(joueur) → ignorer
      │      calculer distance² ; garder le plus proche
      │
      └─ renvoie le meilleur (ou null)

si la cible a changé (best != current) :
      current = best
      OnFocusChanged(current)   → l'UI montre le prompt (ou le cache si null)

si current != null ET touche E pressée :
      current.Interact(joueur)  → l'action se déclenche
```

---

## Le cycle « focus »

```
Le joueur s'approche d'un coffre
      │
      ▼
FindBest trouve le coffre → OnFocusChanged(coffre)
      │
      ▼
L'UI affiche « Appuyez sur E pour Ouvrir »

Le joueur s'éloigne
      │
      ▼
FindBest ne trouve plus rien → OnFocusChanged(null)
      │
      ▼
L'UI masque le prompt
```

Le prompt suit toujours l'objet le plus proche « faisable ». S'il y a deux
objets, le plus proche gagne ; en t'approchant de l'autre, le focus bascule.

---

## L'interaction

```
E pressée avec le coffre en focus
      │
      ▼
coffre.Interact(joueur)
      │
      ├─ SimpleInteractable → onInteracted.Invoke()  (UnityEvent câblé dans l'Inspector)
      │        └─ ex. LootDropper.Drop() → items dans l'inventaire
      │
      └─ ou ta classe Chest → sa logique Interact() en code
```

---

## CanInteract : le filtre

`CanInteract` permet de rendre un objet temporairement non-interactif :
```
Porte verrouillée      → CanInteract = false tant que pas de clé
Coffre déjà ouvert     → SimpleInteractable "once" → false après usage
PNJ déjà parlé         → false selon un flag
```

Un objet dont `CanInteract` renvoie false est **ignoré** par FindBest → pas de
prompt, pas d'interaction. Dès qu'il redevient faisable, il réapparaît.

---

## Exemple concret : coffre → butin → inventaire

```
SimpleInteractable sur le coffre :
   Prompt = "Ouvrir"
   Once = ✔
   OnInteracted → LootDropper.Drop()   (branché dans l'Inspector)

LootDropper.OnLootDropped → ajoute à l'inventaire du joueur

Résultat : E devant le coffre → butin roulé → dans l'inventaire → coffre inactif.
```

Zéro ligne de code écrite — juste du câblage entre systèmes.

Lis ensuite `03_choix_de_conception.md`.
