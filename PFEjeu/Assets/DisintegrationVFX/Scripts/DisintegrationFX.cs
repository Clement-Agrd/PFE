using UnityEngine;

namespace Core.DisintegrationVFX
{
    /// <summary>
    /// Prefab poolé : éclats de papier, cendres, braises, fumée, lueur, tache de brûlure.
    /// Tous les systèmes sont en espace monde et sans émission automatique : le DisintegrationController
    /// les alimente depuis la surface du mesh, dans la bande du front.
    /// </summary>
    public sealed class DisintegrationFX : MonoBehaviour
    {
        [Header("Systèmes")]
        [SerializeField] private ParticleSystem scraps;
        [SerializeField] private ParticleSystem ash;
        [SerializeField] private ParticleSystem embers;
        [SerializeField] private ParticleSystem smoke;

        [Header("Lueur")]
        [SerializeField] private Light glow;
        [SerializeField, Range(0f, 8f)] private float glowRange = 3f;

        [Header("Tache de brûlure")]
        [SerializeField] private Renderer scorch;
        [SerializeField, Range(0.3f, 2f)] private float scorchSize = 0.9f;
        [SerializeField, Range(1f, 12f)] private float scorchFadeDelay = 6f;
        [SerializeField, Range(0.2f, 4f)] private float scorchFadeTime = 2f;

        private static readonly int TintId = Shader.PropertyToID("_Tint");

        private static readonly int HotId = Shader.PropertyToID("_HotColor");
        private MaterialPropertyBlock _scrapMpb, _emberMpb;
        private ParticleSystemRenderer _scrapR, _emberR;
        private DisintegrationStyle _style;
        private DisintegrationQuality _quality;
        private ParticleSystem.EmitParams _ep;
        private MaterialPropertyBlock _mpb;
        private Gradient _emberGrad, _ashGrad, _smokeGrad;
        private GradientColorKey[] _ck = new GradientColorKey[3];
        private GradientAlphaKey[] _ak = new GradientAlphaKey[3];
        private Vector3 _center;
        private float _scorchAppear, _age, _finishedAt = -1f, _glowBase, _flickerSeed;
        private bool _useSmoke, _useLight;

        public float Scale => (_quality == DisintegrationQuality.High ? 1f : _quality == DisintegrationQuality.Medium ? 0.6f : 0.22f);
        public bool UseSmoke => _useSmoke;

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            _scrapMpb = new MaterialPropertyBlock();
            _emberMpb = new MaterialPropertyBlock();
            if (scraps != null) _scrapR = scraps.GetComponent<ParticleSystemRenderer>();
            if (embers != null) _emberR = embers.GetComponent<ParticleSystemRenderer>();
            _emberGrad = new Gradient();
            _ashGrad = new Gradient();
            _smokeGrad = new Gradient();
        }

        public void Begin(DisintegrationStyle style, DisintegrationQuality quality, Vector3 center, Vector3 groundPoint, bool hasGround, float bodyWidth)
        {
            _style = style;
            _quality = quality;
            _center = center;
            _age = 0f;
            _finishedAt = -1f;
            _scorchAppear = 0f;
            _flickerSeed = Random.value * 100f;
            _useSmoke = quality != DisintegrationQuality.Low && style.smokeCount > 0;
            _useLight = quality != DisintegrationQuality.Low;
            transform.position = Vector3.zero;

            ParticleSystem[] all = { scraps, ash, embers, smoke };
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                all[i].Clear(true);
                all[i].Play(true);
            }

            var m = scraps.main; m.gravityModifier = style.gravity;
            m = ash.main; m.gravityModifier = style.gravity * 0.15f + (style.motion == ParticleMotion.Fall ? 0.25f : 0.04f);
            m = embers.main; m.gravityModifier = style.motion == ParticleMotion.Fall ? style.gravity : -0.12f * style.speed;
            m = smoke.main; m.gravityModifier = -0.03f;

            SetGradient(embers, _emberGrad, style.emberColor, style.emberColorEnd, new Color(0.08f, 0, 0), 0.85f);
            SetGradient(ash, _ashGrad, style.ashColor, style.ashColor * 0.35f, Color.black, 0.9f);
            SetGradient(smoke, _smokeGrad, style.smokeColor, style.smokeColor, style.smokeColor, 0.7f);

            if (_scrapR != null)
            {
                _scrapR.GetPropertyBlock(_scrapMpb);
                _scrapMpb.SetColor(TintId, style.scrapColor);
                _scrapMpb.SetColor(HotId, style.scrapHot);
                _scrapR.SetPropertyBlock(_scrapMpb);
            }

            if (glow != null)
            {
                glow.color = style.lightColor;
                glow.range = glowRange;
                glow.intensity = 0f;
                glow.enabled = _useLight;
                _glowBase = style.lightIntensity;
            }

            if (scorch != null)
            {
                scorch.enabled = hasGround;
                if (hasGround)
                {
                    Transform t = scorch.transform;
                    t.position = groundPoint + Vector3.up * 0.025f;
                    t.rotation = Quaternion.Euler(90f, Random.value * 360f, 0f);
                    float s = scorchSize * Mathf.Clamp(bodyWidth / 0.8f, 0.7f, 1.6f);
                    _scorchFactor = s;
                    SetScorch(0f);
                }
            }
        }

        private void SetGradient(ParticleSystem ps, Gradient g, Color a, Color b, Color c, float fadeStart)
        {
            _ck[0] = new GradientColorKey(a, 0f);
            _ck[1] = new GradientColorKey(b, 0.45f);
            _ck[2] = new GradientColorKey(c, 1f);
            _ak[0] = new GradientAlphaKey(0f, 0f);
            _ak[1] = new GradientAlphaKey(1f, 0.08f);
            _ak[2] = new GradientAlphaKey(0f, 1f);
            g.SetKeys(_ck, _ak);
            var col = ps.colorOverLifetime;
            col.color = g;
        }

        // ---------------------------------------------------------------- emission

        public void EmitScrap(Vector3 p, Vector3 n, Vector3 center)
        {
            if (scraps == null) return;
            _ep = default;
            _ep.position = p + n * 0.02f;
            _ep.velocity = Motion(p, n, center, 0.5f, 1.5f, true);
            _ep.startLifetime = Random.Range(1f, 2f);
            _ep.startSize = Random.Range(0.12f, 0.3f);
            _ep.startColor = Color.white;
            _ep.rotation3D = new Vector3(Random.value * 360f, Random.value * 360f, Random.value * 360f);
            _ep.angularVelocity3D = new Vector3(Random.Range(-220f, 220f), Random.Range(-220f, 220f), Random.Range(-220f, 220f));
            scraps.Emit(_ep, 1);
        }

        public void EmitAsh(Vector3 p, Vector3 n, Vector3 center)
        {
            if (ash == null) return;
            _ep = default;
            _ep.position = p;
            _ep.velocity = Motion(p, n, center, 0.1f, 0.6f, false);
            _ep.startLifetime = Random.Range(2f, 3.5f);
            _ep.startSize = Random.Range(0.035f, 0.1f);
            _ep.startColor = Color.white;
            _ep.rotation = Random.value * 360f;
            _ep.angularVelocity = Random.Range(-90f, 90f);
            ash.Emit(_ep, 1);
        }

        public void EmitEmber(Vector3 p, Vector3 n, Vector3 center)
        {
            if (embers == null) return;
            _ep = default;
            _ep.position = p;
            _ep.velocity = Motion(p, n, center, 0.4f, 1.4f, false) + Random.insideUnitSphere * 0.25f;
            _ep.startLifetime = Random.Range(0.8f, 1.6f);
            _ep.startSize = Random.Range(0.018f, 0.045f);
            _ep.startColor = Color.white;
            embers.Emit(_ep, 1);
        }

        public void EmitSmoke(Vector3 p)
        {
            if (!_useSmoke || smoke == null) return;
            _ep = default;
            _ep.position = p;
            _ep.velocity = new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(0.2f, 0.5f), Random.Range(-0.1f, 0.1f));
            _ep.startLifetime = Random.Range(2f, 3.2f);
            _ep.startSize = Random.Range(0.4f, 0.7f);
            _ep.startColor = new Color(1f, 1f, 1f, 0.4f);
            _ep.rotation = Random.value * 360f;
            smoke.Emit(_ep, 1);
        }

        private Vector3 Motion(Vector3 p, Vector3 n, Vector3 center, float min, float max, bool scrap)
        {
            float v = Random.Range(min, max) * _style.speed;
            switch (_style.motion)
            {
                case ParticleMotion.Inward:
                    Vector3 toC = center - p;
                    toC.y *= 0.4f;
                    return toC.normalized * v * 1.3f + Random.insideUnitSphere * 0.15f;
                case ParticleMotion.Fall:
                    return new Vector3(n.x, -Mathf.Abs(n.y) * 0.2f - 0.3f, n.z) * v * 0.5f + Random.insideUnitSphere * 0.1f;
                case ParticleMotion.Shatter:
                    return (n + Random.insideUnitSphere * 0.6f + Vector3.up * 0.2f).normalized * v * (scrap ? 1.8f : 1f);
                default:
                    return Vector3.up * v + n * (v * 0.3f) + Random.insideUnitSphere * 0.2f;
            }
        }

        // ---------------------------------------------------------------- front / lueur / tache

        public void SetFront(Vector3 pos, float intensity01)
        {
            if (glow == null || !_useLight) return;
            glow.transform.position = pos + Vector3.up * 0.1f;
            float flick = 0.75f + 0.5f * Mathf.PerlinNoise(_flickerSeed, Time.time * 18f);
            glow.intensity = _glowBase * intensity01 * flick;
        }

        public void SetScorchAppear(float a)
        {
            _scorchAppear = Mathf.Max(_scorchAppear, a);
            if (_finishedAt < 0f) SetScorch(1f);
        }

        private void SetScorch(float fade)
        {
            if (scorch == null || !scorch.enabled) return;
            scorch.GetPropertyBlock(_mpb);
            float scale = Mathf.SmoothStep(0.15f, 1f, _scorchAppear);
            scorch.transform.localScale = Vector3.one * _scorchFactor * scale;
            _mpb.SetColor(TintId, new Color(1f, 1f, 1f, 0.92f * fade * Mathf.Clamp01(_scorchAppear * 2f)));
            scorch.SetPropertyBlock(_mpb);
        }

        private float _scorchFactor = 1f;

        /// <summary>Plus d'émission : le FX continue seul (cendres, fumée, tache) puis retourne au pool.</summary>
        public void Finish()
        {
            if (_finishedAt >= 0f) return;
            _finishedAt = _age;
            if (glow != null) glow.intensity = 0f;
        }

        private void Update()
        {
            _age += Time.deltaTime;

            // scintillement des braises
            if (_emberR != null)
            {
                float f = 2.2f + 2.2f * Mathf.PerlinNoise(_flickerSeed, _age * 14f);
                _emberR.GetPropertyBlock(_emberMpb);
                _emberMpb.SetColor(TintId, new Color(f, f, f, 1f));
                _emberR.SetPropertyBlock(_emberMpb);
            }

            if (_finishedAt < 0f)
                return;

            float since = _age - _finishedAt;
            // la lueur s'éteint doucement
            if (glow != null && glow.enabled && glow.intensity > 0f)
                glow.intensity = Mathf.MoveTowards(glow.intensity, 0f, Time.deltaTime * 2f);

            float fade = 1f - Mathf.Clamp01((since - (scorchFadeDelay - 2.2f)) / scorchFadeTime);
            SetScorch(fade);

            if (since > scorchFadeDelay - 2.2f + scorchFadeTime && TotalParticles() == 0)
            {
                if (scorch != null) scorch.enabled = false;
                DisintegrationManager.Return(this);
            }
        }

        private int TotalParticles()
        {
            return (scraps != null ? scraps.particleCount : 0) + (ash != null ? ash.particleCount : 0)
                 + (embers != null ? embers.particleCount : 0) + (smoke != null ? smoke.particleCount : 0);
        }

        /// <summary>Coupe net (reset / ressuscitation).</summary>
        public void Kill()
        {
            if (scorch != null) scorch.enabled = false;
            DisintegrationManager.Return(this);
        }
    }
}
