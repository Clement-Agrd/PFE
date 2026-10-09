using System.Collections.Generic;
using UnityEngine;

namespace Core.Minimap
{
    public enum MarkerType
    {
        Player,
        Building,
        Tavern,
        Expedition,
        Gate,
        Tower,
        EnemySpawn,
        Enemy
    }

    /// <summary>
    /// Pose cet objet sur n'importe quel élément du monde pour qu'il apparaisse
    /// sur la minimap et sur la grande carte (avec sa légende).
    /// </summary>
    public sealed class MinimapMarker : MonoBehaviour
    {
        private static readonly List<MinimapMarker> s_All = new();
        public static IReadOnlyList<MinimapMarker> All => s_All;

        [SerializeField] private MarkerType type = MarkerType.Building;
        [Tooltip("Texte affiché à côté de l'icône sur la grande carte (laisser vide pour aucun).")]
        [SerializeField] private string label;

        public MarkerType Type => type;
        public string Label => label;

        private void OnEnable() => s_All.Add(this);
        private void OnDisable() => s_All.Remove(this);
    }

    /// <summary>Apparence + texte de légende de chaque type de marqueur.</summary>
    public static class MarkerStyle
    {
        public static Color ColorOf(MarkerType t) => t switch
        {
            MarkerType.Player => new Color(0.35f, 0.85f, 1f),
            MarkerType.Building => new Color(0.95f, 0.78f, 0.35f),
            MarkerType.Tavern => new Color(0.95f, 0.55f, 0.25f),
            MarkerType.Expedition => new Color(0.55f, 0.9f, 0.5f),
            MarkerType.Gate => new Color(0.85f, 0.85f, 0.9f),
            MarkerType.Tower => new Color(0.6f, 0.65f, 1f),
            MarkerType.EnemySpawn => new Color(0.9f, 0.25f, 0.25f),
            MarkerType.Enemy => new Color(1f, 0.2f, 0.2f),
            _ => Color.white
        };

        public static string LegendOf(MarkerType t) => t switch
        {
            MarkerType.Player => "Joueur",
            MarkerType.Building => "Bâtiment du village",
            MarkerType.Tavern => "Taverne",
            MarkerType.Expedition => "Poste d'expédition",
            MarkerType.Gate => "Herse / entrée",
            MarkerType.Tower => "Tour de défense",
            MarkerType.EnemySpawn => "Zone d'apparition ennemie",
            MarkerType.Enemy => "Ennemi",
            _ => t.ToString()
        };

        public static float SizeOf(MarkerType t) => t switch
        {
            MarkerType.Player => 26f,
            MarkerType.Enemy => 11f,
            MarkerType.EnemySpawn => 20f,
            _ => 17f
        };
    }
}
