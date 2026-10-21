using UnityEngine;

namespace ProfessionalTPS
{
    /// <summary>
    /// Animation visuelle du projectile magique (apparition, spirales,
    /// pulsation de la tête, ondulation de la traîne, étincelles).
    ///
    /// Le déplacement et les dégâts restent gérés par CombatProjectile :
    /// ce composant ne fait que de l'habillage.
    /// Les pièces sont retrouvées par leur nom (voir MagicProjectileSetup).
    /// </summary>
    public sealed class MagicProjectileVisual :
        MonoBehaviour
    {
        [Header("Références")]

        [Tooltip("Racine du modèle. Vide = enfant 'Visual'.")]
        [SerializeField]
        private Transform visualRoot;

        [Tooltip("Axe de la traîne (de la tête vers l'arrière) dans le repère de 'Visual'.")]
        [SerializeField]
        private Vector3 trailAxis =
            Vector3.back;


        [Header("Apparition")]

        [SerializeField, Range(0.01f, 1f)]
        private float spawnDuration = 0.15f;

        [SerializeField]
        private AnimationCurve spawnCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);


        [Header("Spirales")]

        [Tooltip("Vitesse de rotation (°/s).")]
        [SerializeField, Range(0f, 1440f)]
        private float spinSpeed = 360f;

        [SerializeField, Range(-2f, 2f)]
        private float spiral2Factor = -0.85f;

        [SerializeField, Range(-2f, 2f)]
        private float spiral3Factor = 1.15f;


        [Header("Pulsation")]

        [SerializeField, Range(0f, 30f)]
        private float pulseSpeed = 9f;

        [SerializeField, Range(0f, 0.5f)]
        private float pulseAmount = 0.12f;

        [Tooltip("Multiplicateur de fréquence du noyau.")]
        [SerializeField, Range(0.2f, 4f)]
        private float coreFrequencyFactor = 1.7f;


        [Header("Traîne")]

        [SerializeField, Range(0f, 30f)]
        private float waveSpeed = 12f;

        [SerializeField, Range(0f, 0.5f)]
        private float waveAmount = 0.12f;

        [Tooltip("Étirement maximal de la traîne à vitesse de référence.")]
        [SerializeField, Range(0f, 1f)]
        private float stretchAmount = 0.25f;

        [SerializeField, Min(1f)]
        private float referenceSpeed = 30f;


        [Header("Étincelles")]

        [SerializeField, Min(0f)]
        private float sparkSpeed = 3f;

        [Tooltip("Distance (repère de 'Visual') avant retour vers la tête.")]
        [SerializeField, Min(0.1f)]
        private float trailLength = 2.4f;

        [SerializeField, Min(0f)]
        private float sparkSpread = 0.12f;

        [SerializeField, Range(0.1f, 3f)]
        private float sparkScaleMin = 0.5f;

        [SerializeField, Range(0.1f, 3f)]
        private float sparkScaleMax = 1.5f;


        private Transform _core;
        private Transform _shell;
        private Transform _glow;
        private Transform _spiral1;
        private Transform _spiral2;
        private Transform _spiral3;
        private Transform _cone;
        private Transform _inner;
        private Transform[] _sparks;

        private Vector3 _rootBaseScale;
        private Vector3 _coreScale;
        private Vector3 _shellScale;
        private Vector3 _glowScale;
        private Vector3 _coneScale;
        private Vector3 _innerScale;
        private Vector3[] _sparkScales;

        private float _age;
        private float _phase;
        private float _speed;
        private Vector3 _lastPosition;


        private void Awake()
        {
            if (visualRoot == null)
                visualRoot = transform.Find("Visual");

            if (visualRoot == null)
            {
                Debug.LogWarning(
                    "[MagicProjectileVisual] Enfant 'Visual' introuvable.",
                    this
                );

                enabled = false;
                return;
            }

            trailAxis = trailAxis.normalized;

            _rootBaseScale = visualRoot.localScale;
            _phase = Random.value * Mathf.PI * 2f;

            _core = Find("orb_core");
            _shell = Find("orb_shell");
            _glow = Find("orb_glow");
            _spiral1 = Find("spiral_1");
            _spiral2 = Find("spiral_2");
            _spiral3 = Find("spiral_3");
            _cone = Find("tail_cone");
            _inner = Find("tail_inner");

            _coreScale = BaseScale(_core);
            _shellScale = BaseScale(_shell);
            _glowScale = BaseScale(_glow);
            _coneScale = BaseScale(_cone);
            _innerScale = BaseScale(_inner);

            _sparks = new Transform[18];
            _sparkScales = new Vector3[18];

            for (int i = 0; i < _sparks.Length; i++)
            {
                _sparks[i] = Find("spark_" + (i + 1));
                _sparkScales[i] = BaseScale(_sparks[i]);
            }
        }


        private void OnEnable()
        {
            _age = 0f;
            _lastPosition = transform.position;

            if (visualRoot != null)
                visualRoot.localScale = Vector3.zero;
        }


        private void Update()
        {
            float dt = Time.deltaTime;

            if (dt <= 0f)
                return;

            _age += dt;

            // Vitesse réelle, lissée (la traîne s'étire avec).
            float instant =
                (transform.position - _lastPosition).magnitude / dt;

            _speed = Mathf.Lerp(_speed, instant, 10f * dt);
            _lastPosition = transform.position;

            float t = _age + _phase;

            AnimateSpawn();
            AnimateSpirals(dt);
            AnimateHead(t);
            AnimateTail(t);
            AnimateSparks(dt);
        }


        // Apparition : 0 -> 1 sur spawnDuration.
        private void AnimateSpawn()
        {
            float k =
                spawnCurve.Evaluate(
                    Mathf.Clamp01(_age / spawnDuration)
                );

            visualRoot.localScale = _rootBaseScale * k;
        }


        // Spirales : la 2 tourne en sens inverse, vitesses légèrement différentes.
        private void AnimateSpirals(float dt)
        {
            Spin(_spiral1, spinSpeed);
            Spin(_spiral2, spinSpeed * spiral2Factor);
            Spin(_spiral3, spinSpeed * spiral3Factor);

            void Spin(Transform part, float speed)
            {
                if (part == null)
                    return;

                part.localRotation =
                    Quaternion.AngleAxis(speed * dt, trailAxis) *
                    part.localRotation;
            }
        }


        // Pulsation : coque et halo ensemble, noyau à une autre fréquence.
        private void AnimateHead(float t)
        {
            float wave = Mathf.Sin(t * pulseSpeed);
            float coreWave =
                Mathf.Sin(t * pulseSpeed * coreFrequencyFactor);

            if (_shell != null)
                _shell.localScale =
                    _shellScale * (1f + wave * pulseAmount);

            if (_glow != null)
                _glow.localScale =
                    _glowScale * (1f + wave * pulseAmount * 1.5f);

            if (_core != null)
                _core.localScale =
                    _coreScale * (1f + coreWave * pulseAmount);
        }


        // Traîne : ondulation du rayon (décalée entre les cônes) + étirement.
        private void AnimateTail(float t)
        {
            float stretch =
                1f + stretchAmount *
                Mathf.Clamp01(_speed / referenceSpeed);

            Wave(_cone, _coneScale, t * waveSpeed);
            Wave(_inner, _innerScale, t * waveSpeed + 1.7f);

            void Wave(Transform part, Vector3 baseScale, float angle)
            {
                if (part == null)
                    return;

                float radial = 1f + Mathf.Sin(angle) * waveAmount;
                float radial2 = 1f + Mathf.Cos(angle) * waveAmount;

                part.localScale =
                    ScaleOnAxes(baseScale, radial, radial2, stretch);
            }
        }


        // Étincelles : dérivent vers l'arrière puis reviennent près de la tête.
        private void AnimateSparks(float dt)
        {
            if (_core == null)
                return;

            Vector3 head = _core.localPosition;

            for (int i = 0; i < _sparks.Length; i++)
            {
                Transform spark = _sparks[i];

                if (spark == null)
                    continue;

                spark.localPosition += trailAxis * (sparkSpeed * dt);

                float along =
                    Vector3.Dot(spark.localPosition - head, trailAxis);

                if (along <= trailLength)
                    continue;

                Vector3 lateral =
                    Vector3.ProjectOnPlane(
                        Random.insideUnitSphere,
                        trailAxis
                    ) * sparkSpread;

                spark.localPosition = head + lateral;

                spark.localScale =
                    _sparkScales[i] *
                    Random.Range(sparkScaleMin, sparkScaleMax);
            }
        }


        // Applique (radial, radial2, longueur) sur les axes du modèle.
        private Vector3 ScaleOnAxes(
            Vector3 baseScale,
            float radial,
            float radial2,
            float length)
        {
            Vector3 axis = trailAxis;

            return new Vector3(
                baseScale.x * (Mathf.Abs(axis.x) > 0.5f ? length : radial),
                baseScale.y * (Mathf.Abs(axis.y) > 0.5f ? length : radial),
                baseScale.z * (Mathf.Abs(axis.z) > 0.5f ? length : radial2)
            );
        }


        private Transform Find(string partName)
        {
            Transform part = FindRecursive(visualRoot, partName);

            if (part == null)
            {
                Debug.LogWarning(
                    "[MagicProjectileVisual] Pièce '" + partName +
                    "' introuvable.",
                    this
                );
            }

            return part;
        }


        private static Transform FindRecursive(
            Transform parent,
            string partName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == partName)
                    return child;

                Transform found = FindRecursive(child, partName);

                if (found != null)
                    return found;
            }

            return null;
        }


        private static Vector3 BaseScale(Transform part) =>
            part != null
                ? part.localScale
                : Vector3.one;
    }
}
