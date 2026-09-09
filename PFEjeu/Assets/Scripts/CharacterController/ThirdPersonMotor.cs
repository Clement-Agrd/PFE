using System;
using UnityEngine;

namespace ProfessionalTPS
{
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public sealed class ThirdPersonMotor : MonoBehaviour
    {
        [Header("References")]

        [SerializeField]
        private PlayerInputReader input;

        [SerializeField]
        private Transform cameraTransform;

        [SerializeField]
        private PlayerCombatController combat;

        [SerializeField]
        private PlayerStamina stamina;


        // ============================================================
        // MOVEMENT
        // ============================================================

        [Header("Movement")]

        [SerializeField, Min(0f)]
        private float moveSpeed = 5.5f;

        [SerializeField, Min(0f)]
        private float acceleration = 28f;

        [SerializeField, Min(0f)]
        private float deceleration = 34f;

        [SerializeField, Min(0f)]
        private float rotationSharpness = 14f;


        // ============================================================
        // SPRINT
        // ============================================================

        [Header("Sprint")]

        [SerializeField, Min(0f)]
        private float sprintSpeed = 8.5f;

        [Tooltip(
            "Stamina consommée par seconde pendant le sprint."
        )]
        [SerializeField, Min(0f)]
        private float sprintStaminaPerSecond = 18f;

        [Tooltip(
            "Quantité minimale d'input nécessaire " +
            "pour considérer que le joueur se déplace."
        )]
        [SerializeField, Range(0f, 1f)]
        private float minimumSprintInput = 0.35f;

        [Tooltip(
            "Autorise le sprint pendant la visée."
        )]
        [SerializeField]
        private bool allowSprintWhileAiming = false;

        [Tooltip(
            "Autorise le sprint pendant une attaque."
        )]
        [SerializeField]
        private bool allowSprintWhileAttacking = false;


        // ============================================================
        // JUMP
        // ============================================================

        [Header("Jump & Gravity")]

        [SerializeField, Min(0f)]
        private float jumpHeight = 1.6f;

        [SerializeField]
        private float gravity = -25f;

        [SerializeField]
        private float groundedVerticalVelocity = -2f;

        [SerializeField, Min(0f)]
        private float coyoteTime = 0.12f;

        [SerializeField, Min(0f)]
        private float jumpBufferTime = 0.12f;

        [SerializeField]
        private float terminalVelocity = -45f;


        // ============================================================
        // ROLL
        // ============================================================

        [Header("Roll")]

        [SerializeField, Min(0.05f)]
        private float rollDuration = 0.55f;

        [SerializeField, Min(0f)]
        private float rollSpeed = 8.5f;

        [SerializeField, Min(0f)]
        private float rollCooldown = 0.25f;

        [SerializeField]
        private AnimationCurve rollSpeedCurve =
            AnimationCurve.EaseInOut(
                0f,
                1f,
                1f,
                0.6f
            );


        [Header("Roll - Stamina")]

        [Tooltip(
            "Si activé, chaque roulade consomme de la stamina."
        )]
        [SerializeField]
        private bool rollUsesStamina = true;

        [SerializeField, Min(0f)]
        private float rollStaminaCost = 20f;


        // ============================================================
        // PRIVATE
        // ============================================================

        private CharacterController _controller;

        private Vector3 _horizontalVelocity;

        private float _verticalVelocity;


        private float _lastGroundedTime =
            float.NegativeInfinity;

        private float _jumpBufferedUntil =
            float.NegativeInfinity;

        private float _lastRollEndTime =
            float.NegativeInfinity;


        private bool _wasGrounded;


        private float _rollTimer;

        private Vector3 _rollDirection;


        // ============================================================
        // PUBLIC
        // ============================================================

        public bool IsGrounded =>
            _controller != null &&
            _controller.isGrounded;


        public bool IsRolling { get; private set; }

        public bool IsSprinting { get; private set; }


        public float HorizontalSpeed =>
            new Vector3(
                _horizontalVelocity.x,
                0f,
                _horizontalVelocity.z
            ).magnitude;


        public float VerticalVelocity =>
            _verticalVelocity;


        /// <summary>
        /// Vitesse maximale de locomotion.
        /// Pratique pour l'Animator.
        /// </summary>
        public float MaxMoveSpeed =>
            Mathf.Max(
                moveSpeed,
                sprintSpeed
            );


        public event Action Jumped;

        public event Action Landed;

        public event Action RollStarted;

        public event Action RollEnded;

        public event Action SprintStarted;

        public event Action SprintEnded;


        // ============================================================
        // UNITY
        // ============================================================

        private void Awake()
        {
            _controller =
                GetComponent<CharacterController>();

            _wasGrounded =
                _controller.isGrounded;
        }


        private void OnEnable()
        {
            if (input == null)
                return;


            input.JumpPressed +=
                BufferJump;

            input.RollPressed +=
                TryStartRoll;
        }


        private void OnDisable()
        {
            if (input == null)
                return;


            input.JumpPressed -=
                BufferJump;

            input.RollPressed -=
                TryStartRoll;
        }


        private void Update()
        {
            bool groundedBeforeMove =
                _controller.isGrounded;


            // ------------------------------
            // Ground
            // ------------------------------

            if (groundedBeforeMove)
            {
                _lastGroundedTime =
                    Time.time;


                if (_verticalVelocity < 0f)
                {
                    _verticalVelocity =
                        groundedVerticalVelocity;
                }
            }


            // ------------------------------
            // Movement
            // ------------------------------

            if (IsRolling)
            {
                StopSprint();

                TickRoll();
            }
            else
            {
                TickLocomotion();
            }


            // ------------------------------
            // Gravity / Jump
            // ------------------------------

            TickGravityAndJump(
                groundedBeforeMove
            );


            // CharacterController n'est pas poussé
            // automatiquement par des forces :
            // on lui transmet explicitement le déplacement.
            _controller.Move(
                (
                    _horizontalVelocity +
                    Vector3.up *
                    _verticalVelocity
                )
                *
                Time.deltaTime
            );


            // ------------------------------
            // Landing
            // ------------------------------

            bool groundedAfterMove =
                _controller.isGrounded;


            if (!_wasGrounded &&
                groundedAfterMove &&
                _verticalVelocity <= 0f)
            {
                Landed?.Invoke();
            }


            _wasGrounded =
                groundedAfterMove;
        }


        // ============================================================
        // LOCOMOTION
        // ============================================================

        private void TickLocomotion()
        {
            Vector2 moveInput =
                input != null
                    ? input.Move
                    : Vector2.zero;


            Vector3 cameraForward =
                cameraTransform != null
                    ? cameraTransform.forward
                    : transform.forward;


            Vector3 cameraRight =
                cameraTransform != null
                    ? cameraTransform.right
                    : transform.right;


            cameraForward.y = 0f;

            cameraRight.y = 0f;


            cameraForward.Normalize();

            cameraRight.Normalize();


            Vector3 desiredDirection =
                cameraForward *
                moveInput.y
                +
                cameraRight *
                moveInput.x;


            if (desiredDirection.sqrMagnitude > 1f)
            {
                desiredDirection.Normalize();
            }


            // ------------------------------
            // Sprint
            // ------------------------------

            UpdateSprint(
                moveInput,
                desiredDirection
            );


            float targetSpeed =
                IsSprinting
                    ? sprintSpeed
                    : moveSpeed;


            float movementMultiplier =
                combat != null
                    ? combat.MovementMultiplier
                    : 1f;


            Vector3 desiredVelocity =
                desiredDirection *
                (
                    targetSpeed *
                    movementMultiplier
                );


            // ------------------------------
            // Acceleration
            // ------------------------------

            float rate =
                desiredVelocity.sqrMagnitude >
                0.001f
                    ? acceleration
                    : deceleration;


            _horizontalVelocity =
                Vector3.MoveTowards(
                    _horizontalVelocity,
                    desiredVelocity,
                    rate *
                    Time.deltaTime
                );


            // ------------------------------
            // Rotation
            // ------------------------------

            Vector3 facingDirection =
                desiredDirection;


            if (combat != null &&
                combat.ShouldFaceCamera)
            {
                facingDirection =
                    cameraForward;
            }


            if (facingDirection.sqrMagnitude >
                0.001f)
            {
                Quaternion targetRotation =
                    Quaternion.LookRotation(
                        facingDirection,
                        Vector3.up
                    );


                float rotationT =
                    1f -
                    Mathf.Exp(
                        -rotationSharpness *
                        Time.deltaTime
                    );


                transform.rotation =
                    Quaternion.Slerp(
                        transform.rotation,
                        targetRotation,
                        rotationT
                    );
            }
        }


        // ============================================================
        // SPRINT
        // ============================================================

        private void UpdateSprint(
            Vector2 moveInput,
            Vector3 desiredDirection)
        {
            bool sprintButtonHeld =
                input != null &&
                input.SprintHeld;


            bool enoughMovement =
                desiredDirection.sqrMagnitude >=
                minimumSprintInput *
                minimumSprintInput;


            bool grounded =
                _controller.isGrounded;


            bool aiming =
                input != null &&
                input.AimHeld;


            bool attacking =
                combat != null &&
                combat.IsBusy;


            bool stateAllowsSprint =
                grounded &&
                enoughMovement &&
                (
                    allowSprintWhileAiming ||
                    !aiming
                )
                &&
                (
                    allowSprintWhileAttacking ||
                    !attacking
                );


            bool wantsSprint =
                sprintButtonHeld &&
                stateAllowsSprint;


            // Le joueur ne demande plus le sprint
            // ou son état ne l'autorise plus.
            if (!wantsSprint)
            {
                StopSprint();

                return;
            }


            // ------------------------------
            // Début du sprint
            // ------------------------------

            if (!IsSprinting)
            {
                bool canStart =
                    stamina == null ||
                    stamina.CanStartSprint;


                if (!canStart)
                    return;


                IsSprinting = true;

                SprintStarted?.Invoke();
            }


            // ------------------------------
            // Vérification stamina
            // ------------------------------

            if (stamina != null)
            {
                if (!stamina.CanContinueSprint)
                {
                    StopSprint();

                    return;
                }


                stamina.SpendContinuous(
                    sprintStaminaPerSecond *
                    Time.deltaTime
                );


                if (!stamina.CanContinueSprint)
                {
                    StopSprint();
                }
            }
        }


        private void StopSprint()
        {
            if (!IsSprinting)
                return;


            IsSprinting = false;

            SprintEnded?.Invoke();
        }


        // ============================================================
        // JUMP
        // ============================================================

        private void TickGravityAndJump(
            bool grounded)
        {
            if (!grounded)
            {
                _verticalVelocity =
                    Mathf.Max(
                        terminalVelocity,
                        _verticalVelocity +
                        gravity *
                        Time.deltaTime
                    );
            }


            bool buffered =
                Time.time <=
                _jumpBufferedUntil;


            bool withinCoyote =
                Time.time -
                _lastGroundedTime <=
                coyoteTime;


            if (!IsRolling &&
                buffered &&
                withinCoyote &&
                (
                    combat == null ||
                    combat.CanJump
                ))
            {
                _jumpBufferedUntil =
                    float.NegativeInfinity;


                _verticalVelocity =
                    Mathf.Sqrt(
                        jumpHeight *
                        -2f *
                        gravity
                    );


                StopSprint();


                Jumped?.Invoke();
            }
        }


        private void BufferJump()
        {
            _jumpBufferedUntil =
                Time.time +
                jumpBufferTime;
        }


        // ============================================================
        // ROLL
        // ============================================================

        private void TryStartRoll()
        {
            if (IsRolling)
                return;


            if (!_controller.isGrounded)
                return;


            if (Time.time <
                _lastRollEndTime +
                rollCooldown)
            {
                return;
            }


            if (combat != null &&
                !combat.CanRoll)
            {
                return;
            }


            // ------------------------------
            // Stamina
            // ------------------------------

            if (rollUsesStamina &&
                stamina != null)
            {
                if (!stamina.TrySpend(
                    rollStaminaCost))
                {
                    // Pas assez d'endurance.
                    return;
                }
            }


            StopSprint();


            Vector2 moveInput =
                input != null
                    ? input.Move
                    : Vector2.zero;


            Vector3 cameraForward =
                cameraTransform != null
                    ? cameraTransform.forward
                    : transform.forward;


            Vector3 cameraRight =
                cameraTransform != null
                    ? cameraTransform.right
                    : transform.right;


            cameraForward.y = 0f;

            cameraRight.y = 0f;


            cameraForward.Normalize();

            cameraRight.Normalize();


            Vector3 desired =
                cameraForward *
                moveInput.y
                +
                cameraRight *
                moveInput.x;


            // Si aucun input :
            // roulade vers l'avant.
            _rollDirection =
                desired.sqrMagnitude > 0.01f
                    ? desired.normalized
                    : transform.forward;


            _rollTimer = 0f;

            IsRolling = true;


            RollStarted?.Invoke();


            if (combat != null)
            {
                combat.NotifyRollStarted();
            }
        }


        private void TickRoll()
        {
            _rollTimer +=
                Time.deltaTime;


            float normalized =
                Mathf.Clamp01(
                    _rollTimer /
                    rollDuration
                );


            float curve =
                rollSpeedCurve != null
                    ? rollSpeedCurve.Evaluate(
                        normalized
                    )
                    : 1f;


            _horizontalVelocity =
                _rollDirection *
                (
                    rollSpeed *
                    curve
                );


            if (_rollTimer >=
                rollDuration)
            {
                IsRolling = false;


                _lastRollEndTime =
                    Time.time;


                RollEnded?.Invoke();
            }
        }
    }
}