using System;
using UnityEngine;

namespace BurnFx
{
    /// <summary>
    /// FX de l'état "brûle" posé sur une cible (ennemi ou allié) : flammes qui lèchent le corps, flammèches fines, fumée sombre,
    /// braises qui s'envolent, lueur orange qui vacille, et carbonisation progressive de la cible.
    /// Réutilise les matériaux du kit Fire (flipbooks de flammes et de fumée).
    ///
    /// À instancier en enfant de l'unité (pivot aux pieds). Le gameplay appelle <see cref="BeginBurn()"/> quand l'état est appliqué
    /// et <see cref="EndBurn"/> quand il se termine ; ou <see cref="BeginBurn(SOEtat)"/> pour laisser le FX s'arrêter tout seul après la durée de l'état.
    /// La fin est une dissipation douce (les flammes s'éteignent, la fumée finit de monter, la couleur revient).
    ///
    /// BurnTarget (prefab)
    /// ├── Flames      ParticleSystem (flipbook de flammes, émis dans le volume du corps)
    /// ├── FlamesFine  ParticleSystem (flammèches plus petites et plus vives)
    /// ├── Smoke       ParticleSystem (fumée éclairée par le feu, émise vers le haut du corps)
    /// ├── Embers      ParticleSystem (braises)
    /// └── GlowLight   Light (point light orange, vacillante)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BurnTargetFx : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        // Part d'émission de la fumée prise sur le haut du corps.
        private const float SmokeTopRatio = 0.9f;
        private const float FireLightRange = 5f;
        // La boîte d émission des flammes dépasse un peu le corps en largeur.
        private const float ShellScale = 1.15f;

        [Header("Références (remplies par l'installeur)")]
        [SerializeField] private ParticleSystem flames;
        [SerializeField] private ParticleSystem fineFlames;
        [SerializeField] private ParticleSystem smoke;
        [SerializeField] private ParticleSystem embers;
        [SerializeField] private Light glowLight;

        [Header("Intensité")]
        [Tooltip("Multiplicateur global du nombre de particules.")]
        [SerializeField, Min(0f)] private float intensity = 1f;
        [SerializeField, Min(0f)] private float flameRate = 56f;
        [SerializeField, Min(0f)] private float fineFlameRate = 34f;
        [SerializeField, Min(0f)] private float smokeRate = 9f;
        [SerializeField, Min(0f)] private float emberRate = 16f;

        [Header("Cycle")]
        [SerializeField, Min(0.05f)] private float fadeInTime = 0.6f;
        [SerializeField, Min(0.05f)] private float fadeOutTime = 1.4f;

        [Header("Lumière")]
        [SerializeField, Min(0f)] private float lightIntensity = 3.2f;
        [SerializeField, Range(0f, 0.6f)] private float lightFlicker = 0.28f;
        [SerializeField] private Color lightColor = new Color(1f, 0.52f, 0.16f, 1f);

        [Header("Adaptation au corps")]
        [Tooltip("Mesure le corps de la cible au démarrage et y cale l'émission des flammes, de la fumée et de la lumière.")]
        [SerializeField] private bool autoFit = true;
        [Tooltip("Part du volume du corps couverte par les flammes.")]
        [SerializeField, Range(0.3f, 1.2f)] private float bodyCoverage = 0.85f;

        [Header("Carbonisation")]
        [Tooltip("Assombrissement de la cible quand elle brûle (0 = aucun). Ne touche qu'aux matériaux qui ont _BaseColor ou _Color.")]
        [SerializeField, Range(0f, 1f)] private float charring = 0.4f;

        /// <summary>Déclenché quand l'état commence (true) ou se termine (false).</summary>
        public event Action<bool> BurningChanged;

        private bool burning;
        private float burnFade;            // 0 = éteint, 1 = pleine intensité
        private float timer;               // durée restante d'un état chronométré
        private bool timed;
        private float appliedCharring = -1f;

        private Renderer[] bodyRenderers;
        private Color[] originalColors;
        private int[] colorIds;
        private MaterialPropertyBlock charBlock;

        public bool IsBurning => burning;

        #region Cycle de vie Unity
        private void Awake()
        {
            charBlock = new MaterialPropertyBlock();
            if (charring > 0f) CacheBody();
            if (autoFit) FitToTarget();
            PushState();
        }

        private void OnDisable()
        {
            RestoreBody();
            appliedCharring = -1f;
        }

        private void Update()
        {
            if (timed)
            {
                timer -= Time.deltaTime;
                if (timer <= 0f)
                {
                    timed = false;
                    EndBurn();
                }
            }

            float target = burning ? 1f : 0f;
            float duration = burning ? fadeInTime : fadeOutTime;
            burnFade = Mathf.MoveTowards(burnFade, target, Time.deltaTime / duration);
            PushState();
        }
        #endregion

        #region API publique
        /// <summary>Début de l'état : les flammes montent progressivement. Reste allumé jusqu'à <see cref="EndBurn"/>.</summary>
        public void BeginBurn()
        {
            timed = false;
            SetBurning(true);
        }

        /// <summary>Début de l'état pour une durée donnée (secondes) : s'éteint tout seul ensuite. 0 ou moins = jusqu'à EndBurn.</summary>
        public void BeginBurn(float duration)
        {
            timed = duration > 0f;
            timer = duration;
            SetBurning(true);
        }

        /// <summary>Début de l'état décrit par un SOEtat : la durée de l'état pilote la durée du FX.</summary>
        public void BeginBurn(SOEtat state)
        {
            BeginBurn(state != null ? state.Duration : 0f);
        }

        /// <summary>Fin de l'état : extinction progressive.</summary>
        public void EndBurn()
        {
            timed = false;
            SetBurning(false);
        }

        public void SetIntensity(float newIntensity)
        {
            intensity = Mathf.Max(0f, newIntensity);
        }

        /// <summary>Mesure à nouveau le corps de la cible et recale les émissions (à appeler si la cible change de taille).</summary>
        public void FitToTarget()
        {
            Bounds bounds = MeasureBody(out bool found);
            if (!found) return;

            Vector3 center = transform.InverseTransformPoint(bounds.center);
            Vector3 size = bounds.size * bodyCoverage;

            // Flammes et braises naissent sur l'enveloppe d'une boîte un peu plus large que le corps : elles l'enveloppent
            // au lieu d'être cachées à l'intérieur (et ça marche pour n'importe quel mesh, lisible ou non).
            Vector3 shellSize = new Vector3(size.x * ShellScale, size.y, size.z * ShellScale);
            PlaceShell(flames, center, shellSize);
            PlaceShell(fineFlames, center, shellSize);
            PlaceShell(embers, center, shellSize);

            // La fumée part du haut du corps.
            float topY = center.y + bounds.extents.y * SmokeTopRatio;
            PlaceBox(smoke, new Vector3(center.x, topY, center.z), new Vector3(size.x * 0.6f, 0.15f, size.z * 0.6f));

            if (glowLight != null) glowLight.transform.localPosition = center;
        }

        /// <summary>Utilisé par l'installeur Editor pour câbler le prefab.</summary>
        public void SetReferences(ParticleSystem flameSystem, ParticleSystem fineFlameSystem, ParticleSystem smokeSystem,
            ParticleSystem emberSystem, Light light)
        {
            flames = flameSystem;
            fineFlames = fineFlameSystem;
            smoke = smokeSystem;
            embers = emberSystem;
            glowLight = light;
        }
        #endregion

        // Émission sur l enveloppe d une boîte (BoxShell) : le volume intérieur reste libre, les flammes naissent autour du corps.
        private static void PlaceShell(ParticleSystem system, Vector3 localCenter, Vector3 localSize)
        {
            if (system == null) return;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.BoxShell;
            shape.position = localCenter;
            shape.scale = localSize;
        }

        #region Application de l'état
        private void SetBurning(bool value)
        {
            if (burning == value) return;
            burning = value;
            BurningChanged?.Invoke(burning);
        }

        // Appelé à chaque frame : aucune allocation (blocks et tableaux réutilisés).
        private void PushState()
        {
            float scale = intensity * burnFade;
            SetRate(flames, flameRate * scale);
            SetRate(fineFlames, fineFlameRate * scale);
            SetRate(smoke, smokeRate * scale);
            SetRate(embers, emberRate * scale);

            if (glowLight != null)
            {
                bool lit = burnFade > 0.01f;
                glowLight.enabled = lit;
                if (lit)
                {
                    // Vacillement : deux bruits à des fréquences différentes pour casser la régularité.
                    float noise = Mathf.PerlinNoise(Time.time * 7.3f, 1.7f) * 0.6f + Mathf.PerlinNoise(Time.time * 2.1f, 9.4f) * 0.4f;
                    float flicker = 1f + (noise - 0.5f) * 2f * lightFlicker;
                    glowLight.color = lightColor;
                    glowLight.intensity = lightIntensity * burnFade * flicker;
                    FireVFX.FireLights.Report(glowLight.transform.position, lightColor, glowLight.intensity, FireLightRange);
                }
            }

            ApplyCharring(burnFade * charring);
        }

        private static void SetRate(ParticleSystem system, float rate)
        {
            if (system == null) return;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTimeMultiplier = rate;
        }

        private static void PlaceBox(ParticleSystem system, Vector3 localCenter, Vector3 localSize)
        {
            if (system == null) return;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = localCenter;
            shape.scale = localSize;
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

        // Mémorise une fois les renderers de la cible et leur couleur d'origine (pour la carbonisation).
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

        private void ApplyCharring(float amount)
        {
            if (bodyRenderers == null || Mathf.Abs(amount - appliedCharring) < 0.003f) return;
            appliedCharring = amount;

            // 1 -> 0.3 : on assombrit sans jamais passer au noir complet.
            float factor = Mathf.Lerp(1f, 0.3f, amount);
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                if (bodyRenderers[i] == null || colorIds[i] < 0) continue;
                bodyRenderers[i].GetPropertyBlock(charBlock);
                Color original = originalColors[i];
                charBlock.SetColor(colorIds[i], new Color(original.r * factor, original.g * factor, original.b * factor, original.a));
                bodyRenderers[i].SetPropertyBlock(charBlock);
            }
        }

        private void RestoreBody()
        {
            if (bodyRenderers == null) return;
            for (int i = 0; i < bodyRenderers.Length; i++)
                if (bodyRenderers[i] != null && colorIds[i] >= 0) bodyRenderers[i].SetPropertyBlock(null);
        }
        #endregion
    }
}
