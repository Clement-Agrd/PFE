using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Core.TweenSystem;

namespace Core.Village
{
    /// <summary>
    /// Bascule la caméra entre la vue joueur et une vue du dessus du village.
    /// EnterVillageView() est appelée par VillageTableTrigger (ou tout autre
    /// déclencheur). Le joueur est figé pendant la vue village ; Échap ou un
    /// clic sur un bouton "Sortir" y met fin, la caméra retrouve EXACTEMENT
    /// sa pose d'origine (pas de saut visuel).
    /// </summary>
    public sealed class VillageViewController : MonoBehaviour
    {
        [Header("Caméra")]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Transform villageViewPose;

        [Header("Transition")]
        [SerializeField, Min(0.1f)] private float transitionDuration = 1.2f;
        [SerializeField] private Ease ease = Ease.InOutCubic;

        [Header("Scripts du joueur à figer (PAS la Camera elle-même)")]
        [SerializeField] private MonoBehaviour thirdPersonCamera;
        [SerializeField] private MonoBehaviour thirdPersonMotor;
        [SerializeField] private MonoBehaviour playerCombatController;

        [Header("Sélection de bâtiment")]
        [SerializeField] private LayerMask buildingLayer = ~0;

        public bool IsInVillageView { get; private set; }

        public event Action<Building> OnBuildingClicked;
        public event Action OnEnteredVillageView;
        public event Action OnExitedVillageView;

        private Vector3 _savedPosition;
        private Quaternion _savedRotation;
        private bool _isTransitioning;

        private void Update()
        {
            if (!IsInVillageView || _isTransitioning) return;

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                ExitVillageView();

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                TrySelectBuilding();
        }

        /// <summary>À appeler pour entrer dans la vue village (table, bouton...).</summary>
        public void EnterVillageView()
        {
            if (IsInVillageView || _isTransitioning) return;
            if (targetCamera == null || villageViewPose == null) return;

            _savedPosition = targetCamera.transform.position;
            _savedRotation = targetCamera.transform.rotation;

            SetPlayerControlEnabled(false);
            targetCamera.transform.KillTweens();

            _isTransitioning = true;
            targetCamera.transform.TweenMove(villageViewPose.position, transitionDuration).SetEase(ease);
            targetCamera.transform.TweenRotate(villageViewPose.rotation, transitionDuration)
                .SetEase(ease)
                .OnComplete(() =>
                {
                    _isTransitioning = false;
                    IsInVillageView = true;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    OnEnteredVillageView?.Invoke();
                });
        }

        /// <summary>Revient à la vue joueur. Peut aussi être appelée par un bouton UI.</summary>
        public void ExitVillageView()
        {
            if (!IsInVillageView || _isTransitioning || targetCamera == null) return;

            IsInVillageView = false;
            _isTransitioning = true;
            targetCamera.transform.KillTweens();

            targetCamera.transform.TweenMove(_savedPosition, transitionDuration).SetEase(ease);
            targetCamera.transform.TweenRotate(_savedRotation, transitionDuration)
                .SetEase(ease)
                .OnComplete(() =>
                {
                    _isTransitioning = false;
                    SetPlayerControlEnabled(true);
                    OnExitedVillageView?.Invoke();
                });
        }

        private void SetPlayerControlEnabled(bool enabled)
        {
            if (thirdPersonCamera != null) thirdPersonCamera.enabled = enabled;
            if (thirdPersonMotor != null) thirdPersonMotor.enabled = enabled;
            if (playerCombatController != null) playerCombatController.enabled = enabled;
        }

        private void TrySelectBuilding()
        {
            // Un clic sur l'UI (bouton du panneau...) ne doit pas sélectionner le bâtiment derrière.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = targetCamera.ScreenPointToRay(mousePos);

            if (Physics.Raycast(ray, out RaycastHit hit, 500f, buildingLayer))
            {
                if (hit.collider.GetComponentInParent<Building>() is Building building)
                    OnBuildingClicked?.Invoke(building);
            }
        }
    }
}