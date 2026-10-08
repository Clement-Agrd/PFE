using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Core.MainMenu
{
    /// <summary>
    /// Anime le menu en roue : à l'ouverture les boutons glissent le long de l'arc
    /// avec un ressort (léger dépassement), les deux anneaux tournent en sens
    /// inverse, puis au survol le bouton avance, ses voisins s'écartent, l'anneau
    /// reçoit une impulsion de rotation et un curseur lumineux suit le bouton.
    /// Les positions sont recalculées chaque frame depuis la mise en page de départ,
    /// donc le script cohabite avec <see cref="MenuButtonAnimator"/> (échelle seulement).
    /// </summary>
    public sealed class MenuWheelAnimator : MonoBehaviour
    {
        [Header("Références")]
        [SerializeField] private RectTransform outerRing;
        [SerializeField] private RectTransform innerRing;
        [SerializeField] private RectTransform selector;
        [SerializeField] private RectTransform[] items;

        [Header("Intro")]
        [SerializeField] private float introAngle = 80f;
        [SerializeField] private float introDelay = 0.25f;
        [SerializeField] private float introStagger = 0.14f;
        [SerializeField] private float ringIntroDuration = 1.1f;

        [Header("Ressort")]
        [SerializeField] private float stiffness = 70f;
        [SerializeField] private float damping = 9f;

        [Header("Rotation des anneaux (°/s)")]
        [SerializeField] private float outerSpeed = 5f;
        [SerializeField] private float innerSpeed = 8f;

        [Header("Survol")]
        [SerializeField] private float hoverPush = 42f;
        [SerializeField] private float neighbourRepel = 3.2f;
        [SerializeField] private float hoverImpulse = 55f;
        [SerializeField] private float clickImpulse = 340f;

        private sealed class Item
        {
            public RectTransform rt;
            public CanvasGroup group;
            public float baseAngle, baseRadius;
            public float angle, angleVel, radius, radiusVel;
        }

        private Item[] _items;
        private Vector2 _center;
        private float _time;
        private float _ringImpulse;
        private float _outerRot, _innerRot;
        private int _hover = -1;
        private float _selAngle, _selAngleVel, _selAlpha;
        private Image _outerImg, _innerImg, _selImg;
        private float _outerA, _innerA, _selA;
        private Canvas _canvas;
        private bool _built;

        private void Build()
        {
            _built = true;
            _canvas = GetComponentInParent<Canvas>();
            if (_canvas != null) _canvas = _canvas.rootCanvas;
            _center = outerRing.anchoredPosition;

            _outerImg = outerRing.GetComponent<Image>(); _outerA = _outerImg.color.a;
            _innerImg = innerRing.GetComponent<Image>(); _innerA = _innerImg.color.a;
            _selImg = selector.GetComponent<Image>(); _selA = _selImg.color.a;

            _items = new Item[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                Vector2 v = items[i].anchoredPosition - _center;
                var cg = items[i].GetComponent<CanvasGroup>();
                if (cg == null) cg = items[i].gameObject.AddComponent<CanvasGroup>();
                _items[i] = new Item
                {
                    rt = items[i], group = cg,
                    baseRadius = v.magnitude,
                    baseAngle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg,
                };
            }
        }

        private void OnEnable()
        {
            if (!_built) Build();
            _time = 0f;
            _ringImpulse = 0f;
            _hover = -1;
            _selAlpha = 0f;
            for (int i = 0; i < _items.Length; i++)
            {
                var it = _items[i];
                it.angle = it.baseAngle + introAngle + i * 6f;
                it.angleVel = 0f; it.radius = 0f; it.radiusVel = 0f;
                it.group.alpha = 0f;
                Place(it);
            }
            _selAngle = _items.Length > 0 ? _items[0].baseAngle : 0f;
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.033f);
            _time += dt;

            int hover = FindHover();
            if (hover != _hover)
            {
                if (hover >= 0) _ringImpulse += hoverImpulse;
                _hover = hover;
            }
            if (_hover >= 0 && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                _ringImpulse += clickImpulse;

            UpdateRings(dt);
            UpdateItems(dt);
            UpdateSelector(dt);
        }

        private void UpdateRings(float dt)
        {
            _ringImpulse *= Mathf.Exp(-2.2f * dt);
            _outerRot += (outerSpeed + _ringImpulse) * dt;
            _innerRot -= (innerSpeed + _ringImpulse * 1.6f) * dt;

            float t = Mathf.Clamp01(_time / ringIntroDuration);
            float e = EaseOutBack(t);
            float fade = Mathf.Clamp01(_time / 0.6f);

            outerRing.localRotation = Quaternion.Euler(0f, 0f, _outerRot - (1f - e) * 140f);
            outerRing.localScale = Vector3.one * Mathf.LerpUnclamped(0.55f, 1f, e);
            innerRing.localRotation = Quaternion.Euler(0f, 0f, _innerRot + (1f - e) * 200f);
            innerRing.localScale = Vector3.one * Mathf.LerpUnclamped(0.35f, 1f, e);

            SetAlpha(_outerImg, _outerA * fade);
            SetAlpha(_innerImg, _innerA * fade);
        }

        private void UpdateItems(float dt)
        {
            for (int i = 0; i < _items.Length; i++)
            {
                var it = _items[i];
                float local = _time - (introDelay + i * introStagger);
                if (local < 0f) { Place(it); continue; }

                it.group.alpha = Mathf.Clamp01(local / 0.25f);

                float targetAngle = it.baseAngle + Mathf.Sin(_time * 0.9f + i * 1.7f) * 0.7f;
                float targetRadius = Mathf.Sin(_time * 0.7f + i * 1.3f) * 4f;
                if (_hover >= 0)
                {
                    if (i == _hover) targetRadius += hoverPush;
                    else targetAngle += (i < _hover ? 1f : -1f) * neighbourRepel;
                }

                Spring(ref it.angle, ref it.angleVel, targetAngle, dt);
                Spring(ref it.radius, ref it.radiusVel, targetRadius, dt);
                Place(it);
                it.rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Clamp(-it.angleVel * 0.025f, -7f, 7f));
            }
        }

        private void UpdateSelector(float dt)
        {
            if (_items.Length == 0) return;
            if (_hover >= 0) _selAngle = Mathf.SmoothDampAngle(_selAngle, _items[_hover].angle, ref _selAngleVel, 0.07f, Mathf.Infinity, dt);
            _selAlpha = Mathf.MoveTowards(_selAlpha, _hover >= 0 ? 1f : 0f, dt * 6f);

            float a = _selAngle * Mathf.Deg2Rad;
            selector.anchoredPosition = _center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _items[0].baseRadius;
            float pulse = 1f + Mathf.Sin(_time * 5f) * 0.12f;
            selector.localScale = Vector3.one * pulse;
            SetAlpha(_selImg, _selA * _selAlpha * Mathf.Clamp01(_time / 0.8f));
        }

        private void Spring(ref float value, ref float velocity, float target, float dt)
        {
            velocity += (stiffness * (target - value) - damping * velocity) * dt;
            value += velocity * dt;
        }

        private void Place(Item it)
        {
            float a = it.angle * Mathf.Deg2Rad;
            it.rt.anchoredPosition = _center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (it.baseRadius + it.radius);
        }

        private int FindHover()
        {
            if (Mouse.current == null) return -1;
            Vector2 p = Mouse.current.position.ReadValue();
            Camera cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
            for (int i = 0; i < _items.Length; i++)
                if (_items[i].group.alpha > 0.5f && RectTransformUtility.RectangleContainsScreenPoint(_items[i].rt, p, cam))
                    return i;
            return -1;
        }

        private static void SetAlpha(Image img, float a)
        {
            Color c = img.color; c.a = a; img.color = c;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
