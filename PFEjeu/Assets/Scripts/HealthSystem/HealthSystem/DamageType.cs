using UnityEngine;

namespace Core.HealthSystem
{
    /// <summary>
    /// Catégorie de dégâts (physique, feu, poison, glace...). Sert aux résistances :
    /// une entité peut réduire/annuler/amplifier les dégâts d'un type donné.
    /// Création : Assets > Create > Combat > Damage Type
    /// </summary>
    [CreateAssetMenu(fileName = "DamageType", menuName = "Combat/Damage Type")]
    public sealed class DamageType : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;

        public string Id => id;
        public string DisplayName => displayName;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(id)) id = name;
        }
#endif
    }
}
