using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Core.TweenSystem;
using Core;

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
        [SerializeField] private Transform defenseViewPose;

        [Header("Transition")]
        [SerializeField, Min(0.1f)] private float transitionDuration = 1.2f;
        [SerializeField] private Ease ease = Ease.InOutCubic;

        [Header("Scripts du joueur à figer (PAS la Camera elle-même)")]
        [SerializeField] private MonoBehaviour thirdPersonCamera;
        [SerializeField] private MonoBehaviour thirdPersonMotor;
        [SerializeField] private MonoBehaviour playerCombatController;
        [Tooltip("Autre contrôleur de la caméra à suspendre pendant la vue village (par exemple CinemachineBrain).")]
        [SerializeField] private MonoBehaviour cameraDriver;

        [Header("Sélection de bâtiment")]
        [SerializeField] private LayerMask buildingLayer = ~0;

        [Header("Input")]
        [SerializeField] private VillageInputReader input;

        public bool IsInVillageView { get; private set; }
        public bool IsDefenseMap { get; private set; }
        public bool CanSelectMap => IsInVillageView && !_isTransitioning;
        public Camera MapCamera => targetCamera;
        public event Action OnMapAreaChanged;

        public event Action<Building> OnBuildingClicked;
        public event Action OnEnteredVillageView;
        public event Action OnExitedVillageView;

        private Vector3 _savedPosition;
        private Quaternion _savedRotation;
        private bool _isTransitioning;
        private bool _cameraWasEnabled, _motorWasEnabled, _combatWasEnabled, _driverWasEnabled;

        private void OnEnable()
        {
            if (input == null) return;
            input.ExitPressed += HandleExitPressed;
            input.ClickPressed += HandleClickPressed;
        }

        private void OnDisable()
        {
            if (input == null) return;
            input.ExitPressed -= HandleExitPressed;
            input.ClickPressed -= HandleClickPressed;
        }

        private void HandleExitPressed()
        {
            if (!IsInVillageView || _isTransitioning) return;
            ExitVillageView();
        }

        private void HandleClickPressed()
        {
            if (!IsInVillageView || _isTransitioning) return;
            TrySelectBuilding();
        }

        /// <summary>À appeler pour entrer dans la vue village (table, bouton...).</summary>
        public void EnterVillageView()
        {
            if (IsInVillageView || _isTransitioning) return;
            if (targetCamera == null || villageViewPose == null) return;

            IsDefenseMap = false;
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
                    // Après la transition seulement : Tween tourne sur Time.deltaTime
                    // (scaled), donc figer le temps plus tôt bloquerait l'animation.
                    GameFreeze.RequestPause(this);
                    GameFreeze.RequestCursorUnlock(this);
                    OnEnteredVillageView?.Invoke();
                });
        }

        /// <summary>Revient à la vue joueur. Peut aussi être appelée par un bouton UI.</summary>
        public void ExitVillageView()
        {
            if (!IsInVillageView || _isTransitioning || targetCamera == null) return;

            IsInVillageView = false;
            OnMapAreaChanged?.Invoke();
            // Libère avant de relancer le tween retour : sinon le temps resterait
            // figé et l'animation ne progresserait jamais.
            GameFreeze.ReleasePause(this);
            GameFreeze.ReleaseCursorUnlock(this);

            _isTransitioning = true;
            targetCamera.transform.KillTweens();

            targetCamera.transform.TweenMove(_savedPosition, transitionDuration).SetEase(ease).SetUnscaledTime(true);
            targetCamera.transform.TweenRotate(_savedRotation, transitionDuration)
                .SetEase(ease).SetUnscaledTime(true)
                .OnComplete(() =>
                {
                    _isTransitioning = false;
                    SetPlayerControlEnabled(true);
                    OnExitedVillageView?.Invoke();
                });
        }

        public void ShowDefenseMap() => ChangeMap(true);
        public void ShowVillageMap() => ChangeMap(false);
        private void ChangeMap(bool defenses)
        {
            var pose = defenses ? defenseViewPose : villageViewPose;
            if (!CanSelectMap || pose == null || targetCamera == null) return;
            OnMapAreaChanged?.Invoke();
            IsDefenseMap = defenses;
            _isTransitioning = true;
            targetCamera.transform.KillTweens();
            targetCamera.transform.TweenMove(pose.position, transitionDuration).SetEase(ease).SetUnscaledTime(true);
            targetCamera.transform.TweenRotate(pose.rotation, transitionDuration).SetEase(ease).SetUnscaledTime(true)
                .OnComplete(() => _isTransitioning = false);
        }
        public void SelectMapRay(Ray ray)
        {
            if (!CanSelectMap) return;
            if (Physics.Raycast(ray, out var hit, 500f, buildingLayer))
            {
                var slot = hit.collider.GetComponentInParent<Core.TowerDefense.DefenseBuildSlot>();
                if (slot != null && !slot.IsOccupied) { slot.OpenFromVillageView(); return; }
                if (hit.collider.GetComponentInParent<Building>() is Building building)
                    OnBuildingClicked?.Invoke(building);
            }
        }

        private void SetPlayerControlEnabled(bool enabled)
        {
            if (!enabled)
            {
                _cameraWasEnabled = thirdPersonCamera != null && thirdPersonCamera.enabled;
                _motorWasEnabled = thirdPersonMotor != null && thirdPersonMotor.enabled;
                _combatWasEnabled = playerCombatController != null && playerCombatController.enabled;
                _driverWasEnabled = cameraDriver != null && cameraDriver.enabled;
            }
            if (thirdPersonCamera != null) thirdPersonCamera.enabled = enabled && _cameraWasEnabled;
            if (thirdPersonMotor != null) thirdPersonMotor.enabled = enabled && _motorWasEnabled;
            if (playerCombatController != null) playerCombatController.enabled = enabled && _combatWasEnabled;
            if (cameraDriver != null) cameraDriver.enabled = enabled && _driverWasEnabled;
        }

        private void TrySelectBuilding()
        {
            // Un clic sur l'UI (bouton du panneau...) ne doit pas sélectionner le bâtiment derrière.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            if (Mouse.current == null) return;
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = targetCamera.ScreenPointToRay(mousePos);

            SelectMapRay(ray);
        }
    }
}
