using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Core.Minimap
{
    /// <summary>
    /// Affiche une des deux cartes (HUD ou grande carte) : l'image rendue par
    /// la caméra du MinimapSystem + les icônes des marqueurs par-dessus.
    /// En mode World : molette = zoom, glisser = déplacer, légende des noms.
    /// En mode Mini : clic = ouvre la grande carte.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class MinimapView : MonoBehaviour,
        IPointerClickHandler, IDragHandler, IScrollHandler
    {
        public enum ViewMode { Mini, World }

        [SerializeField] private ViewMode mode = ViewMode.Mini;
        [SerializeField] private RawImage image;
        [SerializeField] private RectTransform markerLayer;
        [SerializeField] private UnityEvent onClicked;

        [Header("Mini")]
        [Tooltip("Rayon (en unités canvas) au-delà duquel les marqueurs sont collés au bord.")]
        [SerializeField] private float edgeRadius = 96f;

        [Header("World")]
        [SerializeField, Min(1f)] private float maxZoom = 5f;
        [Tooltip("Affiche le nom à côté des icônes sur la grande carte (la légende suffit en général).")]
        [SerializeField] private bool showLabels = false;

        private struct Item
        {
            public Vector3 pos;
            public MarkerType type;
            public string label;
            public float yaw;
        }

        private sealed class Icon
        {
            public RectTransform rect;
            public Image image;
            public TMP_Text text;
        }

        private readonly List<Item> _items = new();
        private readonly List<Icon> _icons = new();
        private RectTransform _rect;
        private Canvas _canvas;
        private float _zoom = 1f;
        private Vector2 _pan;
        private bool _renderRequested;

        public ViewMode Mode => mode;
        public float Zoom => _zoom;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _canvas = GetComponentInParent<Canvas>();
        }

        private void OnEnable()
        {
            if (mode == ViewMode.World) RequestRender(true);
        }

        private void OnDisable()
        {
            if (mode == ViewMode.World) RequestRender(false);
        }

        private void RequestRender(bool on)
        {
            var sys = MinimapSystem.Instance;
            if (sys == null || _renderRequested == on) return;
            _renderRequested = on;
            sys.RequestWorldRender(on);
        }

        private void LateUpdate()
        {
            var sys = MinimapSystem.Instance;
            if (sys == null) return;

            // La grande carte peut s'activer avant que le système existe.
            if (mode == ViewMode.World && !_renderRequested) RequestRender(true);

            Camera cam = mode == ViewMode.Mini ? sys.MiniCamera : sys.WorldCamera;
            if (cam == null) return;

            if (image != null)
            {
                RenderTexture rt = mode == ViewMode.Mini ? sys.MiniTexture : sys.WorldTexture;
                if (image.texture != rt) image.texture = rt;
            }
            if (mode == ViewMode.World) sys.FitWorldCamera(_zoom, _pan);

            BuildItems(sys);
            Layout(cam);
        }

        private void BuildItems(MinimapSystem sys)
        {
            _items.Clear();
            foreach (var m in MinimapMarker.All)
                _items.Add(new Item { pos = m.transform.position, type = m.Type, label = m.Label });

            foreach (var e in sys.Enemies)
                if (e != null) _items.Add(new Item { pos = e.position, type = MarkerType.Enemy });

            if (sys.Player != null)
                _items.Add(new Item
                {
                    pos = sys.Player.position,
                    type = MarkerType.Player,
                    label = mode == ViewMode.World ? "Vous" : null,
                    yaw = sys.Player.eulerAngles.y
                });
        }

        private void Layout(Camera cam)
        {
            Rect r = _rect.rect;
            float limit = mode == ViewMode.Mini ? edgeRadius : 0f;
            var layer = markerLayer != null ? markerLayer : _rect;

            for (int i = 0; i < _items.Count; i++)
            {
                Item it = _items[i];
                Icon icon = GetIcon(i, layer);

                Vector3 vp = cam.WorldToViewportPoint(it.pos);
                Vector2 local = new Vector2((vp.x - 0.5f) * r.width, (vp.y - 0.5f) * r.height);
                bool edge = false;
                bool visible = true;

                if (mode == ViewMode.Mini)
                {
                    float d = local.magnitude;
                    if (d > limit)
                    {
                        if (it.type == MarkerType.Enemy) visible = false;
                        else { local = local / d * limit; edge = true; }
                    }
                }
                else
                {
                    visible = vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
                }

                icon.rect.gameObject.SetActive(visible);
                if (!visible) continue;

                float size = MarkerStyle.SizeOf(it.type);
                if (mode == ViewMode.World) size *= 1.25f;
                icon.rect.anchoredPosition = local;
                icon.rect.sizeDelta = new Vector2(size, size);
                icon.rect.localRotation = it.type == MarkerType.Player
                    ? Quaternion.Euler(0f, 0f, -it.yaw)
                    : Quaternion.identity;

                Color c = MarkerStyle.ColorOf(it.type);
                if (edge) c.a = 0.6f;
                icon.image.sprite = MinimapSprites.For(it.type);
                icon.image.color = c;

                bool showText = showLabels && mode == ViewMode.World && !string.IsNullOrEmpty(it.label);
                if (icon.text != null)
                {
                    icon.text.gameObject.SetActive(showText);
                    if (showText)
                    {
                        icon.text.text = it.label;
                        // le texte ne doit pas tourner avec la flèche du joueur
                        icon.text.rectTransform.rotation = Quaternion.identity;
                    }
                }
            }

            for (int i = _items.Count; i < _icons.Count; i++)
                _icons[i].rect.gameObject.SetActive(false);
        }

        private Icon GetIcon(int index, RectTransform layer)
        {
            while (_icons.Count <= index)
            {
                var go = new GameObject("Marker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var rt = (RectTransform)go.transform;
                rt.SetParent(layer, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                var img = go.GetComponent<Image>();
                img.raycastTarget = false;
                var outline = go.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                outline.effectDistance = new Vector2(1.5f, -1.5f);

                TMP_Text text = null;
                if (mode == ViewMode.World)
                {
                    var tgo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                    var trt = (RectTransform)tgo.transform;
                    trt.SetParent(rt, false);
                    trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0f);
                    trt.pivot = new Vector2(0.5f, 1f);
                    trt.anchoredPosition = new Vector2(0f, -2f);
                    trt.sizeDelta = new Vector2(260f, 28f);
                    text = tgo.GetComponent<TextMeshProUGUI>();
                    text.fontSize = 20f;
                    text.alignment = TextAlignmentOptions.Top;
                    text.color = Color.white;
                    text.fontStyle = FontStyles.Bold;
                    text.outlineWidth = 0.35f;
                    text.outlineColor = new Color32(0, 0, 0, 255);
                    text.raycastTarget = false;
                    text.textWrappingMode = TextWrappingModes.NoWrap;
                    text.gameObject.SetActive(false);
                }
                _icons.Add(new Icon { rect = rt, image = img, text = text });
            }
            return _icons[index];
        }

        // ---------- interactions ----------

        public void OnPointerClick(PointerEventData e)
        {
            if (mode == ViewMode.Mini && e.button == PointerEventData.InputButton.Left)
                onClicked?.Invoke();
        }

        public void OnDrag(PointerEventData e)
        {
            var sys = MinimapSystem.Instance;
            if (mode != ViewMode.World || sys == null) return;
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            float metersPerUnit = sys.WorldMetersPerViewportHeight(_zoom) / _rect.rect.height;
            _pan -= e.delta / scale * metersPerUnit;
            ClampPan(sys);
        }

        public void OnScroll(PointerEventData e)
        {
            if (mode != ViewMode.World) return;
            SetZoom(_zoom * (1f + e.scrollDelta.y * 0.12f));
        }

        public void ZoomIn() => SetZoom(_zoom * 1.35f);
        public void ZoomOut() => SetZoom(_zoom / 1.35f);

        public void ResetView()
        {
            _zoom = 1f;
            _pan = Vector2.zero;
        }

        public void CenterOnPlayer()
        {
            var sys = MinimapSystem.Instance;
            if (sys == null || sys.Player == null) return;
            _pan = new Vector2(sys.Player.position.x, sys.Player.position.z) - sys.WorldCenter;
            ClampPan(sys);
        }

        private void SetZoom(float z)
        {
            _zoom = Mathf.Clamp(z, 1f, maxZoom);
            var sys = MinimapSystem.Instance;
            if (sys != null) ClampPan(sys);
        }

        private void ClampPan(MinimapSystem sys)
        {
            if (_zoom <= 1.001f) { _pan = Vector2.zero; return; }
            Vector2 half = sys.WorldSize * 0.5f;
            _pan.x = Mathf.Clamp(_pan.x, -half.x, half.x);
            _pan.y = Mathf.Clamp(_pan.y, -half.y, half.y);
        }
    }
}
