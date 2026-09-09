using UnityEngine;

namespace Core.StatsSystem
{
    /// <summary>
    /// Identité d'une stat (asset partagé) : Force, PVMax, Vitesse, Défense...
    /// Les autres systèmes référencent une stat par cet asset.
    /// Création : Assets > Create > Stats > Stat Definition
    /// </summary>
    [CreateAssetMenu(fileName = "Stat", menuName = "Stats/Stat Definition")]
    public sealed class StatDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private float defaultBaseValue = 0f;

        public string Id => id;
        public string DisplayName => displayName;
        public float DefaultBaseValue => defaultBaseValue;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(id)) id = name;
        }
#endif
    }
}
