using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace SpiritVFX
{
    public enum SpiritState { Closed, Cracking, Building, Tearing, Open, Closing }

    /// Portail dimensionnel infernal. Pivot = centre de la base au sol, face avant = +Z (côté d'où sortent les unités).
    /// Open() : fissures qui se propagent → tension → déchirure. Close() : effondrement. SpawnPoint = devant le portail.
    [DisallowMultipleComponent]
    public class SpiritGate : MonoBehaviour
    {
        [Header("Forme (m)")]
        public float width = 6.5f;
        public float height = 10f;
        [Tooltip("Marge autour de l'ovale pour les fissures dans l'air.")] public float padding = 2.2f;
        [Header("Palette HDR : hot = lumière dorée, mid = sève verte, deep = ombre de forêt")]
        [ColorUsage(false, true)] public Color hot = new Color(1.7f, 1.5f, 0.9f);
        [ColorUsage(false, true)] public Color mid = new Color(0.2f, 1.15f, 0.6f);
        [ColorUsage(false, true)] public Color deep = new Color(0.012f, 0.06f, 0.04f);
        [Header("Séquence d'ouverture")]
        public float crackTime = 2f;
        public float buildTime = 0.9f, tearTime = 1.8f, closeTime = 1.3f;
        public bool playOnStart;
        [Header("Look")]
        [Range(0.2f, 2f)] public float intensity = 1f;
        [Range(0f, 2f)] public float parallax = 1f;
        [Range(0f, 2f)] public float shake = 1f;
        public bool distortion = true;
        [Tooltip("Faux : portail flottant dans l'espace (pas de fissures au sol, pas de gouttes de lave). Vrai : posé sur un sol.")] public bool grounded = true;
        public bool snapToGround = true;
        public LayerMask groundMask = ~0;
        [Header("Références (remplies par Tools/Spirit VFX/Build All)")]
        public Renderer surface, distortionR, ground;
        public ParticleSystem crackSparks, rimSparks, embers, ash, burst, groundSparks, leaves;
        public Light glow, lowGlow;
        [Header("Events")]
        public UnityEvent onOpened, onClosed;

        public SpiritState State { get; private set; } = SpiritState.Closed;
        public bool IsOpen => State == SpiritState.Open;
        public Vector3 SpawnPoint => transform.position + transform.forward * 2.2f;

        static readonly int Open_ = Shader.PropertyToID("_Open"), Crack_ = Shader.PropertyToID("_Crack"), Flash_ = Shader.PropertyToID("_Flash"),
            Breath_ = Shader.PropertyToID("_Breath"), Seed_ = Shader.PropertyToID("_Seed"), Par_ = Shader.PropertyToID("_Parallax"), Int_ = Shader.PropertyToID("_Intensity"),
            Wave_ = Shader.PropertyToID("_Wave"), Size_ = Shader.PropertyToID("_Size"), Spirit_ = Shader.PropertyToID("_Spirit"),
            Hot_ = Shader.PropertyToID("_Hot"), Mid_ = Shader.PropertyToID("_Mid"), Deep_ = Shader.PropertyToID("_Deep");
        MaterialPropertyBlock pbS, pbD, pbG;
        float crack, open, flash, wave, breath, seed, baseRumble;
        Coroutine run;
        float[] baseRates;

        void Awake()
        {
            pbS = new MaterialPropertyBlock(); pbD = new MaterialPropertyBlock(); pbG = new MaterialPropertyBlock();
            seed = UnityEngine.Random.Range(1f, 40f);
            if (distortionR && distortion)
            {
                var urp = GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
                if (urp != null && !urp.supportsCameraOpaqueTexture) { Debug.LogWarning("[SpiritVFX] Opaque Texture désactivée dans l'URP Asset : distorsion thermique coupée (Tools/Spirit VFX/Setup URP)."); distortion = false; }
            }
            Layout(); SetVisible(false);
        }
        void Start() { if (playOnStart) Open(); }
        void OnDisable() { if (run != null) { StopCoroutine(run); run = null; } }

        public void Open() { if (State == SpiritState.Open || State == SpiritState.Cracking || State == SpiritState.Building || State == SpiritState.Tearing) return; Run(OpenRoutine()); }
        public void Close() { if (State == SpiritState.Closed || State == SpiritState.Closing) return; Run(CloseRoutine()); }
        public void Toggle() { if (State == SpiritState.Closed || State == SpiritState.Closing) Open(); else Close(); }
        void Run(IEnumerator r) { if (run != null) StopCoroutine(run); run = StartCoroutine(r); }

        static float Ease(float p) { return p * p * (3f - 2f * p); }
        static float OutBack(float x) { x = Mathf.Clamp01(x); const float c1 = 1.0f, c3 = c1 + 1f; return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f); }

        /// Place et dimensionne tous les enfants selon width / height.
        public void Layout()
        {
            float W = width, H = height, P = padding;
            if (grounded && snapToGround && Application.isPlaying && Physics.Raycast(transform.position + Vector3.up * 2f, Vector3.down, out var hit, 8f, groundMask, QueryTriggerInteraction.Ignore))
            { if (ground) ground.transform.position = new Vector3(transform.position.x, hit.point.y + 0.03f, transform.position.z); }
            else if (ground) ground.transform.localPosition = new Vector3(0, 0.03f, 0);
            if (surface) { surface.transform.localPosition = new Vector3(0, H * 0.5f, 0); surface.transform.localScale = new Vector3(W * P, H * P, 1f); }
            if (distortionR) { distortionR.transform.localPosition = new Vector3(0, H * 0.5f, 0.02f); distortionR.transform.localScale = new Vector3(W * P * 1.15f, H * P * 1.15f, 1f); }
            if (ground) { ground.gameObject.SetActive(grounded); float g = Mathf.Max(W, H) * 3.4f; ground.transform.localScale = new Vector3(g, g, 1f); ground.transform.localRotation = Quaternion.Euler(90f, 0, 0); }
            Shape(rimSparks, new Vector3(W * 0.75f, H * 0.75f, 1f), new Vector3(0, H * 0.5f, 0.3f));
            Shape(embers, new Vector3(W * 0.4f, H * 0.4f, 1f), new Vector3(0, H * 0.5f, 0));
            Shape(ash, new Vector3(W * 0.5f, H * 0.5f, 1f), new Vector3(0, H * 0.5f, 0));
            Shape(burst, new Vector3(W * 0.45f, H * 0.45f, 1f), new Vector3(0, H * 0.5f, 0));
            Shape(crackSparks, new Vector3(W * 1.3f, H * 1.0f, 1f), new Vector3(0, H * 0.5f, 0.1f));
            Shape(leaves, new Vector3(W * 0.5f, H * 0.5f, 1f), new Vector3(0, H * 0.5f, 0));
            if (groundSparks) { var s = groundSparks.shape; s.scale = new Vector3(W * 1.5f, 1.2f, 0.1f); }
            if (glow) glow.transform.localPosition = new Vector3(0, H * 0.45f, 3.5f);
            if (lowGlow) lowGlow.transform.localPosition = grounded ? new Vector3(0, 0.8f, 2f) : new Vector3(0, H * 0.5f, 7f);
            if (glow) glow.range = H * 5f; if (lowGlow) lowGlow.range = H * 2.5f;
            baseRates = new[] { Rate(crackSparks), Rate(rimSparks), Rate(embers), Rate(ash), Rate(groundSparks), Rate(leaves) };
            ApplyColors();
        }
        static void Shape(ParticleSystem ps, Vector3 scale, Vector3 pos)
        {
            if (!ps) return; ps.transform.localPosition = pos; var s = ps.shape; s.scale = scale;
        }
        static float Rate(ParticleSystem ps) { return ps ? ps.emission.rateOverTimeMultiplier : 0f; }
        static void SetRate(ParticleSystem ps, float r) { if (!ps) return; var e = ps.emission; e.rateOverTimeMultiplier = r; }
        void ApplyColors()
        {
            Tint(crackSparks, hot); Tint(rimSparks, Color.Lerp(mid, hot, 0.5f)); Tint(embers, mid); Tint(burst, Color.Lerp(mid, hot, 0.5f)); Tint(groundSparks, Color.Lerp(mid, hot, 0.5f));
            if (glow) glow.color = Color.Lerp(mid, hot, 0.15f); if (lowGlow) lowGlow.color = mid;
        }
        static void Tint(ParticleSystem ps, Color c)
        {
            if (!ps) return; var m = ps.main; var sc = m.startColor;
            Color a = sc.colorMin, b = sc.colorMax; float la = Mathf.Max(a.r, a.g, a.b, 0.001f), lb = Mathf.Max(b.r, b.g, b.b, 0.001f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(c.r * a.r / la, c.g * a.g / la, c.b * a.b / la, a.a), new Color(c.r * b.r / lb, c.g * b.g / lb, c.b * b.b / lb, b.a));
        }
        void SetVisible(bool v)
        {
            if (surface) surface.enabled = v; if (ground) ground.enabled = v && grounded; if (distortionR) distortionR.enabled = v && distortion;
            if (glow) glow.enabled = v; if (lowGlow) lowGlow.enabled = v;
        }

        void Apply()
        {
            Vector4 sz = new Vector4(width * padding, height * padding, 0, 0), pz = new Vector4(width, height, 0, 0);
            Fill(pbS, sz, pz); if (surface) surface.SetPropertyBlock(pbS);
            Fill(pbD, new Vector4(width * padding * 1.15f, height * padding * 1.15f, 0, 0), pz); if (distortionR) distortionR.SetPropertyBlock(pbD);
            float g = Mathf.Max(width, height) * 3.4f; Fill(pbG, new Vector4(g, g, 0, 0), pz); if (ground) ground.SetPropertyBlock(pbG);
            float ob = Mathf.Clamp01(open), fl = 0.85f + 0.15f * Mathf.Sin(Time.time * 23f) * Mathf.Sin(Time.time * 7.3f);
            float k = Mathf.Max(0.2f, intensity);
            if (glow) glow.intensity = (crack * 4f * (1f - ob) + ob * 6f * fl + flash * 20f) * k;
            if (lowGlow) lowGlow.intensity = (crack * 0.5f + ob * 2.5f + flash * 8f) * fl * k;
            if (baseRates != null)
            {
                SetRate(crackSparks, baseRates[0] * crack * (1f - ob)); SetRate(rimSparks, baseRates[1] * ob); SetRate(embers, baseRates[2] * ob);
                SetRate(ash, baseRates[3] * ob); SetRate(groundSparks, grounded ? baseRates[4] * ob : 0f); SetRate(leaves, baseRates[5] * ob);
            }
        }
        void Fill(MaterialPropertyBlock pb, Vector4 size, Vector4 portal)
        {
            pb.SetVector(Size_, size); pb.SetVector(Spirit_, portal); pb.SetFloat(Open_, open); pb.SetFloat(Crack_, crack); pb.SetFloat(Flash_, flash);
            pb.SetFloat(Breath_, breath); pb.SetFloat(Seed_, seed); pb.SetFloat(Par_, parallax); pb.SetFloat(Int_, intensity); pb.SetFloat(Wave_, wave);
            pb.SetColor(Hot_, hot); pb.SetColor(Mid_, mid); pb.SetColor(Deep_, deep);
        }
        void Rumble(float amount) { SpiritCameraShake.Hit(transform.position + Vector3.up * height * 0.5f, amount * shake, Mathf.Max(width, height) * 5f); }
        void Play(ParticleSystem ps) { if (ps) ps.Play(true); }
        void Stop(ParticleSystem ps) { if (ps) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); }

        IEnumerator OpenRoutine()
        {
            seed = UnityEngine.Random.Range(1f, 40f); Layout(); SetVisible(true); crack = open = flash = wave = breath = 0f;
            foreach (var p in new[] { crackSparks, rimSparks, embers, ash, groundSparks, leaves }) Play(p);
            // 1. fissures
            State = SpiritState.Cracking;
            for (float t = 0; t < crackTime; t += Time.deltaTime)
            { float p = t / crackTime; crack = Ease(p); Rumble(0.05f + 0.08f * p); Apply(); yield return null; }
            // 2. tension
            State = SpiritState.Building; crack = 1f;
            for (float t = 0; t < buildTime; t += Time.deltaTime)
            { float p = t / buildTime; breath = p; flash = 0.2f * p * p + 0.12f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 12f)), 4f) * p; Rumble(0.12f + 0.25f * p); Apply(); yield return null; }
            // 3. déchirure
            State = SpiritState.Tearing;
            if (burst) { burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); burst.Play(true); }
            Rumble(0.7f); if (leaves) leaves.Emit(70);
            for (float t = 0; t < tearTime; t += Time.deltaTime)
            { float p = t / tearTime; open = OutBack(p); flash = Mathf.Max(0f, 1f - p * 2.2f); wave = p; Apply(); yield return null; }
            open = 1f; wave = 0f; State = SpiritState.Open; onOpened?.Invoke();
            while (State == SpiritState.Open)
            { flash = Mathf.Max(0f, flash - Time.deltaTime * 1.5f); breath = Mathf.Min(1f, breath + Time.deltaTime * 0.8f); Apply(); yield return null; }
        }
        IEnumerator CloseRoutine()
        {
            State = SpiritState.Closing; float o0 = open, c0 = crack, b0 = breath;
            for (float t = 0; t < closeTime; t += Time.deltaTime)
            {
                float p = t / closeTime; open = o0 * (1f - p * p); flash = Mathf.Sin(p * Mathf.PI) * 0.9f; crack = c0 * (1f - Mathf.Max(0f, p - 0.5f) * 2f); breath = b0 * (1f - p); wave = p * 0.9f;
                Rumble(0.5f * (1f - p)); Apply(); yield return null;
            }
            crack = open = flash = wave = breath = 0f; Apply();
            foreach (var p in new[] { crackSparks, rimSparks, embers, ash, groundSparks, leaves }) Stop(p);
            SetVisible(false); State = SpiritState.Closed; run = null; onClosed?.Invoke();
        }

#if UNITY_EDITOR
        void OnValidate() { if (!Application.isPlaying) Layout(); }
#endif
    }
}
