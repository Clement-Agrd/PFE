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
    [SerializeField] private float zoneImpact = - 3f;
    [SerializeField] private int arrowCount = 10;

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

        

        // Donne au projectile sa cible et le type de dégâts à infliger
        // (le type sert aux résistances éventuelles côté Health de la cible).

        StatType damageStat = GetDamageStat(tower.TowerData.DamageType.Category);
        
        int finalDamage = Mathf.RoundToInt(stats.GetStat(damageStat));
        
        SpawnArrow(
            tower.IATower.CurrentTarget.transform,
            tower.IATower.CurrentTarget.transform.position,
            finalDamage,
            true);
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
        if (tower == null) return;
        if (tower.IATower == null) return;
        if (tower.TowerData == null) return;
        if (stats == null) return;
        if (tower.IATower.CurrentTarget == null) return;
        
        Vector3 impactPosition = tower.IATower.CurrentTarget.transform.position;
        
        StatType damageStat = GetDamageStat(tower.TowerData.DamageType.Category);
        
        int finalDamage = Mathf.RoundToInt(stats.GetStat(damageStat));

        Collider[] enemiesInZone = Physics.OverlapSphere(impactPosition, zoneImpact, tower.IATower.EnemyLayer);

        for (int i = 0; i < enemiesInZone.Length; i++)
        {
            if (enemiesInZone[i] == null)
            {
                continue;
            }

            if (enemiesInZone[i].TryGetComponent(out Health health))
            {
                health.TakeDamage(new DamageInfo(finalDamage, tower.TowerData.DamageType));
            }
        }
        
        for (int i = 0; i < arrowCount; i++)
        {
            Vector3 randomOffset = UnityEngine.Random.insideUnitSphere * zoneImpact;
            randomOffset.y = 0f;

            Vector3 arrowImpactPosition = impactPosition + randomOffset;
            SpawnArrow(
                null,
                arrowImpactPosition,
                finalDamage,
                false
            );
        }
    }

    
    
    // Action spécial
    private void SpawnArrow(Transform target, Vector3 impactPosition, int finalDamage, bool damageEnable)
    {
        GameObject arrow = PoolManager.Instance.Spawn(
            tower.TowerData.ProjectilePrefab, 
            firePoint.position, 
            firePoint.rotation);
        
        if (arrow == null) return;

        if (!arrow.TryGetComponent(out TowerProjectil projectile)) return;

        projectile.SetTarget(
            target,
            tower.TowerData.DamageType,
            finalDamage,
            tower.TowerData.ProjectileMovement,
            impactPosition,
            damageEnable);
    }
    
    private void ApplyAreaDamage(
        Vector3 impactPosition,
        float radius,
        int finalDamage)
    {
        if (tower == null) return;
        if (tower.IATower == null) return;
        if (tower.TowerData == null) return;

        Collider[] enemiesInZone = Physics.OverlapSphere(
            impactPosition,
            radius,
            tower.IATower.EnemyLayer
        );

        for (int i = 0; i < enemiesInZone.Length; i++)
        {
            if (enemiesInZone[i] == null) continue;

            if (enemiesInZone[i].TryGetComponent(out Health health))
            {
                health.TakeDamage(
                    new DamageInfo(finalDamage, tower.TowerData.DamageType)
                );
            }
        }
    }
}
