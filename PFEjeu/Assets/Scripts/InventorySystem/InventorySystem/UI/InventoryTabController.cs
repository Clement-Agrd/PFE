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

        [Header("Héros (optionnel)")]
        [SerializeField] private GameObject heroesContent;
        [SerializeField] private Button heroesTabButton;
        [SerializeField] private Core.BarracksSystem.HeroInventoryUI heroInventory;

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
            if (heroesTabButton != null) heroesTabButton.onClick.AddListener(ShowHeroesTab);
        }

        private int _page;
        private void OnEnable()
        {
            ShowInventoryTab();
            Core.UiTabNavigation.Register(transform, ChangeTab);
        }
        private void ChangeTab(int direction)
        {
            var contents = new[] { inventoryContent, villageContent, mapContent, heroesContent };
            var buttons = new[] { inventoryTabButton, villageTabButton, mapTabButton, heroesTabButton };
            for (int step = 1; step <= contents.Length; step++)
            {
                int page = (_page + direction * step + contents.Length * 2) % contents.Length;
                if (contents[page] == null) continue;
                switch (page) { case 0: ShowInventoryTab(); break; case 1: ShowVillageTab(); break; case 2: ShowMapTab(); break; default: ShowHeroesTab(); break; }
                if (buttons[page] != null) Core.UiNavigation.RememberSelection(buttons[page].gameObject);
                break;
            }
        }

        /// <summary>Vrai quand l'onglet Carte est celui affiché.</summary>
        public bool IsMapShown => mapContent != null && mapContent.activeSelf;

        public void ShowInventoryTab()
        {
            _page = 0;
            if (inventoryContent != null) inventoryContent.SetActive(true);
            if (villageContent != null) villageContent.SetActive(false);
            if (mapContent != null) mapContent.SetActive(false);
            if (heroesContent != null) heroesContent.SetActive(false);
            SetTabVisual(inventoryTabButton, true);
            SetTabVisual(villageTabButton, false);
            SetTabVisual(mapTabButton, false);
            SetTabVisual(heroesTabButton, false);
        }

        public void ShowVillageTab()
        {
            _page = 1;
            if (inventoryContent != null) inventoryContent.SetActive(false);
            if (villageContent != null) villageContent.SetActive(true);
            if (mapContent != null) mapContent.SetActive(false);
            if (heroesContent != null) heroesContent.SetActive(false);
            SetTabVisual(inventoryTabButton, false);
            SetTabVisual(villageTabButton, true);
            SetTabVisual(mapTabButton, false);
            SetTabVisual(heroesTabButton, false);
            villageOverviewPanel?.Refresh();
        }

        public void ShowMapTab()
        {
            _page = 2;
            if (inventoryContent != null) inventoryContent.SetActive(false);
            if (villageContent != null) villageContent.SetActive(false);
            if (mapContent != null) mapContent.SetActive(true);
            if (heroesContent != null) heroesContent.SetActive(false);
            SetTabVisual(inventoryTabButton, false);
            SetTabVisual(villageTabButton, false);
            SetTabVisual(mapTabButton, true);
            SetTabVisual(heroesTabButton, false);
        }

        public void ShowHeroesTab()
        {
            _page = 3;
            if (inventoryContent != null) inventoryContent.SetActive(false);
            if (villageContent != null) villageContent.SetActive(false);
            if (mapContent != null) mapContent.SetActive(false);
            if (heroesContent != null) heroesContent.SetActive(true);
            SetTabVisual(inventoryTabButton, false);
            SetTabVisual(villageTabButton, false);
            SetTabVisual(mapTabButton, false);
            SetTabVisual(heroesTabButton, true);
            heroInventory?.Refresh();
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
