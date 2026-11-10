using UnityEngine;

namespace HealFx
{
    /// <summary>
    /// Couleurs d'une faction de soin. Un asset par camp (alliés / ennemis) : on change la lecture du FX sans toucher au prefab.
    /// v2 : ajout d'une couleur de cœur HDR (rampe cœur chaud -> bords) et d'une couleur de lumière ambiante.
    /// Primary / Secondary / Set(...) sont inchangés.
    /// </summary>
    [CreateAssetMenu(menuName = "Heal FX/Palette", fileName = "HealPalette")]
    public sealed class HealFxPalette : ScriptableObject
    {
        [Tooltip("Anneaux, ondes, anneau au pied des cibles, croix montantes, bord de la zone.")]
        [SerializeField] private Color primary = new Color(0.30f, 1.00f, 0.50f, 1f);

        [Tooltip("Croix au sol, étincelles, runes.")]
        [SerializeField] private Color secondary = new Color(1.00f, 0.88f, 0.45f, 1f);

        [Header("v2 : rampe et lumière")]
        [Tooltip("Cœur chaud de la zone (HDR : valeurs > 1 pour que le Bloom la fasse rayonner). Le sol et la colonne passent de cette couleur à Primary vers les bords.")]
        [SerializeField, ColorUsage(false, true)] private Color core = new Color(2.2f, 1.9f, 1.1f, 1f);

        [Tooltip("Couleur de la point light douce qui éclaire le sol et le personnage.")]
        [SerializeField] private Color light = new Color(0.55f, 1.00f, 0.65f, 1f);

        public Color Primary => primary;
        public Color Secondary => secondary;
        public Color Core => core;
        public Color Light => light;

        /// <summary>Utilisé par l'Editor builder pour créer les palettes par défaut.</summary>
        public void Set(Color primaryColor, Color secondaryColor)
        {
            primary = primaryColor;
            secondary = secondaryColor;
        }

        /// <summary>Utilisé par l'Editor builder : cœur HDR et lumière ambiante.</summary>
        public void SetRamp(Color coreColor, Color lightColor)
        {
            core = coreColor;
            light = lightColor;
        }
    }
}
