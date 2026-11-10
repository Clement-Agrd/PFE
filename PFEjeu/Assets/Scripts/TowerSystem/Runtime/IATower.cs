using System;
using System.Collections.Generic;
using UnityEngine;
using Core.StatsSystem;
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
            tower.TowerData.DetectionRange,
            tower.TowerData.TargetLayer);
        
        bool healing = tower.TowerData.ShootMode == EnumShootType.ShootMode.Heal;
        SelectTarget(Array.FindAll(targets, target =>
        {
            var health = target.GetComponentInParent<Health>();
            if (healing)
                return health != null && !health.IsDead && health.CurrentHealth < health.MaxHealth &&
                    (health.GetComponentInParent<Core.TowerDefense.EnemyController>() == null);
            return health == null || !health.IsDead;
        }));
        
    }
    
    // Récupére les stats des ennemis
    private float GetEnemyStat(Collider enemy, StatType statType)
    {
        EntityStats enemyStats = enemy.GetComponentInParent<EntityStats>();

        if (enemyStats == null)
            return 0f;

        return enemyStats.GetStat(statType);
    }

    // Selectionne la cible en fonction du Target priority du SOTower
    private void SelectTarget(Collider[]target)
    {
        // vide la selection
        if (Array.IndexOf(target, currentTarget) == -1)
        {
            currentTarget = null;
        }
        
        allTargets.RemoveAll(enemy => Array.IndexOf(target, enemy) == -1);
        
        switch (tower.TowerData.TargetPriority)
        {
            case EnumTargetPriority.TargetPriority.None:
                
                // Cible simple : le premier trouvé.
                if (currentTarget == null)
                currentTarget = target.Length > 0 ? target[0] : null;

                break;
            
            case EnumTargetPriority.TargetPriority.HighDefPhy:
                

                
                if (currentTarget == null &&  target.Length > 0)
                {
                    currentTarget = target[0];
                    
                    float HighDefense = GetEnemyStat(currentTarget, StatType.DefensePhysic);
                    
                    for (int i = 1; i < target.Length; i++)
                    {
                        Collider enemy = target[i];
                        float enemyDef = GetEnemyStat(enemy, StatType.DefensePhysic);

                        if (HighDefense < enemyDef)
                        {
                            HighDefense = enemyDef;
                            currentTarget = enemy;
                        }
                    }
                }
                break;
            
            case EnumTargetPriority.TargetPriority.HighDefMag:
                


                if (currentTarget == null && target.Length > 0)
                {
                    currentTarget = target[0];
                    
                    float HighDefMag = GetEnemyStat(currentTarget, StatType.DefenseMagic);

                    for (int i = 1; i < target.Length; i++)
                    {
                        Collider ennemy = target[i];
                        float DefMag = GetEnemyStat(ennemy, StatType.DefenseMagic);

                        if (HighDefMag < DefMag)
                        {
                            HighDefMag = DefMag;
                            currentTarget = ennemy;
                        }
                    }
                }
                break;
            
            case EnumTargetPriority.TargetPriority.Tank:
                


                if (currentTarget == null && target.Length > 0)
                {
                    currentTarget = target[0];
                    
                    float HighHp = GetEnemyStat(currentTarget, StatType.Life);

                    for (int i = 1; i < target.Length; i++)
                    {
                        Collider ennemy = target[i];
                        float TankEnnemy = GetEnemyStat(ennemy, StatType.Life);

                        if (HighHp < TankEnnemy)
                        {
                            HighHp = TankEnnemy;
                            currentTarget = ennemy;
                        }
                    }
                }
                break;
            
            case EnumTargetPriority.TargetPriority.squishy :
                
                if (currentTarget == null && target.Length > 0)
                {
                    currentTarget = target[0];
                    
                    float LowHp = GetEnemyStat(currentTarget, StatType.Life);

                    for (int i = 1; i < target.Length; i++)
                    {
                        Collider ennemy = target[i];
                        float SquishyEnnemy = GetEnemyStat(ennemy, StatType.Life);

                        if (LowHp > SquishyEnnemy)
                        {
                            LowHp = SquishyEnnemy;
                            currentTarget = ennemy;
                        }
                    }
                }
                break;
            
            case EnumTargetPriority.TargetPriority.All:
                
                for (int i = 0; i < target.Length; i++)
                {
                    if (!allTargets.Contains(target[i]))
                    {
                        allTargets.Add(target[i]);
                    }
                }
                break;
        }
    }
 }

