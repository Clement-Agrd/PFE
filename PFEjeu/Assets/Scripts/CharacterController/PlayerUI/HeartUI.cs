using UnityEngine;
using UnityEngine.UI;

namespace ProfessionalTPS.UI
{
    public sealed class HeartUI : MonoBehaviour
    {
        [Header("Images")]

        [Tooltip("Le cœur vide / sombre derrière.")]
        [SerializeField]
        private Image background;

        [Tooltip("Le cœur rouge qui se réduit selon les PV.")]
        [SerializeField]
        private Image fill;


        [Header("Fill")]

        [Tooltip(
            "Si activé, le cœur rouge rétrécit horizontalement."
        )]
        [SerializeField]
        private bool useScale = true;


        [Header("Heartbeat")]

        [Tooltip(
            "Les cœurs qui ne sont pas pleins battent légèrement."
        )]
        [SerializeField]
        private bool beatWhenNotFull = true;

        [SerializeField, Min(0.1f)]
        private float beatsPerSecond = 1.35f;

        [SerializeField, Range(0f, 0.3f)]
        private float beatScale = 0.09f;

        [SerializeField, Min(0f)]
        private float returnSpeed = 12f;


        private Vector3 _baseScale =
            Vector3.one;

        private float _fillAmount =
            1f;


        private void Awake()
        {
            _baseScale =
                transform.localScale;
        }


        private void Update()
        {
            bool shouldBeat =
                beatWhenNotFull &&
                _fillAmount < 0.999f;

            if (!shouldBeat)
            {
                transform.localScale =
                    Vector3.Lerp(
                        transform.localScale,
                        _baseScale,
                        1f -
                        Mathf.Exp(
                            -returnSpeed *
                            Time.unscaledDeltaTime
                        )
                    );

                return;
            }

            float phase =
                Mathf.Repeat(
                    Time.unscaledTime *
                    beatsPerSecond,
                    1f
                );

            float firstBeat =
                GaussianPulse(
                    phase,
                    0.12f,
                    0.055f
                );

            float secondBeat =
                GaussianPulse(
                    phase,
                    0.30f,
                    0.075f
                ) *
                0.65f;

            float pulse =
                Mathf.Clamp01(
                    firstBeat +
                    secondBeat
                );

            transform.localScale =
                _baseScale *
                (
                    1f +
                    beatScale *
                    pulse
                );
        }


        public void SetFill(
            float amount)
        {
            amount =
                Mathf.Clamp01(
                    amount
                );

            _fillAmount =
                amount;

            if (fill == null)
                return;

            if (useScale)
            {
                Vector3 scale =
                    fill.rectTransform
                        .localScale;

                scale.x =
                    amount;

                scale.y =
                    amount;

                fill.rectTransform
                    .localScale =
                        scale;
            }
            else
            {
                fill.fillAmount =
                    amount;
            }
        }


        private static float GaussianPulse(
            float phase,
            float center,
            float width)
        {
            float delta =
                phase -
                center;

            float safeWidth =
                Mathf.Max(
                    0.001f,
                    width
                );

            float normalized =
                delta /
                safeWidth;

            return Mathf.Exp(
                -normalized *
                normalized
            );
        }
    }
}
