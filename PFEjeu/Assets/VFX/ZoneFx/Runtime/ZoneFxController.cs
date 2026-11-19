using System;
using UnityEngine;

namespace ZoneFx
{
    /// <summary>Comment l'anneau est dessiné sur le sol.</summary>
    public enum ZoneRingMode
    {
        Flat = 0,       // quad horizontal juste au-dessus du sol : simple, ne suit pas le relief
        Projected = 1   // volume qui se projette sur le décor : épouse le relief (besoin de la Depth Texture URP)
    }

    /// <summary>Références des enfants du prefab (remplies par l'installateur).</summary>
    [Serializable]
    public struct ZoneFxBindings
    {
        public Renderer ring;
        public Transform ringTransform;
        public Renderer veil;
        public Transform veilTransform;
        public ParticleSystem sparks;
        public ParticleSystem motes;
    }

    /// <summary>
    /// Zone lumineuse au sol (heal, buff, portée...) : anneau + traînées de lumière + étincelles + poussières.
    ///
    /// ZoneFx_Flat / ZoneFx_Projected (prefab racine, ce script)
    /// ├── Ring     (quad ou cube + matériau d'anneau)
    /// ├── Sparks   (ParticleSystem)
    /// └── Motes    (ParticleSystem)
    ///
    /// API : SetRadius, SetPreset, SetColor, Show, Hide, Pulse (onde de "tick"). Place le prefab en enfant de la tour (pivot au sol).
    /// Le script se désactive quand tout est stable : coût nul au repos (l'anneau s'anime seul dans le shader).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ZoneFxController : MonoBehaviour
    {
        #region Réglages
        [Header("Zone")]
        [SerializeField, Min(0.5f)] private float radius = 5f;
        [SerializeField] private ZoneFxPreset preset = ZoneFxPreset.Green;
        [SerializeField] private bool useCustomColor;
        [SerializeField, ColorUsage(false)] private Color customColor = new Color(0.4f, 1f, 0.25f);
        [SerializeField] private ZoneRingMode mode = ZoneRingMode.Flat;

        [Header("Apparence")]
        [SerializeField, Range(0f, 2f)] private float intensity = 1f;
        [SerializeField, Min(0.5f)] private float hdrIntensity = 2.4f;     // > 1 : alimente le bloom
        [SerializeField, Range(0f, 2f)] private float veilIntensity = 1f;  // voile de lumière au-dessus de l'anneau (0 = aucun)
        [SerializeField, Min(0.1f)] private float veilHeightPerMeter = 0.6f;
        [SerializeField, Min(0.1f)] private float veilMinHeight = 1.2f;
        [SerializeField, Min(0.1f)] private float veilMaxHeight = 3.5f;
        [SerializeField, Min(0.05f)] private float fadeDuration = 0.5f;
        [SerializeField] private bool showOnStart = true;

        [Header("Tick de soin (Pulse)")]
        [SerializeField, Min(0f)] private float burstSparksPerMeter = 6f;

        [Header("Placement")]
        [SerializeField] private float groundOffset = 0.05f;               // Flat : hauteur au-dessus du sol (évite le z-fighting)
        [SerializeField, Min(0.5f)] private float projectionHeight = 6f;   // Projected : hauteur du volume
        [SerializeField, Min(0f)] private float projectionBelow = 2f;      // Projected : profondeur sous le pivot (terrain en pente)

        [Header("Références (remplies par l'installateur)")]
        [SerializeField] private ZoneFxBindings bindings;
        #endregion

        #region Constantes
        private const float RingHaloMargin = 1.25f;      // demi-côté du carré = rayon x marge (le shader dessine l'anneau à 0,8)
        private const float VisibleEpsilon = 0.001f;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
        private static readonly int VisibleId = Shader.PropertyToID("_Visible");
        private const float VeilHdrFactor = 0.3f;       // le voile est large : moins d'HDR que l'anneau
        private static readonly int DashCountId = Shader.PropertyToID("_DashCount");
        private static readonly int DashRatioId = Shader.PropertyToID("_DashRatio");
        private static readonly int DashSpeedId = Shader.PropertyToID("_DashSpeed");
        #endregion

        #region État
        private MaterialPropertyBlock block;
        private bool shown;
        private float visibility;          // 0..1 (fondu)
        private bool initialized;
        #endregion

        #region API publique
        public float Radius => radius;
        public bool IsShown => shown;
        public ZoneFxPreset Preset => preset;

        /// <summary>Change le rayon de la zone (en mètres, monde).</summary>
        public void SetRadius(float meters)
        {
            radius = Mathf.Max(0.5f, meters);
            Refresh();
        }

        public void SetPreset(ZoneFxPreset value)
        {
            preset = value;
            useCustomColor = false;
            Refresh();
        }

        /// <summary>Couleur libre (garde les tirets / le style du preset courant).</summary>
        public void SetColor(Color color)
        {
            customColor = color;
            useCustomColor = true;
            Refresh();
        }

        public void SetIntensity(float value)
        {
            intensity = Mathf.Clamp(value, 0f, 2f);
            Refresh();
        }

        public void SetMode(ZoneRingMode value)
        {
            mode = value;
            Refresh();
        }

        /// <summary>Apparition (fondu, ou immédiate si instant).</summary>
        public void Show(bool instant = false)
        {
            shown = true;
            if (instant) visibility = 1f;
            Wake();
        }

        /// <summary>Disparition (fondu, ou immédiate si instant).</summary>
        public void Hide(bool instant = false)
        {
            shown = false;
            if (instant) visibility = 0f;
            Wake();
        }

        public void SetVisible(bool visible, bool instant = false)
        {
            if (visible) Show(instant); else Hide(instant);
        }

        /// <summary>Rafale d'étincelles : à appeler à chaque tick de soin.</summary>
        public void Pulse()
        {
            if (!shown) return;
            EnsureInitialized();
            if (bindings.sparks != null)
                bindings.sparks.Emit(Mathf.RoundToInt(burstSparksPerMeter * radius * intensity));
        }

        /// <summary>Réapplique tous les réglages (utile dans l'éditeur après modification).</summary>
        public void Refresh()
        {
            EnsureInitialized();
            ApplyAll();
            Wake();
        }
        #endregion

        #region Cycle de vie Unity
        private void Awake()
        {
            EnsureInitialized();
        }

        private void Start()
        {
            // Start (et non Awake) : les autres scripts ont fini de configurer le rayon / le preset.
            visibility = showOnStart ? 1f : 0f;
            shown = showOnStart;
            ApplyAll();
            Wake();
        }

        private void OnEnable()
        {
            if (initialized) Wake();
        }

        private void OnDisable()
        {
            // Le script se met lui-même en veille (enabled = false) quand tout est stable : ça ne doit PAS couper les particules.
            // On ne les arrête que si l'objet est réellement désactivé.
            if (!gameObject.activeInHierarchy) StopParticles();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            visibility = Mathf.MoveTowards(visibility, shown ? 1f : 0f, dt / fadeDuration);

            ApplyDynamic();

            // Tout est stable (visible à fond / masqué) : plus besoin de tourner.
            bool settled = shown ? visibility >= 1f : visibility <= 0f;
            if (settled)
            {
                if (!shown) StopParticles();
                enabled = false;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Aperçu en édition (prefab ouvert / scène) : on ne lance pas les particules, on met juste à jour la forme et la couleur.
            if (Application.isPlaying) return;
            visibility = showOnStart ? 1f : 0f;
            shown = showOnStart;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                EnsureInitialized();
                ApplyAll();
            };
        }
#endif
        #endregion

        #region Application des réglages
        private void EnsureInitialized()
        {
            if (initialized && block != null) return;
            block = new MaterialPropertyBlock();
            initialized = true;
        }

        private void Wake()
        {
            if (!isActiveAndEnabled || !Application.isPlaying) return;
            enabled = true;     // Update() se recoupe tout seul quand tout est stable
        }

        /// <summary>Forme, taille, couleur : tout ce qui ne change pas à chaque image.</summary>
        private void ApplyAll()
        {
            ApplyRingGeometry();
            ApplyColors();
            ApplyDynamic();
        }

        private void ApplyVeilGeometry()
        {
            Transform veilTransform = bindings.veilTransform;
            if (veilTransform == null) return;

            float height = Mathf.Clamp(radius * veilHeightPerMeter, veilMinHeight, veilMaxHeight);
            veilTransform.localRotation = Quaternion.identity;
            veilTransform.localPosition = new Vector3(0f, mode == ZoneRingMode.Flat ? groundOffset : 0f, 0f);
            veilTransform.localScale = new Vector3(radius, height, radius);     // maillage unitaire : rayon 1, hauteur 1
        }

        private void ApplyRingGeometry()
        {
            ApplyVeilGeometry();
            Transform ringTransform = bindings.ringTransform;
            if (ringTransform == null) return;

            float size = radius * RingHaloMargin * 2f;     // côté du carré
            if (mode == ZoneRingMode.Flat)
            {
                ringTransform.localRotation = Quaternion.Euler(90f, 0f, 0f);       // quad couché, face vers le haut
                ringTransform.localPosition = new Vector3(0f, groundOffset, 0f);
                ringTransform.localScale = new Vector3(size, size, 1f);
            }
            else
            {
                ringTransform.localRotation = Quaternion.identity;
                ringTransform.localPosition = new Vector3(0f, projectionHeight * 0.5f - projectionBelow, 0f);
                ringTransform.localScale = new Vector3(size, projectionHeight, size);
            }
        }

        private void ApplyColors()
        {
            ZonePresetData data = ZoneFxPresets.Get(preset);
            Color baseColor = useCustomColor ? customColor : data.Color;
            Color coreColor = useCustomColor ? Color.Lerp(customColor, Color.white, 0.55f) : data.Core;

            if (bindings.ring != null)
            {
                bindings.ring.GetPropertyBlock(block);
                block.SetColor(ColorId, ToHdr(baseColor));
                block.SetColor(CoreColorId, ToHdr(coreColor));
                block.SetFloat(DashCountId, data.DashCount);
                block.SetFloat(DashRatioId, data.DashRatio);
                block.SetFloat(DashSpeedId, data.DashSpeed);
                bindings.ring.SetPropertyBlock(block);
            }

            if (bindings.veil != null)
            {
                bindings.veil.GetPropertyBlock(block);
                block.SetColor(ColorId, ToHdr(baseColor, VeilHdrFactor));
                block.SetColor(CoreColorId, ToHdr(coreColor, VeilHdrFactor));
                bindings.veil.SetPropertyBlock(block);
            }

            Color tint = new Color(baseColor.r, baseColor.g, baseColor.b, 1f);
            Color coreTint = new Color(coreColor.r, coreColor.g, coreColor.b, 1f);
            if (bindings.sparks != null) ZoneParticleSetup.SetTint(bindings.sparks, coreTint);
            if (bindings.motes != null) ZoneParticleSetup.SetTint(bindings.motes, tint);
        }

        /// <summary>Ce qui bouge pendant un fondu / une onde : visibilité, onde, débits.</summary>
        private void ApplyDynamic()
        {
            bool visible = visibility > VisibleEpsilon;

            if (bindings.ring != null)
            {
                bindings.ring.enabled = visible;
                if (visible)
                {
                    bindings.ring.GetPropertyBlock(block);
                    block.SetFloat(VisibleId, visibility * intensity);
                    bindings.ring.SetPropertyBlock(block);
                }
            }

            if (bindings.veil != null)
            {
                bindings.veil.enabled = visible && veilIntensity > 0f;
                if (bindings.veil.enabled)
                {
                    bindings.veil.GetPropertyBlock(block);
                    block.SetFloat(VisibleId, visibility * intensity * veilIntensity);
                    bindings.veil.SetPropertyBlock(block);
                }
            }

            float amount = visibility * intensity;
            ApplyParticles(bindings.sparks, ZoneParticleKind.Sparks, visible, amount);
            ApplyParticles(bindings.motes, ZoneParticleKind.Motes, visible, amount);
        }

        private void ApplyParticles(ParticleSystem system, ZoneParticleKind kind, bool visible, float amount)
        {
            if (system == null) return;
            ZoneParticleSetup.Apply(system, kind, radius, amount);
            if (!Application.isPlaying) return;
            if (visible && !system.isPlaying) system.Play();
        }

        private void StopParticles()
        {
            StopSystem(bindings.sparks);
            StopSystem(bindings.motes);
        }

        private static void StopSystem(ParticleSystem system)
        {
            if (system != null && system.isPlaying) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private Color ToHdr(Color color, float factor = 1f)
        {
            float k = hdrIntensity * factor;
            return new Color(color.r * k, color.g * k, color.b * k, 1f);
        }
        #endregion
    }
}
