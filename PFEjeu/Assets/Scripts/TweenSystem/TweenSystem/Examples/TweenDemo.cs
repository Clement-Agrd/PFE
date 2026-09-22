using UnityEngine;

namespace Core.TweenSystem.Examples
{
    /// <summary>
    /// Démo : 1 = bouge (OutBack), 2 = grossit et rebondit (OutBounce),
    /// 3 = tourne, 4 = va-et-vient infini (pingpong). Pose ce composant sur un objet.
    /// </summary>
    public sealed class TweenDemo : MonoBehaviour
    {
        [SerializeField] private Vector3 moveOffset = new Vector3(3f, 0f, 0f);
        [SerializeField] private float duration = 0.6f;

        private Vector3 _startPos;
        private Vector3 _startScale;

        private void Awake()
        {
            _startPos = transform.position;
            _startScale = transform.localScale;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                transform.KillTweens();
                transform.TweenMove(_startPos + moveOffset, duration).SetEase(Ease.OutBack)
                    .OnComplete(() => transform.TweenMove(_startPos, duration).SetEase(Ease.InOutQuad));
            }

            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                transform.KillTweens();
                transform.TweenScale(_startScale * 1.5f, duration).SetEase(Ease.OutBounce);
            }

            if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                transform.KillTweens();
                transform.TweenRotate(transform.rotation * Quaternion.Euler(0f, 0f, 180f), duration)
                    .SetEase(Ease.InOutCubic);
            }

            if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                transform.KillTweens();
                transform.TweenMove(_startPos + moveOffset, duration)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, pingpong: true); // va-et-vient infini
            }
        }
    }
}
