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


        public void SetFill(float amount)
        {
            amount =
                Mathf.Clamp01(amount);


            if (fill == null)
                return;


            if (useScale)
            {
                Vector3 scale =
                    fill.rectTransform.localScale;

                scale.x =
                    amount;

                fill.rectTransform.localScale =
                    scale;
            }
            else
            {
                fill.fillAmount =
                    amount;
            }
        }
    }
}