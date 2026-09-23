using UnityEngine;

namespace Core.InteractionSystem
{
    /// <summary>
    /// Tout ce avec quoi le joueur peut interagir (porte, coffre, PNJ, levier...).
    /// L'Interactor ne connaît que cette interface, pas tes classes concrètes.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Texte contextuel affiché : « Appuyez sur E pour {Prompt} ».</summary>
        string Prompt { get; }

        /// <summary>Vrai si l'interaction est possible en ce moment (verrouillé, épuisé...).</summary>
        bool CanInteract(GameObject interactor);

        /// <summary>Déclenche l'interaction.</summary>
        void Interact(GameObject interactor);
    }
}
