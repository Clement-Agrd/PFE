using UnityEngine;
using Core.Village;
namespace Core.TowerDefense
{
    [CreateAssetMenu(menuName="Tower Defense/Defense Building",fileName="DefenseBuilding")]
    public sealed class DefenseBuildingDefinition : ScriptableObject
    {
        public string displayName="Défense";
        public GameObject prefab;
        [Min(1)] public int requiredTownHallLevel=1;
        public ResourceCost[] costs;
        public Vector3 placementOffset;
        public Vector3 footprint=new(3,3,3);
    }
}
