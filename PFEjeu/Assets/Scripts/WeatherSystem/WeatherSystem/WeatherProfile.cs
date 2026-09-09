using UnityEngine;

namespace Core.WeatherSystem
{
    /// <summary>
    /// Un état météo (asset) : brouillard, particules (pluie/neige...), force du vent.
    /// Réglé dans l'Inspector. Le WeatherController mélange ces valeurs en douceur.
    /// Création : Assets > Create > Environment > Weather Profile
    /// </summary>
    [CreateAssetMenu(fileName = "Weather", menuName = "Environment/Weather Profile")]
    public sealed class WeatherProfile : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;

        [Header("Brouillard")]
        [SerializeField] private bool useFog = false;
        [SerializeField] private Color fogColor = new Color(0.6f, 0.6f, 0.65f);
        [SerializeField, Min(0f)] private float fogDensity = 0.01f;

        [Header("Particules (optionnel)")]
        [Tooltip("Prefab d'effet suivant la caméra/joueur (pluie, neige, feuilles...).")]
        [SerializeField] private GameObject particlePrefab;

        [Header("Vent")]
        [Tooltip("Force du vent, lisible par d'autres systèmes (arbres, WindZone...).")]
        [SerializeField] private float windStrength = 0f;

        public string Id => id;
        public string DisplayName => displayName;
        public bool UseFog => useFog;
        public Color FogColor => fogColor;
        public float FogDensity => fogDensity;
        public GameObject ParticlePrefab => particlePrefab;
        public float WindStrength => windStrength;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(id)) id = name;
        }
#endif
    }
}
