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
    private float damageTimer;
    private float durationTimer;
    public bool IsFinished => durationTimer >= etatData.Duration;

    public void UpdateEtat(float deltaTime)
    {
        durationTimer += deltaTime;
        damageTimer += deltaTime;

        while (damageTimer >= etatData.Interval) 
        {
            damageTimer -= etatData.Interval;
            if (health != null && etatData.Damage > 0)
            {
                health.TakeDamage(etatData.Damage);
            }
        }
    }
    
    public Etat(SOEtat etatData, EntityStats entityStats, Health health)
    {
        this.etatData = etatData;
        this.entityStats = entityStats;
        this.health = health;
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
    
    public void ResetDuration()
    {
        durationTimer = 0f;
    }

    public void RemoveEtat()
    {
        if (entityStats == null) return;

        entityStats.RemoveModifiersFromSource(this);
    }
}