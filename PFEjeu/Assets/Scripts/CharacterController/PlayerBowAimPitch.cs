using UnityEngine;

namespace ProfessionalTPS
{
    /// <summary>
    /// Oriente les deux épaules avec la visée verticale de l'arc,
    /// sans remplacer les animations Kevin Iglesias (Idle/Load/Hold/Release).
    /// A placer sur le Player, pas sur le modèle animé.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(500)]
    public sealed class PlayerBowAimPitch : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerCombatController combat;
        [SerializeField] private Animator animator;

        [Tooltip("Os B-chest du rig Kevin Iglesias, parent des deux épaules.")]
        [SerializeField] private Transform chest;

        [Header("Vertical Bow Aim")]
        [Tooltip("1 = hauteur de la caméra; ajuster si l'arc vise trop haut ou trop bas.")]
        [SerializeField, Range(0f, 1.5f)] private float pitchMultiplier = 1f;

        [SerializeField, Range(0f, 89f)] private float maxUpAngle = 65f;
        [SerializeField, Range(0f, 89f)] private float maxDownAngle = 55f;

        [Tooltip("Décalage pour compenser l'orientation de base du clip Bow Hold.")]
        [SerializeField, Range(-30f, 30f)] private float pitchOffset = 0f;

        [Tooltip("Vitesse de suivi : 0 = immédiat.")]
        [SerializeField, Min(0f)] private float followSpeed = 12f;

        private float _currentPitch;

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();

            if (chest == null)
                Debug.LogWarning(
                    "[PlayerBowAimPitch] Os B-chest introuvable. " +
                    "Assigne le B-chest du modèle Kevin Iglesias dans le prefab Player.",
                    this
                );
        }

        private void LateUpdate()
        {
            if (chest == null || combat == null || animator == null ||
                !animator.isActiveAndEnabled)
                return;

            float targetPitch = 0f;
            bool aimingBow =
                combat.CurrentMode == CombatMode.Bow &&
                (combat.IsAiming || combat.IsBowDrawing);

            // La caméra fournit la même inclinaison verticale que le viseur.
            // On ne modifie pas le projectile ni les animations de tir.
            if (aimingBow && combat.AimCamera != null)
            {
                Vector3 direction = combat.AimCamera.transform.forward;
                Vector3 horizontal = Vector3.ProjectOnPlane(direction, transform.up);

                float pitch = Mathf.Atan2(
                    Vector3.Dot(direction, transform.up),
                    horizontal.magnitude
                ) * Mathf.Rad2Deg;

                targetPitch = Mathf.Clamp(
                    (pitch + pitchOffset) * pitchMultiplier,
                    -maxDownAngle,
                    maxUpAngle
                );
            }

            float t = followSpeed <= 0f
                ? 1f
                : 1f - Mathf.Exp(-followSpeed * Time.deltaTime);

            _currentPitch = Mathf.Lerp(_currentPitch, targetPitch, t);

            if (Mathf.Abs(_currentPitch) < 0.001f)
                return;

            // LateUpdate s'exécute après l'Animator : on ajoute le pitch
            // à la pose Kevin du frame courant, sans accumulation.
            // B-chest porte les deux B-shoulder, et ne touche pas aux jambes.
            chest.rotation =
                Quaternion.AngleAxis(-_currentPitch, transform.right) *
                chest.rotation;
        }

        private void ResolveReferences()
        {
            if (combat == null)
                combat = GetComponent<PlayerCombatController>();

            if (animator == null)
                animator = GetComponentInChildren<Animator>(true);

            if (chest != null || animator == null)
                return;

            Transform[] bones = animator.GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i].name != "B-chest")
                    continue;

                chest = bones[i];
                return;
            }
        }
    }
}
