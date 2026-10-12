using UnityEngine;
using Core.StatsSystem;
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

        // Cible simple : le premier trouvé. (Pour cibler le plus proche/le plus
        // avancé sur le chemin, il faudrait trier "targets" ici.)
        currentTarget = targets.Length > 0 ? targets[0] : null;
        Debug.Log(currentTarget != null
            ? "IATower : cible trouvée - " + currentTarget.name
            : "IATower : aucune cible trouvée");
        
    }
    
    private float GetEnemyStat(Collider enemy, StatType statType)
    {
        EntityStats enemyStats = enemy.GetComponentInParent<EntityStats>();

        if (enemyStats == null)
            return 0f;

        return enemyStats.GetStat(statType);
    }

    private void SelectTarget(Collider[]target)
    {
        
        float HighDefense = 0f;

        for (int i = 0; i < target.Length; i++)
        {
            Collider enemy = target[i];
            float enemyDef = GetEnemyStat(enemy, StatType.DefensePhysic);

            if (HighDefense < enemyDef)
            {
                currentTarget = enemy;
                HighDefense = enemyDef;
            }
        }
    }
    
    // Dessine la portée de détection dans l'éditeur (visible quand la tour est sélectionnée).
    private void OnDrawGizmosSelected()
    {
        if (towerData == null) return;
        Gizmos.DrawWireSphere(transform.position, towerData.DetectionRange);
    }
}
