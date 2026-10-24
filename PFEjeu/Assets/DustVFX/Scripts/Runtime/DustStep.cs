using UnityEngine;

namespace DustVFX
{
    /// <summary>
    /// One pooled step of dust: puffs, low veil, grains, wisps and landing ring, each a world-space
    /// ParticleSystem fed with manual Emit calls (no timers, no shape module). Total max particles &lt;= 60.
    /// </summary>
    [AddComponentMenu("Dust VFX/Dust Step")]
    public sealed class DustStep : MonoBehaviour
    {
        [Header("Particle Systems")]
        public ParticleSystem puffA;
        public ParticleSystem puffB;
        public ParticleSystem veil;
        public ParticleSystem grains;
        public ParticleSystem wisp;
        public ParticleSystem ring;
        [Tooltip("Plane used by the grain collision module (kept on the ground under the step).")]
        public Transform groundPlane;

        [Header("Look")]
        [Tooltip("Puff max size range in metres (walk -> sprint).")]
        public Vector2 puffSize = new Vector2(0.65f, 1.15f);
        public Vector2 puffLifetime = new Vector2(0.7f, 1.2f);
        public Vector2 veilSize = new Vector2(1.5f, 2.4f);
        public Vector2 veilLifetime = new Vector2(1.5f, 2.0f);
        [Range(0f, 1f)] public float veilAlpha = 0.25f;
        public Vector2 ringSize = new Vector2(1.8f, 3.6f);

        public float StartTime { get; private set; }
        public bool Dying { get; private set; }
        public bool Trailing { get; set; }

        ParticleSystem[] all;
        ParticleSystemRenderer grainRenderer;
        ParticleSystem.CollisionModule grainCollision;
        bool bound;
        Mesh[] defaultMeshes;
        int defaultMeshCount;
        bool meshOverridden;

#if DUSTVFX_SFX
        AudioSource audioSrc;
#endif

        void Bind()
        {
            if (bound) return;
            bound = true;
            all = new[] { puffA, puffB, veil, grains, wisp, ring };
            if (grains != null)
            {
                grainRenderer = grains.GetComponent<ParticleSystemRenderer>();
                defaultMeshes = new Mesh[Mathf.Max(1, grainRenderer.meshCount)];
                defaultMeshCount = grainRenderer.GetMeshes(defaultMeshes);
                grainCollision = grains.collision;
            }
        }

        // ------------------------------------------------------------------ lifecycle

        public bool IsFinished()
        {
            if (all == null) return true;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].IsAlive(false)) return false;
            return true;
        }

        public void ResetForPool()
        {
            Bind();
            Dying = false;
            Trailing = false;
            SetSpeed(1f);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null) all[i].Clear(false);
        }

        /// <summary>Fast but smooth fade-out (used when the global step limit cuts the oldest step).</summary>
        public void Hurry()
        {
            Bind();
            Dying = true;
            SetSpeed(3.5f);
        }

        void SetSpeed(float s)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                var m = all[i].main;
                m.simulationSpeed = s;
            }
        }

        // ------------------------------------------------------------------ main burst

        public void Play(in DustRequest r)
        {
            Bind();
            DustSettings st = DustSettings.Instance;
            DustSurface surf = r.surface;
            if (surf == null || !surf.EmitsDust) return;

            StartTime = Time.time;
            Dying = false;

            float k = st.intensity * surf.densityMul * (1f - 0.85f * st.wetness);
            if (k <= 0.02f) return;

            DustQuality q = st.quality;
            float t = Mathf.Clamp01(r.speed01);
            Frame f = MakeFrame(r);
            transform.SetPositionAndRotation(f.origin, Quaternion.LookRotation(f.back, f.normal));
            if (groundPlane != null)
                groundPlane.SetPositionAndRotation(r.position, Quaternion.FromToRotation(Vector3.up, f.normal));

            float alpha = Mathf.Clamp01(k) * Mathf.Clamp01(surf.color.a);
            float sizeBoost = k > 1f ? Mathf.Lerp(1f, 1.2f, Mathf.Min(1f, k - 1f)) : 1f;
            float sizeMul = surf.sizeMul * sizeBoost;
            float lifeMul = surf.lifetimeMul;
            bool radial = r.kind == DustEventKind.Land;
            bool stopping = r.kind == DustEventKind.Stop;
            float impact = Mathf.Clamp01(r.impact01);

            // ---- a. main puffs
            int puffCount;
            if (radial) puffCount = 4;
            else if (stopping) puffCount = 3;
            else puffCount = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(2f, 4f, t) * Mathf.Min(1f, k + 0.35f)), 1, 4);
            if (q == DustQuality.Low) puffCount = 1;
            else if (q == DustQuality.Medium) puffCount = Mathf.Min(puffCount, 2);

            for (int i = 0; i < puffCount; i++)
            {
                ParticleSystem ps = (i & 1) == 0 ? puffA : puffB;
                if (ps == null) continue;
                float size = Mathf.Lerp(puffSize.x, puffSize.y, radial ? 0.5f + impact * 0.5f : t) * sizeMul * Random.Range(0.85f, 1.15f);
                float life = Random.Range(puffLifetime.x, puffLifetime.y) * lifeMul * (radial ? 1.15f : 1f);
                Vector3 side = Vector3.Cross(f.normal, f.back);
                Vector3 vel;
                Vector3 pos;
                if (radial || stopping)
                {
                    float ang = (i + Random.value * 0.7f) / puffCount * Mathf.PI * 2f;
                    Vector3 dir = (side * Mathf.Cos(ang) + f.back * Mathf.Sin(ang));
                    if (stopping) dir = (-f.back * 0.8f + dir * 0.5f).normalized;
                    float spd = radial ? Random.Range(0.8f, 1.7f) * (0.6f + impact) : Random.Range(0.5f, 1.1f) * (0.5f + t);
                    vel = dir * spd + f.normal * Random.Range(0.1f, 0.35f) * surf.riseMul;
                    pos = f.origin + dir * (radial ? 0.25f : 0.12f) + f.normal * (size * 0.2f);
                }
                else
                {
                    Vector2 c = Random.insideUnitCircle * 0.12f;
                    vel = f.back * Random.Range(0.3f, 0.8f) * (0.6f + 0.8f * t)
                          + side * Random.Range(-0.2f, 0.2f)
                          + f.normal * Random.Range(0.12f, 0.35f) * surf.riseMul;
                    pos = f.origin + side * c.x + f.back * (0.08f + c.y) + f.normal * (size * 0.22f);
                }
                Emit(ps, pos, vel, size, life, Random.Range(0f, 360f), SurfaceColor(surf, alpha * Random.Range(0.85f, 1f)));
            }

            // ---- b. low veil
            if (veil != null && q != DustQuality.Low && surf.veilMul > 0.01f)
            {
                int n = (t > 0.5f || radial) ? 2 : 1;
                for (int i = 0; i < n; i++)
                {
                    float size = Mathf.Lerp(veilSize.x, veilSize.y, radial ? impact : t) * sizeMul * Random.Range(0.85f, 1.15f);
                    float life = Random.Range(veilLifetime.x, veilLifetime.y) * lifeMul;
                    Vector3 pos = f.origin + f.back * Random.Range(0.1f, 0.5f) + f.normal * 0.025f;
                    Vector3 vel = f.back * Random.Range(0.1f, 0.35f);
                    Emit(veil, pos, vel, size, life, Random.Range(0f, 360f), SurfaceColor(surf, Mathf.Clamp01(alpha * surf.veilMul * 1.2f)));
                }
            }

            // ---- c. grains
            EmitGrains(surf, f, t, k, radial, impact, stopping, 1f, q);

            // ---- d. wisps (running)
            if (wisp != null && q == DustQuality.High && (t > 0.55f || stopping) && !radial)
            {
                int n = Mathf.RoundToInt(Mathf.Lerp(2f, 5f, t));
                for (int i = 0; i < n; i++)
                    EmitWisp(surf, f, t, alpha, stopping ? -1f : 1f);
            }

            // ---- e. ring (landing, sprint stop)
            if (ring != null && (radial || stopping) && q != DustQuality.Low)
            {
                float size = Mathf.Lerp(ringSize.x, ringSize.y, impact) * sizeMul * (stopping ? 0.7f : 1f);
                float life = Random.Range(0.9f, 1.2f) * lifeMul;
                Emit(ring, f.origin + f.normal * 0.03f, Vector3.zero, size, life, Random.Range(0f, 360f),
                     SurfaceColor(surf, Mathf.Clamp01(alpha * (0.6f + 0.4f * impact))));
            }

#if DUSTVFX_SFX
            PlaySfx(surf, t);
#endif
        }

        // ------------------------------------------------------------------ trail (slide / turn-around)

        public void EmitTrail(in DustRequest r)
        {
            Bind();
            DustSettings st = DustSettings.Instance;
            DustSurface surf = r.surface;
            if (surf == null || !surf.EmitsDust) return;
            float k = st.intensity * surf.densityMul * (1f - 0.85f * st.wetness);
            if (k <= 0.02f) return;

            StartTime = Time.time;
            Frame f = MakeFrame(r);
            transform.SetPositionAndRotation(f.origin, Quaternion.LookRotation(f.back, f.normal));
            if (groundPlane != null)
                groundPlane.SetPositionAndRotation(r.position, Quaternion.FromToRotation(Vector3.up, f.normal));

            float alpha = Mathf.Clamp01(k) * Mathf.Clamp01(surf.color.a);
            Vector3 side = Vector3.Cross(f.normal, f.back);
            ParticleSystem ps = Random.value < 0.5f ? puffA : puffB;
            if (ps != null)
            {
                float size = Random.Range(0.6f, 0.95f) * surf.sizeMul;
                Vector2 c = Random.insideUnitCircle * 0.2f;
                Vector3 vel = f.back * Random.Range(0.3f, 0.9f) + f.normal * Random.Range(0.1f, 0.3f) * surf.riseMul + side * Random.Range(-0.3f, 0.3f);
                Emit(ps, f.origin + side * c.x + f.back * c.y + f.normal * (size * 0.2f), vel, size,
                     Random.Range(0.7f, 1.0f) * surf.lifetimeMul, Random.Range(0f, 360f), SurfaceColor(surf, alpha * 0.85f));
            }

            if (DustSettings.Instance.quality != DustQuality.Low)
                EmitGrains(surf, f, 0.7f, k, false, 0f, false, 0.35f, st.quality);
            if (wisp != null && st.quality == DustQuality.High && Random.value < 0.6f)
                EmitWisp(surf, f, 0.8f, alpha, 1f);
        }

        // ------------------------------------------------------------------ helpers

        struct Frame
        {
            public Vector3 origin, normal, back;
        }

        static Frame MakeFrame(in DustRequest r)
        {
            Frame f;
            f.normal = r.normal.sqrMagnitude > 0.001f ? r.normal.normalized : Vector3.up;
            Vector3 dir = Vector3.ProjectOnPlane(r.moveDir, f.normal);
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.ProjectOnPlane(Vector3.forward, f.normal);
            // +Z of the model = back of the step, so the dust goes opposite to the movement
            f.back = (-dir).normalized;
            f.origin = r.position + f.normal * 0.02f;
            return f;
        }

        static Color SurfaceColor(DustSurface s, float alpha)
        {
            Color c = s.color;
            c.a = Mathf.Clamp01(alpha);
            return c;
        }

        static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float life, float rotDeg, Color col)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = vel,
                startSize = size,
                startLifetime = life,
                rotation = rotDeg,
                startColor = col
            };
            ps.Emit(ep, 1);
        }

        void EmitWisp(DustSurface surf, in Frame f, float t, float alpha, float dirSign)
        {
            Vector3 side = Vector3.Cross(f.normal, f.back);
            Vector3 vel = f.back * dirSign * Random.Range(1.2f, 2.6f) * (0.6f + 0.6f * t) + f.normal * Random.Range(0.05f, 0.25f);
            Vector3 pos = f.origin + side * Random.Range(-0.18f, 0.18f) + f.back * Random.Range(0.05f, 0.25f) + f.normal * Random.Range(0.05f, 0.3f);
            Emit(wisp, pos, vel, Random.Range(0.14f, 0.26f) * surf.sizeMul, Random.Range(0.5f, 0.9f) * surf.lifetimeMul,
                 0f, SurfaceColor(surf, alpha * 0.8f));
        }

        void EmitGrains(DustSurface surf, in Frame f, float t, float k, bool radial, float impact, bool stopping,
                        float countScale, DustQuality q)
        {
            if (grains == null || q == DustQuality.Low || surf.grainCount <= 0) return;

            float qScale = q == DustQuality.Medium ? 0.5f : 1f;
            int n = Mathf.RoundToInt(surf.grainCount * Mathf.Lerp(0.6f, 1f, t) * Mathf.Min(1.3f, k) * qScale * countScale * (radial ? 1.4f : 1f));
            n = Mathf.Clamp(n, surf.grainCount > 0 ? 1 : 0, 15);

            if (surf.grainMesh != null)
            {
                if (grainRenderer.mesh != surf.grainMesh) grainRenderer.mesh = surf.grainMesh;
                meshOverridden = true;
            }
            else if (meshOverridden)
            {
                grainRenderer.SetMeshes(defaultMeshes, defaultMeshCount);
                meshOverridden = false;
            }

            var bounce = grainCollision;
            bounce.bounce = surf.grainBounce;

            Vector3 side = Vector3.Cross(f.normal, f.back);
            float sp = surf.grainSpeedMul * Mathf.Lerp(0.75f, 1.25f, t);
            for (int i = 0; i < n; i++)
            {
                Vector3 vel;
                Vector3 pos = f.origin + side * Random.Range(-0.1f, 0.1f) + f.normal * 0.04f;
                if (radial || stopping)
                {
                    float ang = (i + Random.value) / n * Mathf.PI * 2f;
                    Vector3 dir = side * Mathf.Cos(ang) + f.back * Mathf.Sin(ang);
                    if (stopping) dir = (-f.back + dir * 0.5f).normalized;
                    float spd = Random.Range(1.5f, 3.6f) * (0.5f + (radial ? impact : 0.6f));
                    vel = (dir * spd + f.normal * Random.Range(0.8f, 2.0f)) * sp;
                    pos += dir * 0.2f;
                }
                else
                {
                    vel = (f.back * Random.Range(0.4f, 1.8f) + f.normal * Random.Range(1.2f, 2.8f) + side * Random.Range(-0.8f, 0.8f)) * sp;
                }

                float s = Random.Range(surf.grainSize.x, surf.grainSize.y);
                Color gc = surf.grainColor;
                float shade = Random.Range(0.75f, 1.15f);
                gc = new Color(gc.r * shade, gc.g * shade, gc.b * shade, 1f);

                var ep = new ParticleSystem.EmitParams
                {
                    position = pos,
                    velocity = vel,
                    startSize3D = new Vector3(s * Random.Range(0.8f, 1.25f), s * Random.Range(0.7f, 1.2f), s * Random.Range(0.8f, 1.25f)),
                    startLifetime = Random.Range(0.7f, 1.2f),
                    rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)),
                    startColor = gc
                };
                grains.Emit(ep, 1);
            }
        }

#if DUSTVFX_SFX
        void PlaySfx(DustSurface s, float t)
        {
            if (!DustSettings.Instance.sfx || s.sfx == null || s.sfx.Length == 0) return;
            if (audioSrc == null)
            {
                audioSrc = gameObject.GetComponent<AudioSource>();
                if (audioSrc == null) audioSrc = gameObject.AddComponent<AudioSource>();
                audioSrc.playOnAwake = false;
                audioSrc.spatialBlend = 1f;
            }
            AudioClip c = s.sfx[Random.Range(0, s.sfx.Length)];
            if (c == null) return;
            audioSrc.pitch = Random.Range(0.9f, 1.1f) * Mathf.Lerp(0.95f, 1.1f, t);
            audioSrc.PlayOneShot(c, s.sfxVolume * Mathf.Lerp(0.6f, 1f, t));
        }
#endif
    }
}
