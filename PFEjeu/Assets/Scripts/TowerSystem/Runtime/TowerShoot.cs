using Core.HealthSystem;
using Core.PoolingSystem;
using Core.StatsSystem;
using Core.TowerDefense;
using StatType = Core.StatsSystem.EnumStats.StatTypes;
using UnityEngine;

public class TowerShoot : MonoBehaviour
{
    [SerializeField] private IATower iaTower;
    [SerializeField] private Tower tower;
    [SerializeField] private SOTower towerData;
    [SerializeField] private EntityStats stats;
    [SerializeField] private PoolManager poolManager;

    public Transform firePoint => tower.FirePoint;
    
    /// <summary>
    /// Fait apparaître un projectile via le pool et lui donne sa cible + son
    /// type de dégâts. Toute la logique de vol/impact vit ensuite dans
    /// TowerProjectil, pas ici.
    /// </summary>
    public void Shoot()
    {
        if (iaTower == null) return;
        if (stats == null) return;
        if (towerData == null) return;
        if (iaTower.CurrentTarget == null) return;
        if (towerData.ProjectilePrefab == null) return;
        if (firePoint == null) return; // le modèle du niveau actuel n'a pas de FirePoint valide

        GameObject arrow = PoolManager.Instance.Spawn(towerData.ProjectilePrefab, firePoint.position, firePoint.rotation);
        if (arrow == null) return;

        if (!arrow.TryGetComponent(out TowerProjectil projectile)) return;

        // Donne au projectile sa cible et le type de dégâts à infliger
        // (le type sert aux résistances éventuelles côté Health de la cible).

        StatType damageStat;
        switch (towerData.DamageType.Category)
        {
            case DamageCategory.Physical :
                damageStat = StatType.PhysicDamage;
                break;
            
            case DamageCategory.Magical:
                damageStat = StatType.MagicDamage;
                break;
            
            default:
                damageStat = StatType.PhysicDamage;
                break;
        }
        
        int finalDamage = Mathf.RoundToInt(stats.GetStat(damageStat));
        
        projectile.SetTarget(iaTower.CurrentTarget.transform, towerData.DamageType, finalDamage);
    }

    public void Slow()
    {
        
    }
}
