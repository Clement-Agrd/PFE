using UnityEngine;

namespace ProfessionalTPS
{
    /// <summary>Affiche les armes équipées sans modifier les règles de combat.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(600)]
    public sealed class PlayerWeaponVisuals : MonoBehaviour
    {
        [SerializeField] private PlayerCombatController combat;
        [SerializeField] private GameObject sword;
        [SerializeField] private GameObject bow;
        [SerializeField] private LineRenderer bowstring;
        [SerializeField] private Transform upperTip;
        [SerializeField] private Transform lowerTip;
        [SerializeField] private Transform nockPoint;
        [SerializeField] private Transform stringHand;
        [SerializeField] private Transform upperLimb;
        [SerializeField] private Transform lowerLimb;

        private Vector3 _nockRest;
        private Quaternion _upperRest, _lowerRest;

        private void Awake()
        {
            if (combat == null) combat = GetComponent<PlayerCombatController>();
            if (nockPoint != null) _nockRest = nockPoint.localPosition;
            if (upperLimb != null) _upperRest = upperLimb.localRotation;
            if (lowerLimb != null) _lowerRest = lowerLimb.localRotation;
        }

        private void OnEnable()
        {
            if (combat != null) combat.ModeChanged += Refresh;
            Refresh(combat != null ? combat.CurrentMode : CombatMode.Magic);
        }

        private void Start() => Refresh(combat != null ? combat.CurrentMode : CombatMode.Magic);

        private void OnDisable()
        {
            if (combat != null) combat.ModeChanged -= Refresh;
            if (sword != null) sword.SetActive(false);
            if (bow != null) bow.SetActive(false);
            ResetBow();
        }

        private void Refresh(CombatMode mode)
        {
            if (sword != null) sword.SetActive(mode == CombatMode.Melee);
            UpdateBowVisibility();
            if (mode != CombatMode.Bow) ResetBow();
        }

        private void UpdateBowVisibility()
        {
            if (bow == null) return;
            bool visible = combat != null && combat.CurrentMode == CombatMode.Bow &&
                (combat.IsAiming || combat.IsBowDrawing);
            if (bow.activeSelf == visible) return;
            bow.SetActive(visible);
            if (!visible) ResetBow();
        }

        private void ResetBow()
        {
            if (nockPoint != null) nockPoint.localPosition = _nockRest;
            if (upperLimb != null) upperLimb.localRotation = _upperRest;
            if (lowerLimb != null) lowerLimb.localRotation = _lowerRest;
        }

        private void LateUpdate()
        {
            UpdateBowVisibility();
            if (bow == null || !bow.activeInHierarchy) return;
            float charge = combat != null && combat.IsBowDrawing ? combat.BowCharge01 : 0f;
            if (upperLimb != null) upperLimb.localRotation = _upperRest * Quaternion.Euler(0, 0, -15f * charge);
            if (lowerLimb != null) lowerLimb.localRotation = _lowerRest * Quaternion.Euler(0, 0, -15f * charge);
            if (nockPoint != null)
            {
                nockPoint.localPosition = _nockRest;
                if (stringHand != null) nockPoint.position = Vector3.Lerp(nockPoint.position, stringHand.position, charge);
            }
            if (bowstring == null || upperTip == null || lowerTip == null || nockPoint == null) return;
            bowstring.useWorldSpace = true;
            bowstring.positionCount = 3;
            bowstring.SetPosition(0, upperTip.position);
            bowstring.SetPosition(1, nockPoint.position);
            bowstring.SetPosition(2, lowerTip.position);
        }
    }
}
