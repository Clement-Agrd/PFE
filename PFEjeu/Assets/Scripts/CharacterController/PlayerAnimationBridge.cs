using UnityEngine;

namespace ProfessionalTPS
{
    /// <summary>
    /// Couche entre le gameplay et l'Animator.
    ///
    /// Le gameplay fonctionne même si aucun Animator
    /// ou aucune animation n'est encore assigné.
    ///
    /// Paramètres Animator prévus :
    ///
    /// Float :
    /// - Speed
    /// - VerticalSpeed
    ///
    /// Bool :
    /// - Grounded
    /// - Aiming
    /// - Sprinting
    ///
    /// Int :
    /// - CombatMode
    ///
    /// Trigger :
    /// - Jump
    /// - Roll
    /// - Melee1
    /// - Melee2
    /// - Melee3
    /// - BowAttack
    /// - MagicAttack
    /// </summary>
    public sealed class PlayerAnimationBridge :
        MonoBehaviour
    {
        [SerializeField]
        private Animator animator;

        [SerializeField]
        private ThirdPersonMotor motor;

        [SerializeField]
        private PlayerInputReader input;


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

        private static readonly int CombatModeHash =
            Animator.StringToHash(
                "CombatMode"
            );


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
            if (animator == null ||
                motor == null)
            {
                return;
            }


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
                AimingHash,
                input != null &&
                input.AimHeld
            );


            animator.SetBool(
                SprintingHash,
                motor.IsSprinting
            );
        }


        public void SetCombatMode(
            CombatMode mode)
        {
            if (animator != null)
            {
                animator.SetInteger(
                    CombatModeHash,
                    (int)mode
                );
            }
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
            if (animator != null)
            {
                animator.SetTrigger(
                    BowAttackHash
                );
            }
        }


        public void PlayMagicAttack()
        {
            if (animator != null)
            {
                animator.SetTrigger(
                    MagicAttackHash
                );
            }
        }


        private void OnJumped()
        {
            if (animator != null)
            {
                animator.SetTrigger(
                    JumpHash
                );
            }
        }


        private void OnRollStarted()
        {
            if (animator != null)
            {
                animator.SetTrigger(
                    RollHash
                );
            }
        }
    }
}