using UnityEngine;

namespace ProfessionalTPS
{
    /// <summary>
    /// Optional audio layer. Leave every AudioClip empty until your audio assets arrive.
    /// </summary>
    public sealed class PlayerAudioBridge : MonoBehaviour
    {
        [SerializeField] private AudioSource oneShotSource;
        [SerializeField] private ThirdPersonMotor motor;

        [Header("Movement")]
        [SerializeField] private AudioClip jump;
        [SerializeField] private AudioClip land;
        [SerializeField] private AudioClip roll;
        [SerializeField] private AudioClip[] footsteps;

        [Header("Automatic Footsteps")]
        [SerializeField]
        private bool automaticFootsteps = true;

        [SerializeField, Min(0f)]
        private float minimumFootstepSpeed = 0.35f;

        [SerializeField, Min(0.05f)]
        private float walkStepInterval = 0.52f;

        [SerializeField, Min(0.05f)]
        private float runStepInterval = 0.36f;

        [SerializeField, Min(0.05f)]
        private float sprintStepInterval = 0.28f;

        [SerializeField, Min(0f)]
        private float firstStepDelay = 0.08f;

        [Header("Combat")]
        [SerializeField] private AudioClip[] meleeSwings = new AudioClip[3];
        [SerializeField] private AudioClip bowRelease;
        [SerializeField] private AudioClip magicCast;

        [Header("Mix")]
        [SerializeField, Range(0f, 1f)] private float movementVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float combatVolume = 1f;
        
        [Header("Gathering")]

        [SerializeField]
        private AudioClip axeSwing;

        [SerializeField]
        private AudioClip pickaxeSwing;
        
        private int _footstepIndex;
        private float _footstepTimer;
        private bool _wasMovingOnGround;

        private void Awake()
        {
            if (oneShotSource == null)
                oneShotSource = GetComponent<AudioSource>();
        }

        private void OnEnable()
        {
            if (motor == null)
                return;

            motor.Jumped += OnJump;
            motor.Landed += OnLand;
            motor.RollStarted += OnRoll;
        }

        private void OnDisable()
        {
            if (motor == null)
                return;

            motor.Jumped -= OnJump;
            motor.Landed -= OnLand;
            motor.RollStarted -= OnRoll;
        }

        private void Update()
        {
            TickAutomaticFootsteps();
        }

        public void PlayMeleeSwing(int comboIndex)
        {
            if (meleeSwings == null || meleeSwings.Length == 0)
                return;

            int index = Mathf.Clamp(comboIndex, 0, meleeSwings.Length - 1);
            Play(meleeSwings[index], combatVolume);
        }

        public void PlayBowRelease() => Play(bowRelease, combatVolume);
        public void PlayMagicCast() => Play(magicCast, combatVolume);

        /// <summary>
        /// Peut être appelé par un Animation Event.
        /// Le timer automatique est aussi recalé pour éviter un double pas.
        /// </summary>
        public void PlayFootstep()
        {
            if (footsteps == null || footsteps.Length == 0)
                return;

            AudioClip clip =
                footsteps[
                    _footstepIndex %
                    footsteps.Length
                ];

            _footstepIndex++;

            Play(
                clip,
                movementVolume
            );

            _footstepTimer =
                GetCurrentFootstepInterval();
        }

        private void TickAutomaticFootsteps()
        {
            if (!automaticFootsteps ||
                motor == null)
            {
                return;
            }

            bool canPlayFootstep =
                motor.IsGrounded &&
                !motor.IsRolling &&
                motor.HorizontalSpeed >=
                    minimumFootstepSpeed &&
                footsteps != null &&
                footsteps.Length > 0;

            if (!canPlayFootstep)
            {
                _wasMovingOnGround =
                    false;

                _footstepTimer =
                    0f;

                return;
            }

            if (!_wasMovingOnGround)
            {
                _wasMovingOnGround =
                    true;

                _footstepTimer =
                    firstStepDelay;
            }

            _footstepTimer -=
                Time.deltaTime;

            if (_footstepTimer > 0f)
                return;

            PlayFootstep();
        }

        private float GetCurrentFootstepInterval()
        {
            if (motor == null)
                return runStepInterval;

            if (motor.IsSprinting)
                return sprintStepInterval;

            float normalizedSpeed =
                motor.MaxMoveSpeed > 0.001f
                    ? Mathf.Clamp01(
                        motor.HorizontalSpeed /
                        motor.MaxMoveSpeed
                    )
                    : 0f;

            float runBlend =
                Mathf.InverseLerp(
                    0.2f,
                    0.65f,
                    normalizedSpeed
                );

            return Mathf.Lerp(
                walkStepInterval,
                runStepInterval,
                runBlend
            );
        }

        private void OnJump() => Play(jump, movementVolume);
        private void OnLand() => Play(land, movementVolume);
        private void OnRoll() => Play(roll, movementVolume);

        private void Play(AudioClip clip, float volume)
        {
            if (oneShotSource != null && clip != null)
                oneShotSource.PlayOneShot(clip, volume);
        }
        
        public void PlayGatheringSwing(
            bool isAxe)
        {
            AudioClip clip =
                isAxe
                    ? axeSwing
                    : pickaxeSwing;

            Play(
                clip,
                combatVolume
            );
        }
    }
}
