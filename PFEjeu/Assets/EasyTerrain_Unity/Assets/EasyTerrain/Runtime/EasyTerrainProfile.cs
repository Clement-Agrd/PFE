using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyTerrain
{
    /// <summary>
    /// Règle de peinture automatique d'une couche de texture (altitude, pente, bruit)
    /// + options pour casser la répétition (variante mélangée par bruit).
    /// </summary>
    [Serializable]
    public sealed class LayerRule
    {
        public string label = "Couche";
        public TerrainLayer layer;
        [Range(0f, 1f)] public float weight = 1f;

        [Header("Altitude (mètres au-dessus de la base du terrain)")]
        public Vector2 heightRange = new Vector2(0f, 1000f);
        public float heightFalloff = 8f;

        [Header("Pente (degrés)")]
        public Vector2 slopeRange = new Vector2(0f, 90f);
        public float slopeFalloff = 6f;

        [Header("Bruit (taches naturelles)")]
        public float noiseScale = 35f;
        [Range(0f, 1f)] public float noiseAmount = 0.35f;

        [Header("Casser la répétition")]
        [Tooltip("Mélange une variante de la couche par taches de bruit, pour masquer le motif répétitif.")]
        public bool breakRepetition = true;
        [Tooltip("Vide = variante créée automatiquement (même texture, taille différente). Tu peux y mettre une AUTRE texture : c'est ta 'texture posée sur une autre'.")]
        public TerrainLayer variantLayer;
        [Range(0.1f, 3f)] public float variantTileScale = 0.37f;
        public float breakupNoiseScale = 14f;
        [Range(0f, 1f)] public float breakupAmount = 0.5f;
        [Range(0f, 1f)] public float breakupSharpness = 0.5f;
    }

    /// <summary>Règle de dispersion d'arbres.</summary>
    [Serializable]
    public sealed class TreeRule
    {
        public string label = "Arbre";
        public GameObject prefab;
        [Tooltip("Nombre d'arbres par hectare (100 m x 100 m).")]
        public float treesPerHectare = 60f;
        public float minSpacing = 4f;
        public Vector2 heightRange = new Vector2(0f, 1000f);
        [Range(0f, 90f)] public float maxSlope = 30f;
        [Tooltip("Vide = partout. Sinon uniquement là où cette couche domine.")]
        public TerrainLayer onlyOnLayer;
        [Range(0f, 1f)] public float minLayerWeight = 0.5f;
        public Vector2 scaleRange = new Vector2(0.8f, 1.25f);
        [Range(0f, 1f)] public float colorVariation = 0.15f;
        [Tooltip("0 = répartition uniforme, 1 = bosquets (bruit).")]
        [Range(0f, 1f)] public float clumping = 0.5f;
        public float clumpScale = 40f;
    }

    /// <summary>Règle de dispersion d'herbe / petits objets (détails).</summary>
    [Serializable]
    public sealed class DetailRule
    {
        public string label = "Herbe";
        [Tooltip("Coché = maillage (prefab). Décoché = texture 2D (billboard d'herbe).")]
        public bool useMesh;
        public GameObject prefab;
        public Texture2D texture;
        [Range(1, 16)] public int density = 8;
        public Vector2 widthRange = new Vector2(0.8f, 1.4f);
        public Vector2 heightRange = new Vector2(0.6f, 1.2f);
        public Color healthyColor = new Color(0.45f, 0.62f, 0.25f, 1f);
        public Color dryColor = new Color(0.62f, 0.58f, 0.3f, 1f);
        public Vector2 altitudeRange = new Vector2(0f, 1000f);
        [Range(0f, 90f)] public float maxSlope = 35f;
        public TerrainLayer onlyOnLayer;
        [Range(0f, 1f)] public float clumping = 0.6f;
        public float clumpScale = 12f;
    }

    /// <summary>
    /// Profil réutilisable : toutes les règles d'un biome. Le même profil peut être appliqué à plusieurs terrains.
    ///
    /// EasyTerrainProfile
    /// ├── layers  (LayerRule)  -> peinture auto + variantes anti-répétition
    /// ├── trees   (TreeRule)   -> dispersion d'arbres
    /// └── details (DetailRule) -> herbe / petits objets
    /// </summary>
    [CreateAssetMenu(menuName = "Easy Terrain/Profil", fileName = "EasyTerrainProfile")]
    public sealed class EasyTerrainProfile : ScriptableObject
    {
        public int seed = 1;
        public List<LayerRule> layers = new List<LayerRule>();
        public List<TreeRule> trees = new List<TreeRule>();
        public List<DetailRule> details = new List<DetailRule>();
    }
}
