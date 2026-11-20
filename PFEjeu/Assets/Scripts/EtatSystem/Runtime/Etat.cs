using Core.StatsSystem;
using Core.HealthSystem;

public class Etat
{
    // System
    private SOEtat etatData;
    public SOEtat EtatData => etatData;
    
    private EntityStats entityStats;
    private Health health;
    
    // Temps
    private int? damageOverride;
    private float damageTimer;
    private float durationTimer;
    public bool IsFinished => durationTimer >= etatData.Duration;

    public void UpdateEtat(float deltaTime)
    {
        float activeTime = UnityEngine.Mathf.Min(deltaTime, etatData.Duration - durationTimer);
        durationTimer += activeTime;
        damageTimer += activeTime;

        while (damageTimer >= etatData.Interval) 
        {
            damageTimer -= etatData.Interval;
            int damage = damageOverride ?? etatData.Damage;
            if (health != null && damage > 0)
            {
                health.TakeDamage(new DamageInfo(damage, etatData.DamageType));
            }
        }
    }
    
    public Etat(SOEtat etatData, EntityStats entityStats, Health health, int? damageOverride = null)
    {
        this.etatData = etatData;
        this.entityStats = entityStats;
        this.health = health;
        this.damageOverride = damageOverride.HasValue ? System.Math.Max(0, damageOverride.Value) : null;
    }
    
    public void ApplyStatModifier()
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
    
    public void ResetDuration(int? damageOverride = null)
    {
        durationTimer = 0f;
        this.damageOverride = damageOverride.HasValue ? System.Math.Max(0, damageOverride.Value) : null;
    }

    public void RemoveEtat()
    {
        if (entityStats == null) return;

        entityStats.RemoveModifiersFromSource(this);
    }
}