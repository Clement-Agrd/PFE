using UnityEngine;

namespace VertexCloth
{
    /// <summary>Sens du dégradé de poids : va du côté fixe vers le côté libre (espace local du mesh).</summary>
    public enum GradientDirection
    {
        TopToBottom,
        BottomToTop,
        PositiveX,
        NegativeX,
        PositiveZ,
        NegativeZ
    }

    /// <summary>
    /// Recette réutilisable d'un type d'élément souple (robe, cape, cheveux, drapeau...) :
    /// réglages de simulation + règles de génération automatique des poids.
    /// </summary>
    [CreateAssetMenu(fileName = "VertexClothPreset", menuName = "Vertex Cloth/Preset")]
    public sealed class VertexClothPreset : ScriptableObject
    {
        public VertexClothSettings settings;

        [Header("Poids automatiques")]
        [Tooltip("Si le mesh a des vertex colors, le canal rouge est utilisé comme poids (0 = fixe, 1 = libre).")]
        public bool useVertexColorRed;

        public GradientDirection direction = GradientDirection.TopToBottom;

        [Tooltip("Position normalisée (0..1 le long du sens) jusqu'à laquelle le tissu reste 100 % fixe.")]
        [Range(0f, 1f)] public float pinnedUntil = 0.3f;

        [Tooltip("Position normalisée (0..1) à partir de laquelle le tissu est 100 % libre.")]
        [Range(0f, 1f)] public float freeFrom = 0.85f;

        [Range(0.2f, 4f)] public float curve = 1f;

        [Tooltip("Normalise le dégradé séparément pour chaque morceau de mesh connecté.")]
        public bool normalizePerIsland = true;

        public Vector3 DirectionVector
        {
            get
            {
                switch (direction)
                {
                    case GradientDirection.TopToBottom: return Vector3.down;
                    case GradientDirection.BottomToTop: return Vector3.up;
                    case GradientDirection.PositiveX: return Vector3.right;
                    case GradientDirection.NegativeX: return Vector3.left;
                    case GradientDirection.PositiveZ: return Vector3.forward;
                    default: return Vector3.back;
                }
            }
        }
    }
}
