using UnityEngine;

namespace Core.DisintegrationVFX
{
    public enum BurnDirection { BottomUp, TopDown, FromHitPoint }

    public enum ParticleMotion { Rise, Inward, Fall, Shatter }

    public enum DisintegrationQuality { Low, Medium, High }

    /// <summary>
    /// Variante visuelle de la désintégration (papier/feu, spectral, void, sable, glace).
    /// Même prefab, mêmes meshes : tout passe par les propriétés matériau et les couleurs de particules.
    /// </summary>
    [CreateAssetMenu(menuName = "VFX/Disintegration Style", fileName = "DisintegrationStyle")]
    public sealed class DisintegrationStyle : ScriptableObject
    {
        [Header("Shader")]
        [Tooltip("Ramp 256x1 : bord lumineux -> combustion -> noir.")]
        public Texture2D ramp;
        [Range(0.3f, 6f)] public float noiseScale = 2.4f;
        [Range(0.02f, 0.3f)] public float edgeWidth = 0.06f;
        [Range(0.02f, 0.5f)] public float charWidth = 0.2f;
        [Range(0f, 0.3f)] public float curl = 0.05f;
        [Range(0f, 0.6f)] public float noiseAmp = 0.1f;
        [Range(0f, 12f)] public float edgeIntensity = 4.5f;
        public Color charColor = new Color(0.03f, 0.02f, 0.015f, 1f);
        [ColorUsage(false, true)] public Color flashColor = new Color(4f, 2.2f, 1f, 1f);

        [Header("Timeline")]
        [Range(0.6f, 6f)] public float duration = 2.2f;
        public BurnDirection direction = BurnDirection.BottomUp;

        [Header("Particules")]
        public ParticleMotion motion = ParticleMotion.Rise;
        [Tooltip("Multiplicateur de vitesse verticale / radiale.")]
        [Range(0.2f, 3f)] public float speed = 1f;
        [Range(-0.5f, 1.5f)] public float gravity = 0.1f;
        [Range(0, 100)] public int emberCount = 70;
        [Range(0, 250)] public int ashCount = 140;
        [Range(0, 60)] public int scrapCount = 30;
        [Range(0, 15)] public int smokeCount = 10;
        public Color scrapColor = new Color(0.1f, 0.08f, 0.07f, 1f);
        public Color ashColor = new Color(0.45f, 0.43f, 0.42f, 1f);
        public Color emberColor = new Color(1f, 0.65f, 0.2f, 1f);
        public Color emberColorEnd = new Color(1f, 0.12f, 0.02f, 1f);
        public Color smokeColor = new Color(0.12f, 0.11f, 0.11f, 1f);
        [ColorUsage(false, true)] public Color scrapHot = new Color(6f, 2.5f, 0.6f, 1f);
        public Color lightColor = new Color(1f, 0.5f, 0.12f, 1f);
        [Range(0f, 4f)] public float lightIntensity = 1.5f;
    }
}
