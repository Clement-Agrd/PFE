using UnityEngine;

namespace Core.HealthSystem
{
    public enum DamageCategory
    {
        Physical,
        Magical
    }

    /// <summary>
    /// Type de dégâts.
    ///
    /// Dans ton projet actuel il suffira normalement
    /// d'avoir deux assets :
    ///
    /// Physical
    /// Magic
    /// </summary>
    [CreateAssetMenu(
        fileName = "DamageType",
        menuName = "Combat/Damage Type"
    )]
    public sealed class DamageType : ScriptableObject
    {
        [SerializeField]
        private string id;

        [SerializeField]
        private string displayName;

        [SerializeField]
        private DamageCategory category;

        public string Id => id;

        public string DisplayName =>
            displayName;

        public DamageCategory Category =>
            category;

#if UNITY_EDITOR

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(id))
            {
                id = name;
            }

            if (string.IsNullOrEmpty(displayName))
            {
                displayName = name;
            }
        }

#endif
    }
}