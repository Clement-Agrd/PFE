using UnityEngine;

namespace ProfessionalTPS
{
    /// <summary>
    /// Pont entre le gameplay et les animations.
    ///
    /// Le jeu continue de fonctionner
    /// si aucun Animator n'est assigné.
    /// </summary>
    public sealed class PlayerAnimationBridge :
        MonoBehaviour
    {
        [Header("References")]

        [SerializeField]
        private Animator animator;

        [SerializeField]
        private ThirdPersonMotor motor;

        [SerializeField]
        private PlayerInputReader input;

        [SerializeField]
        private PlayerCombatController combat;

        // ============================================================
        // LOCOMOTION
        // ============================================================

        private static readonly int SpeedHash =
            Animator.StringToHash(
                "Speed"
            );

        private static readonly int VerticalSpeedHash =
            Animator.StringToHash(
                "VerticalSpeed"
            );

        private static readonly int GroundedHash =
            Animator.StringToHash(
                "Grounded"
            );

        private static readonly int AimingHash =
            Animator.StringToHash(
                "Aiming"
            );

        private static readonly int SprintingHash =
            Animator.StringToHash(
                "Sprinting"
            );

        // ============================================================
        // COMBAT
        // ============================================================

        private static readonly int CombatModeHash =
            Animator.StringToHash(
                "CombatMode"
            );

        private static readonly int AttackSpeedHash =
            Animator.StringToHash(
                "AttackSpeed"
            );

        private static readonly int BowDrawingHash =
            Animator.StringToHash(
                "BowDrawing"
            );

        private static readonly int BowChargeHash =
            Animator.StringToHash(
                "BowCharge"
            );

        // ============================================================
        // TRIGGERS
        // ============================================================

        private static readonly int JumpHash =
            Animator.StringToHash(
                "Jump"
            );

        private static readonly int RollHash =
            Animator.StringToHash(
                "Roll"
            );

        private static readonly int Melee1Hash =
            Animator.StringToHash(
                "Melee1"
            );

        private static readonly int Melee2Hash =
            Animator.StringToHash(
                "Melee2"
            );

        private static readonly int Melee3Hash =
            Animator.StringToHash(
                "Melee3"
            );

        private static readonly int BowAttackHash =
            Animator.StringToHash(
                "BowAttack"
            );

        private static readonly int MagicAttackHash =
            Animator.StringToHash(
                "MagicAttack"
            );

        private void OnEnable()
        {
            if (motor == null)
                return;

            motor.Jumped +=
                OnJumped;

            motor.RollStarted +=
                OnRollStarted;
        }

        private void OnDisable()
        {
            if (motor == null)
                return;

            motor.Jumped -=
                OnJumped;

            motor.RollStarted -=
                OnRollStarted;
        }

        private void Update()
        {
            if (animator == null)
                return;

            if (motor != null)
            {
                float normalizedSpeed =
                    motor.MaxMoveSpeed > 0.001f
                        ? motor.HorizontalSpeed /
                          motor.MaxMoveSpeed
                        : 0f;

                animator.SetFloat(
                    SpeedHash,
                    normalizedSpeed,
                    0.1f,
                    Time.deltaTime
                );

                animator.SetFloat(
                    VerticalSpeedHash,
                    motor.VerticalVelocity
                );

                animator.SetBool(
                    GroundedHash,
                    motor.IsGrounded
                );

                animator.SetBool(
                    SprintingHash,
                    motor.IsSprinting
                );
            }

            animator.SetBool(
                AimingHash,
                input != null &&
                input.AimHeld
            );

            animator.SetFloat(
                AttackSpeedHash,
                combat != null
                    ? combat.AttackSpeed
                    : 1f
            );

            animator.SetBool(
                BowDrawingHash,
                combat != null &&
                combat.IsBowDrawing
            );

            animator.SetFloat(
                BowChargeHash,
                combat != null
                    ? combat.BowCharge01
                    : 0f
            );
        }

        public void SetCombatMode(
            CombatMode mode)
        {
            if (animator == null)
                return;

            animator.SetInteger(
                CombatModeHash,
                (int)mode
            );
        }

        public void PlayMeleeAttack(
            int comboIndex)
        {
            if (animator == null)
                return;

            switch (comboIndex)
            {
                case 0:

                    animator.SetTrigger(
                        Melee1Hash
                    );

                    break;

                case 1:

                    animator.SetTrigger(
                        Melee2Hash
                    );

                    break;

                default:

                    animator.SetTrigger(
                        Melee3Hash
                    );

                    break;
            }
        }

        public void PlayBowAttack()
        {
            if (animator == null)
                return;

            animator.SetTrigger(
                BowAttackHash
            );
        }

        public void PlayMagicAttack()
        {
            if (animator == null)
                return;

            animator.SetTrigger(
                MagicAttackHash
            );
        }

        private void OnJumped()
        {
            if (animator == null)
                return;

            animator.SetTrigger(
                JumpHash
            );
        }

        private void OnRollStarted()
        {
            if (animator == null)
                return;

            animator.SetTrigger(
                RollHash
            );
        }
    }
}