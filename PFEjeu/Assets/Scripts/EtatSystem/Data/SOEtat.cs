using UnityEngine;
using StatType = Core.StatsSystem.EnumStats.StatTypes;

[CreateAssetMenu(fileName = "SOEtat", menuName = "Scriptable Objects/SOEtat")]
public class SOEtat : ScriptableObject
{
    [Header("Info")]
    [SerializeField] private float duration;
    public float Duration => duration;

    [SerializeField] private GameObject visualEffect;
    public GameObject VisualEffect => visualEffect;
    
    [SerializeField] private StatModifierData[] statModifiers;
    public StatModifierData[] StatModifiers => statModifiers ?? System.Array.Empty<StatModifierData>();

    [Header("Damage")]
    [SerializeField] private Core.HealthSystem.DamageType damageType;
    public Core.HealthSystem.DamageType DamageType => damageType;

    [SerializeField, Min(0)] private int damage;
    public int Damage => Mathf.Max(0, damage);
    
    [SerializeField, Min(0.01f)] private float interval = 1f;
    public float Interval => Mathf.Max(0.01f, interval);
    
    [System.Serializable]
    public class StatModifierData
    {
        public StatType stat;
        public float modifier;
    }
}