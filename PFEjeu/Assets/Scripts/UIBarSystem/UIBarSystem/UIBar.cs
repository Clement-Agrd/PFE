using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.UIBarSystem
{
    public sealed class UIBar : MonoBehaviour
    {
        [Header("Images")]
        [SerializeField] private Image fill;
        [SerializeField] private Image delayedFill;

        [Header("Animation")]
        [SerializeField, Min(0f)] private float fillSpeed = 8f;
        [SerializeField, Min(0f)] private float delayedSpeed = 2.5f;
        [SerializeField, Min(0f)] private float delayBeforeCatchUp = 0.4f;

        [Header("Shaders (Optionnel)")]
        [Tooltip("Coche ceci si tu utilises un Custom Material avec _FillAmount au lieu du FillAmount classique de l'UI.")]
        [SerializeField] private bool useShaderFill = false;

        [Header("Extras")]
        [SerializeField] private TMP_Text label;

        private float _target01 = 1f;
        private float _catchUpTimer;
        
        
        // Variables pour stocker les valeurs actuelles
        private float _currentFill = 1f;
        private float _currentDelayedFill = 1f;
        public float Normalized => _target01;

        private static readonly int FillAmountProp = Shader.PropertyToID("_FillAmount");

        public void SetValue(float current, float max)
        {
            float previous = _target01;
            _target01 = max > 0f ? Mathf.Clamp01(current / max) : 0f;

            if (label != null)
                label.text = $"{Mathf.CeilToInt(current)}/{Mathf.CeilToInt(max)}";

            if (delayedFill != null && _target01 < previous)
                _catchUpTimer = delayBeforeCatchUp;
        }

        public void SnapToValue()
        {
            _currentFill = _target01;
            _currentDelayedFill = _target01;
            ApplyFill(_currentFill, fill);
            ApplyFill(_currentDelayedFill, delayedFill);
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (fill != null)
            {
                _currentFill = Mathf.MoveTowards(_currentFill, _target01, fillSpeed * dt);
                ApplyFill(_currentFill, fill);
            }

            if (delayedFill != null)
            {
                if (_target01 > _currentDelayedFill)
                {
                    _currentDelayedFill = _target01;
                }
                else if (_catchUpTimer > 0f)
                {
                    _catchUpTimer -= dt;
                }
                else
                {
                    _currentDelayedFill = Mathf.MoveTowards(_currentDelayedFill, _target01, delayedSpeed * dt);
                }
                ApplyFill(_currentDelayedFill, delayedFill);
            }
        }

        private void ApplyFill(float amount, Image img)
        {
            if (img == null) return;

            if (useShaderFill)
            {
                // Modifie la valeur dans le Shader directement
                img.materialForRendering.SetFloat(FillAmountProp, amount);
            }
            else
            {
                // Méthode classique native d'Unity
                img.fillAmount = amount;
            }
        }
    }
}