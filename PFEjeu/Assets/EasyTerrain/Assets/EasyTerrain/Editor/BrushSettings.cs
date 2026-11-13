using System;
using UnityEngine;

namespace EasyTerrain.EditorTools
{
    public enum BrushMode
    {
        Raise, Lower, Smooth, Flatten,
        PaintTexture, Overlay,
        AddGrass, EraseGrass,
        AddTrees, EraseTrees
    }

    /// <summary>Réglages du pinceau Scene (sérialisés dans la fenêtre : survivent au rechargement de domaine).</summary>
    [Serializable]
    public sealed class BrushSettings
    {
        public BrushMode mode = BrushMode.Raise;
        public float radius = 12f;
        [Range(0.02f, 1f)] public float strength = 0.35f;

        [Tooltip("Index de la couche dans le terrain (liste de la fenêtre).")]
        public int layerIndex;
        [Tooltip("Hauteur cible du mode Aplatir (m). 0 = hauteur du premier clic.")]
        public float flattenHeight;

        [Header("Overlay : texture posée en taches sur l'existant")]
        public float overlayScale = 9f;
        [Range(0f, 1f)] public float overlayAmount = 0.5f;
        [Range(0f, 1f)] public float overlaySharpness = 0.5f;
        public int overlaySeed = 1;

        [Tooltip("Index dans la liste 'trees' / 'details' du profil.")]
        public int treeRuleIndex;
        public int detailRuleIndex;
        [Range(1, 12)] public int treesPerStroke = 3;
    }
}
