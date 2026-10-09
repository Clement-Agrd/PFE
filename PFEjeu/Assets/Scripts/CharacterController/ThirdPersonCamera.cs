using UnityEngine;
using Unity.Cinemachine;
using Core.HealthSystem;

namespace ProfessionalTPS
{
    [DisallowMultipleComponent]
    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private Transform target;
        [SerializeField] private Transform playerRoot;
        [SerializeField] private Camera controlledCamera;
        [SerializeField] private Health playerHealth;

        [Header("Orbit")]
        [SerializeField, Min(0.01f)] private float mouseSensitivity = 0.12f;
        [SerializeField, Min(1f)] private float gamepadDegreesPerSecond = 180f;
        [SerializeField] private float minPitch = -35f;
        [SerializeField] private float maxPitch = 70f;
        [SerializeField] private bool lockCursor = true;

        [Header("Normal Camera")]
        [SerializeField, Min(0.5f)] private float distance = 4.2f;
        [SerializeField] private Vector3 shoulderOffset = new Vector3(0.55f, 0.15f, 0f);
        [SerializeField] private float normalFov = 60f;

        [Header("Aim Camera")]
        [SerializeField, Min(0.5f)] private float aimDistance = 3.0f;
        [SerializeField] private Vector3 aimShoulderOffset = new Vector3(0.75f, 0.2f, 0f);
        [SerializeField] private float aimFov = 52f;
        [SerializeField, Min(0f)] private float aimBlendSpeed = 10f;

        [Header("Cinemachine")]
        [SerializeField] private CinemachineCamera cinemachineCamera;
        [SerializeField] private CinemachineThirdPersonFollow thirdPersonFollow;
        [SerializeField] private CinemachineImpulseSource damageImpulseSource;
        [SerializeField] private CinemachineImpulseListener impulseListener;
        [SerializeField] private Vector3 followDamping = new Vector3(0.06f, 0.08f, 0.06f);

        [Header("Damage Camera Shake")]
        [SerializeField, Min(0f)] private float minimumShakeStrength = 0.35f;
        [SerializeField, Min(0f)] private float maximumShakeStrength = 1.2f;
        [SerializeField, Min(1f)] private float damageForMaximumShake = 35f;
        [SerializeField, Range(0f, 2f)] private float shakeListenerGain = 1f;

        private float _yaw;
        private float _pitch;
        private float _aimBlend;
        private bool _healthSubscribed;

        public Vector3 PlanarForward
        {
            get
            {
                Vector3 forward =
                    target != null
                        ? target.forward
                        : transform.forward;

                forward.y = 0f;

                return forward.sqrMagnitude > 0.001f
                    ? forward.normalized
                    : Vector3.forward;
            }
        }

        private void Awake()
        {
            if (controlledCamera == null)
                controlledCamera = GetComponent<Camera>();

            if (playerRoot == null)
                playerRoot = transform.root;

            if (playerHealth == null)
                playerHealth = GetComponentInParent<Health>();

            if (playerRoot != null)
                _yaw = playerRoot.eulerAngles.y;
            else
                _yaw = transform.eulerAngles.y;

            _pitch =
                NormalizePitch(
                    transform.eulerAngles.x
                );

            EnsureCinemachineRig();
            ApplyCinemachineSettings();
        }

        private void OnEnable()
        {
            EnsureCinemachineRig();

            if (cinemachineCamera != null)
                cinemachineCamera.enabled = true;

            SubscribeHealth();

            if (!lockCursor)
                return;

            Core.GameFreeze.ReleaseCursorUnlock(this);

            if (!Core.GameFreeze.IsCursorUnlocked)
            {
                Cursor.lockState =
                    CursorLockMode.Locked;

                Cursor.visible =
                    false;
            }
        }

        private void OnDisable()
        {
            UnsubscribeHealth();

            if (cinemachineCamera != null)
                cinemachineCamera.enabled = false;

            if (!lockCursor)
                return;

            Core.GameFreeze.RequestCursorUnlock(this);
        }

        private void LateUpdate()
        {
            if (input == null ||
                target == null)
            {
                return;
            }

            if (Core.GameFreeze.IsCursorUnlocked)
                return;

            UpdateLook();
            UpdateAimBlend();
            UpdateTrackingTarget();
            ApplyCinemachineSettings();
        }

        private void UpdateLook()
        {
            Vector2 look =
                input.Look;

            if (input.LookIsPointerDelta)
            {
                _yaw +=
                    look.x *
                    mouseSensitivity;

                _pitch -=
                    look.y *
                    mouseSensitivity;
            }
            else
            {
                _yaw +=
                    look.x *
                    gamepadDegreesPerSecond *
                    Time.unscaledDeltaTime;

                _pitch -=
                    look.y *
                    gamepadDegreesPerSecond *
                    Time.unscaledDeltaTime;
            }

            _pitch =
                Mathf.Clamp(
                    _pitch,
                    minPitch,
                    maxPitch
                );
        }

        private void UpdateAimBlend()
        {
            float targetBlend =
                input.AimHeld
                    ? 1f
                    : 0f;

            _aimBlend =
                Mathf.MoveTowards(
                    _aimBlend,
                    targetBlend,
                    aimBlendSpeed *
                    Time.unscaledDeltaTime
                );
        }

        private void UpdateTrackingTarget()
        {
            target.rotation =
                Quaternion.Euler(
                    _pitch,
                    _yaw,
                    0f
                );
        }

        private void EnsureCinemachineRig()
        {
            if (controlledCamera == null)
                return;

            if (controlledCamera.GetComponent<
                    CinemachineBrain
                >() == null)
            {
                controlledCamera.gameObject
                    .AddComponent<
                        CinemachineBrain
                    >();
            }

            if (cinemachineCamera == null)
            {
                CinemachineCamera existing =
                    playerRoot != null
                        ? playerRoot
                            .GetComponentInChildren<
                                CinemachineCamera
                            >(true)
                        : null;

                if (existing != null)
                {
                    cinemachineCamera =
                        existing;
                }
                else
                {
                    GameObject cameraRig =
                        new GameObject(
                            "CinemachinePlayerCamera"
                        );

                    if (playerRoot != null)
                    {
                        cameraRig.transform
                            .SetParent(
                                playerRoot,
                                true
                            );
                    }

                    cameraRig.transform
                        .SetPositionAndRotation(
                            controlledCamera
                                .transform
                                .position,
                            controlledCamera
                                .transform
                                .rotation
                        );

                    cinemachineCamera =
                        cameraRig.AddComponent<
                            CinemachineCamera
                        >();
                }
            }

            if (cinemachineCamera == null)
                return;

            CameraTarget cameraTarget =
                cinemachineCamera.Target;

            cameraTarget.TrackingTarget =
                target;

            cameraTarget.CustomLookAtTarget =
                false;

            cinemachineCamera.Target =
                cameraTarget;

            if (thirdPersonFollow == null)
            {
                thirdPersonFollow =
                    cinemachineCamera
                        .GetComponent<
                            CinemachineThirdPersonFollow
                        >();

                if (thirdPersonFollow == null)
                {
                    thirdPersonFollow =
                        cinemachineCamera
                            .gameObject
                            .AddComponent<
                                CinemachineThirdPersonFollow
                            >();
                }
            }

            if (damageImpulseSource == null)
            {
                damageImpulseSource =
                    cinemachineCamera
                        .GetComponent<
                            CinemachineImpulseSource
                        >();

                if (damageImpulseSource == null)
                {
                    damageImpulseSource =
                        cinemachineCamera
                            .gameObject
                            .AddComponent<
                                CinemachineImpulseSource
                            >();
                }
            }

            if (impulseListener == null)
            {
                impulseListener =
                    cinemachineCamera
                        .GetComponent<
                            CinemachineImpulseListener
                        >();

                if (impulseListener == null)
                {
                    impulseListener =
                        cinemachineCamera
                            .gameObject
                            .AddComponent<
                                CinemachineImpulseListener
                            >();
                }
            }

            impulseListener.Gain =
                shakeListenerGain;
        }

        private void ApplyCinemachineSettings()
        {
            if (cinemachineCamera == null ||
                thirdPersonFollow == null)
            {
                return;
            }

            float currentDistance =
                Mathf.Lerp(
                    distance,
                    aimDistance,
                    _aimBlend
                );

            Vector3 currentOffset =
                Vector3.Lerp(
                    shoulderOffset,
                    aimShoulderOffset,
                    _aimBlend
                );

            thirdPersonFollow.Damping =
                followDamping;

            thirdPersonFollow.CameraDistance =
                currentDistance;

            thirdPersonFollow.ShoulderOffset =
                currentOffset;

            thirdPersonFollow.VerticalArmLength =
                0f;

            thirdPersonFollow.CameraSide =
                currentOffset.x >= 0f
                    ? 1f
                    : 0f;

            LensSettings lens =
                cinemachineCamera.Lens;

            lens.FieldOfView =
                Mathf.Lerp(
                    normalFov,
                    aimFov,
                    _aimBlend
                );

            cinemachineCamera.Lens =
                lens;

            if (impulseListener != null)
            {
                impulseListener.Gain =
                    shakeListenerGain;
            }
        }

        private void SubscribeHealth()
        {
            if (_healthSubscribed ||
                playerHealth == null)
            {
                return;
            }

            playerHealth.OnDamageResolved +=
                OnDamageResolved;

            _healthSubscribed =
                true;
        }

        private void UnsubscribeHealth()
        {
            if (!_healthSubscribed ||
                playerHealth == null)
            {
                return;
            }

            playerHealth.OnDamageResolved -=
                OnDamageResolved;

            _healthSubscribed =
                false;
        }

        private void OnDamageResolved(
            DamageInfo info,
            int finalDamage)
        {
            if (damageImpulseSource == null ||
                finalDamage <= 0)
            {
                return;
            }

            float damage01 =
                Mathf.InverseLerp(
                    1f,
                    Mathf.Max(
                        1f,
                        damageForMaximumShake
                    ),
                    finalDamage
                );

            float strength =
                Mathf.Lerp(
                    minimumShakeStrength,
                    maximumShakeStrength,
                    damage01
                );

            Vector3 direction =
                new Vector3(
                    Random.Range(
                        -0.35f,
                        0.35f
                    ),
                    Random.Range(
                        -0.15f,
                        0.25f
                    ),
                    -1f
                ).normalized;

            damageImpulseSource
                .GenerateImpulse(
                    direction *
                    strength
                );
        }

        private static float NormalizePitch(
            float pitch)
        {
            if (pitch > 180f)
                pitch -= 360f;

            return pitch;
        }
    }
}
