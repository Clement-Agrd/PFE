using System.Collections.Generic;
using UnityEngine;
using Core.StatsSystem;
using Core.TowerDefense;
using Core.BarracksSystem;
using Priority = EnumTargetPriority.TargetPriority;
using Core.HealthSystem;
using StatType = Core.StatsSystem.EnumStats.StatTypes;

public class IATower : MonoBehaviour
{
    [Header("System")]
    [SerializeField] private Tower tower;
    
    private float timer;
    
    // Cilbe
    private Collider currentTarget;
    public Collider CurrentTarget => currentTarget ;
    
    private List<Collider> allTargets = new List<Collider>();
    public List<Collider> AllTargets => allTargets;
    
    public void ClearTargets()
    {
        currentTarget = null;
        allTargets.Clear();
        timer = 0f;
    }

    private void Awake()
    {
        tower = GetComponent<Tower>();
        if (tower == null)
        {
            Debug.LogError("IATower : aucun composant Tower trouvé sur ce GameObject.", this);
            return;
        }
    }
    
    // Update is called once per frame
    void Update()
    {
        // On ne cherche une cible que toutes les 0.1s (pas chaque frame) :
        // largement suffisant pour du gameplay, et bien moins coûteux en performance.
        if (tower == null || tower.TowerData == null) return;
        timer += Time.deltaTime;
        if (timer < 0.1f) return;
        timer = 0f;

        // Cherche tous les colliders ennemis dans le rayon de détection de la tour.
        Collider[] targets = Physics.OverlapSphere(
            transform.position,
            tower.DetectionRange,
            tower.TowerData.TargetLayer);
        
        SelectTarget(targets);
    }

    private void SelectTarget(Collider[] targets)
    {
        SOTower profile = tower.TowerData;
        Priority priority = profile.TargetPriority;
        Collider previous = currentTarget;
        currentTarget = null;
        allTargets.Clear();
        float best = float.PositiveInfinity;

        foreach (var target in targets)
        {
            var health = target.GetComponentInParent<Health>();
            if (health != null && (health.IsDead || !health.isActiveAndEnabled)) continue;

            if (profile.ShootMode == EnumShootType.ShootMode.Heal)
            {
                if (health == null || health.GetComponentInParent<EnemyController>() != null ||
                    health.GetComponentInParent<NavEnemy>() != null) continue;
                if (profile.ETAT == null && health.CurrentHealth >= health.MaxHealth) continue;
                if (profile.ETAT != null && target.gameObject.layer != LayerMask.NameToLayer("Player") &&
                    health.GetComponentInParent<BarracksSoldier>() == null) continue;
            }
            else if (health == null && priority == Priority.FirstOnPath)
            {
                continue;
            }

            if (priority == Priority.All)
            {
                allTargets.Add(target);
                continue;
            }

            float score = priority switch
            {
                Priority.FirstOnPath => FirstScore(target),
                Priority.Tank => -(health != null ? health.MaxHealth : GetEnemyStat(target, StatType.Life)),
                Priority.squishy => health != null ? health.CurrentHealth : GetEnemyStat(target, StatType.Life),
                Priority.HighDefPhy => -GetEnemyStat(target, StatType.DefensePhysic),
                Priority.HighDefMag => -GetEnemyStat(target, StatType.DefenseMagic),
                _ => target == previous ? -1f : 0f
            };
            if (currentTarget == null || score < best)
            {
                currentTarget = target;
                best = score;
            }
        }
    }

    private float FirstScore(Collider target)
    {
        var path = target.GetComponentInParent<EnemyPathFollower>();
        if (path != null && !float.IsPositiveInfinity(path.DistanceRemaining))
            return path.DistanceRemaining;
        return 100000f + (target.transform.position - transform.position).sqrMagnitude;
    }

    private static float GetEnemyStat(Collider enemy, StatType type)
    {
        var stats = enemy.GetComponentInParent<EntityStats>();
        return stats != null ? stats.GetStat(type) : 0f;
    }
}
