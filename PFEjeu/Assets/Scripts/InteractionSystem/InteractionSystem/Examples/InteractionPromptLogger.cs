using UnityEngine;

namespace Core.InteractionSystem.Examples
{
    /// <summary>
    /// Démo : logge le prompt contextuel quand la cible en vue change. En vrai jeu,
    /// remplace ça par une vraie UI (voir README) : afficher un panneau
    /// « Appuyez sur {touche} pour {Prompt} ».
    /// </summary>
    [RequireComponent(typeof(Interactor))]
    public sealed class InteractionPromptLogger : MonoBehaviour
    {
        private Interactor _interactor;

        private void Awake() => _interactor = GetComponent<Interactor>();

        private void OnEnable() => _interactor.OnFocusChanged += HandleFocus;
        private void OnDisable() => _interactor.OnFocusChanged -= HandleFocus;

        private void HandleFocus(IInteractable interactable)
        {
            if (interactable != null)
                Debug.Log($"[Interaction] Appuyez sur {_interactor.InteractKey} pour {interactable.Prompt}");
            else
                Debug.Log("[Interaction] (rien à portée)");
        }
    }
}
