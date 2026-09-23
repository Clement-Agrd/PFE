using UnityEngine;
using UnityEngine.Events;

namespace Core.InteractionSystem.Examples
{
    /// <summary>
    /// Interactable câblable dans l'Inspector via un UnityEvent : « ouvrir la porte »,
    /// « lancer le dialogue », « ramasser »... se branche SANS code sur tes autres
    /// systèmes (Loot, Dialogue, Inventory...).
    /// </summary>
    public sealed class SimpleInteractable : InteractableBase
    {
        [Header("Action")]
        [SerializeField] private UnityEvent onInteracted;

        [Tooltip("Si vrai, utilisable une seule fois (coffre, item au sol...).")]
        [SerializeField] private bool once;

        private bool _used;

        public override bool CanInteract(GameObject interactor)
            => base.CanInteract(interactor) && !(once && _used);

        public override void Interact(GameObject interactor)
        {
            _used = true;
            onInteracted?.Invoke();
        }
    }
}
