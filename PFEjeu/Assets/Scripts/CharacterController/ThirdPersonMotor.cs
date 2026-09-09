using System;
using UnityEngine;

namespace ProfessionalTPS
{
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public sealed class ThirdPersonMotor : MonoBehaviour
    {
        // ============================================================
        // REFERENCES
        // ============================================================

        [Header("References")]
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private PlayerCombatController combat;
        [SerializeField] private PlayerStamina stamina;


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
        // AIR CONTROL
        // ============================================================

        [Header("Air Control")]

        [SerializeField, Range(0f, 1f)]
        private float airControl = 0.35f;

        [SerializeField, Min(0f)]
        private float airDeceleration = 0.4f;

        [SerializeField, Range(0f, 1f)]
        private float airRotationMultiplier = 0.65f;


        // ============================================================
        // SPRINT
        // ============================================================

        [Header("Sprint")]

        [SerializeField, Min(0f)]
        private float sprintSpeed = 8.5f;

        [SerializeField, Min(0f)]
        private float sprintStaminaPerSecond = 18f;

        [SerializeField, Range(0f, 1f)]
        private float minimumSprintInput = 0.35f;

        [SerializeField]
        private bool allowSprintWhileAiming = false;

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
        // GROUND DETECTION
        // ============================================================

        [Header("Ground Detection")]

        [SerializeField]
        private LayerMask groundMask = ~0;

        [SerializeField, Min(0.01f)]
        private float groundProbeDistance = 0.22f;

        [SerializeField, Range(0.5f, 1f)]
        private float groundProbeRadiusMultiplier = 0.9f;

        [SerializeField, Min(0.01f)]
        private float groundProbeStartOffset = 0.08f;

        [Tooltip(
            "Petite tolérance ajoutée au Slope Limit du CharacterController."
        )]
        [SerializeField, Min(0f)]
        private float slopeAngleTolerance = 0.5f;


        // ============================================================
        // STEEP SLOPE
        // ============================================================

        [Header("Steep Slope Sliding")]

        [Tooltip(
            "Temps avant de déclencher la glissade forcée.\n" +
            "0 = immédiat."
        )]
        [SerializeField, Min(0f)]
        private float steepSlopeSlideDelay = 0.05f;

        [Tooltip(
            "Vitesse maximale de glissade."
        )]
        [SerializeField, Min(0f)]
        private float steepSlopeSlideSpeed = 6.5f;

        [Tooltip(
            "Vitesse à laquelle le personnage atteint sa vitesse de glissade."
        )]
        [SerializeField, Min(0f)]
        private float steepSlopeAcceleration = 22f;

        [Tooltip(
            "Force verticale appliquée lorsque la glissade forcée commence."
        )]
        [SerializeField, Min(0f)]
        private float steepSlopeDownForce = 7f;

        [Tooltip(
            "Petit contrôle conservé pendant la glissade."
        )]
        [SerializeField, Range(0f, 1f)]
        private float steepSlopeControl = 0.15f;

        [Tooltip(
            "Durée pendant laquelle on mémorise un contact avec une pente/mur " +
            "trop raide. Évite que la glissade clignote entre deux frames."
        )]
        [SerializeField, Min(0f)]
        private float steepContactMemory = 0.08f;
        
        [Tooltip(
            "À partir de cet angle, la surface est considérée comme un mur " +
            "et non comme une pente sur laquelle on doit appliquer la glissade forcée."
        )]
        [SerializeField, Range(60f, 89.9f)]
        private float verticalWallAngleThreshold = 85f;


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


        [Header("Roll Stamina")]

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

        private float _rollTimer;

        private Vector3 _rollDirection;


        // Ground probe
        private bool _isGrounded;
        private bool _groundProbeSteep;

        private Vector3 _groundNormal =
            Vector3.up;

        private float _groundAngle;


        // Steep wall contact
        private Vector3 _steepContactNormal =
            Vector3.up;

        private float _steepContactAngle;

        private float _lastSteepContactTime =
            float.NegativeInfinity;

        private bool _steepContactIsWall;

        // Sliding
        private float _steepSurfaceTimer;

        private bool _isSlidingSteep;


        // Previous state
        private bool _wasGrounded;

        private float _airborneSpeedCap;


        private readonly RaycastHit[] _groundHits =
            new RaycastHit[12];


        // ============================================================
        // PUBLIC
        // ============================================================

        public bool IsGrounded =>
            _isGrounded;


        /// <summary>
        /// True si on se trouve sur une surface
        /// trop raide pour être grimpée.
        /// </summary>
        public bool IsOnSteepSlope =>
            HasActiveSteepSurface();


        public bool IsSlidingOnSteepSlope =>
            _isSlidingSteep;


        public float GroundAngle =>
            _groundAngle;


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

            UpdateGroundInfo();

            _wasGrounded =
                _isGrounded;

            _airborneSpeedCap =
                moveSpeed;
        }


        private void OnEnable()
        {
            if (input == null)
                return;

            input.JumpPressed += BufferJump;
            input.RollPressed += TryStartRoll;
        }


        private void OnDisable()
        {
            if (input == null)
                return;

            input.JumpPressed -= BufferJump;
            input.RollPressed -= TryStartRoll;
        }


        private void Update()
        {
            // --------------------------------------------------------
            // SOL
            // --------------------------------------------------------

            UpdateGroundInfo();


            bool steepSurface =
                HasActiveSteepSurface();


            UpdateSteepSurfaceTimer(
                steepSurface
            );


            if (_wasGrounded &&
                !_isGrounded)
            {
                CaptureAirMomentum();
            }


            if (_isGrounded)
            {
                _lastGroundedTime =
                    Time.time;

                if (_verticalVelocity < 0f)
                {
                    _verticalVelocity =
                        groundedVerticalVelocity;
                }
            }


            // --------------------------------------------------------
            // MOVEMENT
            // --------------------------------------------------------

            if (IsRolling)
            {
                StopSprint();

                TickRoll();
            }
            else if (steepSurface)
            {
                StopSprint();

                Vector3 steepNormal =
                    GetActiveSteepNormal();

                // Dès qu'on touche une surface trop raide,
                // on empêche la vélocité de continuer à pousser dedans.
                RemoveVelocityIntoSurface(
                    steepNormal
                );


                if (_isSlidingSteep)
                {
                    TickSteepSlope(
                        steepNormal
                    );
                }
                else
                {
                    // Pendant le délai avant glissade,
                    // on garde simplement le comportement aérien.
                    TickAirLocomotion(
                        steepNormal
                    );
                }
            }
            else
            {
                TickLocomotion();
            }


            // --------------------------------------------------------
            // GRAVITY / JUMP
            // --------------------------------------------------------

            TickGravityAndJump(
                steepSurface
            );


            Vector3 totalVelocity =
                _horizontalVelocity +
                Vector3.up *
                _verticalVelocity;


            // On conserve les contacts précédents jusqu'ici.
            // Les nouveaux contacts seront reçus pendant Move()
            // via OnControllerColliderHit.
            _controller.Move(
                totalVelocity *
                Time.deltaTime
            );


            // --------------------------------------------------------
            // APRES MOVE
            // --------------------------------------------------------

            bool groundedBeforeRefresh =
                _isGrounded;


            UpdateGroundInfo();


            if (!groundedBeforeRefresh &&
                _isGrounded &&
                _verticalVelocity <= 0f)
            {
                Landed?.Invoke();
            }


            _wasGrounded =
                _isGrounded;
        }


        // ============================================================
        // CONTROLLER COLLISIONS
        // ============================================================

        /// <summary>
        /// Permet de détecter les surfaces presque verticales
        /// que le SphereCast dirigé vers le bas ne peut pas détecter.
        /// </summary>
        private void OnControllerColliderHit(
            ControllerColliderHit hit)
        {
            if (hit.collider == null)
                return;

            if (hit.transform == transform ||
                hit.transform.IsChildOf(transform))
            {
                return;
            }

            Vector3 normal =
                hit.normal.normalized;

            float angle =
                Vector3.Angle(
                    normal,
                    Vector3.up
                );

            float maximumWalkable =
                _controller.slopeLimit +
                slopeAngleTolerance;

            // Surface praticable.
            if (angle <= maximumWalkable)
                return;

            // Plafond / dessous d'un collider.
            if (normal.y < -0.01f)
                return;

            if (angle > 90.5f)
                return;

            bool isVerticalWall =
                angle >= verticalWallAngleThreshold;

            _steepContactNormal =
                normal;

            _steepContactAngle =
                angle;

            _steepContactIsWall =
                isVerticalWall;

            _lastSteepContactTime =
                Time.time;

            // --------------------------------------------------------
            // IMPORTANT :
            // on retire uniquement la vitesse qui pousse DANS
            // la surface.
            //
            // On ne touche jamais à la vitesse verticale du saut.
            // --------------------------------------------------------

            RemoveVelocityIntoSurface(
                normal
            );

            // --------------------------------------------------------
            // MUR
            // --------------------------------------------------------

            if (isVerticalWall)
            {
                // Le saut continue normalement.
                // La gravité nous fera redescendre.
                return;
            }

            // --------------------------------------------------------
            // PENTE NON GRIMPABLE
            // --------------------------------------------------------

            // On ne coupe PAS le saut déjà commencé.
            //
            // Par contre cette pente ne devient jamais
            // une nouvelle source de saut.
            _jumpBufferedUntil =
                float.NegativeInfinity;

            _lastGroundedTime =
                float.NegativeInfinity;
        }


        // ============================================================
        // NORMAL LOCOMOTION
        // ============================================================

        private void TickLocomotion()
        {
            GetMovementDirection(
                out Vector2 moveInput,
                out Vector3 desiredDirection,
                out Vector3 cameraForward
            );


            if (_isGrounded)
            {
                TickGroundLocomotion(
                    moveInput,
                    desiredDirection,
                    cameraForward
                );
            }
            else
            {
                StopSprint();

                Vector3 blockingNormal =
                    HasRecentSteepContact()
                        ? _steepContactNormal
                        : Vector3.zero;

                TickAirLocomotion(
                    blockingNormal
                );
            }
        }


        private void TickGroundLocomotion(
            Vector2 moveInput,
            Vector3 desiredDirection,
            Vector3 cameraForward)
        {
            UpdateSprint(
                moveInput,
                desiredDirection
            );


            // Si on longe un mur/pente trop raide
            // tout en restant sur un sol praticable,
            // on retire uniquement la partie du mouvement
            // qui pousse dans le mur.
            if (HasRecentSteepContact())
            {
                desiredDirection =
                    RedirectAlongSteepSurface(
                        desiredDirection,
                        _steepContactNormal
                    );

                RemoveVelocityIntoSurface(
                    _steepContactNormal
                );
            }


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
                targetSpeed *
                movementMultiplier;


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


            RotateCharacter(
                desiredDirection,
                cameraForward,
                1f
            );
        }


        // ============================================================
        // AIR CONTROL
        // ============================================================

        private void TickAirLocomotion(
            Vector3 steepNormal)
        {
            GetMovementDirection(
                out _,
                out Vector3 desiredDirection,
                out Vector3 cameraForward
            );


            // Si on touche une pente très raide,
            // on retire la composante qui pousse dedans.
            if (steepNormal.sqrMagnitude >
                0.001f)
            {
                desiredDirection =
                    RedirectAlongSteepSurface(
                        desiredDirection,
                        steepNormal
                    );
            }


            if (desiredDirection.sqrMagnitude >
                0.001f)
            {
                float airAcceleration =
                    acceleration *
                    airControl;


                _horizontalVelocity +=
                    desiredDirection *
                    airAcceleration *
                    Time.deltaTime;


                Vector3 planar =
                    new Vector3(
                        _horizontalVelocity.x,
                        0f,
                        _horizontalVelocity.z
                    );


                float maxAirSpeed =
                    Mathf.Max(
                        moveSpeed,
                        _airborneSpeedCap
                    );


                if (planar.magnitude >
                    maxAirSpeed)
                {
                    planar =
                        planar.normalized *
                        maxAirSpeed;
                }


                _horizontalVelocity.x =
                    planar.x;

                _horizontalVelocity.z =
                    planar.z;
            }
            else
            {
                _horizontalVelocity =
                    Vector3.MoveTowards(
                        _horizontalVelocity,
                        Vector3.zero,
                        airDeceleration *
                        Time.deltaTime
                    );
            }


            if (steepNormal.sqrMagnitude >
                0.001f)
            {
                RemoveVelocityIntoSurface(
                    steepNormal
                );
            }


            RotateCharacter(
                desiredDirection,
                cameraForward,
                airRotationMultiplier
            );
        }


        // ============================================================
        // STEEP SLOPE SLIDING
        // ============================================================

        private void TickSteepSlope(
            Vector3 steepNormal)
        {
            GetMovementDirection(
                out _,
                out Vector3 desiredDirection,
                out Vector3 cameraForward
            );


            // Direction naturelle de descente.
            Vector3 downhill =
                Vector3.ProjectOnPlane(
                    Vector3.down,
                    steepNormal
                );


            Vector3 horizontalDownhill =
                new Vector3(
                    downhill.x,
                    0f,
                    downhill.z
                );


            if (horizontalDownhill.sqrMagnitude >
                0.001f)
            {
                horizontalDownhill.Normalize();
            }


            // Input projeté le long de la pente/mur.
            desiredDirection =
                RedirectAlongSteepSurface(
                    desiredDirection,
                    steepNormal
                );


            Vector3 targetVelocity =
                horizontalDownhill *
                steepSlopeSlideSpeed;


            targetVelocity +=
                desiredDirection *
                moveSpeed *
                steepSlopeControl;


            _horizontalVelocity =
                Vector3.MoveTowards(
                    _horizontalVelocity,
                    targetVelocity,
                    steepSlopeAcceleration *
                    Time.deltaTime
                );


            RemoveVelocityIntoSurface(
                steepNormal
            );


            // Sur un mur quasi vertical,
            // horizontalDownhill devient quasiment nul.
            //
            // Cette force verticale est donc particulièrement
            // importante : elle fait descendre le personnage.
            _verticalVelocity =
                Mathf.Min(
                    _verticalVelocity,
                    -steepSlopeDownForce
                );


            RotateCharacter(
                desiredDirection,
                cameraForward,
                0.5f
            );
        }


        private void UpdateSteepSurfaceTimer(
            bool steepSurface)
        {
            if (!steepSurface)
            {
                _steepSurfaceTimer = 0f;
                _isSlidingSteep = false;

                return;
            }


            _steepSurfaceTimer +=
                Time.deltaTime;


            _isSlidingSteep =
                _steepSurfaceTimer >=
                steepSlopeSlideDelay;
        }


        // ============================================================
        // STEEP HELPERS
        // ============================================================

        private bool HasActiveSteepSurface()
        {
            // Le probe sous les pieds détecte réellement
            // une pente trop raide.
            if (_groundProbeSteep)
                return true;

            // Si on a un vrai sol praticable sous les pieds,
            // toucher un mur sur le côté ne doit jamais
            // déclencher la glissade.
            if (_isGrounded)
                return false;

            if (!HasRecentSteepContact())
                return false;

            // Un mur quasi vertical n'utilise PAS
            // le système de glissade forcée.
            //
            // La gravité suffit à nous faire redescendre.
            if (_steepContactIsWall)
                return false;

            return true;
        }


        private bool HasRecentSteepContact()
        {
            return
                Time.time -
                _lastSteepContactTime <=
                steepContactMemory;
        }


        private Vector3 GetActiveSteepNormal()
        {
            if (_groundProbeSteep)
                return _groundNormal;


            if (HasRecentSteepContact())
                return _steepContactNormal;


            return Vector3.up;
        }


        /// <summary>
        /// Retire la partie du déplacement qui pousse
        /// directement dans le mur.
        /// La vitesse tangentielle est conservée.
        /// </summary>
        private void RemoveVelocityIntoSurface(
            Vector3 normal)
        {
            Vector3 planarNormal =
                new Vector3(
                    normal.x,
                    0f,
                    normal.z
                );


            if (planarNormal.sqrMagnitude <
                0.001f)
            {
                return;
            }


            planarNormal.Normalize();


            float velocityIntoNormal =
                Vector3.Dot(
                    _horizontalVelocity,
                    planarNormal
                );


            // La normale pointe vers l'extérieur.
            // Une valeur négative signifie donc
            // que nous poussons dans la surface.
            if (velocityIntoNormal < 0f)
            {
                _horizontalVelocity -=
                    planarNormal *
                    velocityIntoNormal;
            }
        }


        /// <summary>
        /// Transforme un mouvement diagonal contre une paroi
        /// en mouvement le long de cette paroi.
        /// </summary>
        private static Vector3 RedirectAlongSteepSurface(
            Vector3 direction,
            Vector3 normal)
        {
            if (direction.sqrMagnitude <
                0.001f)
            {
                return direction;
            }


            Vector3 planarNormal =
                new Vector3(
                    normal.x,
                    0f,
                    normal.z
                );


            if (planarNormal.sqrMagnitude <
                0.001f)
            {
                return direction;
            }


            planarNormal.Normalize();


            float intoSurface =
                Vector3.Dot(
                    direction,
                    planarNormal
                );


            // Si on ne se dirige pas dans la surface,
            // aucune correction n'est nécessaire.
            if (intoSurface >= 0f)
                return direction;


            direction -=
                planarNormal *
                intoSurface;


            direction.y = 0f;


            return Vector3.ClampMagnitude(
                direction,
                1f
            );
        }


        // ============================================================
        // JUMP / GRAVITY
        // ============================================================

        private void TickGravityAndJump(
            bool steepSurface)
        {
            if (!_isGrounded)
            {
                _verticalVelocity =
                    Mathf.Max(
                        terminalVelocity,
                        _verticalVelocity +
                        gravity *
                        Time.deltaTime
                    );
            }


            if (_isSlidingSteep)
            {
                _verticalVelocity =
                    Mathf.Min(
                        _verticalVelocity,
                        -steepSlopeDownForce
                    );
            }


            bool buffered =
                Time.time <=
                _jumpBufferedUntil;


            bool withinCoyote =
                Time.time -
                _lastGroundedTime <=
                coyoteTime;


            // Impossible de sauter depuis
            // une surface trop raide.
            if (!IsRolling &&
                !steepSurface &&
                buffered &&
                withinCoyote &&
                (combat == null ||
                 combat.CanJump))
            {
                _jumpBufferedUntil =
                    float.NegativeInfinity;

                // Le droit au saut est consommé immédiatement.
                _lastGroundedTime =
                    float.NegativeInfinity;

                CaptureAirMomentum();

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


        private void CaptureAirMomentum()
        {
            _airborneSpeedCap =
                Mathf.Max(
                    moveSpeed,
                    HorizontalSpeed
                );
        }


        // ============================================================
        // GROUND PROBE
        // ============================================================

        private void UpdateGroundInfo()
        {
            _isGrounded = false;

            _groundProbeSteep = false;


            _groundNormal =
                Vector3.up;

            _groundAngle =
                90f;


            if (_verticalVelocity > 0.1f)
                return;


            if (!TryGetGroundHit(
                    out RaycastHit hit))
            {
                return;
            }


            _groundNormal =
                hit.normal.normalized;


            _groundAngle =
                Vector3.Angle(
                    _groundNormal,
                    Vector3.up
                );


            float maximumWalkable =
                _controller.slopeLimit +
                slopeAngleTolerance;


            if (_groundAngle <=
                maximumWalkable)
            {
                _isGrounded = true;

                return;
            }


            // Une pente est trop raide mais reste une pente.
            // Au-delà de verticalWallAngleThreshold,
            // on considère que c'est un mur et non du sol.
            if (_groundAngle <
                verticalWallAngleThreshold)
            {
                _groundProbeSteep = true;
            }
        }


        private bool TryGetGroundHit(
            out RaycastHit bestHit)
        {
            bestHit = default;


            Vector3 worldCenter =
                transform.TransformPoint(
                    _controller.center
                );


            float halfHeight =
                Mathf.Max(
                    _controller.height * 0.5f,
                    _controller.radius
                );


            Vector3 bottomSphereCenter =
                worldCenter -
                Vector3.up *
                (
                    halfHeight -
                    _controller.radius
                );


            float probeRadius =
                Mathf.Max(
                    0.01f,
                    _controller.radius *
                    groundProbeRadiusMultiplier
                );


            Vector3 castOrigin =
                bottomSphereCenter +
                Vector3.up *
                groundProbeStartOffset;


            float castDistance =
                groundProbeStartOffset +
                groundProbeDistance;


            int hitCount =
                Physics.SphereCastNonAlloc(
                    castOrigin,
                    probeRadius,
                    Vector3.down,
                    _groundHits,
                    castDistance,
                    groundMask,
                    QueryTriggerInteraction.Ignore
                );


            bool found = false;


            float nearestDistance =
                float.PositiveInfinity;


            for (int i = 0;
                 i < hitCount;
                 i++)
            {
                RaycastHit candidate =
                    _groundHits[i];


                if (candidate.collider == null)
                    continue;


                if (candidate.transform ==
                    transform)
                {
                    continue;
                }


                if (candidate.transform.IsChildOf(
                    transform))
                {
                    continue;
                }


                if (candidate.distance <
                    nearestDistance)
                {
                    nearestDistance =
                        candidate.distance;

                    bestHit =
                        candidate;

                    found = true;
                }
            }


            return found;
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


            bool aiming =
                input != null &&
                input.AimHeld;


            bool attacking =
                combat != null &&
                combat.IsBusy;


            bool stateAllowsSprint =
                _isGrounded &&
                enoughMovement &&
                (allowSprintWhileAiming ||
                 !aiming) &&
                (allowSprintWhileAttacking ||
                 !attacking);


            bool wantsSprint =
                sprintButtonHeld &&
                stateAllowsSprint;


            if (!wantsSprint)
            {
                StopSprint();

                return;
            }


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
        // ROLL
        // ============================================================

        private void TryStartRoll()
        {
            if (IsRolling ||
                !_isGrounded ||
                HasActiveSteepSurface())
            {
                return;
            }


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


            if (rollUsesStamina &&
                stamina != null)
            {
                if (!stamina.TrySpend(
                        rollStaminaCost))
                {
                    return;
                }
            }


            StopSprint();


            GetMovementDirection(
                out _,
                out Vector3 desired,
                out _
            );


            _rollDirection =
                desired.sqrMagnitude > 0.01f
                    ? desired.normalized
                    : transform.forward;


            _rollTimer = 0f;

            IsRolling = true;


            RollStarted?.Invoke();


            combat?.NotifyRollStarted();
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
                rollSpeed *
                curve;


            if (_rollTimer >=
                rollDuration)
            {
                IsRolling = false;


                _lastRollEndTime =
                    Time.time;


                RollEnded?.Invoke();
            }
        }


        // ============================================================
        // MOVEMENT HELPERS
        // ============================================================

        private void GetMovementDirection(
            out Vector2 moveInput,
            out Vector3 desiredDirection,
            out Vector3 cameraForward)
        {
            moveInput =
                input != null
                    ? input.Move
                    : Vector2.zero;


            cameraForward =
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


            desiredDirection =
                cameraForward *
                moveInput.y +
                cameraRight *
                moveInput.x;


            if (desiredDirection.sqrMagnitude >
                1f)
            {
                desiredDirection.Normalize();
            }
        }


        private void RotateCharacter(
            Vector3 desiredDirection,
            Vector3 cameraForward,
            float multiplier)
        {
            Vector3 facingDirection =
                desiredDirection;


            if (combat != null &&
                combat.ShouldFaceCamera)
            {
                facingDirection =
                    cameraForward;
            }


            if (facingDirection.sqrMagnitude <=
                0.001f)
            {
                return;
            }


            Quaternion targetRotation =
                Quaternion.LookRotation(
                    facingDirection,
                    Vector3.up
                );


            float sharpness =
                rotationSharpness *
                multiplier;


            float rotationT =
                1f -
                Mathf.Exp(
                    -sharpness *
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
}