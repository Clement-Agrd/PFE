using UnityEngine;
using UnityEngine.UI;

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

        [Header("Raccourci")]
        [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

        private Inventory _inventory;
        private InventorySlotUI[] _slots;
        private bool _isOpen = false;

        public Inventory Inventory => _inventory;

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

        private void OnDestroy()
        {
            if (_inventory == null) return;
            _inventory.OnSlotChanged -= RefreshSlot;
            _inventory.OnChanged -= RefreshAll;
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                SetOpen(!_isOpen);
            }
        }

        public void SetOpen(bool open)
        {
            _isOpen = open;
            if (panel != null) panel.SetActive(_isOpen);

            Time.timeScale = open ? 0f : 1f;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;

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

            Canvas mainCanvas = GetComponentInParent<Canvas>();

            _slots = new InventorySlotUI[_inventory.Capacity];
            for (int i = 0; i < _inventory.Capacity; i++)
            {
                _slots[i] = Instantiate(slotPrefab, slotsParent);
                _slots[i].Init(this, i, mainCanvas);
            }
        }

        public bool IsPointerInsidePanel(Vector2 screenPosition)
        {
            if (panel == null) return false;
            RectTransform rectTransform = panel.GetComponent<RectTransform>();
            return RectTransformUtility.RectangleContainsScreenPoint(rectTransform, screenPosition);
        }

        public void DropSlotItem(int slotIndex)
        {
            ItemStack stack = _inventory.GetSlot(slotIndex);
            if (stack == null || stack.IsEmpty) return;

            // Spawne l'objet 3D au sol devant le joueur s'il existe
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
                _slots[i].SetSlot(_inventory.GetSlot(i));
        }

        public void RefreshSlot(int index)
        {
            if (_slots == null || index < 0 || index >= _slots.Length) return;
            _slots[index].SetSlot(_inventory.GetSlot(index));
        }
    }
}