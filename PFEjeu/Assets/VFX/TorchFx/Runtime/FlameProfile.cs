using System;
using UnityEngine;

namespace TorchFx
{
    /// <summary>
    /// Réglages partagés d'une flamme (taille, couches, scintillement, allumage).
    /// Un même profil peut servir à toutes les torches d'un niveau : on change le look à un seul endroit.
    /// Les couleurs et les seuils des bandes toon vivent sur le matériau (shader TorchFx/StylizedFlame).
    /// </summary>
    [CreateAssetMenu(fileName = "FlameProfile", menuName = "TorchFx/Flame Profile")]
    public sealed class FlameProfile : ScriptableObject
    {
        /// <summary>Une couche de flamme (ex : enveloppe externe, cœur chaud).</summary>
        [Serializable]
        public struct Layer
        {
            public string name;
            [Min(0.05f)] public float widthScale;
            [Min(0.05f)] public float heightScale;
            [Tooltip("Décalage du bruit : évite que les couches bougent à l'identique.")]
            public float seedOffset;
            [Tooltip("Multiplie la couleur du matériau (HDR). Alpha = opacité de la couche.")]
            [ColorUsage(true, true)] public Color tint;
            [Min(0f)] public float speedMultiplier;
        }

        #region Inspector
        [Header("Rendu")]
        [Tooltip("Matériau utilisant le shader TorchFx/StylizedFlame.")]
        [SerializeField] private Material material;
        [Tooltip("Largeur du quad en mètres. La flamme visible occupe environ 74 % de cette largeur.")]
        [SerializeField, Min(0.01f)] private float width = 0.32f;
        [Tooltip("Hauteur de la flamme en mètres.")]
        [SerializeField, Min(0.01f)] private float height = 0.46f;
        [SerializeField] private Layer[] layers =
        {
            new Layer { name = "Externe", widthScale = 1f, heightScale = 1f, seedOffset = 0f, tint = new Color(1f, 1f, 1f, 1f), speedMultiplier = 1f },
            new Layer { name = "Coeur", widthScale = 0.6f, heightScale = 0.62f, seedOffset = 4.3f, tint = new Color(1.25f, 1.15f, 0.9f, 1f), speedMultiplier = 1.35f }
        };

        [Header("Scintillement")]
        [SerializeField, Min(0f)] private float flickerFrequency = 3.2f;
        [SerializeField, Range(0f, 0.6f)] private float heightFlicker = 0.18f;
        [SerializeField, Range(0f, 0.4f)] private float widthFlicker = 0.08f;
        [SerializeField, Range(0f, 0.8f)] private float brightnessFlicker = 0.25f;

        [Header("Allumage")]
        [SerializeField, Min(0.01f)] private float igniteDuration = 0.6f;
        [SerializeField, Min(0.01f)] private float extinguishDuration = 0.35f;
        #endregion

        #region Accès en lecture seule
        public Material Material => material;
        public float Width => width;
        public float Height => height;
        public Layer[] Layers => layers;
        public float FlickerFrequency => flickerFrequency;
        public float HeightFlicker => heightFlicker;
        public float WidthFlicker => widthFlicker;
        public float BrightnessFlicker => brightnessFlicker;
        public float IgniteDuration => igniteDuration;
        public float ExtinguishDuration => extinguishDuration;
        #endregion
    }
}
