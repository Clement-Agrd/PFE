using System;
using UnityEngine;

namespace ProfessionalTPS
{
    [DisallowMultipleComponent]
    public sealed class PlayerCombatController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private ThirdPersonMotor motor;
        [SerializeField] private Camera aimCamera;
        [SerializeField] private PlayerAnimationBridge animationBridge;
        [SerializeField] private PlayerAudioBridge audioBridge;

        [Header("Combat Modules")]
        [SerializeField] private MeleeCombatModule melee;
        [SerializeField] private BowCombatModule bow;
        [SerializeField] private MagicCombatModule magic;

        [Header("Aiming")]
        [SerializeField] private LayerMask aimMask = ~0;
        [SerializeField, Min(1f)] private float defaultAimRange = 200f;

        private CombatModule _active;

        public CombatMode CurrentMode =>
            _active != null
                ? _active.Mode
                : CombatMode.Melee;

        public bool IsBusy =>
            _active != null &&
            _active.IsBusy;

        public float MovementMultiplier =>
            _active != null
                ? _active.MovementMultiplier
                : 1f;

        public bool ShouldFaceCamera =>
            (input != null && input.AimHeld) ||
            (_active != null &&
             _active.IsBusy &&
             _active.FaceCameraWhileBusy);

        public bool CanRoll =>
            _active == null ||
            _active.CanRoll;

        public bool CanJump =>
            _active == null ||
            _active.CanJump;

        public PlayerAnimationBridge Animation =>
            animationBridge;

        public PlayerAudioBridge Audio =>
            audioBridge;

        public Camera AimCamera =>
            aimCamera;

        public LayerMask AimMask =>
            aimMask;

        public bool IsBowDrawing =>
            bow != null &&
            bow.IsDrawing;

        public float BowCharge01 =>
            bow != null
                ? bow.Charge01
                : 0f;

        public event Action<CombatMode> ModeChanged;

        private void Awake()
        {
            melee?.Initialize(this);
            bow?.Initialize(this);
            magic?.Initialize(this);

            _active =
                melee != null ? melee :
                bow != null ? bow :
                magic;

            if (_active != null)
                animationBridge?.SetCombatMode(_active.Mode);
        }

        private void OnEnable()
        {
            if (input == null)
                return;

            input.AttackPressed += OnAttackPressed;
            input.AttackReleased += OnAttackReleased;

            input.SelectMeleePressed += SelectMelee;
            input.SelectBowPressed += SelectBow;
            input.SelectMagicPressed += SelectMagic;
        }

        private void OnDisable()
        {
            if (input == null)
                return;

            input.AttackPressed -= OnAttackPressed;
            input.AttackReleased -= OnAttackReleased;

            input.SelectMeleePressed -= SelectMelee;
            input.SelectBowPressed -= SelectBow;
            input.SelectMagicPressed -= SelectMagic;
        }

        private void Update()
        {
            _active?.Tick(Time.deltaTime);
        }

        private void OnAttackPressed()
        {
            if (motor != null &&
                motor.IsRolling)
            {
                return;
            }

            _active?.AttackPressed();
        }

        private void OnAttackReleased()
        {
            _active?.AttackReleased();
        }

        public void SelectMelee()
            => TrySwitch(melee);

        public void SelectBow()
            => TrySwitch(bow);

        public void SelectMagic()
            => TrySwitch(magic);

        private void TrySwitch(
            CombatModule target)
        {
            if (target == null ||
                target == _active)
            {
                return;
            }

            if (_active != null &&
                _active.IsBusy)
            {
                return;
            }

            _active?.Cancel();

            _active = target;

            animationBridge?.SetCombatMode(
                _active.Mode
            );

            ModeChanged?.Invoke(
                _active.Mode
            );
        }

        public Vector3 GetAimDirection(
            Vector3 origin,
            float range = -1f)
        {
            float finalRange =
                range > 0f
                    ? range
                    : defaultAimRange;

            if (aimCamera == null)
                return transform.forward;

            Ray centerRay =
                aimCamera.ViewportPointToRay(
                    new Vector3(
                        0.5f,
                        0.5f,
                        0f
                    )
                );

            Vector3 targetPoint =
                centerRay.origin +
                centerRay.direction *
                finalRange;

            if (Physics.Raycast(
                    centerRay,
                    out RaycastHit hit,
                    finalRange,
                    aimMask,
                    QueryTriggerInteraction.Ignore))
            {
                targetPoint = hit.point;
            }

            Vector3 direction =
                targetPoint - origin;

            return direction.sqrMagnitude > 0.001f
                ? direction.normalized
                : transform.forward;
        }

        public void NotifyRollStarted()
        {
        }

        public void AnimationEvent_Impact()
        {
            _active?.AnimationImpact();
        }

        public void AnimationEvent_ActionFinished()
        {
            _active?.AnimationFinished();
        }
    }
}