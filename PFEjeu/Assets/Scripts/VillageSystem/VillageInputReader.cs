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

        public event Action ExitPressed;
        public event Action ClickPressed;

        private void OnEnable()
        {
            BindPressed(exitVillageView, OnExit);
            BindPressed(click, OnClick);
        }

        private void OnDisable()
        {
            UnbindPressed(exitVillageView, OnExit);
            UnbindPressed(click, OnClick);
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
    }
}