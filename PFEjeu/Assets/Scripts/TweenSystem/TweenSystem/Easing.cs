using UnityEngine;

namespace Core.TweenSystem
{
    /// <summary>
    /// Évalue une courbe d'easing : transforme une progression linéaire t (0→1)
    /// en une progression accélérée/décélérée. Peut renvoyer &lt;0 ou &gt;1
    /// (Back/Elastic) → à utiliser avec LerpUnclamped.
    /// </summary>
    public static class Easing
    {
        private const float BackC1 = 1.70158f;
        private const float BackC2 = BackC1 * 1.525f;
        private const float BackC3 = BackC1 + 1f;

        public static float Evaluate(Ease ease, float t)
        {
            t = Mathf.Clamp01(t);

            switch (ease)
            {
                case Ease.Linear: return t;

                case Ease.InQuad: return t * t;
                case Ease.OutQuad: return t * (2f - t);
                case Ease.InOutQuad: return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;

                case Ease.InCubic: return t * t * t;
                case Ease.OutCubic: return 1f - Mathf.Pow(1f - t, 3f);
                case Ease.InOutCubic: return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;

                case Ease.InSine: return 1f - Mathf.Cos(t * Mathf.PI / 2f);
                case Ease.OutSine: return Mathf.Sin(t * Mathf.PI / 2f);
                case Ease.InOutSine: return -(Mathf.Cos(Mathf.PI * t) - 1f) / 2f;

                case Ease.InBack: return BackC3 * t * t * t - BackC1 * t * t;
                case Ease.OutBack:
                {
                    float u = t - 1f;
                    return 1f + BackC3 * u * u * u + BackC1 * u * u;
                }
                case Ease.InOutBack:
                    return t < 0.5f
                        ? Mathf.Pow(2f * t, 2f) * ((BackC2 + 1f) * 2f * t - BackC2) / 2f
                        : (Mathf.Pow(2f * t - 2f, 2f) * ((BackC2 + 1f) * (t * 2f - 2f) + BackC2) + 2f) / 2f;

                case Ease.OutBounce: return OutBounce(t);

                case Ease.OutElastic:
                {
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    const float c4 = (2f * Mathf.PI) / 3f;
                    return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f;
                }

                default: return t;
            }
        }

        private static float OutBounce(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;

            if (t < 1f / d1) return n1 * t * t;
            if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
            if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }
    }
}
