using UnityEngine;

namespace Core.InteractionSystem
{
    /// <summary>
    /// Base pratique pour un interactable : gère le prompt et un CanInteract par
    /// défaut. Hérite-la et implémente Interact (ou utilise SimpleInteractable).
    /// </summary>
    public abstract class InteractableBase : MonoBehaviour, IInteractable
    {
        [SerializeField] private string prompt = "Interagir";

        public virtual string Prompt => prompt;

        public virtual bool CanInteract(GameObject interactor) => isActiveAndEnabled;

        public abstract void Interact(GameObject interactor);
    }
}
