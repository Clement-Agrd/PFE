using System;
using System.Collections;
using System.Collections.Generic;
using Core.HealthSystem;
using Core.PoolingSystem;
using Core.StatsSystem;
using Core.TowerDefense;
using StatType = Core.StatsSystem.EnumStats.StatTypes;
using UnityEngine;
using UnityEngine.AI;

public class TowerShoot : MonoBehaviour
{
    [Header("System")]
    [SerializeField] private Tower tower;
    [SerializeField] private EntityStats stats;
    [SerializeField] private PoolManager poolManager;

    [Header("Data")]
    [SerializeField] private float zoneImpact = 3f;
    [SerializeField] private int arrowCount = 10;

    [Header("Tir prédictif (RainArrow)")]
    [SerializeField] private bool predictMovement = true;
    // Temps de vol estimé des flèches, utilisé pour anticiper le déplacement de la cible.
    [SerializeField] private float estimatedFlightTime = 1f;
    
    private readonly List<Health> healthsTouched = new List<Health>();

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
        if (tower == null) return;
        if (tower.TowerData == null) return;
        
        switch (tower.TowerData.ShootMode)
        {
            case EnumShootType.ShootMode.Classic:
                ClassicShoot();
                break;

            case EnumShootType.ShootMode.MagicPower:
                MagicPower();
                break;

            case EnumShootType.ShootMode.Heal:
                HealAllies();
                break;

            case EnumShootType.ShootMode.RainArrow:
                RainArrow();
                break;
        }
    }

    public static StatType GetDamageStat(DamageCategory category)
    {
        switch (category)
        {
            case DamageCategory.Physical:
                return StatType.PhysicDamage;

            case DamageCategory.Magical:
                return StatType.MagicDamage;

            default:
                return StatType.PhysicDamage;
        }
    }

    // CORRIGÉ : ce calcul était recopié dans 3 méthodes, il est maintenant au même endroit.
    private int GetFinalDamage()
    {
        StatType damageStat = GetDamageStat(tower.TowerData.DamageType.Category);
        return Mathf.RoundToInt(stats.GetStat(damageStat));
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

        int finalDamage = GetFinalDamage();

        SpawnArrow(
            tower.IATower.CurrentTarget.transform,
            tower.IATower.CurrentTarget.transform.position,
            finalDamage,
            true);
    }
    
    public void HealAllies()
    {
        if (tower.IATower == null) return;
        if (tower.TowerData == null) return;
        if (stats == null) return;

        int healAmount = Mathf.RoundToInt(stats.GetStat(StatType.MagicDamage));
        if (healAmount <= 0) return;
        healthsTouched.Clear();
        
        for (int i = 0; i < tower.IATower.AllTargets.Count; i++)
        {
            Collider ally = tower.IATower.AllTargets[i];

            if (ally == null) continue;
            
            Health allyHealth = ally.GetComponentInParent<Health>();

            if (allyHealth == null)
            {
                Debug.LogWarning($"Pas d'EntityStats sur {ally.name}", ally);
                continue;
            }
            if (healthsTouched.Contains(allyHealth)) continue;
            healthsTouched.Add(allyHealth);

            allyHealth.Heal(healAmount);
        }
    }

    public void RainArrow()
    {
        if (tower == null) return;
        if (tower.IATower == null) return;
        if (tower.TowerData == null) return;
        if (stats == null) return;
        if (tower.IATower.CurrentTarget == null) return;

        // on vise l'endroit où l'ennemi SERA, pas où il est au moment du tir.
        Vector3 impactPosition = PredictPosition(tower.IATower.CurrentTarget, estimatedFlightTime);

        int finalDamage = GetFinalDamage();

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

        // La zone reste fixe : c'est là que les flèches tombent visuellement.
        StartCoroutine(AreaDamageAfterDelay(null, impactPosition, finalDamage, duration));
    }

    private void MagicPower()
    {
        if (tower == null) return;
        if (tower.IATower == null) return;
        if (tower.TowerData == null) return;
        if (stats == null) return;
        if (tower.IATower.CurrentTarget == null) return;

        Transform target = tower.IATower.CurrentTarget.transform;
        Vector3 impactPosition = target.position;

        int finalDamage = GetFinalDamage();

        float duration = SpawnArrow(
            target,
            impactPosition,
            finalDamage,
            false
        );

        // on passe la cible, pour lire sa position au moment de l'impact.
        StartCoroutine(AreaDamageAfterDelay(target, impactPosition, finalDamage, duration));
    }
    
    private IEnumerator AreaDamageAfterDelay(Transform followTarget, Vector3 fallbackPosition, int finalDamage, float delay)
    {
        yield return new WaitForSeconds(delay);

        bool targetStillValid = followTarget != null && followTarget.gameObject.activeInHierarchy;
        Vector3 position = targetStillValid ? followTarget.position : fallbackPosition;

        ApplyAreaDamage(position, zoneImpact, finalDamage);
    }

    // Position estimée de la cible dans "time" secondes.
    // Fonctionne si l'ennemi utilise un NavMeshAgent ; sinon, renvoie sa position actuelle.
    private Vector3 PredictPosition(Collider target, float time)
    {
        Vector3 position = target.transform.position;

        if (!predictMovement) return position;

        NavMeshAgent agent = target.GetComponentInParent<NavMeshAgent>();

        if (agent != null)
        {
            position += agent.velocity * time;
        }

        return position;
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
            tower.TowerData.TargetLayer
        );

        healthsTouched.Clear();

        for (int i = 0; i < enemiesInZone.Length; i++)
        {
            Collider enemy = enemiesInZone[i];

            if (enemy == null) continue;

            Health health = enemy.GetComponentInParent<Health>();
            
            if (health == null || healthsTouched.Contains(health)) continue;
            healthsTouched.Add(health);

            // Appliquer l'état défini dans le SOTower
            EtatManager etatManager = enemy.GetComponentInParent<EtatManager>();

            if (etatManager != null && tower.TowerData.ETAT != null)
            {
                etatManager.AddEtat(tower.TowerData.ETAT);
            }

            // Infliger les dégâts
            health.TakeDamage(
                new DamageInfo(
                    finalDamage,
                    tower.TowerData.DamageType
                )
            );
        }
    }
}
