using System.Collections.Generic;
using UnityEngine;

namespace Core.Village.Exploration
{
    /// <summary>Données d'un type de mission d'exploration (GDD §3.3).</summary>
    [CreateAssetMenu(fileName = "NewMission", menuName = "Village/Exploration/Mission")]
    public sealed class ExplorationMissionDefinition : ScriptableObject
    {
        [field: SerializeField] public string DisplayName { get; private set; } = "Nouvelle mission";
        [field: SerializeField] public ExplorationZone Zone { get; private set; }
        [field: SerializeField, Min(1)] public int DurationCycles { get; private set; } = 1;
        [field: SerializeField] public MissionRisk BaseRisk { get; private set; } = MissionRisk.Faible;
        [field: SerializeField] public List<LootEntry> Loot { get; private set; } = new();
    }
}
