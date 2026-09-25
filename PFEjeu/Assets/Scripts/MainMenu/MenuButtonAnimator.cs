using Core.TweenSystem;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Core.MainMenu
{
    /// <summary>
    /// Anime un bouton de menu : grossit au survol (rebond élastique), petit
    /// "squash" à l'appui. Purement visuel — ne touche pas à Button.onClick.
    /// </summary>
    public sealed class MenuButtonAnimator : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField, Min(1f)] private float hoverScale = 1.15f;
        [SerializeField, Range(0f, 1f)] private float pressScale = 0.92f;
        [SerializeField, Min(0f)] private float hoverDuration = 0.25f;
        [SerializeField, Min(0f)] private float pressDuration = 0.1f;

        private Transform _target;
        private Vector3 _baseScale;
        private bool _hovering;

        private void Awake()
        {
            _target = transform;
            _baseScale = _target.localScale;
        }

        private void OnDisable()
        {
            _hovering = false;
            _target.KillTweens();
            _target.localScale = _baseScale;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovering = true;
            AnimateTo(hoverScale, hoverDuration, Ease.OutBack);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovering = false;
            AnimateTo(1f, hoverDuration, Ease.OutQuad);
        }

        public void OnPointerDown(PointerEventData eventData) => AnimateTo(pressScale, pressDuration, Ease.OutQuad);

        public void OnPointerUp(PointerEventData eventData) => AnimateTo(_hovering ? hoverScale : 1f, hoverDuration, Ease.OutBack);

        private void AnimateTo(float scaleFactor, float duration, Ease ease)
        {
            _target.KillTweens();
            _target.TweenScale(_baseScale * scaleFactor, duration).SetEase(ease);
        }
    }
}
