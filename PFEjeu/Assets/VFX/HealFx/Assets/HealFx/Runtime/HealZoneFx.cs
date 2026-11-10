using System;
using UnityEngine;

namespace HealFx
{
    /// <summary>
    /// FX d'une zone de soin posée au sol, en couches superposées :
    ///   (1) sol : disque, anneaux, runes, croix, bord qui pulse (shader)
    ///   (2) colonne de lumière douce (shader)
    ///   (3) volutes montantes (flipbook 8x8, érosion, soft depth)
    ///   (4) fines particules qui s'élèvent en spirale
    ///   (5) étincelles qui scintillent (+ croix montantes)
    ///   (6) lueur ambiante (point light, flicker léger)
    /// Le gameplay appelle <see cref="Pulse"/> à chaque tick de soin : une onde part du centre vers le bord.
    /// Cycle de vie en 3 temps : <see cref="Play"/> (apparition + onde de choc) -> boucle stable (respiration lente) -> <see cref="Stop"/> (dissipation douce).
    ///
    /// HealZone (prefab, pivot au sol)
    /// │
    /// ├── Ground          MeshRenderer (HealFx/ZoneGround)  -> échelle = 2 x rayon
    /// ├── Column          MeshRenderer (HealFx/Column)      -> tube de lumière
    /// ├── Crosses         ParticleSystem (croix qui montent)
    /// ├── Sparkles        ParticleSystem (étincelles)
    /// ├── Volutes         ParticleSystem (flipbook de brume lumineuse)
    /// ├── Spiral          ParticleSystem (fines particules en spirale)
    /// └── GlowLight       Light (point light douce)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HealZoneFx : MonoBehaviour
    {
        #region Constantes et identifiants shader
        // --- Propriétés shader (cachées une fois pour toutes)
        private static readonly int ColorAId = Shader.PropertyToID("_ColorA");
        private static readonly int ColorBId = Shader.PropertyToID("_ColorB");
        private static readonly int ColorCoreId = Shader.PropertyToID("_ColorCore");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int PulseId = Shader.PropertyToID("_Pulse");
        private static readonly int PopId = Shader.PropertyToID("_Pop");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly int HealTimeId = Shader.PropertyToID("_HealTime");
        private static readonly int QualityId = Shader.PropertyToID("_Quality");

        // --- Débits de base pour une zone de rayon 3 m (mis à l'échelle de la surface)
        private const float BaseCrossRate = 7f;
        private const float BaseSparkleRate = 16f;
        private const float BaseVoluteRate = 9f;
        private const float BaseSpiralRate = 30f;
        private const float ReferenceRadius = 3f;
        private const float ParticleSpawnRadiusRatio = 0.95f;
        private const float VoluteSpawnRadiusRatio = 0.80f;
        private const float SpiralSpawnRadiusRatio = 0.70f;
        private const float ColumnRadiusRatio = 0.34f;

        // Multiplicateur de particules par qualité (Low, Medium, High) ; la spirale est coupée en Low.
        private static readonly float[] QualityParticleScale = { 0.4f, 1f, 1.6f };
        #endregion

        #region Champs sérialisés
        [Header("Références (remplies par l'installeur)")]
        [SerializeField] private HealFxPalette palette;
        [SerializeField] private Renderer groundRenderer;
        [SerializeField] private ParticleSystem crossParticles;
        [SerializeField] private ParticleSystem sparkleParticles;

        [Header("Couches v2 (remplies par l'installeur, optionnelles)")]
        [SerializeField] private Renderer columnRenderer;
        [SerializeField] private ParticleSystem voluteParticles;
        [SerializeField] private ParticleSystem spiralParticles;
        [SerializeField] private Light glowLight;

        [Header("Zone")]
        [SerializeField, Min(0.5f)] private float radius = 3.4f;
        [SerializeField, Min(0f)] private float intensity = 1.2f;
        [SerializeField, Min(0f)] private float particleDensity = 1f;
        [SerializeField, Min(0.1f)] private float speed = 1f;
        [Tooltip("Décale la teinte de la palette (0 = palette telle quelle) ; sert au réglage en direct.")]
        [SerializeField, Range(0f, 1f)] private float hueShift;
        [SerializeField] private HealFxQuality quality = HealFxQuality.Medium;

        [Header("Cycle de vie")]
        [Tooltip("Lance l'apparition dès l'activation (comportement historique : la zone est visible tout de suite).")]
        [SerializeField] private bool playOnEnable = true;
        [SerializeField, Min(0.1f)] private float appearDuration = 1.1f;
        [SerializeField, Min(0.1f)] private float disperseDuration = 1.8f;
        [Tooltip("Période de la respiration en boucle (secondes).")]
        [SerializeField, Min(0.5f)] private float breathPeriod = 4f;
        [SerializeField, Range(0f, 0.4f)] private float breathDepth = 0.12f;

        [Header("Lumière")]
        [SerializeField, Min(0f)] private float lightIntensity = 3.5f;
        [SerializeField, Range(0f, 0.5f)] private float lightFlicker = 0.12f;

        [Header("Colonne")]
        [SerializeField, Min(0.5f)] private float columnHeight = 6f;

        [Header("Tick de soin")]
        [Tooltip("Démo : lance une impulsion toute seule. En jeu, laisse décoché et appelle Pulse() depuis ton système de soin.")]
        [SerializeField] private bool autoPulse;
        [SerializeField, Min(0.2f)] private float pulseInterval = 1.6f;
        [SerializeField, Min(0.1f)] private float pulseDuration = 1.1f;
        #endregion

        /// <summary>Déclenché à chaque impulsion (pour synchroniser HealTargetFx, sons, etc.).</summary>
        public event Action Pulsed;

        /// <summary>Déclenché à chaque changement de phase (apparition, boucle, fin, repos).</summary>
        public event Action<HealZonePhase> PhaseChanged;

        #region État interne
        private MaterialPropertyBlock groundBlock;
        private MaterialPropertyBlock columnBlock;
        private MaterialPropertyBlock particleBlock;

        private float pulseProgress = -1f;   // -1 = aucune onde en cours
        private float autoTimer;
        private float shaderTime;            // temps propre de l'effet : avance avec "speed", sans à-coup quand on la change
        private HealZonePhase phase = HealZonePhase.Idle;
        private float phaseTime;
        private float fade;                  // fondu global courant (0 = invisible, ~1 = plein)
        private float disperseStartFade = 1f;
        private float popProgress = -1f;     // -1 = aucune onde d'apparition
        private Color colorPrimary, colorSecondary, colorCore, colorLight;
        #endregion

        #region Propriétés publiques
        public float Radius => radius;
        public float Intensity => intensity;
        public float Speed => speed;
        public float ParticleDensity => particleDensity;
        public float HueShift => hueShift;
        public HealFxQuality Quality => quality;
        public HealZonePhase Phase => phase;
        public float AppearDuration => appearDuration;
        public float DisperseDuration => disperseDuration;
        #endregion

        #region Cycle de vie Unity
        private void Awake()
        {
            groundBlock = new MaterialPropertyBlock();
            columnBlock = new MaterialPropertyBlock();
            particleBlock = new MaterialPropertyBlock();
            autoTimer = pulseInterval * 0.25f;
            ApplyAll();
        }

        private void OnEnable()
        {
            pulseProgress = -1f;
            SetPhase(HealZonePhase.Idle);
            fade = 0f;
            ApplyAll();
            if (playOnEnable && Application.isPlaying) Play();
            else if (playOnEnable) ShowStatic();   // Édition : on voit la zone sans lancer le Play
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            shaderTime += dt * speed;

            if (autoPulse)
            {
                autoTimer -= dt;
                if (autoTimer <= 0f)
                {
                    autoTimer = pulseInterval;
                    Pulse();
                }
            }

            if (pulseProgress >= 0f)
            {
                pulseProgress += dt / pulseDuration;
                if (pulseProgress > 1f) pulseProgress = -1f;
            }

            AdvancePhase(dt);
            PushVisualState();
        }

        private void OnValidate()
        {
            // Édition dans l'Inspector : on garde l'aperçu cohérent sans attendre le Play.
            if (isActiveAndEnabled)
            {
                ApplyAll();
                if (phase == HealZonePhase.Idle && playOnEnable && !Application.isPlaying) ShowStatic();
            }
        }
        #endregion

        #region API publique
        /// <summary>À appeler à chaque tick de soin : lance l'onde et prévient les abonnés.</summary>
        public void Pulse()
        {
            pulseProgress = 0f;
            Pulsed?.Invoke();
        }

        /// <summary>Lance (ou relance) l'apparition : pop + onde de choc douce, puis boucle stable.</summary>
        public void Play()
        {
            phaseTime = 0f;
            popProgress = 0f;
            SetPhase(HealZonePhase.Appear);
        }

        /// <summary>Lance la dissipation (pas de coupure sèche) ; la zone revient au repos à la fin.</summary>
        public void Stop()
        {
            if (phase == HealZonePhase.Idle || phase == HealZonePhase.Disperse) return;
            disperseStartFade = Mathf.Max(fade, 0.01f);
            phaseTime = 0f;
            SetPhase(HealZonePhase.Disperse);
        }

        /// <summary>Coupe tout de suite (sans dissipation) : rarement utile, surtout pour réinitialiser.</summary>
        public void StopImmediate()
        {
            fade = 0f;
            popProgress = -1f;
            SetPhase(HealZonePhase.Idle);
            PushVisualState();
        }

        /// <summary>Active / coupe l'impulsion automatique (démo).</summary>
        public void SetAutoPulse(bool enabledAutoPulse)
        {
            autoPulse = enabledAutoPulse;
        }

        public void SetRadius(float newRadius)
        {
            radius = Mathf.Max(0.5f, newRadius);
            ApplyAll();
        }

        public void SetPalette(HealFxPalette newPalette)
        {
            palette = newPalette;
            ApplyAll();
        }

        public void SetIntensity(float newIntensity)
        {
            intensity = Mathf.Max(0f, newIntensity);
            ApplyAll();
        }

        /// <summary>Vitesse globale : défilement du shader et simulation des particules.</summary>
        public void SetSpeed(float newSpeed)
        {
            speed = Mathf.Max(0.1f, newSpeed);
            ApplyAll();
        }

        public void SetParticleDensity(float newDensity)
        {
            particleDensity = Mathf.Max(0f, newDensity);
            ApplyAll();
        }

        public void SetHueShift(float newHueShift)
        {
            hueShift = Mathf.Repeat(newHueShift, 1f);
            ApplyAll();
        }

        public void SetQuality(HealFxQuality newQuality)
        {
            quality = newQuality;
            ApplyAll();
        }

        /// <summary>Utilisé par l'installeur Editor pour câbler le prefab.</summary>
        public void SetReferences(HealFxPalette zonePalette, Renderer ground, ParticleSystem crosses, ParticleSystem sparkles)
        {
            palette = zonePalette;
            groundRenderer = ground;
            crossParticles = crosses;
            sparkleParticles = sparkles;
        }

        /// <summary>Utilisé par l'installeur Editor : câble les couches v2 (colonne, volutes, spirale, lumière).</summary>
        public void SetLayerReferences(Renderer column, ParticleSystem volutes, ParticleSystem spiral, Light light)
        {
            columnRenderer = column;
            voluteParticles = volutes;
            spiralParticles = spiral;
            glowLight = light;
        }
        #endregion

        #region Phases
        private void SetPhase(HealZonePhase newPhase)
        {
            if (phase == newPhase) return;
            phase = newPhase;
            PhaseChanged?.Invoke(newPhase);
        }

        private void AdvancePhase(float dt)
        {
            switch (phase)
            {
                case HealZonePhase.Appear:
                {
                    phaseTime += dt;
                    float p = Mathf.Clamp01(phaseTime / appearDuration);
                    popProgress = p;
                    // montée douce avec léger dépassement : le "pop"
                    fade = Mathf.SmoothStep(0f, 1f, p) * (1f + 0.25f * Mathf.Sin(p * Mathf.PI));
                    if (p >= 1f)
                    {
                        popProgress = -1f;
                        phaseTime = 0f;
                        SetPhase(HealZonePhase.Loop);
                    }
                    break;
                }
                case HealZonePhase.Loop:
                {
                    phaseTime += dt;
                    // respiration lente autour de 1
                    fade = 1f + breathDepth * Mathf.Sin(phaseTime / breathPeriod * Mathf.PI * 2f);
                    break;
                }
                case HealZonePhase.Disperse:
                {
                    phaseTime += dt;
                    float p = Mathf.Clamp01(phaseTime / disperseDuration);
                    fade = disperseStartFade * (1f - Mathf.SmoothStep(0f, 1f, p));
                    if (p >= 1f)
                    {
                        fade = 0f;
                        SetPhase(HealZonePhase.Idle);
                    }
                    break;
                }
            }
        }

        // Aperçu en édition : zone pleine, sans animation de phase.
        private void ShowStatic()
        {
            fade = 1f;
            PushVisualState();
        }
        #endregion

        #region Application des réglages
        private void ApplyAll()
        {
            if (groundBlock == null) groundBlock = new MaterialPropertyBlock();
            if (columnBlock == null) columnBlock = new MaterialPropertyBlock();
            if (particleBlock == null) particleBlock = new MaterialPropertyBlock();

            ResolveColors();

            if (groundRenderer != null)
                groundRenderer.transform.localScale = new Vector3(radius * 2f, 1f, radius * 2f);

            if (columnRenderer != null)
            {
                float columnRadius = radius * ColumnRadiusRatio;
                columnRenderer.transform.localScale = new Vector3(columnRadius, columnHeight, columnRadius);
            }

            if (glowLight != null) glowLight.range = radius * 1.8f;

            ConfigureParticles(crossParticles, colorPrimary, colorPrimary, ParticleSpawnRadiusRatio);
            ConfigureParticles(sparkleParticles, colorSecondary, colorCore, ParticleSpawnRadiusRatio);
            ConfigureParticles(voluteParticles, colorPrimary, colorCore, VoluteSpawnRadiusRatio);
            ConfigureParticles(spiralParticles, colorSecondary, colorPrimary, SpiralSpawnRadiusRatio);
            PushVisualState();
        }

        private void ResolveColors()
        {
            Color primary = palette != null ? palette.Primary : Color.white;
            Color secondary = palette != null ? palette.Secondary : Color.white;
            Color core = palette != null ? palette.Core : new Color(2.2f, 1.9f, 1.1f, 1f);
            Color lightColor = palette != null ? palette.Light : Color.white;

            colorPrimary = ShiftHue(primary);
            colorSecondary = ShiftHue(secondary);
            colorCore = ShiftHue(core);
            colorLight = ShiftHue(lightColor);
        }

        private Color ShiftHue(Color color)
        {
            if (hueShift <= 0.0001f) return color;
            Color.RGBToHSV(color, out float h, out float s, out float v);
            Color shifted = Color.HSVToRGB(Mathf.Repeat(h + hueShift, 1f), s, v, true);
            shifted.a = color.a;
            return shifted;
        }

        // Appelé à chaque frame : pousse le fondu, le temps et les débits de particules (aucune allocation).
        private void PushVisualState()
        {
            float visible = Mathf.Max(fade, 0f);
            bool lowQuality = quality == HealFxQuality.Low;

            if (groundRenderer != null && groundBlock != null)
            {
                groundRenderer.enabled = visible > 0.001f;
                groundRenderer.GetPropertyBlock(groundBlock);
                groundBlock.SetColor(ColorAId, colorPrimary);
                groundBlock.SetColor(ColorBId, colorSecondary);
                groundBlock.SetColor(ColorCoreId, colorCore);
                groundBlock.SetFloat(IntensityId, intensity);
                groundBlock.SetFloat(PulseId, pulseProgress);
                groundBlock.SetFloat(PopId, popProgress);
                groundBlock.SetFloat(FadeId, visible);
                groundBlock.SetFloat(HealTimeId, shaderTime);
                groundBlock.SetFloat(QualityId, (float)quality);
                groundRenderer.SetPropertyBlock(groundBlock);
            }

            if (columnRenderer != null && columnBlock != null)
            {
                columnRenderer.enabled = visible > 0.001f;
                columnRenderer.GetPropertyBlock(columnBlock);
                columnBlock.SetColor(ColorAId, colorPrimary);
                columnBlock.SetColor(ColorCoreId, colorCore);
                columnBlock.SetFloat(IntensityId, intensity * 0.55f);
                columnBlock.SetFloat(FadeId, visible);
                columnBlock.SetFloat(HealTimeId, shaderTime);
                columnRenderer.SetPropertyBlock(columnBlock);
            }

            if (glowLight != null)
            {
                glowLight.enabled = visible > 0.01f;
                float flicker = 1f + (Mathf.PerlinNoise(Time.time * 3.1f, 7.3f) - 0.5f) * 2f * lightFlicker;
                glowLight.color = colorLight;
                glowLight.intensity = lightIntensity * visible * flicker;
            }

            float scale = QualityParticleScale[(int)quality] * particleDensity * Mathf.Clamp01(visible);
            float area = (radius / ReferenceRadius) * (radius / ReferenceRadius);
            SetEmission(crossParticles, BaseCrossRate * area * scale);
            SetEmission(sparkleParticles, BaseSparkleRate * area * scale);
            SetEmission(voluteParticles, BaseVoluteRate * area * scale);
            SetEmission(spiralParticles, lowQuality ? 0f : BaseSpiralRate * area * scale);
        }

        private static void SetEmission(ParticleSystem system, float rate)
        {
            if (system == null) return;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTimeMultiplier = rate;
        }

        private void ConfigureParticles(ParticleSystem system, Color colorFrom, Color colorTo, float spawnRadiusRatio)
        {
            if (system == null) return;

            ParticleSystem.MainModule main = system.main;
            main.startColor = new ParticleSystem.MinMaxGradient(colorFrom, colorTo);
            main.simulationSpeed = speed;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.radius = radius * spawnRadiusRatio;

            var particleRenderer = system.GetComponent<ParticleSystemRenderer>();
            if (particleRenderer != null)
            {
                particleRenderer.GetPropertyBlock(particleBlock);
                particleBlock.SetFloat(IntensityId, intensity);
                particleRenderer.SetPropertyBlock(particleBlock);
            }
        }
        #endregion
    }
}
