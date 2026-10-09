using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Core.Village.UI;

namespace Core.InventorySystem.UI
{
    /// <summary>
    /// Bascule entre le contenu "Inventaire" et "Village" à l'intérieur de la
    /// même fenêtre d'inventaire. Vit sur InventoryPanel : OnEnable (donc
    /// chaque fois que l'inventaire s'ouvre) revient toujours sur l'onglet
    /// Inventaire.
    /// </summary>
    public sealed class InventoryTabController : MonoBehaviour
    {
        [SerializeField] private GameObject inventoryContent;
        [SerializeField] private GameObject villageContent;
        [SerializeField] private Button inventoryTabButton;
        [SerializeField] private Button villageTabButton;
        [SerializeField] private VillageOverviewPanelUI villageOverviewPanel;

        [Header("Carte (optionnel)")]
        [SerializeField] private GameObject mapContent;
        [SerializeField] private Button mapTabButton;

        [Header("Couleurs")]
        [SerializeField] private Color activeTabColor = new(0.95f, 0.78f, 0.35f);
        [SerializeField] private Color inactiveTabColor = new(0.3f, 0.32f, 0.36f);
        [SerializeField, Min(1f)] private float activeTabScale = 1.12f;
        [SerializeField] private Color activeTextColor = new(0.1f, 0.1f, 0.12f);
        [SerializeField] private Color inactiveTextColor = new(0.85f, 0.87f, 0.9f);

        private void Awake()
        {
            if (inventoryTabButton != null) inventoryTabButton.onClick.AddListener(ShowInventoryTab);
            if (villageTabButton != null) villageTabButton.onClick.AddListener(ShowVillageTab);
            if (mapTabButton != null) mapTabButton.onClick.AddListener(ShowMapTab);
        }

        private void OnEnable() => ShowInventoryTab();

        /// <summary>Vrai quand l'onglet Carte est celui affiché.</summary>
        public bool IsMapShown => mapContent != null && mapContent.activeSelf;

        public void ShowInventoryTab()
        {
            if (inventoryContent != null) inventoryContent.SetActive(true);
            if (villageContent != null) villageContent.SetActive(false);
            if (mapContent != null) mapContent.SetActive(false);
            SetTabVisual(inventoryTabButton, true);
            SetTabVisual(villageTabButton, false);
            SetTabVisual(mapTabButton, false);
        }

        public void ShowVillageTab()
        {
            if (inventoryContent != null) inventoryContent.SetActive(false);
            if (villageContent != null) villageContent.SetActive(true);
            if (mapContent != null) mapContent.SetActive(false);
            SetTabVisual(inventoryTabButton, false);
            SetTabVisual(villageTabButton, true);
            SetTabVisual(mapTabButton, false);
            villageOverviewPanel?.Refresh();
        }

        public void ShowMapTab()
        {
            if (inventoryContent != null) inventoryContent.SetActive(false);
            if (villageContent != null) villageContent.SetActive(false);
            if (mapContent != null) mapContent.SetActive(true);
            SetTabVisual(inventoryTabButton, false);
            SetTabVisual(villageTabButton, false);
            SetTabVisual(mapTabButton, true);
        }

        private void SetTabVisual(Button button, bool active)
        {
            if (button == null) return;
            Image image = button.GetComponent<Image>();
            if (image != null) image.color = active ? activeTabColor : inactiveTabColor;
            button.transform.localScale = Vector3.one * (active ? activeTabScale : 1f);
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            if (label != null) label.color = active ? activeTextColor : inactiveTextColor;
        }
    }
}
