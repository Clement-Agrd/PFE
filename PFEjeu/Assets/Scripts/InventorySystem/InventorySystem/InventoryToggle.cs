using UnityEngine;

namespace Core.InventorySystem.UI
{
    public class InventoryToggle : MonoBehaviour
    {
        [Header("Références UI")]
        [SerializeField] private GameObject inventoryCanvas;
        [SerializeField] private KeyCode toggleKey = KeyCode.I;

        private bool _isOpen;

        public bool IsOpen => _isOpen;

        private void Start()
        {
            // S'assure que le jeu démarre non masqué et non pausé
            SetInventoryState(false);
        }

        private void Update()
        {
            // Update continue de s'exécuter même quand Time.timeScale = 0
            if (Input.GetKeyDown(toggleKey))
            {
                SetInventoryState(!_isOpen);
            }
        }

        public void SetInventoryState(bool open)
        {
            _isOpen = open;

            // 1. Affichage de l'UI
            if (inventoryCanvas != null)
            {
                inventoryCanvas.SetActive(_isOpen);
            }

            // 2. Gestion du temps (Pause)
            Time.timeScale = _isOpen ? 0f : 1f;

            // 3. Liberation / Blocage du curseur pour le Drag & Drop
            Cursor.lockState = _isOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = _isOpen;
        }
    }
}