using System;
using UnityEngine;

namespace FreezeFx
{
    /// <summary>
    /// FX de l'état "glacé" posé sur une cible (personne, ennemi) : des cristaux de glace poussent sur son corps, le givre s'étend au sol,
    /// le corps prend une teinte gelée, une brume froide et des étincelles bleues flottent autour, une lueur froide l'éclaire,
    /// et son animation se fige. Réutilise les cristaux, la brume et les étincelles du kit Ice.
    ///
    /// À instancier en enfant de l'unité (pivot quelconque : le corps est mesuré au démarrage).
    /// Le gameplay appelle <see cref="BeginFreeze()"/> quand l'état est appliqué et <see cref="EndFreeze"/> quand il se termine ;
    /// ou <see cref="BeginFreeze(SOEtat)"/> pour que le FX dégèle tout seul après la durée de l'état.
    /// Le dégel est progressif : les cristaux fondent (dissolve), la teinte et l'animation reviennent.
    ///
    /// FrozenTarget (prefab)
    /// ├── IceSpikes    cristaux (MeshRenderer, shader IceVFX/IceCrystal) qui poussent un par un
    /// ├── GroundFrost  quad de givre posé au sol
    /// ├── Mist         ParticleSystem (brume froide)
    /// ├── Sparkles     ParticleSystem (étincelles bleues qui scintillent)
    /// ├── ShardBurst   ParticleSystem (éclats projetés au moment du gel et du dégel)
    /// └── ColdLight    Light (point light bleutée)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FrozenTargetFx : MonoBehaviour
    {
        #region Types et constantes
        [Serializable]
        private struct IceSpike
        {
            public Transform root;
            public Renderer spikeRenderer;
            [Tooltip("Taille finale relative (1 = taille de base du cristal pour un corps de 2 m).")]
            public Vector3 baseScale;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

        private const float ReferenceBodyHeight = 2f;
        private const float GoldenAngle = 2.39996323f;
        private const float SpikeRadiusRatio = 0.85f;
        #endregion

        #region Champs sérialisés
        [Header("Références (remplies par l'installeur)")]
        [SerializeField] private IceSpike[] spikes;
        [SerializeField] private Renderer groundFrost;
        [SerializeField] private ParticleSystem mist;
        [SerializeField] private ParticleSystem sparkles;
        [SerializeField] private ParticleSystem shardBurst;
        [SerializeField] private ParticleSystem auraGlow;
        [SerializeField] private Light coldLight;

        [Header("Cycle")]
        [SerializeField, Min(0.05f)] private float freezeTime = 0.9f;
        [SerializeField, Min(0.05f)] private float thawTime = 1.8f;

        [Header("Corps de la cible")]
        [Tooltip("Mesure le corps au démarrage et y place les cristaux, la brume et la lumière.")]
        [SerializeField] private bool autoFit = true;
        [Tooltip("Fige l'Animator de la cible pendant le gel (vitesse -> 0), puis le relance au dégel.")]
        [SerializeField] private bool freezeAnimator = true;
        [Tooltip("Teinte de la glace sur le corps.")]
        [SerializeField] private Color frostTint = new Color(0.62f, 0.84f, 1f, 1f);
        [SerializeField, Range(0f, 1f)] private float tintAmount = 0.6f;

        [Header("Particules")]
        [SerializeField, Min(0f)] private float mistRate = 10f;
        [SerializeField, Min(0f)] private float sparkleRate = 16f;
        [Tooltip("Halo bleu doux autour du corps (l aura bleutée) : bouffées lumineuses lentes.")]
        [SerializeField, Min(0f)] private float auraRate = 9f;
        [Tooltip("Éclats projetés au gel et au dégel (0 = aucun).")]
        [SerializeField, Min(0)] private int burstShards;

        [Header("Lumière")]
        [SerializeField, Min(0f)] private float lightIntensity = 2.2f;
        [SerializeField] private Color lightColor = new Color(0.4f, 0.72f, 1f, 1f);
        #endregion

        /// <summary>Déclenché quand l'état commence (true) ou se termine (false).</summary>
        public event Action<bool> FrozenChanged;

        #region État interne
        private bool frozen;
        private float freezeFade;           // 0 = dégelé, 1 = entièrement gelé
        private bool timed;
        private float timer;
        private float appliedFade = -1f;

        private Vector3[] spikeFullScales;
        private float[] spikeDelays;
        private MaterialPropertyBlock spikeBlock;
        private MaterialPropertyBlock frostBlock;

        private Renderer[] bodyRenderers;
        private Color[] originalColors;
        private int[] colorIds;
        private MaterialPropertyBlock tintBlock;

        private Animator[] animators;
        private float[] originalSpeeds;
        private float groundRadius = 1f;
        private float fitScale = 1f;         // taille des cristaux proportionnelle à la hauteur du corps (1 = corps de 2 m)
        #endregion

        public bool IsFrozen => frozen;

        #region Cycle de vie Unity
        private void Awake()
        {
            spikeBlock = new MaterialPropertyBlock();
            frostBlock = new MaterialPropertyBlock();
            tintBlock = new MaterialPropertyBlock();

            CacheBody();
            CacheAnimators();
            if (autoFit) FitToTarget();
            CacheSpikeScales();
            Apply(0f, true);
        }

        private void OnDisable()
        {
            RestoreBody();
            RestoreAnimators();
            appliedFade = -1f;
        }

        private void Update()
        {
            if (timed)
            {
                timer -= Time.deltaTime;
                if (timer <= 0f)
                {
                    timed = false;
                    EndFreeze();
                }
            }

            float target = frozen ? 1f : 0f;
            float duration = frozen ? freezeTime : thawTime;
            freezeFade = Mathf.MoveTowards(freezeFade, target, Time.deltaTime / duration);

            Apply(freezeFade, false);
        }
        #endregion

        #region API publique
        /// <summary>Début de l'état : les cristaux poussent, le givre s'étend. Reste gelé jusqu'à <see cref="EndFreeze"/>.</summary>
        public void BeginFreeze()
        {
            timed = false;
            SetFrozen(true);
        }

        /// <summary>Début de l'état pour une durée donnée (secondes) : dégèle tout seul ensuite. 0 ou moins = jusqu'à EndFreeze.</summary>
        public void BeginFreeze(float duration)
        {
            timed = duration > 0f;
            timer = duration;
            SetFrozen(true);
        }

        /// <summary>Début de l'état décrit par un SOEtat : la durée de l'état pilote la durée du gel.</summary>
        public void BeginFreeze(SOEtat state)
        {
            BeginFreeze(state != null ? state.Duration : 0f);
        }

        /// <summary>Fin de l'état : dégel progressif.</summary>
        public void EndFreeze()
        {
            timed = false;
            SetFrozen(false);
        }

        /// <summary>Mesure à nouveau le corps de la cible et recale cristaux, givre, brume et lumière.</summary>
        public void FitToTarget()
        {
            Bounds bounds = MeasureBody(out bool found);
            if (!found) return;

            Vector3 center = transform.InverseTransformPoint(bounds.center);
            float height = Mathf.Max(bounds.size.y, 0.5f);
            // Rayon du torse : la profondeur du corps, ou la moitié de sa largeur (les bras écartés ne doivent pas éloigner les cristaux).
            float radius = Mathf.Max(bounds.extents.z, bounds.extents.x * 0.5f, 0.25f);
            float bottom = center.y - bounds.extents.y;
            groundRadius = radius * 2.6f;
            fitScale = Mathf.Max(height / ReferenceBodyHeight, 0.3f);

            PlaceSpikes(center, bottom, height, radius);

            if (groundFrost != null)
            {
                groundFrost.transform.localPosition = new Vector3(center.x, bottom + 0.03f, center.z);
                groundFrost.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }

            PlaceShell(mist, new Vector3(center.x, bottom + height * 0.3f, center.z), new Vector3(radius * 2.8f, height * 0.6f, radius * 2.8f));
            PlaceShell(sparkles, center, new Vector3(radius * 2.6f, height * 1.1f, radius * 2.6f));
            PlaceShell(auraGlow, new Vector3(center.x, center.y, center.z), new Vector3(radius * 1.8f, height * 0.9f, radius * 1.8f));
            if (shardBurst != null) shardBurst.transform.localPosition = center;
            if (coldLight != null) coldLight.transform.localPosition = center;

            CacheSpikeScales();
        }

        /// <summary>Utilisé par l installeur Editor : câble le halo bleu (aura).</summary>
        public void SetAuraReference(ParticleSystem aura)
        {
            auraGlow = aura;
        }

        /// <summary>Utilisé par l'installeur Editor pour câbler le prefab.</summary>
        public void SetReferences(Transform[] spikeRoots, Renderer[] spikeRenderers, Vector3[] spikeScales, Renderer frost,
            ParticleSystem mistSystem, ParticleSystem sparkleSystem, ParticleSystem shardSystem, Light light)
        {
            spikes = new IceSpike[spikeRoots.Length];
            for (int i = 0; i < spikeRoots.Length; i++)
                spikes[i] = new IceSpike { root = spikeRoots[i], spikeRenderer = spikeRenderers[i], baseScale = spikeScales[i] };

            groundFrost = frost;
            mist = mistSystem;
            sparkles = sparkleSystem;
            shardBurst = shardSystem;
            coldLight = light;
        }
        #endregion

        #region Application de l'état
        private void SetFrozen(bool value)
        {
            if (frozen == value) return;
            frozen = value;

            // Éclats projetés au gel comme au dégel.
            if (shardBurst != null) shardBurst.Emit(value ? burstShards : burstShards / 2);
            FrozenChanged?.Invoke(frozen);
        }

        // Appelé à chaque frame : aucune allocation (blocks et tableaux réutilisés). Ne réécrit rien quand l'état est stable.
        private void Apply(float fade, bool force)
        {
            if (!force && Mathf.Abs(fade - appliedFade) < 0.0005f) { UpdateAmbient(fade); return; }
            appliedFade = fade;

            ApplySpikes(fade);
            ApplyGroundFrost(fade);
            ApplyTint(fade * tintAmount);
            ApplyAnimators(fade);
            UpdateAmbient(fade);
        }

        // Brume, étincelles et lumière : suivent le fondu à chaque frame.
        private void UpdateAmbient(float fade)
        {
            SetRate(mist, mistRate * fade);
            SetRate(sparkles, sparkleRate * fade);
            SetRate(auraGlow, auraRate * fade);

            if (coldLight != null)
            {
                bool lit = fade > 0.01f;
                coldLight.enabled = lit;
                if (lit)
                {
                    float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 1.7f);
                    coldLight.color = lightColor;
                    coldLight.intensity = lightIntensity * fade * pulse;
                }
            }
        }

        // Chaque cristal pousse avec son propre retard (échelonné de bas en haut), avec un léger dépassement ; au dégel, il fond (dissolve).
        private void ApplySpikes(float fade)
        {
            if (spikes == null) return;

            bool thawing = !frozen;
            float dissolve = thawing ? Mathf.Clamp01(1f - fade * 1.15f) : 0f;

            for (int i = 0; i < spikes.Length; i++)
            {
                if (spikes[i].root == null) continue;

                float delay = spikeDelays[i];
                float progress = Mathf.Clamp01((fade - delay) / Mathf.Max(0.0001f, 1f - delay));
                float eased = EaseOutBack(progress);
                spikes[i].root.localScale = spikeFullScales[i] * eased;

                if (spikes[i].spikeRenderer != null)
                {
                    spikes[i].spikeRenderer.enabled = progress > 0.001f;
                    spikes[i].spikeRenderer.GetPropertyBlock(spikeBlock);
                    spikeBlock.SetFloat(DissolveId, dissolve);
                    spikes[i].spikeRenderer.SetPropertyBlock(spikeBlock);
                }
            }
        }

        private void ApplyGroundFrost(float fade)
        {
            if (groundFrost == null) return;

            groundFrost.enabled = fade > 0.001f;
            float size = groundRadius * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fade * 1.2f));
            groundFrost.transform.localScale = new Vector3(size, size, 1f);

            groundFrost.GetPropertyBlock(frostBlock);
            frostBlock.SetColor(BaseColorId, new Color(0.9f, 1.05f, 1.15f, Mathf.Clamp01(fade)));
            groundFrost.SetPropertyBlock(frostBlock);
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        private static void SetRate(ParticleSystem system, float rate)
        {
            if (system == null) return;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTimeMultiplier = rate;
        }

        private static void PlaceShell(ParticleSystem system, Vector3 localCenter, Vector3 localSize)
        {
            if (system == null) return;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = localCenter;
            shape.scale = localSize;
        }
        #endregion

        #region Cristaux
        // Répartit les cristaux autour du corps (angle d'or) en les étageant de bas en haut, tournés vers l'extérieur et le haut.
        private void PlaceSpikes(Vector3 center, float bottom, float height, float radius)
        {
            if (spikes == null) return;

            for (int i = 0; i < spikes.Length; i++)
            {
                if (spikes[i].root == null) continue;

                float t = (i + 0.5f) / spikes.Length;
                float angle = i * GoldenAngle;
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 position = new Vector3(center.x, bottom + t * height * 0.95f + 0.05f, center.z) + outward * (radius * SpikeRadiusRatio);
                Vector3 direction = (outward + Vector3.up * (0.9f - t * 0.5f)).normalized;

                spikes[i].root.localPosition = position;
                spikes[i].root.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
            }
        }

        // Mémorise une fois les tailles finales et les retards d'apparition (aucun calcul répété en jeu).
        private void CacheSpikeScales()
        {
            if (spikes == null) { spikeFullScales = new Vector3[0]; spikeDelays = new float[0]; return; }

            spikeFullScales = new Vector3[spikes.Length];
            spikeDelays = new float[spikes.Length];
            for (int i = 0; i < spikes.Length; i++)
            {
                spikeFullScales[i] = spikes[i].baseScale * fitScale;
                // le bas gèle d'abord, le haut ensuite
                spikeDelays[i] = Mathf.Lerp(0f, 0.55f, (i + 0.5f) / spikes.Length);
            }
        }
        #endregion

        #region Corps de la cible
        private Transform BodyRoot => transform.parent != null ? transform.parent : transform;

        private bool IsBodyRenderer(Renderer candidate)
        {
            if (candidate is ParticleSystemRenderer) return false;
            return !candidate.transform.IsChildOf(transform);   // exclut nos propres couches
        }

        private Bounds MeasureBody(out bool found)
        {
            found = false;
            var bounds = new Bounds(BodyRoot.position, Vector3.zero);
            Renderer[] all = BodyRoot.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < all.Length; i++)
            {
                if (!IsBodyRenderer(all[i])) continue;
                if (!found) { bounds = all[i].bounds; found = true; }
                else bounds.Encapsulate(all[i].bounds);
            }
            return bounds;
        }

        private void CacheBody()
        {
            Renderer[] all = BodyRoot.GetComponentsInChildren<Renderer>();
            int count = 0;
            for (int i = 0; i < all.Length; i++) if (IsBodyRenderer(all[i])) count++;

            bodyRenderers = new Renderer[count];
            originalColors = new Color[count];
            colorIds = new int[count];

            int index = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (!IsBodyRenderer(all[i])) continue;
                bodyRenderers[index] = all[i];
                colorIds[index] = -1;
                Material material = all[i].sharedMaterial;
                if (material != null)
                {
                    if (material.HasProperty(BaseColorId)) colorIds[index] = BaseColorId;
                    else if (material.HasProperty(ColorId)) colorIds[index] = ColorId;
                    if (colorIds[index] >= 0) originalColors[index] = material.GetColor(colorIds[index]);
                }
                index++;
            }
        }

        private void ApplyTint(float amount)
        {
            if (bodyRenderers == null) return;
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                if (bodyRenderers[i] == null || colorIds[i] < 0) continue;
                bodyRenderers[i].GetPropertyBlock(tintBlock);
                Color original = originalColors[i];
                Color tinted = Color.Lerp(original, new Color(frostTint.r, frostTint.g, frostTint.b, original.a), amount);
                tintBlock.SetColor(colorIds[i], tinted);
                bodyRenderers[i].SetPropertyBlock(tintBlock);
            }
        }

        private void RestoreBody()
        {
            if (bodyRenderers == null) return;
            for (int i = 0; i < bodyRenderers.Length; i++)
                if (bodyRenderers[i] != null && colorIds[i] >= 0) bodyRenderers[i].SetPropertyBlock(null);
        }
        #endregion

        #region Animation de la cible
        private void CacheAnimators()
        {
            animators = freezeAnimator ? BodyRoot.GetComponentsInChildren<Animator>() : new Animator[0];
            originalSpeeds = new float[animators.Length];
            for (int i = 0; i < animators.Length; i++) originalSpeeds[i] = animators[i].speed;
        }

        // L'animation ralentit jusqu'à l'arrêt complet pendant le gel, puis reprend sa vitesse d'origine au dégel.
        private void ApplyAnimators(float fade)
        {
            for (int i = 0; i < animators.Length; i++)
                if (animators[i] != null) animators[i].speed = Mathf.Lerp(originalSpeeds[i], 0f, Mathf.SmoothStep(0f, 1f, fade));
        }

        private void RestoreAnimators()
        {
            if (animators == null) return;
            for (int i = 0; i < animators.Length; i++)
                if (animators[i] != null) animators[i].speed = originalSpeeds[i];
        }
        #endregion
    }
}
