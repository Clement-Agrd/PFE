using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace Core.InventorySystem.UI
{
    public sealed class InventorySlotUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, ISelectHandler, IDeselectHandler, ISubmitHandler, ICancelHandler
    {
        [Header("Composants Enfants")]
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text amountText;

        public static InventorySlotUI DraggedSlot { get; private set; }

        private InventoryUI _ownerUI;
        private int _slotIndex;
        private Canvas _canvas;
        private static InventorySlotUI _keyboardSource;
        private UnityEngine.UI.Outline _focus;
        private bool _selected;

        private void Awake()
        {
            if (!TryGetComponent<UnityEngine.UI.Button>(out var button)) button = gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = GetComponent<UnityEngine.UI.Image>();
            button.transition = UnityEngine.UI.Selectable.Transition.None;
            var image = GetComponent<UnityEngine.UI.Graphic>();
            if (image != null)
            {
                _focus = gameObject.AddComponent<UnityEngine.UI.Outline>();
                _focus.effectColor = new Color(1f,.8f,.3f);_focus.effectDistance = new Vector2(3,-3);_focus.enabled=false;
            }
        }
        private void OnDisable()
        {
            if (_keyboardSource == this) _keyboardSource = null;
            _selected = false;if (_focus != null) _focus.enabled=false;
        }
        public void OnSelect(BaseEventData _) { _selected=true;UpdateFocus();OnPointerEnter(null); }
        public void OnDeselect(BaseEventData _) { _selected=false;UpdateFocus();OnPointerExit(null); }
        private void UpdateFocus() { if(_focus!=null)_focus.enabled=_selected||_keyboardSource==this; }
        public void OnSubmit(BaseEventData _)
        {
            if (_ownerUI == null || _ownerUI.Inventory == null) return;
            if (_keyboardSource != null)
            {
                var source=_keyboardSource;_keyboardSource=null;source.UpdateFocus();
                if (source!=this && source._ownerUI==_ownerUI) _ownerUI.Inventory.SwapSlots(source._slotIndex,_slotIndex);
                OnPointerEnter(null);return;
            }
            if (_ownerUI.TryConsume(_slotIndex)) { OnPointerEnter(null);return; }
            var item=_ownerUI.Inventory.GetSlot(_slotIndex);
            if (item==null||item.IsEmpty||_ownerUI.Inventory.IsReserved(_slotIndex)) return;
            _keyboardSource=this;UpdateFocus();
        }
        public void OnCancel(BaseEventData data)
        {
            if (_keyboardSource==null) return;
            var source=_keyboardSource;_keyboardSource=null;source.UpdateFocus();data.Use();
        }

        private GameObject _dragIconObj;

        public void Init(InventoryUI ownerUI, int slotIndex, Canvas canvas)
        {
            _ownerUI = ownerUI;
            _slotIndex = slotIndex;
            _canvas = canvas;
        }

        public void SetSlot(ItemStack stack)
        {
            bool hasItem = stack != null && !stack.IsEmpty;

            if (iconImage != null)
            {
                bool hasSprite = hasItem && stack.Definition != null && stack.Definition.Icon != null;
                iconImage.sprite = hasSprite ? stack.Definition.Icon : null;
                iconImage.color = hasSprite ? Color.white : Color.clear;
                iconImage.enabled = hasSprite;
            }

            if (amountText != null)
            {
                // Niveau d'amélioration ("+3") prioritaire sur la quantité.
                bool upgraded = hasItem && stack.UpgradeLevel > 0;
                bool showAmount = upgraded || (hasItem && stack.Quantity > 1);
                amountText.text = upgraded ? $"+{stack.UpgradeLevel}" : (showAmount ? stack.Quantity.ToString() : "");
                amountText.enabled = showAmount;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_ownerUI == null || _ownerUI.Inventory == null || DraggedSlot != null) return;
            ItemDescriptionPanelUI panel = ItemDescriptionPanelUI.Instance;
            if (panel != null) panel.Show(_ownerUI.Inventory.GetSlot(_slotIndex));
        }

        public void OnPointerExit(PointerEventData eventData) => ItemDescriptionPanelUI.Instance?.Hide();

        /// <summary>Clic droit sur un consommable : le consomme (1 unité).</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Right || _ownerUI == null) return;
            if (_ownerUI.TryConsume(_slotIndex) && _ownerUI.Inventory.GetSlot(_slotIndex) == null)
                ItemDescriptionPanelUI.Instance?.Hide();
            else
                OnPointerEnter(eventData); // rafraîchit la bulle (quantité restante)
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            ItemDescriptionPanelUI.Instance?.Hide();
            if (_ownerUI == null || _ownerUI.Inventory == null) return;

            ItemStack stack = _ownerUI.Inventory.GetSlot(_slotIndex);
            if (stack == null || stack.IsEmpty || stack.Definition == null || stack.Definition.Icon == null) return;

            DraggedSlot = this;

            _dragIconObj = new GameObject("DragIcon", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            _dragIconObj.transform.SetParent(_canvas.transform, false);
            _dragIconObj.transform.SetAsLastSibling();

            Image img = _dragIconObj.GetComponent<Image>();
            img.sprite = stack.Definition.Icon;
            img.raycastTarget = false;

            CanvasGroup cg = _dragIconObj.GetComponent<CanvasGroup>();
            cg.blocksRaycasts = false;

            RectTransform rect = _dragIconObj.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(60, 60);

            if (iconImage != null) iconImage.enabled = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_dragIconObj != null)
            {
                _dragIconObj.transform.position = eventData.position;
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_dragIconObj != null) Destroy(_dragIconObj);

            if (_ownerUI == null || _ownerUI.Inventory == null) return;

            ItemStack stack = _ownerUI.Inventory.GetSlot(_slotIndex);
            if (stack == null || stack.IsEmpty) return;

            // Si lâché en dehors du panneau principal -> On jette au sol
            if (!_ownerUI.IsPointerInsidePanel(eventData.position))
            {
                _ownerUI.DropSlotItem(_slotIndex);
            }
            else
            {
                _ownerUI.RefreshSlot(_slotIndex);
            }

            DraggedSlot = null;
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (DraggedSlot == null || DraggedSlot == this) return;

            // Échange les items entre la case source et cette case
            _ownerUI.Inventory.SwapSlots(DraggedSlot._slotIndex, _slotIndex);
        }
    }
}