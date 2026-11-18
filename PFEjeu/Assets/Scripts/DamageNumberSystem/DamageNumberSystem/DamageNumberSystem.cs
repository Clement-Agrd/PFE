using TMPro;
using UnityEngine;

namespace Core.DamageNumberSystem
{
    public class DamageNumber : MonoBehaviour
    {
        [Header("Références")]
        [SerializeField] private TMP_Text label;

        [Header("Décalage & Déplacement")]
        [SerializeField] private float floatSpeed = 1.2f;
        [Tooltip("Écartement aléatoire sur les axes X et Z pour éviter la superposition.")]
        [SerializeField] private Vector2 randomJitter = new Vector2(0.5f, 0.2f);

        [Header("Échelle dynamique (Taille)")]
        [SerializeField] private float minScale = 0.7f;
        [SerializeField] private float maxScale = 1.8f;
        [Tooltip("Dégâts nécessaires pour atteindre la taille maximale.")]
        [SerializeField] private int maxDamageReference = 100;
        [Tooltip("Animation de rebond à l'apparition.")]
        [SerializeField] private AnimationCurve popCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.15f, 1.2f),
            new Keyframe(0.3f, 1f),
            new Keyframe(1f, 1f)
        );

        [Header("Durée & Fondu (Fade)")]
        [SerializeField] private float lifetime = 1.2f;
        [Tooltip("Transparence du texte sur la durée de vie.")]
        [SerializeField] private AnimationCurve alphaCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        private Core.PoolingSystem.PooledObject _pooled;
        private bool _initialized;
        private float _elapsed;

        private void Awake() => TryGetComponent(out _pooled);
        private void OnDisable() => _initialized = false;
        private Color _baseColor = Color.white;
        private Vector3 _targetScale = Vector3.one;

        public void Initialize(int amount, string text, Color color, bool isCrit)
        {
            if (_pooled == null) TryGetComponent(out _pooled);
            _elapsed = 0f;
            _initialized = true;
            // 1. Décalage aléatoire pour ne pas empiler les chiffres
            Vector3 jitterOffset = new Vector3(
                Random.Range(-randomJitter.x, randomJitter.x),
                Random.Range(-randomJitter.y, randomJitter.y),
                Random.Range(-randomJitter.x, randomJitter.x)
            );
            transform.position += jitterOffset;

            // 2. Configuration du texte
            if (label != null)
            {
                label.text = text;
                label.color = color;
                _baseColor = color;
            }

            // 3. Calcul de la taille en fonction des dégâts
            float damageRatio = Mathf.Clamp01((float)amount / maxDamageReference);
            float calculatedScale = Mathf.Lerp(minScale, maxScale, damageRatio);

            // Bonus d'échelle si c'est un coup critique
            if (isCrit) calculatedScale *= 1.25f;

            _targetScale = Vector3.one * calculatedScale;
            transform.localScale = Vector3.zero;

            // La durée est suivie dans Update pour pouvoir réutiliser ce texte.
        }

        private void Update()
        {
            if (!_initialized) return;
            _elapsed += Time.deltaTime;
            if (_elapsed >= lifetime)
            {
                _initialized = false;
                if (_pooled != null) _pooled.Release();
                else Destroy(gameObject);
                return;
            }
            float progress = Mathf.Clamp01(_elapsed / lifetime);

            // Montée progressive
            transform.position += Vector3.up * (floatSpeed * Time.deltaTime);

            // Animation d'échelle (Rebond)
            float scaleMultiplier = popCurve.Evaluate(progress);
            transform.localScale = _targetScale * scaleMultiplier;

            // Fondu en transparence
            if (label != null)
            {
                Color c = _baseColor;
                c.a = alphaCurve.Evaluate(progress);
                label.color = c;
            }
        }
    }
}