using UnityEngine;

namespace Core.GroundFog
{
    /// <summary>
    /// Préréglage de brouillard au sol (Cour, Égouts, Marais, Forêt de nuit...).
    /// Chaque état a une version "jour" (léger) et "nuit" (dense) ; la zone interpole entre les deux.
    /// Tout est modifiable à la main dans l'Inspector. Valeurs par défaut : brume lente et laiteuse
    /// façon Ocarina of Time (grosses masses douces qui dérivent lentement).
    /// </summary>
    [CreateAssetMenu(menuName = "Core/Ground Fog/Preset", fileName = "GroundFogPreset")]
    public sealed class GroundFogPreset : ScriptableObject
    {
        [System.Serializable]
        public struct State
        {
            [ColorUsage(false, true)] public Color color;
            [Range(0f, 5f)] public float density;
            [Range(0.05f, 5f)] public float thickness;
            [Range(0f, 1f)] public float noiseStrength;
        }

        [Header("Jour (léger)")]
        [SerializeField] private State day = new State
        {
            color = new Color(0.95f, 0.96f, 1f), density = 0.35f, thickness = 0.5f, noiseStrength = 0.5f
        };

        [Header("Nuit (dense)")]
        [SerializeField] private State night = new State
        {
            color = new Color(0.9f, 0.93f, 1f), density = 1.4f, thickness = 0.7f, noiseStrength = 0.6f
        };

        [Header("Bruit (taille des masses de brume)")]
        [Tooltip("Petit = grosses masses douces (style Ocarina). Grand = brume fine et agitée.")]
        [SerializeField, Range(0.02f, 3f)] private float noiseScale = 0.12f;

        [Header("Vent (dérive des masses)")]
        [Tooltip("Direction du vent, en degrés (0 = +X, 90 = +Z).")]
        [SerializeField, Range(0f, 360f)] private float windAngle = 20f;
        [Tooltip("Vitesse de dérive de la 1re couche de bruit (grosses masses).")]
        [SerializeField, Range(0f, 2f)] private float layer1Speed = 0.06f;
        [Tooltip("Vitesse de dérive de la 2e couche de bruit (détails, direction perpendiculaire).")]
        [SerializeField, Range(0f, 2f)] private float layer2Speed = 0.04f;

        [Header("Vagues (ondulation de la surface)")]
        [SerializeField, Range(0f, 1f)] private float waveAmplitude = 0.15f;
        [SerializeField, Range(1f, 20f)] private float waveLength = 10f;
        [Tooltip("Vitesse de roulement des vagues (0 = figé).")]
        [SerializeField, Range(0f, 2f)] private float waveSpeed = 0.15f;

        [Header("Lumière / bords")]
        [Tooltip("Plus haut = brouillard plus blanc et lumineux, même de nuit.")]
        [SerializeField, Range(0f, 2f)] private float selfLight = 0.8f;
        [Tooltip("Influence des torches / lumières additionnelles dans la brume.")]
        [SerializeField, Range(0f, 4f)] private float lightInfluence = 1.5f;
        [Tooltip("Fondu sur les bords de la boîte (plus grand = bords plus doux).")]
        [SerializeField, Range(0.01f, 0.5f)] private float edgeFade = 0.25f;

        public State Day => day;
        public State Night => night;
        public float NoiseScale => noiseScale;
        public float WaveAmplitude => waveAmplitude;
        public float WaveLength => waveLength;
        public float WaveSpeed => waveSpeed;
        public float SelfLight => selfLight;
        public float LightInfluence => lightInfluence;
        public float EdgeFade => edgeFade;

        /// <summary>Format attendu par le shader : xy = direction du vent, z/w = vitesses des 2 couches.</summary>
        public Vector4 Wind
        {
            get
            {
                float rad = windAngle * Mathf.Deg2Rad;
                return new Vector4(Mathf.Cos(rad), Mathf.Sin(rad), layer1Speed, layer2Speed);
            }
        }
    }
}
