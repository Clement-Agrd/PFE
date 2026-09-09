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

        [Header("Combat")]
        [SerializeField] private AudioClip[] meleeSwings = new AudioClip[3];
        [SerializeField] private AudioClip bowRelease;
        [SerializeField] private AudioClip magicCast;

        [Header("Mix")]
        [SerializeField, Range(0f, 1f)] private float movementVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float combatVolume = 1f;

        private int _footstepIndex;

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

        public void PlayMeleeSwing(int comboIndex)
        {
            if (meleeSwings == null || meleeSwings.Length == 0)
                return;

            int index = Mathf.Clamp(comboIndex, 0, meleeSwings.Length - 1);
            Play(meleeSwings[index], combatVolume);
        }

        public void PlayBowRelease() => Play(bowRelease, combatVolume);
        public void PlayMagicCast() => Play(magicCast, combatVolume);

        /// <summary>Call from a future footstep Animation Event.</summary>
        public void PlayFootstep()
        {
            if (footsteps == null || footsteps.Length == 0)
                return;

            AudioClip clip = footsteps[_footstepIndex % footsteps.Length];
            _footstepIndex++;
            Play(clip, movementVolume);
        }

        private void OnJump() => Play(jump, movementVolume);
        private void OnLand() => Play(land, movementVolume);
        private void OnRoll() => Play(roll, movementVolume);

        private void Play(AudioClip clip, float volume)
        {
            if (oneShotSource != null && clip != null)
                oneShotSource.PlayOneShot(clip, volume);
        }
    }
}
