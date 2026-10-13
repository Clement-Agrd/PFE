using UnityEngine;
using StatType = Core.StatsSystem.EnumStats.StatTypes;

[CreateAssetMenu(fileName = "SoHero", menuName = "Scriptable Objects/SoHero")]
public class SoHero : ScriptableObject
{
    [Header("Info")]
    [SerializeField] private string heroName;
    [SerializeField] private string heroDescription;
    
    [Header("Stats")]
    [SerializeField] private int minLevel;
    public int MinLevel => minLevel;
    
    [SerializeField] private int maxLevel;
    public int MaxLevel => maxLevel;
    
}
