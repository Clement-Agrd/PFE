using System;
using System.Collections;
using System.Collections.Generic;
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
    [SerializeField] private float slowPower = -0.30f;
    [SerializeField] private float zoneImpact = 3f;
    [SerializeField] private int arrowCount = 10;

    List<Collider> enemySlow = new List<Collider>();
    
    
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
        Debug.Log("Shoot appelé");
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

        Debug.Log($"Slow appelé, cibles : {tower.IATower.AllTargets.Count}");
        
        for (int i = 0; i < tower.IATower.AllTargets.Count; i++)
        {
            Collider enemy = tower.IATower.AllTargets[i];

            if (enemy == null) continue;
            

            EntityStats enemyStats = enemy.GetComponent<EntityStats>();
            
            
            if (enemyStats == null)
            {
                Debug.LogWarning($"Pas d'EntityStats sur {enemy.name}", enemy);     // 2, à la place du continue seul
                continue;
            }
            
            if (!enemySlow.Contains(enemy))
            {
                enemyStats.AddModifier(
                    StatType.Speed,
                    slowPower,
                    StatModifierType.PercentAdditive,
                    this
                );
                
                enemySlow.Add(enemy);
            }
            Debug.Log($"Speed de {enemy.name} : {enemyStats.GetStat(StatType.Speed)}");
        }

        for (int i = enemySlow.Count - 1; i >= 0; i--)
        {
            Collider enemy = enemySlow[i];

            // 1. Vérifier si l'ennemi a été détruit
            if (enemy == null)
            {
                enemySlow.RemoveAt(i);
                continue;
            }

            // 2. Récupérer ses statistiques
            EntityStats enemyStats = enemy.GetComponent<EntityStats>();

            // 3. Vérifier si l'ennemi est sorti de portée
            if (!tower.IATower.AllTargets.Contains(enemy))
            {
                if (enemyStats != null)
                {
                    enemyStats.RemoveModifiersFromSource(this);
                }

                enemySlow.RemoveAt(i);
            }
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

        float duration = 0f;
            
        for (int i = 0; i < arrowCount; i++)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * zoneImpact;
            Vector3 randomOffset = new Vector3(offset.x, 0f, offset.y);

            Vector3 arrowImpactPosition = impactPosition + randomOffset;
            
            duration = Mathf.Max(duration, SpawnArrow(
                null,
                arrowImpactPosition,
                finalDamage,
                false
            ));
        }
        StartCoroutine(RainArrowDamage(impactPosition, finalDamage, duration));
    }
    
    private IEnumerator RainArrowDamage(Vector3 impactPosition, int finalDamage, float duration)
    {
        yield return new WaitForSeconds(duration);

        ApplyAreaDamage(impactPosition, zoneImpact, finalDamage);
    }
    
    public void FireBall()
    {
        MagicPower();
    }

    public void Ice()
    {
        MagicPower();
        
        
    }
    
    // Action spécial
    private float SpawnArrow(Transform target, Vector3 impactPosition, int finalDamage, bool damageEnable)
    {
        GameObject arrow = PoolManager.Instance.Spawn(
            tower.TowerData.ProjectilePrefab,
            firePoint.position,
            firePoint.rotation);

        if (arrow == null) return 0f;

        if (!arrow.TryGetComponent(out TowerProjectil projectile)) return 0f;

        projectile.SetTarget(
            target,
            tower.TowerData.DamageType,
            finalDamage,
            tower.TowerData.ProjectileMovement,
            impactPosition,
            damageEnable);

        return projectile.TravelDuration;
    }

    private void MagicPower()
    {
        if (tower == null) return;
        if (tower.IATower == null) return;
        if (tower.TowerData == null) return;
        if (stats == null) return;
        if (tower.IATower.CurrentTarget == null) return;
        
        Vector3 impactPosition = tower.IATower.CurrentTarget.transform.position;

        StatType damageStat = GetDamageStat(tower.TowerData.DamageType.Category);

        int finalDamage = Mathf.RoundToInt(stats.GetStat(damageStat));
        
        float duration = 0f;
        
        duration = SpawnArrow(
            null,
            impactPosition,
            finalDamage,
            false
        );
        
        StartCoroutine(RainArrowDamage(impactPosition, finalDamage, duration));
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

        switch (tower.TowerData.ShootMode)
        {
            case EnumShootType.ShootMode.Ice :

                for (int i = 0; i < enemiesInZone.Length; i++)
                {
                    Collider enemy = enemiesInZone[i];
                    
                    if (enemy == null) continue;
            

                    EntityStats enemyStats = enemy.GetComponent<EntityStats>();
            
            
                    if (enemyStats == null)
                    {
                        Debug.LogWarning($"Pas d'EntityStats sur {enemy.name}", enemy);     // 2, à la place du continue seul
                        continue;
                    }
            
                    if (!enemySlow.Contains(enemy))
                    {
                        enemyStats.AddModifier(
                            StatType.Speed,
                            slowPower,
                            StatModifierType.PercentAdditive,
                            this
                        );
                
                        enemySlow.Add(enemy);
                    }
                    Debug.Log($"Speed de {enemy.name} : {enemyStats.GetStat(StatType.Speed)}");
                }

                for (int i = 0; i < enemySlow.Count; i++)
                {
                    
                }
                break;
            
            case EnumShootType.ShootMode.Fire :
                
                

                break;
        }

        for (int i = 0; i < enemiesInZone.Length; i++)
        {
            if (enemiesInZone[i] == null) continue;

            Health health = enemiesInZone[i].GetComponentInParent<Health>();

            if (health != null)
            {
                health.TakeDamage(
                    new DamageInfo(finalDamage, tower.TowerData.DamageType)
                );
            }
        }
    }
}
