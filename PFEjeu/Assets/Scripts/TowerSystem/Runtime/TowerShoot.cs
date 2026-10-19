using System;
using Core.HealthSystem;
using Core.PoolingSystem;
using Core.StatsSystem;
using Core.TowerDefense;
using StatType = Core.StatsSystem.EnumStats.StatTypes;
using UnityEngine;

public class TowerShoot : MonoBehaviour
{
    [Header("System")]
    [SerializeField] private Tower tower;
    [SerializeField] private EntityStats stats;
    [SerializeField] private PoolManager poolManager;
    
    [Header("Data")]
    [SerializeField] private float slowPower = - 0.30f;

    public Transform firePoint => tower.FirePoint;

    private void Awake()
    {
        tower = GetComponent<Tower>();
        
        if (tower == null)
        {
            Debug.LogError("TowerShoot : aucun composant Tower trouvé sur ce GameObject.", this);
        }
    }

    public void Shoot()
    {
        switch (tower.TowerData.ShootMode)
        {
            case EnumShootType.ShootMode.Classic :
                ClassicShoot();
                break;
            
            case EnumShootType.ShootMode.Slow :
                Slow();
                break;
            
            case EnumShootType.ShootMode.RainArrow :
                RainArrow();
                break;
        }
    }

    public static StatType GetDamageStat(DamageCategory category)
    {
        switch (category)
        {
            case DamageCategory.Physical :
                return StatType.PhysicDamage;
            
            case DamageCategory.Magical :
                return StatType.MagicDamage;
            
            default:
                return StatType.PhysicDamage;
        }
    }
    
    /// <summary>
    /// Fait apparaître un projectile via le pool et lui donne sa cible + son
    /// type de dégâts. Toute la logique de vol/impact vit ensuite dans
    /// TowerProjectil, pas ici.
    /// </summary>
    public void ClassicShoot()
    {
        // Protection
        if (tower.IATower == null) return;
        if (stats == null) return;
        if (tower.TowerData == null) return;
        if (tower.IATower.CurrentTarget == null) return;
        if (tower.TowerData.ProjectilePrefab == null) return;
        if (firePoint == null) return; // le modèle du niveau actuel n'a pas de FirePoint valide

        GameObject arrow = PoolManager.Instance.Spawn(tower.TowerData.ProjectilePrefab, firePoint.position, firePoint.rotation);
        if (arrow == null) return;

        if (!arrow.TryGetComponent(out TowerProjectil projectile)) return;

        // Donne au projectile sa cible et le type de dégâts à infliger
        // (le type sert aux résistances éventuelles côté Health de la cible).

        StatType damageStat = GetDamageStat(tower.TowerData.DamageType.Category);
        
        int finalDamage = Mathf.RoundToInt(stats.GetStat(damageStat));
        
        projectile.SetTarget(
            tower.IATower.CurrentTarget.transform, 
            tower.TowerData.DamageType, 
            finalDamage, 
            tower.TowerData.ProjectileMovement,
            tower.IATower.CurrentTarget.transform.position);
    }

    public void Slow()
    {
        if (tower.IATower == null) return;
        if (tower.TowerData == null) return;

        for (int i = 0; i < tower.IATower.AllTargets.Count; i++)
        {
            Collider enemy = tower.IATower.AllTargets[i];

            if (enemy == null) continue;

            EntityStats enemyStats = enemy.GetComponent<EntityStats>();

            if (enemyStats == null) continue;

            enemyStats.AddModifier(
                StatType.Speed,
                slowPower,
                StatModifierType.PercentAdditive,
                this
            );
        }
    }

    public void RainArrow()
    {
        
    }
}
