using System;
using UnityEngine.InputSystem;
using UnityEngine;


namespace Core.Village
{
    /// <summary>
    /// Lit les inputs propres à la vue village (sortir de la vue, cliquer un
    /// bâtiment). Suit exactement le même schéma que PlayerInputReader.
    /// </summary>
    public sealed class VillageInputReader : UnityEngine.MonoBehaviour
    {
        [SerializeField] private InputActionReference exitVillageView;
        [SerializeField] private InputActionReference click;
        [Tooltip("Touche d'interaction (E) partagée par les points d'intérêt du village (table HDV, poste d'expédition...).")]
        [SerializeField] private InputActionReference interact;

        public event Action ExitPressed;
        public event Action ClickPressed;
        public event Action InteractPressed;

        private void OnEnable()
        {
            BindPressed(exitVillageView, OnExit);
            BindPressed(click, OnClick);
            BindPressed(interact, OnInteract);
        }

        private void OnDisable()
        {
            UnbindPressed(exitVillageView, OnExit);
            UnbindPressed(click, OnClick);
            UnbindPressed(interact, OnInteract);
        }

        private static void BindPressed(InputActionReference reference, Action<InputAction.CallbackContext> callback)
        {
            if (reference == null) return;
            reference.action.performed += callback;
            reference.action.Enable();
        }

        private static void UnbindPressed(InputActionReference reference, Action<InputAction.CallbackContext> callback)
        {
            if (reference == null) return;
            reference.action.performed -= callback;
            reference.action.Disable();
        }

        private void OnExit(InputAction.CallbackContext _) => ExitPressed?.Invoke();
        private void OnClick(InputAction.CallbackContext _) => ClickPressed?.Invoke();
        private void OnInteract(InputAction.CallbackContext _) => InteractPressed?.Invoke();
    }
}