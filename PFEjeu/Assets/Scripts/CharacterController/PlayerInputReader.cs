using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProfessionalTPS
{
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [Header("Input Actions")]
        [SerializeField] private InputActionReference move;
        [SerializeField] private InputActionReference look;
        [SerializeField] private InputActionReference jump;
        [SerializeField] private InputActionReference roll;
        [SerializeField] private InputActionReference sprint;
        [SerializeField] private InputActionReference attack;
        [SerializeField] private InputActionReference aim;
        [SerializeField] private InputActionReference selectMelee;
        [SerializeField] private InputActionReference selectBow;
        [SerializeField] private InputActionReference selectMagic;

        public Vector2 Move =>
            move != null
                ? move.action.ReadValue<Vector2>()
                : Vector2.zero;

        public Vector2 Look =>
            look != null
                ? look.action.ReadValue<Vector2>()
                : Vector2.zero;

        public bool AimHeld =>
            aim != null &&
            aim.action.IsPressed();

        public bool SprintHeld =>
            sprint != null &&
            sprint.action.IsPressed();

        public bool LookIsPointerDelta =>
            look != null &&
            look.action.activeControl != null &&
            look.action.activeControl.device is Pointer;

        public event Action JumpPressed;
        public event Action RollPressed;

        public event Action AttackPressed;
        public event Action AttackReleased;

        public event Action SelectMeleePressed;
        public event Action SelectBowPressed;
        public event Action SelectMagicPressed;

        private void OnEnable()
        {
            Enable(move);
            Enable(look);

            BindPressed(jump, OnJump);
            BindPressed(roll, OnRoll);

            Enable(sprint);

            BindAttack();

            Enable(aim);

            BindPressed(selectMelee, OnSelectMelee);
            BindPressed(selectBow, OnSelectBow);
            BindPressed(selectMagic, OnSelectMagic);
        }

        private void OnDisable()
        {
            Disable(move);
            Disable(look);

            UnbindPressed(jump, OnJump);
            UnbindPressed(roll, OnRoll);

            Disable(sprint);

            UnbindAttack();

            Disable(aim);

            UnbindPressed(selectMelee, OnSelectMelee);
            UnbindPressed(selectBow, OnSelectBow);
            UnbindPressed(selectMagic, OnSelectMagic);
        }

        private void BindAttack()
        {
            if (attack == null)
                return;

            attack.action.performed += OnAttackPressed;
            attack.action.canceled += OnAttackReleased;

            attack.action.Enable();
        }

        private void UnbindAttack()
        {
            if (attack == null)
                return;

            attack.action.performed -= OnAttackPressed;
            attack.action.canceled -= OnAttackReleased;

            attack.action.Disable();
        }

        private static void Enable(InputActionReference reference)
        {
            if (reference != null)
                reference.action.Enable();
        }

        private static void Disable(InputActionReference reference)
        {
            if (reference != null)
                reference.action.Disable();
        }

        private static void BindPressed(
            InputActionReference reference,
            Action<InputAction.CallbackContext> callback)
        {
            if (reference == null)
                return;

            reference.action.performed += callback;
            reference.action.Enable();
        }

        private static void UnbindPressed(
            InputActionReference reference,
            Action<InputAction.CallbackContext> callback)
        {
            if (reference == null)
                return;

            reference.action.performed -= callback;
            reference.action.Disable();
        }

        private void OnJump(InputAction.CallbackContext _)
            => JumpPressed?.Invoke();

        private void OnRoll(InputAction.CallbackContext _)
            => RollPressed?.Invoke();

        private void OnAttackPressed(InputAction.CallbackContext _)
            => AttackPressed?.Invoke();

        private void OnAttackReleased(InputAction.CallbackContext _)
            => AttackReleased?.Invoke();

        private void OnSelectMelee(InputAction.CallbackContext _)
            => SelectMeleePressed?.Invoke();

        private void OnSelectBow(InputAction.CallbackContext _)
            => SelectBowPressed?.Invoke();

        private void OnSelectMagic(InputAction.CallbackContext _)
            => SelectMagicPressed?.Invoke();
    }
}