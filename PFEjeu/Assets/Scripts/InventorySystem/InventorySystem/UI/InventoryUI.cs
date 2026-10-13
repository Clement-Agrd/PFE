using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Core;

namespace Core.InventorySystem.UI
{
    public sealed class InventoryUI : MonoBehaviour
    {
        [Header("Données")]
        [SerializeField] private InventoryHolder inventoryHolder;

        [Header("UI")]
        [SerializeField] private GameObject panel;
        [SerializeField] private Transform slotsParent;
        [SerializeField] private InventorySlotUI slotPrefab;
        [Tooltip("Conteneur des cases réservées des armes de base, affiché à côté de la grille. Optionnel.")]
        [SerializeField] private Transform baseSlotsParent;

        [Header("Raccourci (optionnel)")]
        [Tooltip("Décoche si ce panneau est piloté uniquement de l'extérieur (ex. la vue village), sans touche dédiée.")]
        [SerializeField] private bool useToggleKey = true;
        [SerializeField] private InputAction toggleAction = new("ToggleInventory", InputActionType.Button, "<Keyboard>/tab");
        [Tooltip("Fige le temps quand ouvert (à désactiver pour un inventaire affiché EN PLUS d'une autre vue, comme la vue village).")]
        [SerializeField] private bool pauseTimeWhenOpen = true;

        private Inventory _inventory;
        private InventorySlotUI[] _slots;
        private bool _isOpen = false;
        private int _openedFrame = -1;

        public Inventory Inventory => _inventory;
        public bool IsOpen => _isOpen;

        private void Start()
        {
            if (inventoryHolder == null || slotsParent == null || slotPrefab == null) return;

            _inventory = inventoryHolder.Inventory;
            if (_inventory == null) return;

            BuildSlots();
            _inventory.OnSlotChanged += RefreshSlot;
            _inventory.OnChanged += RefreshAll;

            SetOpen(false);
        }

        private void OnEnable() => toggleAction.Enable();

        private void OnDisable() => toggleAction.Disable();

        private void OnDestroy()
        {
            if (_inventory == null) return;
            _inventory.OnSlotChanged -= RefreshSlot;
            _inventory.OnChanged -= RefreshAll;
        }

        private void Update()
        {
            if (!useToggleKey) return;

            if (toggleAction.WasPressedThisFrame())
                SetOpen(!_isOpen);
            else if (_isOpen && UiCloseKeys.Pressed(_openedFrame))
                SetOpen(false);
        }

        public void SetOpen(bool open)
        {
            if (open && !_isOpen) _openedFrame = Time.frameCount;
            if (!open) ItemDescriptionPanelUI.Instance?.Hide();
            _isOpen = open;
            if (panel != null) panel.SetActive(_isOpen);

            if (open)
            {
                if (pauseTimeWhenOpen) GameFreeze.RequestPause(this);
                GameFreeze.RequestCursorUnlock(this);
            }
            else
            {
                if (pauseTimeWhenOpen) GameFreeze.ReleasePause(this);
                GameFreeze.ReleaseCursorUnlock(this);
            }

            if (open) RefreshAll();
        }

        private void BuildSlots()
        {
            if (slotsParent.GetComponent<GridLayoutGroup>() == null)
            {
                GridLayoutGroup grid = slotsParent.gameObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(75, 75);
                grid.spacing = new Vector2(10, 10);
            }

            foreach (Transform child in slotsParent) Destroy(child.gameObject);
            if (baseSlotsParent != null)
                foreach (Transform child in baseSlotsParent) Destroy(child.gameObject);

            Canvas mainCanvas = GetComponentInParent<Canvas>();

            _slots = new InventorySlotUI[_inventory.Capacity];
            for (int i = 0; i < _inventory.Capacity; i++)
            {
                bool reserved = _inventory.IsReserved(i);
                // Les armures de base sont déjà montrées par les ArmorSlotUI : pas de doublon ici.
                if (reserved && _inventory.GetSlot(i)?.Definition.Category == ItemCategory.Armor) continue;
                Transform parent = reserved ? baseSlotsParent : slotsParent;
                if (parent == null) continue;

                _slots[i] = Instantiate(slotPrefab, parent);
                _slots[i].Init(this, i, mainCanvas);
            }
        }

        /// <summary>Consomme une unité de la case si c'est un consommable (clic droit).</summary>
        public bool TryConsume(int slotIndex)
            => ConsumableUser.For(inventoryHolder)?.Consume(slotIndex) ?? false;

        public bool IsPointerInsidePanel(Vector2 screenPosition)
        {
            if (panel == null) return false;
            RectTransform rectTransform = panel.GetComponent<RectTransform>();
            return RectTransformUtility.RectangleContainsScreenPoint(rectTransform, screenPosition);
        }

        public void DropSlotItem(int slotIndex)
        {
            if (_inventory.IsReserved(slotIndex)) { RefreshSlot(slotIndex); return; }

            ItemStack stack = _inventory.GetSlot(slotIndex);
            if (stack == null || stack.IsEmpty) return;

            if (stack.Definition.WorldPrefab != null && inventoryHolder != null)
            {
                Vector3 spawnPos = inventoryHolder.transform.position + inventoryHolder.transform.forward * 1.5f + Vector3.up * 0.5f;
                GameObject worldObj = Instantiate(stack.Definition.WorldPrefab, spawnPos, Quaternion.identity);

                if (worldObj.TryGetComponent<ItemPickup>(out var pickup))
                {
                    pickup.Setup(stack.Definition, stack.Quantity);
                }
            }

            _inventory.RemoveAt(slotIndex);
        }

        public void RefreshAll()
        {
            if (_slots == null) return;
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i] != null) _slots[i].SetSlot(_inventory.GetSlot(i));
        }

        public void RefreshSlot(int index)
        {
            if (_slots == null || index < 0 || index >= _slots.Length || _slots[index] == null) return;
            _slots[index].SetSlot(_inventory.GetSlot(index));
        }
    }
}