using UnityEngine;

namespace VertexCloth
{
    /// <summary>Réglages partagés de la simulation (un asset par type de vêtement).</summary>
    [CreateAssetMenu(fileName = "VertexClothSettings", menuName = "Vertex Cloth/Settings")]
    public sealed class VertexClothSettings : ScriptableObject
    {
        [Header("Solveur")]
        [Range(1, 6)] public int substeps = 2;
        [Range(1, 12)] public int iterations = 4;
        [Range(0.01f, 0.1f)] public float maxDeltaTime = 0.033f;
        [Tooltip("Distance (monde) au-delà de laquelle la simulation est réinitialisée. 0 = désactivé.")]
        [Min(0f)] public float teleportDistance = 2f;

        [Header("Forces")]
        public Vector3 gravity = new Vector3(0f, -9.81f, 0f);
        [Range(0f, 2f)] public float gravityScale = 0.5f;
        [Tooltip("Amortissement (1/s).")]
        [Range(0f, 15f)] public float damping = 2f;

        [Header("Contraintes")]
        [Range(0f, 1f)] public float stretchStiffness = 0.9f;
        [Range(0f, 1f)] public float bendStiffness = 0.1f;
        [Tooltip("Retour vers la forme sculptée près des zones fixes (par frame à 60 FPS).")]
        [Range(0f, 1f)] public float shapeStiffnessAtPins = 0.3f;
        [Tooltip("Retour vers la forme sculptée sur les zones libres (par frame à 60 FPS).")]
        [Range(0f, 1f)] public float shapeStiffnessAtFree = 0.02f;
        [Tooltip("Distance max à la pose de repos pour un poids 1 (unités locales du mesh).")]
        [Min(0f)] public float maxTravel = 0.5f;

        [Header("Vent")]
        [Tooltip("Coefficient de réaction au vent (1/s).")]
        [Range(0f, 10f)] public float windInfluence = 1.5f;
        [Tooltip("Turbulence relative à la vitesse du vent.")]
        [Range(0f, 1f)] public float windTurbulence = 0.35f;
        [Min(0f)] public float turbulenceFrequency = 1.2f;
        [Range(0f, 1f)] public float gustAmount = 0.4f;
        [Min(0f)] public float gustFrequency = 0.35f;

        [Header("Collisions")]
        [Min(0f)] public float collisionThickness = 0.005f;
        [Range(0f, 1f)] public float collisionFriction = 0.2f;
    }
}
