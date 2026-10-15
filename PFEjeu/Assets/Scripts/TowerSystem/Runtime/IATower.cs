using System;
using UnityEngine;
using Core.StatsSystem;
using NUnit.Framework;
using StatType = Core.StatsSystem.EnumStats.StatTypes;

public class IATower : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private SOTower towerData;
    
    // Ciblage variable
    [SerializeField] private LayerMask enemyLayer;
    private float timer;
    private Collider currentTarget;
    public Collider CurrentTarget => currentTarget ;
    
    // Update is called once per frame
    void Update()
    {
        // On ne cherche une cible que toutes les 0.1s (pas chaque frame) :
        // largement suffisant pour du gameplay, et bien moins coûteux en performance.
        timer += Time.deltaTime;
        if (timer < 0.1f) return;
        timer = 0f;

        // Cherche tous les colliders ennemis dans le rayon de détection de la tour.
        Collider[] targets = Physics.OverlapSphere(
            transform.position,
            towerData.DetectionRange,
            enemyLayer);
        
        SelectTarget(targets);
        
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
        // Selection la cible
        if (Array.IndexOf(target, currentTarget) == -1)
        {
            currentTarget = null;
        }
        
        switch (towerData.TargetPriority)
        {
            case EnumTargetPriority.TargetPriority.None:
                
                // Cible simple : le premier trouvé.
                if (currentTarget == null)
                currentTarget = target.Length > 0 ? target[0] : null;
                Debug.Log(currentTarget != null
                    ? "IATower : cible trouvée - " + currentTarget.name
                    : "IATower : aucune cible trouvée");
                break;
            
            case EnumTargetPriority.TargetPriority.HighDefPhy:
                
                Debug.Log("Cible la plus grande defense physique");
                
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
                
                Debug.Log("Cible la plus grande defense magic");

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
        }
    }
    
    // Dessine la portée de détection dans l'éditeur (visible quand la tour est sélectionnée).
    private void OnDrawGizmosSelected()
    {
        if (towerData == null) return;
        Gizmos.DrawWireSphere(transform.position, towerData.DetectionRange);
    }
}

