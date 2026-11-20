using Core.HealthSystem;
using UnityEngine;

namespace DamageFx
{
    /// <summary>
    /// Façade unique d'affichage des dégâts. Les scripts du jeu n'appellent que ceci :
    ///   DamageNumbers.Show(amount, worldPoint, kind, isCrit, target);
    ///
    /// Point d'accès statique volontaire (pas un singleton MonoBehaviour) : les appelants sont des Health présents
    /// par centaines dans des prefabs, qui ne peuvent pas référencer un objet de scène. Le gestionnaire s'enregistre
    /// ici à l'OnEnable et se retire à l'OnDisable ; sans gestionnaire, IsActive vaut false et l'ancien système reste utilisé.
    /// </summary>
    public static class DamageNumbers
    {
        private static DamageNumbersManager manager;

        /// <summary>Vrai quand un DamageNumbersManager est actif dans la scène.</summary>
        public static bool IsActive => manager != null;

        internal static void Register(DamageNumbersManager m) { manager = m; }
        internal static void Unregister(DamageNumbersManager m) { if (manager == m) manager = null; }

#if UNITY_EDITOR
        // Désactivation du rechargement de domaine : on repart propre à chaque Play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { manager = null; }
#endif

        /// <summary>Affiche (ou cumule) un chiffre de dégâts / de soin.</summary>
        /// <param name="amount">Montant (positif).</param>
        /// <param name="worldPoint">Point d'impact (ou torse de la cible).</param>
        /// <param name="kind">Famille visuelle (couleur + icône).</param>
        /// <param name="isCrit">Coup critique : chiffre doré HDR, jamais cumulé.</param>
        /// <param name="target">Cible : sert au cumul, au suivi et à l'anti-superposition.</param>
        /// <param name="targetMaxHealth">PV max de la cible (0 = inconnu), pour juger si le coup est "petit".</param>
        /// <param name="isBoss">Boss : garde toujours son chiffre.</param>
        public static void Show(float amount, Vector3 worldPoint, DamageFxKind kind, bool isCrit, Transform target = null,
            float targetMaxHealth = 0f, bool isBoss = false)
        {
            if (manager == null) return;
            var hit = new DamageHit(amount, worldPoint, kind, isCrit, isBoss, target, targetMaxHealth);
            manager.Show(in hit);
        }

        /// <summary>Variante pratique : famille déduite d'un asset DamageType existant (via DamageFxKindMap).</summary>
        public static void Show(float amount, Vector3 worldPoint, DamageType type, bool isCrit, Transform target = null,
            float targetMaxHealth = 0f, bool isBoss = false)
        {
            if (manager == null) return;
            DamageFxKind kind = manager.KindMap != null ? manager.KindMap.Resolve(type) : DamageFxKind.Physical;
            Show(amount, worldPoint, kind, isCrit, target, targetMaxHealth, isBoss);
        }
    }
}
