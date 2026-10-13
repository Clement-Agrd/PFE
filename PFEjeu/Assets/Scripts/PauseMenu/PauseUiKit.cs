using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Core.PauseMenu
{
    /// <summary>
    /// Boîte à outils visuelle du menu pause : palette, sprites arrondis générés en code et
    /// fabriques de widgets (boutons animés, interrupteurs, curseurs, listes défilantes).
    /// Les animations utilisent le temps non mis à l'échelle : elles tournent jeu en pause.
    /// </summary>
    public static class PauseUiKit
    {
        public static readonly Color Dim = new(0.02f, 0.03f, 0.05f, 0.72f);
        public static readonly Color Card = new(0.075f, 0.085f, 0.12f, 0.97f);
        public static readonly Color CardRaised = new(0.125f, 0.14f, 0.19f, 1f);
        public static readonly Color CardHover = new(0.18f, 0.2f, 0.27f, 1f);
        public static readonly Color Accent = new(0.95f, 0.78f, 0.35f, 1f);
        public static readonly Color AccentDark = new(0.62f, 0.47f, 0.16f, 1f);
        public static readonly Color TextMain = new(0.94f, 0.95f, 0.97f, 1f);
        public static readonly Color TextMuted = new(0.6f, 0.64f, 0.72f, 1f);
        public static readonly Color Good = new(0.45f, 0.85f, 0.5f, 1f);
        public static readonly Color Danger = new(0.9f, 0.36f, 0.33f, 1f);

        private static readonly Dictionary<int, Sprite> RoundedCache = new();
        private static Sprite _vignette;

        #region Sprites

        /// <summary>Rectangle arrondi blanc, 9-slice (à utiliser avec Image.Type.Sliced).</summary>
        public static Sprite Rounded(int radius)
        {
            if (RoundedCache.TryGetValue(radius, out Sprite cached) && cached != null) return cached;

            int size = radius * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Distance au rectangle arrondi (négatif à l'intérieur).
                    float dx = Mathf.Abs(x - c) - (c - radius);
                    float dy = Mathf.Abs(y - c) - (c - radius);
                    float d = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) + Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f))
                              + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
                    byte a = (byte)(Mathf.Clamp01(0.5f - d) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            RoundedCache[radius] = sprite;
            return sprite;
        }

        /// <summary>Dégradé radial noir (transparent au centre, opaque aux bords) pour assombrir l'écran.</summary>
        public static Sprite Vignette()
        {
            if (_vignette != null) return _vignette;

            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x / (size - 1f)) * 2f - 1f;
                    float ny = (y / (size - 1f)) * 2f - 1f;
                    float d = Mathf.Clamp01(Mathf.Sqrt(nx * nx + ny * ny) / 1.35f);
                    float a = Mathf.SmoothStep(0.25f, 1f, d);
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)(a * 200f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            _vignette = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            _vignette.hideFlags = HideFlags.HideAndDontSave;
            return _vignette;
        }

        #endregion

        #region Primitives

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rt, float margin = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(margin, margin);
            rt.offsetMax = new Vector2(-margin, -margin);
        }

        public static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        public static Image NewImage(string name, Transform parent, Color color, int radius = 0)
        {
            RectTransform rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            if (radius > 0)
            {
                img.sprite = Rounded(radius);
                img.type = Image.Type.Sliced;
            }
            return img;
        }

        public static TMP_Text NewText(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Left, FontStyles style = FontStyles.Normal)
        {
            RectTransform rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        public static LayoutElement Layout(GameObject go, float width = -1f, float height = -1f, float flexW = -1f, float flexH = -1f)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (width >= 0f) le.preferredWidth = width;
            // flexibleHeight à 0 : sinon le HStack du conteneur (force-expand) impose son propre 1 et la ligne s'étire.
            if (height >= 0f) { le.preferredHeight = height; le.minHeight = height; if (flexH < 0f) le.flexibleHeight = 0f; }
            if (flexW >= 0f) le.flexibleWidth = flexW;
            if (flexH >= 0f) le.flexibleHeight = flexH;
            return le;
        }

        public static VerticalLayoutGroup VStack(GameObject go, float spacing, RectOffset padding = null,
            TextAnchor align = TextAnchor.UpperLeft, bool expandWidth = true)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding ?? new RectOffset();
            v.childAlignment = align;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = expandWidth;
            v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HStack(GameObject go, float spacing, RectOffset padding = null,
            TextAnchor align = TextAnchor.MiddleLeft, bool expandHeight = true)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = padding ?? new RectOffset();
            h.childAlignment = align;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = expandHeight;
            return h;
        }

        #endregion

        #region Boutons

        public enum ButtonStyle { Primary, Secondary, Danger, Tab }

        public static PauseButton NewButton(Transform parent, string label, Action onClick,
            ButtonStyle style = ButtonStyle.Secondary, float fontSize = 30f, int radius = 14)
        {
            RectTransform rt = NewRect("Button_" + label, parent);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = Rounded(radius);
            bg.type = Image.Type.Sliced;

            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.transition = Selectable.Transition.None;

            Image bar = NewImage("Accent", rt, Accent, 3);
            bar.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            bar.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            bar.rectTransform.pivot = new Vector2(0f, 0.5f);
            bar.rectTransform.anchoredPosition = new Vector2(14f, 0f);
            bar.rectTransform.sizeDelta = new Vector2(6f, 0f);

            TMP_Text text = NewText("Label", rt, label, fontSize, TextMain, TextAlignmentOptions.Left, FontStyles.Bold);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(36f, 0f);
            text.rectTransform.offsetMax = new Vector2(-16f, 0f);
            text.alignment = style == ButtonStyle.Tab ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
            if (style == ButtonStyle.Tab) text.rectTransform.offsetMin = new Vector2(16f, 0f);
            text.textWrappingMode = TextWrappingModes.NoWrap;

            var pb = rt.gameObject.AddComponent<PauseButton>();
            pb.Setup(bg, bar, text, style);
            if (onClick != null) button.onClick.AddListener(() => onClick());
            return pb;
        }

        #endregion

        #region Interrupteur

        public static PauseToggle NewToggle(Transform parent, bool value, Action<bool> onChanged)
        {
            RectTransform rt = NewRect("Toggle", parent);
            rt.sizeDelta = new Vector2(84f, 42f);
            Layout(rt.gameObject, 84f, 42f);

            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = Rounded(21);
            bg.type = Image.Type.Sliced;
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.transition = Selectable.Transition.None;

            Image knob = NewImage("Knob", rt, Color.white, 17);
            Anchor(knob.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(21f, 0f), new Vector2(34f, 34f));

            var toggle = rt.gameObject.AddComponent<PauseToggle>();
            toggle.Setup(bg, knob.rectTransform, value);
            button.onClick.AddListener(() => { toggle.Set(!toggle.Value, true); onChanged?.Invoke(toggle.Value); });
            return toggle;
        }

        #endregion

        #region Curseur

        public static Slider NewSlider(Transform parent, float min, float max, float value, Action<float> onChanged)
        {
            RectTransform root = NewRect("Slider", parent);
            Layout(root.gameObject, 360f, 42f);

            Image track = NewImage("Track", root, new Color(1f, 1f, 1f, 0.12f), 7);
            track.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(0f, 14f);

            RectTransform fillArea = NewRect("FillArea", root);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(-24f, 14f);

            Image fill = NewImage("Fill", fillArea, Accent, 7);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.sizeDelta = new Vector2(12f, 0f);

            RectTransform handleArea = NewRect("HandleArea", root);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(12f, 0f);
            handleArea.offsetMax = new Vector2(-12f, 0f);

            Image handle = NewImage("Handle", handleArea, Color.white, 17);
            handle.raycastTarget = true;
            // Le Slider étire la poignée sur toute la hauteur de la zone (42) : hauteur relative pour obtenir 34.
            handle.rectTransform.sizeDelta = new Vector2(34f, -8f);

            var slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.SetValueWithoutNotify(value);
            slider.transition = Selectable.Transition.None;
            if (onChanged != null) slider.onValueChanged.AddListener(v => onChanged(v));

            // Zone cliquable sur toute la piste.
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            return slider;
        }

        #endregion

        #region Liste défilante

        /// <summary>Crée une zone défilante verticale et renvoie son contenu (VerticalLayoutGroup).</summary>
        public static RectTransform NewScroll(Transform parent, string name, float spacing, RectOffset padding, out ScrollRect scroll)
        {
            RectTransform root = NewRect(name, parent);
            var bg = root.gameObject.AddComponent<Image>();
            bg.color = Color.clear;

            RectTransform viewport = NewRect("Viewport", root);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            VStack(content.gameObject, spacing, padding);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 45f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.12f;
            return content;
        }

        #endregion
    }

    /// <summary>Bouton animé : survol/sélection éclaircit le fond, déploie la barre dorée et agrandit légèrement.</summary>
    public sealed class PauseButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        ISelectHandler, IDeselectHandler, IPointerDownHandler, IPointerUpHandler
    {
        private Image _bg, _bar;
        private TMP_Text _label;
        private PauseUiKit.ButtonStyle _style;
        private float _hover, _press;
        private bool _hovered, _selected, _down, _active;
        private float _barMaxHeight = 34f;

        public TMP_Text Label => _label;

        public void Setup(Image bg, Image bar, TMP_Text label, PauseUiKit.ButtonStyle style)
        {
            _bg = bg; _bar = bar; _label = label; _style = style;
            Apply(0f, 0f);
        }

        /// <summary>Onglet actif : reste mis en avant.</summary>
        public void SetActive(bool active) => _active = active;

        private void OnDisable() { _hovered = _selected = _down = false; _hover = _press = 0f; Apply(0f, 0f); }

        private void Update()
        {
            float target = (_hovered || _selected || _active) ? 1f : 0f;
            _hover = Mathf.MoveTowards(_hover, target, Time.unscaledDeltaTime * 9f);
            _press = Mathf.MoveTowards(_press, _down ? 1f : 0f, Time.unscaledDeltaTime * 14f);
            Apply(_hover, _press);
        }

        private void Apply(float hover, float press)
        {
            if (_bg == null) return;

            Color rest, lit;
            switch (_style)
            {
                case PauseUiKit.ButtonStyle.Primary:
                    rest = new Color(0.95f, 0.78f, 0.35f, 0.92f); lit = PauseUiKit.Accent; break;
                case PauseUiKit.ButtonStyle.Danger:
                    rest = PauseUiKit.CardRaised; lit = new Color(0.4f, 0.16f, 0.16f, 1f); break;
                case PauseUiKit.ButtonStyle.Tab:
                    rest = new Color(1f, 1f, 1f, 0.05f); lit = PauseUiKit.CardHover; break;
                default:
                    rest = PauseUiKit.CardRaised; lit = PauseUiKit.CardHover; break;
            }

            _bg.color = Color.Lerp(rest, lit, hover);
            bool primary = _style == PauseUiKit.ButtonStyle.Primary;
            bool tab = _style == PauseUiKit.ButtonStyle.Tab;

            _label.color = primary
                ? new Color(0.1f, 0.09f, 0.06f)
                : Color.Lerp(PauseUiKit.TextMain, _style == PauseUiKit.ButtonStyle.Danger ? PauseUiKit.Danger : PauseUiKit.Accent, hover);

            if (_bar != null)
            {
                _bar.enabled = !primary && !tab;
                _bar.color = _style == PauseUiKit.ButtonStyle.Danger ? PauseUiKit.Danger : PauseUiKit.Accent;
                _bar.rectTransform.sizeDelta = new Vector2(6f, _barMaxHeight * hover);
            }

            transform.localScale = Vector3.one * (1f + 0.025f * hover - 0.02f * press);
        }

        public void OnPointerEnter(PointerEventData e) => _hovered = true;
        public void OnPointerExit(PointerEventData e) { _hovered = false; _down = false; }
        public void OnSelect(BaseEventData e) => _selected = true;
        public void OnDeselect(BaseEventData e) => _selected = false;
        public void OnPointerDown(PointerEventData e) => _down = true;
        public void OnPointerUp(PointerEventData e) => _down = false;
    }

    /// <summary>Interrupteur en pilule avec bouton qui glisse.</summary>
    public sealed class PauseToggle : MonoBehaviour
    {
        private Image _bg;
        private RectTransform _knob;
        private float _t;
        private bool _value;

        public bool Value => _value;

        public void Setup(Image bg, RectTransform knob, bool value)
        {
            _bg = bg; _knob = knob;
            _value = value;
            _t = value ? 1f : 0f;
            Apply();
        }

        public void Set(bool value, bool animate)
        {
            _value = value;
            if (!animate) { _t = value ? 1f : 0f; Apply(); }
        }

        private void Update()
        {
            float target = _value ? 1f : 0f;
            if (Mathf.Approximately(_t, target)) return;
            _t = Mathf.MoveTowards(_t, target, Time.unscaledDeltaTime * 8f);
            Apply();
        }

        private void Apply()
        {
            if (_bg == null) return;
            _bg.color = Color.Lerp(new Color(1f, 1f, 1f, 0.16f), PauseUiKit.Accent, _t);
            _knob.anchoredPosition = new Vector2(Mathf.Lerp(21f, 63f, _t), 0f);
        }
    }
}
