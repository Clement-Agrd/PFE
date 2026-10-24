using System;
using UnityEngine;

namespace DustVFX
{
    /// <summary>
    /// Detects footsteps / landings / slides and asks the pool for dust.
    /// Step source, by preference: Animation Event OnFootstep(int foot) -> two foot Transforms
    /// (foot under groundOffset while moving down) -> travelled distance (stepLength).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Dust VFX/Footstep Dust")]
    public sealed class FootstepDust : MonoBehaviour
    {
        public enum StepSource
        {
            [Tooltip("Animation Events if they arrive, else foot Transforms, else distance.")]
            Auto,
            AnimationEvents,
            FootTransforms,
            Distance
        }

        [Header("Step Source")]
        [Tooltip("Auto picks the best available source at runtime.")]
        [SerializeField] StepSource source = StepSource.Auto;
        [Tooltip("Left foot (or ankle/toe) Transform used by the automatic detection.")]
        public Transform footL;
        [Tooltip("Right foot (or ankle/toe) Transform used by the automatic detection.")]
        public Transform footR;

        [Header("Foot Detection")]
        [Tooltip("A step is a foot passing below this height above the ground while moving down.")]
        [Range(0.01f, 0.5f)] public float groundOffset = 0.10f;
        [Tooltip("Minimum delay between two steps of the same foot.")]
        [Range(0.05f, 0.6f)] public float footCooldown = 0.15f;
        [Tooltip("Minimum downward foot speed (m/s) to count as a contact.")]
        [Range(0f, 1.5f)] public float minFootDownSpeed = 0.1f;

        [Header("Distance Fallback")]
        [Tooltip("Metres travelled per step when no better source exists.")]
        [Range(0.3f, 4f)] public float stepLength = 1.5f;
        [Tooltip("Sideways offset of the alternating fake feet.")]
        [Range(0f, 0.5f)] public float footSpread = 0.12f;

        [Header("Motion")]
        [Tooltip("Speed (m/s) at which the walk cloud is used.")]
        [Min(0.1f)] public float walkSpeed = 2.2f;
        [Tooltip("Speed (m/s) at which the full sprint cloud is used.")]
        [Min(0.2f)] public float sprintSpeed = 6.5f;
        [Tooltip("Below this speed no dust is spawned.")]
        [Min(0f)] public float minStepSpeed = 0.4f;

        [Header("Ground")]
        public LayerMask groundMask = ~0;
        [Range(0.2f, 3f)] public float groundRayLength = 1.2f;
        [Tooltip("Body counts as grounded within this distance of the ground.")]
        [Range(0.02f, 0.6f)] public float groundedDistance = 0.25f;

        [Header("Landing / Slide")]
        [Tooltip("Fall speed (m/s) below which a landing makes no ring.")]
        [Min(0f)] public float landMinSpeed = 3f;
        [Tooltip("Fall speed (m/s) that gives the biggest ring.")]
        [Min(0.5f)] public float landMaxSpeed = 12f;
        [Tooltip("Direction change (degrees) that triggers a turn-around slide.")]
        [Range(60f, 175f)] public float turnAngle = 110f;
        [Tooltip("Hard stop from a run (ring) when speed drops under this fraction of the recent speed.")]
        [Range(0.05f, 0.8f)] public float stopFraction = 0.3f;
        [Min(0.05f)] public float slideDuration = 0.45f;
        public bool autoDetectLandingAndTurns = true;

        [Header("Debug")]
        public bool debugLog;
        public bool drawGizmos = true;

        public event Action<DustRequest> DustRequested;

        public float Speed => speed;
        public bool IsGrounded => grounded;
        public bool IsSliding => Time.time < slideUntil;
        public int StepCount { get; private set; }

        CharacterController cc;
        Rigidbody rb;
        Vector3 lastPos, velocity, dirLag = Vector3.forward;
        float speed, speedLag, vy, fallPeak;
        bool grounded, wasGrounded, warnedFeet;
        float lastEventTime = -10f, nextStopTime, nextTurnTime, slideUntil;
        float distAccum;
        int nextFoot;
        DustStep trail;
        float nextTrailTime;

        struct FootState { public float prevY; public bool down; public float nextTime; public bool init; }
        FootState stateL, stateR;

        Vector3 lastGroundPoint = Vector3.zero;
        Vector3 lastGroundNormal = Vector3.up;

        void Awake()
        {
            cc = GetComponentInParent<CharacterController>();
            rb = GetComponentInParent<Rigidbody>();
            lastPos = transform.position;
        }

        void OnEnable()
        {
            lastPos = transform.position;
        }

        void OnDisable()
        {
            ReleaseTrail();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 1e-5f) return;

            Vector3 pos = transform.position;
            Vector3 raw = (pos - lastPos) / dt;
            lastPos = pos;
            velocity = Vector3.Lerp(velocity, raw, 1f - Mathf.Exp(-25f * dt));
            vy = raw.y;
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            speed = flat.magnitude;
            Vector3 dir = speed > 0.05f ? flat / speed : dirLag;

            wasGrounded = grounded;
            grounded = ProbeGround(pos, out lastGroundPoint, out lastGroundNormal, out _) ||
                       (cc != null && cc.isGrounded);

            if (!grounded)
                fallPeak = Mathf.Min(fallPeak, vy);

            if (autoDetectLandingAndTurns)
                DetectLandingAndMotion(dt, dir);

            speedLag = Mathf.Lerp(speedLag, speed, 1f - Mathf.Exp(-6f * dt));
            dirLag = Vector3.Slerp(dirLag, dir, 1f - Mathf.Exp(-(speed > 0.5f ? 5f : 0f) * dt));

            if (grounded && speed >= minStepSpeed)
            {
                bool eventsAlive = Time.time - lastEventTime < 1f;
                StepSource src = ResolveSource(eventsAlive);
                if (src == StepSource.FootTransforms)
                {
                    DetectFoot(footL, ref stateL, 0, dt);
                    DetectFoot(footR, ref stateR, 1, dt);
                }
                else if (src == StepSource.Distance)
                {
                    distAccum += speed * dt;
                    float len = stepLength * Mathf.Lerp(1f, 1.35f, Speed01());
                    if (distAccum >= len)
                    {
                        distAccum = 0f;
                        int foot = nextFoot;
                        nextFoot ^= 1;
                        Vector3 side = Vector3.Cross(Vector3.up, dir) * (foot == 0 ? -footSpread : footSpread);
                        EmitStep(foot, pos + side);
                    }
                }
            }

            TickTrail(pos, dir);
        }

        StepSource ResolveSource(bool eventsAlive)
        {
            switch (source)
            {
                case StepSource.AnimationEvents:
                    return StepSource.AnimationEvents;
                case StepSource.FootTransforms:
                    if (footL != null && footR != null) return StepSource.FootTransforms;
                    WarnFeet();
                    return StepSource.Distance;
                case StepSource.Distance:
                    return StepSource.Distance;
                default:
                    if (eventsAlive) return StepSource.AnimationEvents;
                    if (footL != null && footR != null) return StepSource.FootTransforms;
                    return StepSource.Distance;
            }
        }

        void WarnFeet()
        {
            if (warnedFeet) return;
            warnedFeet = true;
            Debug.LogWarning("[DustVFX] FootstepDust: footL/footR missing, falling back to distance-based steps.", this);
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Animation Event entry point: 0 = left foot, 1 = right foot.</summary>
        public void OnFootstep(int foot)
        {
            lastEventTime = Time.time;
            if (!grounded || speed < minStepSpeed) return;
            Transform t = foot == 0 ? footL : footR;
            Vector3 p = t != null ? t.position : transform.position;
            EmitStep(foot, p);
        }

        /// <summary>Landing after a fall / jump. impactSpeed is the downward speed in m/s (positive).</summary>
        public void Land(float impactSpeed)
        {
            if (!ProbeGround(transform.position, out Vector3 p, out Vector3 n, out Collider c))
                return;
            DustSurface s = DustSurfaceResolver.Resolve(c, p);
            Vector3 dir = speed > 0.3f ? new Vector3(velocity.x, 0f, velocity.z).normalized : transform.forward;
            float impact = Mathf.InverseLerp(landMinSpeed * 0.6f, landMaxSpeed, impactSpeed);
            Submit(new DustRequest
            {
                kind = DustEventKind.Land,
                position = p,
                normal = n,
                moveDir = dir,
                speed01 = Speed01(),
                impact01 = Mathf.Clamp01(impact),
                surface = s
            });
        }

        /// <summary>Slide / turn-around: starts (or extends) a continuous dust trail.</summary>
        public void Slide()
        {
            slideUntil = Time.time + slideDuration;
            if (debugLog) Debug.Log("[DustVFX] Slide", this);
        }

        // ------------------------------------------------------------------ detection

        float Speed01() => Mathf.Clamp01(Mathf.InverseLerp(walkSpeed, sprintSpeed, speed));

        void DetectLandingAndMotion(float dt, Vector3 dir)
        {
            if (grounded && !wasGrounded)
            {
                float impact = -fallPeak;
                fallPeak = 0f;
                if (impact >= landMinSpeed)
                    Land(impact);
            }
            else if (grounded)
            {
                fallPeak = 0f;
            }

            if (!grounded) return;

            // Hard stop from a run: radial ring.
            if (Time.time >= nextStopTime && speedLag > walkSpeed * 1.5f && speed < speedLag * stopFraction)
            {
                nextStopTime = Time.time + 0.6f;
                if (ProbeGround(transform.position, out Vector3 p, out Vector3 n, out Collider c))
                {
                    Vector3 d = new Vector3(dirLag.x, 0f, dirLag.z).normalized;
                    Submit(new DustRequest
                    {
                        kind = DustEventKind.Stop,
                        position = p,
                        normal = n,
                        moveDir = d,
                        speed01 = Mathf.Clamp01(Mathf.InverseLerp(walkSpeed, sprintSpeed, speedLag)),
                        impact01 = Mathf.Clamp01(Mathf.InverseLerp(walkSpeed, sprintSpeed, speedLag)),
                        surface = DustSurfaceResolver.Resolve(c, p)
                    });
                }
            }

            // Turn-around: velocity direction flips against the recent direction.
            if (Time.time >= nextTurnTime && speed > walkSpeed * 0.6f && speedLag > walkSpeed * 0.6f)
            {
                float cos = Mathf.Cos(turnAngle * Mathf.Deg2Rad);
                if (Vector3.Dot(dir, dirLag) < cos)
                {
                    nextTurnTime = Time.time + 0.8f;
                    Slide();
                }
            }
        }

        void DetectFoot(Transform foot, ref FootState st, int index, float dt)
        {
            if (foot == null) return;
            Vector3 fp = foot.position;
            if (!st.init)
            {
                st.init = true;
                st.prevY = fp.y;
                return;
            }

            float footVy = (fp.y - st.prevY) / dt;
            st.prevY = fp.y;

            float height = FootHeight(fp, out _, out _);
            if (st.down)
            {
                if (height > groundOffset * 1.5f) st.down = false;
                return;
            }

            if (height < groundOffset && footVy < -minFootDownSpeed && Time.time >= st.nextTime)
            {
                st.down = true;
                st.nextTime = Time.time + footCooldown;
                EmitStep(index, fp);
            }
        }

        // ------------------------------------------------------------------ ground

        static readonly RaycastHit[] hitBuffer = new RaycastHit[8];

        /// <summary>Distance between a foot and the ground below it (large if none).</summary>
        float FootHeight(Vector3 footPos, out Vector3 point, out Vector3 normal)
        {
            const float lift = 0.5f;
            if (RaycastGround(footPos + Vector3.up * lift, lift + groundRayLength, out RaycastHit h))
            {
                point = h.point;
                normal = h.normal;
                return h.distance - lift;
            }
            point = footPos;
            normal = Vector3.up;
            return float.MaxValue;
        }

        bool ProbeGround(Vector3 pos, out Vector3 point, out Vector3 normal, out Collider col)
        {
            const float lift = 0.3f;
            if (RaycastGround(pos + Vector3.up * lift, lift + groundedDistance, out RaycastHit h))
            {
                point = h.point;
                normal = h.normal;
                col = h.collider;
                return true;
            }
            point = pos;
            normal = Vector3.up;
            col = null;
            return false;
        }

        bool RaycastGround(Vector3 origin, float length, out RaycastHit best)
        {
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, hitBuffer, length, groundMask, QueryTriggerInteraction.Ignore);
            best = default;
            float bestD = float.MaxValue;
            Transform self = transform.root;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = hitBuffer[i];
                if (h.collider.transform.IsChildOf(self)) continue; // own capsule / limbs
                if (h.distance < bestD)
                {
                    bestD = h.distance;
                    best = h;
                }
            }
            return bestD < float.MaxValue;
        }

        // ------------------------------------------------------------------ emission

        void EmitStep(int foot, Vector3 footPos)
        {
            // Foot hits the ground below footPos: dust only where there is ground.
            if (!RaycastGround(footPos + Vector3.up * 0.4f, 0.4f + groundRayLength, out RaycastHit h))
                return;

            Vector3 dir = speed > 0.1f ? new Vector3(velocity.x, 0f, velocity.z) / speed : transform.forward;
            DustSurface s = DustSurfaceResolver.Resolve(h.collider, h.point);

            StepCount++;
            Submit(new DustRequest
            {
                kind = DustEventKind.Step,
                position = h.point,
                normal = h.normal,
                moveDir = dir,
                speed01 = Speed01(),
                surface = s,
                foot = foot
            });
        }

        void Submit(DustRequest r)
        {
            if (debugLog)
                Debug.Log($"[DustVFX] {r.kind} foot={r.foot} speed01={r.speed01:F2} impact={r.impact01:F2} " +
                          $"surface={(r.surface != null ? r.surface.name : "null")} normal={r.normal}", this);
            DustRequested?.Invoke(r);
            DustPool.Spawn(r);
        }

        // ------------------------------------------------------------------ slide trail

        void TickTrail(Vector3 pos, Vector3 dir)
        {
            if (!IsSliding || !grounded)
            {
                ReleaseTrail();
                return;
            }

            if (Time.time < nextTrailTime) return;
            nextTrailTime = Time.time + 0.06f;

            if (!ProbeGround(pos, out Vector3 p, out Vector3 n, out Collider c)) return;
            DustSurface s = DustSurfaceResolver.Resolve(c, p);
            if (s == null || !s.EmitsDust) return;

            if (trail == null) trail = DustPool.AcquireTrail();
            if (trail == null) return;

            trail.EmitTrail(new DustRequest
            {
                kind = DustEventKind.Slide,
                position = p,
                normal = n,
                moveDir = speed > 0.3f ? dir : new Vector3(dirLag.x, 0f, dirLag.z).normalized,
                speed01 = Mathf.Max(0.5f, Speed01()),
                surface = s
            });
        }

        void ReleaseTrail()
        {
            if (trail == null) return;
            DustPool.ReleaseTrail(trail);
            trail = null;
        }

        void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            Gizmos.color = Color.cyan;
            if (footL != null) Gizmos.DrawWireSphere(footL.position, groundOffset);
            if (footR != null) Gizmos.DrawWireSphere(footR.position, groundOffset);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(lastGroundPoint, lastGroundPoint + lastGroundNormal * 0.5f);
        }
    }
}
