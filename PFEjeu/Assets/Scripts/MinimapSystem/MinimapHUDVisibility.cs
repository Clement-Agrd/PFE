using UnityEngine;
using Core.InventorySystem.UI;
using Core.Village;

namespace Core.Minimap
{
    /// <summary>
    /// Masque la minimap HUD dès qu'un autre panneau est ouvert (inventaire,
    /// fenêtres de bâtiment / taverne / exploration, vue village).
    /// Le futur panneau de pause ne doit PAS être ajouté à la liste : la
    /// minimap reste alors visible derrière lui.
    /// </summary>
    public sealed class MinimapHUDVisibility : MonoBehaviour
    {
        [Tooltip("Éléments UI à masquer/afficher ensemble (la minimap et son cadre).")]
        [SerializeField] private CanvasGroup[] targets;

        [Header("Panneaux qui masquent la minimap")]
        [SerializeField] private InventoryToggle inventoryToggle;
        [SerializeField] private VillageViewController villageView;
        [Tooltip("Panneaux dont l'affichage masque la minimap. Un panneau compte comme ouvert s'il est actif et, " +
                 "s'il a un CanvasGroup, que son alpha est > 0.")]
        [SerializeField] private GameObject[] blockingPanels;

        [SerializeField, Min(0f)] private float fadeSpeed = 8f;

        private float _alpha = 1f;

        public bool AnyPanelOpen()
        {
            if (inventoryToggle != null && inventoryToggle.IsOpen) return true;
            if (villageView != null && villageView.IsInVillageView) return true;
            if (blockingPanels == null) return false;
            foreach (var p in blockingPanels)
            {
                if (p == null || !p.activeInHierarchy) continue;
                var cg = p.GetComponent<CanvasGroup>();
                if (cg == null || cg.alpha > 0.05f) return true;
            }
            return false;
        }

        private void Update()
        {
            bool show = !AnyPanelOpen();
            float target = show ? 1f : 0f;
            _alpha = Mathf.MoveTowards(_alpha, target, fadeSpeed * Time.unscaledDeltaTime);

            if (targets != null)
            {
                foreach (var t in targets)
                {
                    if (t == null) continue;
                    t.alpha = _alpha;
                    t.blocksRaycasts = show;
                    t.interactable = show;
                }
            }

            var sys = MinimapSystem.Instance;
            if (sys != null && sys.MiniCamera != null)
                sys.MiniCamera.enabled = _alpha > 0.01f;
        }
    }
}
