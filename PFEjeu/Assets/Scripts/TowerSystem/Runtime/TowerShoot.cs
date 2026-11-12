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
    
    private readonly RaycastHit[] groundHits = new RaycastHit[24];
    private readonly List<Health> healthsTouched = new List<Health>();

    public float GetImpactRadius(SOTower profile) => profile.ImpactRadius > 0f ? profile.ImpactRadius : zoneImpact;

    public Transform firePoint => tower.FirePoint;

    private void Awake()
    {
        tower = GetComponent<Tower>();
        if (stats == null) stats = GetComponent<EntityStats>();

        if (tower == null)
        {
            Debug.LogError("TowerShoot : aucun composant Tower trouvé sur ce GameObject.", this);
        }
    }

    public void Shoot()
    {
        if (tower == null) return;
        if (tower.TowerData == null) return;
        
        if (tower.TowerData.ShootMode != EnumShootType.ShootMode.Heal && tower.TowerData.DamageType == null) return;

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

        Collider body = GetAimCollider(tower.IATower.CurrentTarget);
        SpawnArrow(body.transform, body.bounds.center, finalDamage, true, aimCollider: body);
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
            EtatManager.ApplyTo(allyHealth, tower.TowerData.ETAT);
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
        float flightTime = tower.TowerData.ProjectilePrefab != null &&
            tower.TowerData.ProjectilePrefab.TryGetComponent<TowerProjectil>(out var arrow)
            ? arrow.TravelDuration : estimatedFlightTime;
        Vector3 impactPosition = PredictPosition(tower.IATower.CurrentTarget, flightTime);

        int finalDamage = GetFinalDamage();

        float duration = 0f;

        for (int i = 0; i < arrowCount; i++)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * GetImpactRadius(tower.TowerData);
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
        StartCoroutine(AreaDamageAfterDelay(null, impactPosition, finalDamage, duration, tower.TowerData));
    }

    private void MagicPower()
    {
        if (tower == null) return;
        if (tower.IATower == null) return;
        if (tower.TowerData == null) return;
        if (stats == null) return;
        if (tower.IATower.CurrentTarget == null) return;

        Collider body = GetAimCollider(tower.IATower.CurrentTarget);
        Vector3 impactPosition = body.bounds.center;
        float heightAboveFeet = body.bounds.extents.y;

        int finalDamage = GetFinalDamage();

        SOTower profile = tower.TowerData;
        SpawnArrow(body.transform, impactPosition, finalDamage, false,
            position =>
            {
                if (this == null) return;
                float radius = GetImpactRadius(profile);
                Vector3 ground = FindImpactGround(position - Vector3.up * heightAboveFeet, out Vector3 normal);
                if (profile.ImpactPrefab != null)
                {
                    Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, normal);
                    var effect = Instantiate(profile.ImpactPrefab, ground + normal * .01f, rotation);
                    if (effect.TryGetComponent<TowerAreaImpact>(out var area))
                        area.Play(radius);
                    Destroy(effect, 3f);
                }
                ApplyAreaDamage(ground, radius, finalDamage, profile);
            }, body);
    }

    private Vector3 FindImpactGround(Vector3 position, out Vector3 normal)
    {
        normal = Vector3.up;
        Vector3 ground = position;
        float closest = float.PositiveInfinity;
        int groundLayer = LayerMask.NameToLayer("Ground");
        int count = Physics.RaycastNonAlloc(position + Vector3.up * .65f, Vector3.down, groundHits, 15f,
            LayerMask.GetMask("Ground", "Default"), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.distance >= closest || hit.normal.y < .35f) continue;
            Collider surface = hit.collider;
            bool levelSurface = surface.gameObject.layer == groundLayer || surface is TerrainCollider ||
                (surface.attachedRigidbody == null && surface.GetComponentInParent<Health>() == null &&
                    surface.GetComponentInParent<Core.Village.Building>() == null);
            if (!levelSurface) continue;
            closest = hit.distance;
            ground = hit.point;
            normal = hit.normal;
        }
        return ground;
    }

    private IEnumerator AreaDamageAfterDelay(Transform followTarget, Vector3 fallbackPosition, int finalDamage, float delay, SOTower profile)
    {
        yield return new WaitForSeconds(delay);

        bool targetStillValid = followTarget != null && followTarget.gameObject.activeInHierarchy;
        Vector3 position = targetStillValid ? followTarget.position : fallbackPosition;

        ApplyAreaDamage(position, GetImpactRadius(profile), finalDamage, profile);
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

    private static Collider GetAimCollider(Collider detected)
    {
        var health = detected.GetComponentInParent<Health>();
        if (health != null && health.TryGetComponent<Collider>(out var body) && body.enabled && !body.isTrigger)
            return body;
        return detected;
    }

    private float SpawnArrow(Transform target, Vector3 impactPosition, int finalDamage, bool damageEnable,
        Action<Vector3> onImpact = null, Collider aimCollider = null)
    {
        var prefab = tower.TowerData.ProjectilePrefab;
        if (prefab == null || firePoint == null) return 0f;
        var pool = poolManager != null ? poolManager : PoolManager.Instance;
        GameObject arrow = pool != null
            ? pool.Spawn(prefab, firePoint.position, firePoint.rotation)
            : Instantiate(prefab, firePoint.position, firePoint.rotation);

        if (arrow == null) return 0f;

        if (!arrow.TryGetComponent(out TowerProjectil projectile)) return 0f;

        projectile.SetTarget(
            target,
            tower.TowerData.DamageType,
            finalDamage,
            tower.TowerData.ProjectileMovement,
            impactPosition,
            damageEnable,
            tower.TowerData.ArmorPenetration,
            onImpact,
            aimCollider);

        return projectile.TravelDuration;
    }

    private void ApplyAreaDamage(
        Vector3 impactPosition,
        float radius,
        int finalDamage,
        SOTower profile)
    {
        if (profile == null) return;

        Collider[] enemiesInZone = Physics.OverlapSphere(
            impactPosition,
            radius,
            profile.TargetLayer
        );

        healthsTouched.Clear();

        for (int i = 0; i < enemiesInZone.Length; i++)
        {
            Collider enemy = enemiesInZone[i];

            if (enemy == null) continue;

            Health health = enemy.GetComponentInParent<Health>();
            
            if (health == null || healthsTouched.Contains(health)) continue;
            healthsTouched.Add(health);

            // Infliger les dégâts
            health.TakeDamage(
                new DamageInfo(
                    finalDamage,
                    profile.DamageType,
                    armorPenetration: profile.ArmorPenetration
                )
            );
            EtatManager.ApplyTo(health, profile.ETAT);
        }
    }
}
