# Interaction System

« Appuyez sur E pour interagir », avec prompt contextuel et détection du plus
proche. Autonome (aucune dépendance à d'autres packages). Namespace :
`Core.InteractionSystem`.

## Installation
Dépose le dossier `InteractionSystem/` dans `Assets/`.

## Mise en place
1. Sur le **joueur** : ajoute `Interactor`. Règle `radius`, `interactableMask`
   (le layer de tes objets interactifs), et la touche (`E` par défaut).
2. Sur un **objet interactif** : ajoute un composant `IInteractable`
   (ex. `SimpleInteractable`) **et un Collider** (non-trigger ou trigger), sur un
   layer inclus dans le mask.
3. Ajoute une UI de prompt (voir plus bas) ou, pour tester, `InteractionPromptLogger`.

## Interactable sans code (SimpleInteractable)
Le plus simple : `SimpleInteractable` expose un `UnityEvent`. Dans l'Inspector,
branche l'action voulue (méthode d'un autre composant) :
```
Prompt : "Ouvrir"
On Interacted () → Door.Open()      (ou LootDropper.Drop, DialogueRunner.StartDialogue...)
Once : ☐ (coché pour un usage unique)
```

## Interactable en code
Hérite de `InteractableBase` :
```csharp
public sealed class Chest : InteractableBase
{
    [SerializeField] private LootTable loot;
    public override void Interact(GameObject interactor)
    {
        foreach (var r in loot.Roll())
            interactor.GetComponent<InventoryHolder>().Inventory.Add(r.Item, r.Amount);
    }
}
```

## L'UI de prompt
L'`Interactor` émet `OnFocusChanged(interactable)` (null = plus rien en vue).
Branche ton panneau dessus :
```csharp
interactor.OnFocusChanged += target =>
{
    if (target != null) { promptPanel.SetActive(true); promptText.text = $"E — {target.Prompt}"; }
    else promptPanel.SetActive(false);
};
```

## API
- `Interactor` : `Current`, `InteractKey`, event `OnFocusChanged`.
- `IInteractable` : `Prompt`, `CanInteract(interactor)`, `Interact(interactor)`.
- `InteractableBase` : base à hériter. `SimpleInteractable` : version UnityEvent.

## Fichiers
- `IInteractable.cs` — le contrat
- `Interactor.cs` — détection + input (MonoBehaviour, sur le joueur)
- `InteractableBase.cs` — base pratique
- `Examples/SimpleInteractable.cs` — interactable UnityEvent (sans code)
- `Examples/InteractionPromptLogger.cs` — logge le prompt (démo)
- `Explications/` — documentation détaillée

## Brancher sur tes autres systèmes (via SimpleInteractable ou en code)
- **Loot / Inventory** : coffre → `LootDropper.Drop()` → ajout à l'inventaire.
- **Dialogue** : PNJ → `DialogueRunner.StartDialogue(startNode)`.
- **Crafting** : établi → ouvrir l'UI de craft.
- **Scene Loader** : porte → `SceneLoader.Instance.LoadScene("Zone_02")`.

## Notes
- **3D** par défaut (`Physics.OverlapSphere`). Pour la **2D**, remplace par
  `Physics2D.OverlapCircleNonAlloc` — dis-moi si tu veux la variante.
- L'interactable peut être sur le collider **ou un parent** (recherche via
  `GetComponentInParent`).

## Tester la démo
Sur le joueur : `Interactor` + `InteractionPromptLogger`. Sur un cube (avec
Collider, sur le bon layer) : `SimpleInteractable` (prompt « Ouvrir », branche un
`Debug.Log` sur l'event). Approche le cube : la Console affiche le prompt ;
appuie sur E : l'event se déclenche.
