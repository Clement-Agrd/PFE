using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DustVFX.Demo
{
    /// <summary>
    /// Test character mover: keyboard (old and new Input System) or scripted commands, plus procedural feet
    /// so the automatic foot-contact detection of FootstepDust has real foot Transforms to work with.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [AddComponentMenu("Dust VFX/Demo Driver")]
    public sealed class DustDemoDriver : MonoBehaviour
    {
        [Header("Rig")]
        public Transform footL;
        public Transform footR;
        public Transform visual;

        [Header("Speeds (m/s)")]
        [Min(0.1f)] public float walkSpeed = 2.2f;
        [Min(0.1f)] public float runSpeed = 4.2f;
        [Min(0.1f)] public float sprintSpeed = 6.5f;

        [Header("Dynamics")]
        [Min(1f)] public float acceleration = 22f;
        [Min(1f)] public float braking = 40f;
        [Min(1f)] public float jumpSpeed = 6.5f;
        public float gravity = -22f;

        [Header("Gait")]
        [Tooltip("Metres per full stride cycle.")]
        [Min(0.5f)] public float strideLength = 1.5f;
        [Range(0f, 0.8f)] public float strideReach = 0.36f;
        [Range(0.05f, 0.5f)] public float footLift = 0.22f;
        [Range(0.05f, 0.4f)] public float footSide = 0.14f;

        [Header("Control")]
        [Tooltip("When false the scripted command* fields drive the character instead of the keyboard.")]
        public bool manual = true;
        public Vector3 commandDir;
        [Min(0f)] public float commandSpeed;
        public Camera referenceCamera;

        CharacterController cc;
        Vector3 velH;
        float velY;
        float phase;
        bool jumpQueued;

        public Vector3 HorizontalVelocity => velH;
        public bool Grounded => cc != null && cc.isGrounded;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            if (referenceCamera == null) referenceCamera = Camera.main;
        }

        public void Jump() => jumpQueued = true;

        public void Teleport(Vector3 pos, float yaw)
        {
            cc.enabled = false;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            cc.enabled = true;
            velH = Vector3.zero;
            velY = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 wish;
            float wishSpeed;
            if (manual) ReadKeyboard(out wish, out wishSpeed);
            else { wish = commandDir; wishSpeed = commandSpeed; }

            Vector3 target = wish.sqrMagnitude > 0.0001f ? wish.normalized * wishSpeed : Vector3.zero;
            float rate = target.sqrMagnitude < velH.sqrMagnitude ? braking : acceleration;
            velH = Vector3.MoveTowards(velH, target, rate * dt);

            if (cc.isGrounded)
            {
                velY = -2f;
                if (jumpQueued) { velY = jumpSpeed; }
            }
            jumpQueued = false;
            velY += gravity * dt;

            cc.Move(new Vector3(velH.x, velY, velH.z) * dt);

            float sp = velH.magnitude;
            if (sp > 0.3f)
            {
                Quaternion face = Quaternion.LookRotation(velH.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, face, 1f - Mathf.Exp(-14f * dt));
            }

            AnimateFeet(sp, dt);
        }

        void AnimateFeet(float sp, float dt)
        {
            if (footL == null || footR == null) return;

            bool air = !cc.isGrounded;
            if (!air && sp > 0.15f)
                phase += sp / strideLength * Mathf.PI * 2f * dt;

            float reach = Mathf.Clamp01(sp / 2.5f) * strideReach * Mathf.Lerp(1f, 1.35f, Mathf.Clamp01(sp / sprintSpeed));
            float lift = footLift * Mathf.Lerp(0.7f, 1.3f, Mathf.Clamp01(sp / sprintSpeed));

            SetFoot(footL, phase, -footSide, reach, lift, air);
            SetFoot(footR, phase + Mathf.PI, footSide, reach, lift, air);
        }

        void SetFoot(Transform foot, float ph, float side, float reach, float lift, bool air)
        {
            float s = Mathf.Sin(ph);
            float c = Mathf.Cos(ph);
            float y = 0.05f + (air ? lift : Mathf.Max(0f, c) * lift * (reach > 0.01f ? 1f : 0f));
            foot.localPosition = new Vector3(side, y, air ? 0.05f * side : s * reach);
        }

        void ReadKeyboard(out Vector3 wish, out float speed)
        {
            Vector2 m = Vector2.zero;
            bool sprint = false, slow = false, jump = false;
#if ENABLE_INPUT_SYSTEM
            Keyboard k = Keyboard.current;
            if (k != null)
            {
                m.x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1f : 0f) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1f : 0f);
                m.y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1f : 0f) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1f : 0f);
                sprint = k.leftShiftKey.isPressed;
                slow = k.leftCtrlKey.isPressed;
                jump = k.spaceKey.wasPressedThisFrame;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            m.x = Input.GetAxisRaw("Horizontal");
            m.y = Input.GetAxisRaw("Vertical");
            sprint = Input.GetKey(KeyCode.LeftShift);
            slow = Input.GetKey(KeyCode.LeftControl);
            jump = Input.GetKeyDown(KeyCode.Space);
#endif
            if (jump) jumpQueued = true;

            Transform cam = referenceCamera != null ? referenceCamera.transform : null;
            Vector3 fwd = cam != null ? Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            wish = fwd * m.y + right * m.x;
            speed = sprint ? sprintSpeed : slow ? walkSpeed : runSpeed;
        }
    }
}
