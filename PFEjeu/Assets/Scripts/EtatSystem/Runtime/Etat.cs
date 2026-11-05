using UnityEngine;
using StatType = Core.StatsSystem.EnumStats.StatTypes;
using Core.StatsSystem;

public class Etat
{
    private SOEtat etatData;
    private EntityStats entityStats;
    
    public Etat(SOEtat etatData, EntityStats entityStats)
    {
        this.etatData = etatData;
        this.entityStats = entityStats;
    }
    
    public void ApplyEtat()
    {
        if (etatData == null) return;
        if (entityStats == null) return;

        foreach (SOEtat.StatModifierData modifierData in etatData.StatModifiers)
        {
            entityStats.AddModifier(
                modifierData.stat,
                modifierData.modifier,
                StatModifierType.PercentAdditive,
                this
            );
        }
    }
}