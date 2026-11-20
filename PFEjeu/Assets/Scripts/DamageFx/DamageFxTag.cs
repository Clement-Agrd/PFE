using UnityEngine;

namespace DamageFx
{
    /// <summary>
    /// Marqueur à poser sur une cible (tour, mur, boss) pour que ses chiffres soient traités en conséquence :
    /// Tour / Mur = chiffre d'alerte toujours prioritaire ; Boss = garde toujours son chiffre.
    /// Sans ce composant, la cible est traitée comme un ennemi normal.
    /// Optionnel : "chest" permet de choisir le point d'apparition par défaut (torse) quand le coup n'a pas de point d'impact.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DamageFxTag : MonoBehaviour
    {
        [SerializeField] private DamageFxRole role = DamageFxRole.Enemy;
        [SerializeField, Tooltip("Optionnel : point d'apparition par défaut des chiffres (torse). Sinon : centre des colliders.")]
        private Transform chest;

        public DamageFxRole Role => role;
        public Transform Chest => chest;
        public bool IsAlertTarget => role == DamageFxRole.Tower || role == DamageFxRole.Wall;
        public bool IsBoss => role == DamageFxRole.Boss;
    }
}
