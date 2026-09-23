# 00 — Vue d'ensemble

Comment le système est assemblé et pourquoi. Les fichiers `01`, `02`, `03`
détaillent chaque script, le flux d'une interaction et les choix de design.

---

## Le problème qu'on résout

« Quand le joueur est près d'un objet, afficher "Appuyez sur E pour ...", et
déclencher l'action à l'appui. » Ça touche plein d'objets différents (portes,
coffres, PNJ, leviers, items au sol) qui font des choses très différentes.

On veut un système **générique** : le joueur détecte ce qui est autour, affiche
le bon prompt, et déclenche l'action — sans connaître la nature de chaque objet.

---

## Schéma global

```
        ┌──────────────────────────────┐
        │          Interactor          │  (sur le joueur)
        │  détecte (OverlapSphere)     │
        │  choisit le plus proche      │
        │  OnFocusChanged → l'UI       │
        │  E → Interact()              │
        └───────────────┬──────────────┘
                        │ dialogue avec
                        ▼
        ┌──────────────────────────────┐
        │         IInteractable        │  (contrat)
        │  Prompt / CanInteract / Interact
        └───────────────┬──────────────┘
                        │ implémenté par
                        ▼
        InteractableBase → SimpleInteractable (UnityEvent), Chest, Door, NPC...
```

---

## Les 3 idées de design (le "pourquoi")

### 1. Interface `IInteractable`
Le joueur ne connaît pas « une porte » ou « un coffre ». Il connaît « quelque
chose d'interactif ». Chaque objet implémente `Prompt` (quoi afficher),
`CanInteract` (est-ce possible ?), `Interact` (fais-le). Ajouter un type d'objet
interactif ne touche jamais à l'Interactor.

### 2. Détection sans configuration
`Physics.OverlapSphereNonAlloc` cherche autour du joueur chaque frame, sans
alloc et sans avoir à poser des triggers spéciaux. On choisit le **plus proche**
qui peut interagir → le prompt suit naturellement l'objet visé.

### 3. Câblage sans code (UnityEvent)
`SimpleInteractable` expose un `UnityEvent` : un designer branche l'action dans
l'Inspector (« ouvrir la porte », « lancer le dialogue »). C'est ce qui relie ce
système à tous les autres sans écrire une ligne.

---

## En une phrase

> Le joueur détecte les interactables à portée, met en avant le plus proche
> (l'UI affiche son prompt), et appuyer sur E déclenche son action.

Lis ensuite `01_role_de_chaque_script.md`.
