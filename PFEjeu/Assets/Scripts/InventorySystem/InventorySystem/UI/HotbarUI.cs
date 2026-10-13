using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using ProfessionalTPS;
using Core.TrapSystem;

namespace Core.InventorySystem.UI
{
    /// <summary>
    /// Barre d'accès rapide façon Minecraft, en bas de l'écran : les modes de combat, puis tous les
    /// consommables et les pièges de l'inventaire. La molette (ou 1-9) change la case sélectionnée :
    /// sur un mode de combat, ça change de mode ; sur un consommable ou un piège, la touche
    /// « utiliser » le mange / lance son placement. Entièrement construite en code et pilotée par
    /// le nouvel Input System : à poser sur le joueur, rien d'autre à brancher.
    /// </summary>
    public sealed class HotbarUI : MonoBehaviour
    {
        [Header("Références (vide = même GameObject)")]
        [SerializeField] private InventoryHolder inventoryHolder;
        [SerializeField] private PlayerCombatController combat;
        [SerializeField] private TrapPlacer trapPlacer;

        [Header("Icônes des modes de combat")]
        [SerializeField] private Sprite meleeIcon;
        [SerializeField] private Sprite bowIcon;
        [SerializeField] private Sprite magicIcon;
        [SerializeField] private Sprite axeIcon;
        [SerializeField] private Sprite pickaxeIcon;

        [Header("Entrées")]
        [SerializeField] private InputAction scrollAction = new("HotbarScroll", InputActionType.Value, "<Mouse>/scroll/y");
        [SerializeField] private InputAction useAction = CreateUseAction();

        [Header("Affichage")]
        [SerializeField, Range(4, 14)] private int visibleSlots = 10;
        [SerializeField, Min(40f)] private float slotSize = 84f;
        [SerializeField, Min(0f)] private float spacing = 8f;
        [SerializeField, Min(0f)] private float bottomMargin = 36f;
        [SerializeField] private Color slotColor = new(0.08f, 0.09f, 0.12f, 0.72f);
        [SerializeField] private Color selectedColor = new(0.95f, 0.78f, 0.35f, 1f);

        private enum Kind { Mode, Item }

        private readonly struct Entry
        {
            public readonly Kind Kind;
            public readonly CombatMode Mode;
            public readonly int InvSlot;

            public Entry(Kind kind, CombatMode mode, int invSlot) { Kind = kind; Mode = mode; InvSlot = invSlot; }
        }

        private sealed class SlotView
        {
            public RectTransform Rect;
            public Image Frame, Icon;
            public TMP_Text Count, Label, KeyLabel;
        }

        private static readonly CombatMode[] Modes =
            { CombatMode.Melee, CombatMode.Bow, CombatMode.Magic, CombatMode.Axe, CombatMode.Pickaxe };

        private readonly List<Entry> _entries = new();
        private readonly List<SlotView> _views = new();
        private Inventory _inventory;
        private ConsumableUser _consumer;
        private int _selected;
        private int _first;
        private RectTransform _bar;
        private TMP_Text _title;
        private TMP_Text _hint;
        private GameObject _canvasGo;

        private static InputAction CreateUseAction()
        {
            var a = new InputAction("HotbarUse", InputActionType.Button);
            a.AddBinding("<Keyboard>/f");
            a.AddBinding("<Gamepad>/buttonWest");
            return a;
        }

        private void Awake()
        {
            if (inventoryHolder == null) inventoryHolder = GetComponent<InventoryHolder>();
            if (combat == null) combat = GetComponent<PlayerCombatController>();
            if (trapPlacer == null) trapPlacer = GetComponent<TrapPlacer>();
        }

        private void OnEnable()
        {
            scrollAction.Enable();
            useAction.Enable();
            if (combat != null) combat.ModeChanged += HandleModeChanged;
        }

        private void OnDisable()
        {
            scrollAction.Disable();
            useAction.Disable();
            if (combat != null) combat.ModeChanged -= HandleModeChanged;
        }

        private void Start()
        {
            _inventory = inventoryHolder != null ? inventoryHolder.Inventory : null;
            _consumer = ConsumableUser.For(inventoryHolder);
            BuildCanvas();
            if (_inventory != null) _inventory.OnChanged += Rebuild;
            Rebuild();
            if (combat != null) SelectEntryForMode(combat.CurrentMode);
            Redraw();
        }

        private void OnDestroy()
        {
            if (_inventory != null) _inventory.OnChanged -= Rebuild;
            if (_canvasGo != null) Destroy(_canvasGo);
        }

        private void Update()
        {
            bool blocked = GameFreeze.IsCursorUnlocked || (trapPlacer != null && (trapPlacer.IsWheelOpen || trapPlacer.IsPlacing));
            if (_canvasGo != null) _canvasGo.SetActive(!GameFreeze.IsCursorUnlocked);
            if (blocked || _entries.Count == 0) return;

            float scroll = scrollAction.ReadValue<float>();
            if (!Mathf.Approximately(scroll, 0f))
                Select(Wrap(_selected + (scroll > 0f ? -1 : 1)));

            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                for (int i = 0; i < 9; i++)
                {
                    if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame)
                    {
                        int index = _first + i;
                        if (i < visibleSlots && index < _entries.Count) Select(index);
                    }
                }
            }

            if (useAction.WasPressedThisFrame()) UseSelected();
        }

        #region Sélection

        private int Wrap(int index) => (index % _entries.Count + _entries.Count) % _entries.Count;

        private void Select(int index)
        {
            if (index == _selected) return;
            _selected = index;

            Entry e = _entries[_selected];
            if (e.Kind == Kind.Mode && combat != null)
            {
                combat.SelectMode(e.Mode);
                // Changement refusé (action en cours) : la sélection suit le vrai mode.
                if (combat.CurrentMode != e.Mode) SelectEntryForMode(combat.CurrentMode);
            }
            Redraw();
        }

        private void HandleModeChanged(CombatMode mode)
        {
            SelectEntryForMode(mode);
            Redraw();
        }

        private void SelectEntryForMode(CombatMode mode)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Kind == Kind.Mode && _entries[i].Mode == mode) { _selected = i; return; }
        }

        private void UseSelected()
        {
            if (_selected < 0 || _selected >= _entries.Count) return;
            Entry e = _entries[_selected];
            if (e.Kind != Kind.Item) return;

            ItemStack stack = _inventory.GetSlot(e.InvSlot);
            if (stack == null || stack.IsEmpty) return;

            if (stack.Definition.IsConsumable) _consumer?.Consume(e.InvSlot);
            else if (stack.Definition.Category == ItemCategory.Trap) trapPlacer?.TryBeginPlacement(e.InvSlot);
        }

        #endregion

        #region Contenu

        private void Rebuild()
        {
            // On retient l'entrée sélectionnée pour la retrouver après la reconstruction.
            Entry? previous = _selected >= 0 && _selected < _entries.Count ? _entries[_selected] : null;
            ItemDefinition previousItem = previous is { Kind: Kind.Item } p && _inventory.GetSlot(p.InvSlot) != null
                ? _inventory.GetSlot(p.InvSlot).Definition : null;

            _entries.Clear();
            foreach (CombatMode mode in Modes)
                if (combat != null && combat.HasMode(mode)) _entries.Add(new Entry(Kind.Mode, mode, -1));

            if (_inventory != null)
            {
                for (int i = 0; i < _inventory.Capacity; i++)
                {
                    ItemStack s = _inventory.GetSlot(i);
                    if (s == null || s.IsEmpty) continue;
                    if (s.Definition.IsConsumable || s.Definition.Category == ItemCategory.Trap)
                        _entries.Add(new Entry(Kind.Item, default, i));
                }
            }

            int found = -1;
            if (previous.HasValue)
            {
                for (int i = 0; i < _entries.Count && found < 0; i++)
                {
                    Entry e = _entries[i];
                    if (previous.Value.Kind == Kind.Mode && e.Kind == Kind.Mode && e.Mode == previous.Value.Mode) found = i;
                    if (previous.Value.Kind == Kind.Item && e.Kind == Kind.Item && previousItem != null
                        && _inventory.GetSlot(e.InvSlot).Definition == previousItem) found = i;
                }
            }
            _selected = found >= 0 ? found : Mathf.Clamp(_selected, 0, Mathf.Max(0, _entries.Count - 1));
            if (found < 0 && combat != null) SelectEntryForMode(combat.CurrentMode);
            Redraw();
        }

        #endregion

        #region Affichage

        private void BuildCanvas()
        {
            _canvasGo = new GameObject("HotbarCanvas", typeof(Canvas), typeof(CanvasScaler));
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _bar = NewRect("Bar", _canvasGo.transform);
            _bar.anchorMin = _bar.anchorMax = _bar.pivot = new Vector2(0.5f, 0f);
            _bar.anchoredPosition = new Vector2(0f, bottomMargin);

            _title = NewText("Title", _bar, 26, TextAlignmentOptions.Bottom);
            _hint = NewText("Hint", _bar, 20, TextAlignmentOptions.Bottom);
            _hint.color = new Color(0.75f, 0.77f, 0.82f);

            for (int i = 0; i < visibleSlots; i++) _views.Add(BuildSlot(i));
        }

        private SlotView BuildSlot(int i)
        {
            var v = new SlotView { Rect = NewRect($"Slot{i}", _bar) };
            v.Rect.anchorMin = v.Rect.anchorMax = new Vector2(0f, 0f);
            v.Rect.pivot = new Vector2(0.5f, 0.5f);
            v.Rect.sizeDelta = new Vector2(slotSize, slotSize);

            v.Frame = v.Rect.gameObject.AddComponent<Image>();
            v.Frame.raycastTarget = false;

            var iconRect = NewRect("Icon", v.Rect);
            iconRect.anchorMin = Vector2.zero; iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(8f, 8f); iconRect.offsetMax = new Vector2(-8f, -8f);
            v.Icon = iconRect.gameObject.AddComponent<Image>();
            v.Icon.raycastTarget = false;
            v.Icon.preserveAspect = true;

            v.Label = NewText("Label", v.Rect, 18, TextAlignmentOptions.Center);
            Stretch(v.Label.rectTransform);
            v.Count = NewText("Count", v.Rect, 22, TextAlignmentOptions.BottomRight);
            Stretch(v.Count.rectTransform, 6f);
            v.KeyLabel = NewText("Key", v.Rect, 16, TextAlignmentOptions.TopLeft);
            Stretch(v.KeyLabel.rectTransform, 6f);
            v.KeyLabel.color = new Color(1f, 1f, 1f, 0.55f);
            return v;
        }

        private void Redraw()
        {
            if (_bar == null) return;

            int count = _entries.Count;
            int shown = Mathf.Min(visibleSlots, count);
            // Fenêtre glissante centrée sur la sélection quand il y a plus d'entrées que de cases.
            _first = Mathf.Clamp(_selected - shown / 2, 0, Mathf.Max(0, count - shown));

            const float groupGap = 18f;
            float x = 0f;
            bool gapDone = false;
            for (int i = 0; i < _views.Count; i++)
            {
                SlotView v = _views[i];
                int index = _first + i;
                bool used = i < shown;
                v.Rect.gameObject.SetActive(used);
                if (!used) continue;

                Entry e = _entries[index];
                if (!gapDone && e.Kind == Kind.Item && i > 0) { x += groupGap; gapDone = true; }
                v.Rect.anchoredPosition = new Vector2(x + slotSize * 0.5f, slotSize * 0.5f);
                x += slotSize + spacing;

                bool selected = index == _selected;
                v.Frame.color = selected ? selectedColor : slotColor;
                v.Rect.localScale = Vector3.one * (selected ? 1.1f : 1f);
                v.KeyLabel.text = i < 9 ? (i + 1).ToString() : "";
                Fill(v, e);
            }

            float width = Mathf.Max(0f, x - spacing);
            _bar.sizeDelta = new Vector2(width, slotSize);
            PlaceLabel(_title, slotSize + 40f);
            PlaceLabel(_hint, slotSize + 12f);
            UpdateCaption();
        }

        private void Fill(SlotView v, Entry e)
        {
            Sprite icon = null;
            string label = "";
            string count = "";

            if (e.Kind == Kind.Mode)
            {
                icon = ModeIcon(e.Mode);
                if (icon == null) label = ModeName(e.Mode);
            }
            else
            {
                ItemStack s = _inventory.GetSlot(e.InvSlot);
                if (s != null && !s.IsEmpty)
                {
                    icon = s.Definition.Icon;
                    if (icon == null) label = s.Definition.DisplayName;
                    if (s.UpgradeLevel > 0) count = $"+{s.UpgradeLevel}";
                    else if (s.Quantity > 1) count = s.Quantity.ToString();
                }
            }

            v.Icon.sprite = icon;
            v.Icon.enabled = icon != null;
            v.Icon.color = Color.white;
            v.Label.text = label;
            v.Count.text = count;
        }

        private void UpdateCaption()
        {
            if (_selected < 0 || _selected >= _entries.Count) { _title.text = ""; _hint.text = ""; return; }

            Entry e = _entries[_selected];
            if (e.Kind == Kind.Mode)
            {
                _title.text = ModeName(e.Mode);
                _hint.text = "";
                return;
            }

            ItemStack s = _inventory.GetSlot(e.InvSlot);
            if (s == null || s.IsEmpty) { _title.text = ""; _hint.text = ""; return; }
            _title.text = s.Definition.DisplayName;
            _hint.text = s.Definition.IsConsumable ? "[F] Consommer" : "[F] Poser (clic molette pour valider)";
        }

        private Sprite ModeIcon(CombatMode mode) => mode switch
        {
            CombatMode.Melee => meleeIcon,
            CombatMode.Bow => bowIcon,
            CombatMode.Magic => magicIcon,
            CombatMode.Axe => axeIcon,
            _ => pickaxeIcon
        };

        private static string ModeName(CombatMode mode) => mode switch
        {
            CombatMode.Melee => "Mêlée",
            CombatMode.Bow => "Arc",
            CombatMode.Magic => "Magie",
            CombatMode.Axe => "Hache",
            _ => "Pioche"
        };

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static TMP_Text NewText(string name, Transform parent, float size, TextAlignmentOptions align)
        {
            RectTransform rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.alignment = align;
            t.color = Color.white;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        private static void Stretch(RectTransform rt, float margin = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(margin, margin); rt.offsetMax = new Vector2(-margin, -margin);
        }

        private void PlaceLabel(TMP_Text t, float y)
        {
            RectTransform rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(800f, 30f);
            rt.anchoredPosition = new Vector2(0f, y);
        }

        #endregion
    }
}
