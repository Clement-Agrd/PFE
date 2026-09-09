using UnityEngine;

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

        [Header("Collision")]
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField, Min(0.01f)] private float collisionRadius = 0.2f;
        [SerializeField, Min(0f)] private float collisionPadding = 0.05f;

        private float _yaw;
        private float _pitch;
        private float _aimBlend;

        public Vector3 PlanarForward
        {
            get
            {
                Vector3 forward = transform.forward;
                forward.y = 0f;
                return forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
            }
        }

        private void Awake()
        {
            if (controlledCamera == null)
                controlledCamera = GetComponent<Camera>();

            if (playerRoot != null)
                _yaw = playerRoot.eulerAngles.y;
            else
                _yaw = transform.eulerAngles.y;

            _pitch = NormalizePitch(transform.eulerAngles.x);
        }

        private void OnEnable()
        {
            if (!lockCursor)
                return;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnDisable()
        {
            if (!lockCursor)
                return;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void LateUpdate()
        {
            if (input == null || target == null)
                return;

            UpdateLook();
            UpdateAimBlend();
            UpdateTransform();
        }

        private void UpdateLook()
        {
            Vector2 look = input.Look;

            if (input.LookIsPointerDelta)
            {
                _yaw += look.x * mouseSensitivity;
                _pitch -= look.y * mouseSensitivity;
            }
            else
            {
                _yaw += look.x * gamepadDegreesPerSecond * Time.unscaledDeltaTime;
                _pitch -= look.y * gamepadDegreesPerSecond * Time.unscaledDeltaTime;
            }

            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
        }

        private void UpdateAimBlend()
        {
            float targetBlend = input.AimHeld ? 1f : 0f;
            _aimBlend = Mathf.MoveTowards(_aimBlend, targetBlend, aimBlendSpeed * Time.unscaledDeltaTime);

            if (controlledCamera != null)
                controlledCamera.fieldOfView = Mathf.Lerp(normalFov, aimFov, _aimBlend);
        }

        private void UpdateTransform()
        {
            Quaternion orbitRotation = Quaternion.Euler(_pitch, _yaw, 0f);
            float currentDistance = Mathf.Lerp(distance, aimDistance, _aimBlend);
            Vector3 currentOffset = Vector3.Lerp(shoulderOffset, aimShoulderOffset, _aimBlend);

            Vector3 pivot = target.position;
            Vector3 desiredPosition =
                pivot +
                orbitRotation * currentOffset +
                orbitRotation * (Vector3.back * currentDistance);

            Vector3 castVector = desiredPosition - pivot;
            float castDistance = castVector.magnitude;

            if (castDistance > 0.001f &&
                Physics.SphereCast(
                    pivot,
                    collisionRadius,
                    castVector.normalized,
                    out RaycastHit hit,
                    castDistance,
                    collisionMask,
                    QueryTriggerInteraction.Ignore))
            {
                float safeDistance = Mathf.Max(0f, hit.distance - collisionPadding);
                desiredPosition = pivot + castVector.normalized * safeDistance;
            }

            transform.SetPositionAndRotation(desiredPosition, orbitRotation);
        }

        private static float NormalizePitch(float pitch)
        {
            if (pitch > 180f)
                pitch -= 360f;
            return pitch;
        }
    }
}
