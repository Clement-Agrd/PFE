using UnityEngine;
using StatType = Core.StatsSystem.EnumStats.StatTypes;

[CreateAssetMenu(fileName = "SOEtat", menuName = "Scriptable Objects/SOEtat")]
public class SOEtat : ScriptableObject
{
    [Header("Info")]
    [SerializeField] private float duration;
    public float Duration => duration;
    
    [SerializeField] private StatModifierData[] statModifiers;
    public StatModifierData[] StatModifiers => statModifiers;
    
    [Header("Stats")]
    [SerializeField] private StatType[] stats;
    public StatType[] Stats => stats;

    [Header("Damage")]
    [SerializeField, Min(0)] private int damage;
    public int Damage => Mathf.Max(0, damage);
    
    [SerializeField, Min(0)] private float interval;
    public float Interval => Mathf.Max(0, interval);
    
    [System.Serializable]
    public class StatModifierData
    {
        public StatType stat;
        public float modifier;
    }
}