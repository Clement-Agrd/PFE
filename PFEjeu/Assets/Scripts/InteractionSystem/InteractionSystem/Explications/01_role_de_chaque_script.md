# 01 — Rôle de chaque script

---

## `IInteractable.cs` — le contrat
```csharp
string Prompt { get; }                      // "Ouvrir", "Parler"...
bool CanInteract(GameObject interactor);    // possible maintenant ?
void Interact(GameObject interactor);       // fais-le
```
Trois membres suffisent à décrire n'importe quel objet interactif. L'Interactor
ne voit que ça.

---

## `Interactor.cs` — le détecteur (sur le joueur)
**Rôle :** trouver, mettre en avant, déclencher.

**Réglages :** `radius`, `interactableMask` (layer), `interactKey`.

**Chaque frame :**
```csharp
best = FindBest();                 // le plus proche interactable "faisable"
if (best != _current)              // la cible en vue a changé ?
{
    _current = best;
    OnFocusChanged?.Invoke(_current);  // l'UI affiche/masque le prompt
}
if (_current != null && Input.GetKeyDown(interactKey))
    _current.Interact(gameObject);     // E → action
```

**FindBest :**
```csharp
count = Physics.OverlapSphereNonAlloc(pos, radius, buffer, mask)
pour chaque collider :
    interactable = collider.GetComponentInParent<IInteractable>()
    si null ou !CanInteract → ignorer
    garder le plus proche (distance²)
```

`OverlapSphereNonAlloc` remplit un buffer pré-alloué → pas de garbage. Le
`GetComponentInParent` permet à l'interactable d'être sur le collider **ou** un
parent.

**Gizmo :** un cercle cyan montre la portée quand l'objet est sélectionné.

---

## `InteractableBase.cs` — la base pratique
```csharp
[SerializeField] private string prompt = "Interagir";
public virtual string Prompt => prompt;
public virtual bool CanInteract(interactor) => isActiveAndEnabled;
public abstract void Interact(interactor);   // à toi de l'implémenter
```
Gère le prompt et un CanInteract raisonnable. Tu n'écris plus que `Interact`.

---

## `Examples/SimpleInteractable.cs` — sans code
```csharp
[SerializeField] private UnityEvent onInteracted;
[SerializeField] private bool once;

public override void Interact(interactor) { _used = true; onInteracted?.Invoke(); }
```
Le pont vers tout le reste : dans l'Inspector, branche `onInteracted` sur la
méthode voulue (ouvrir, dialoguer, ramasser). `once` = usage unique.

---

## `Examples/InteractionPromptLogger.cs` — démo
Logge le prompt à chaque changement de cible. À remplacer par une vraie UI
(panneau + texte) branchée sur `OnFocusChanged`.

Lis ensuite `02_flux_d_une_interaction.md`.
