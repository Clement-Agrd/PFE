using System;
using Core.StatsSystem;
using UnityEngine;

public class Tower : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private SOTower towerData;
    
    [Header("Level")]
    [SerializeField] private int currentLevel;
    
    [Header("Model")]
    [SerializeField] private Transform modelParent;
    private GameObject currentModel;
    
    [Header("Projectile")]
    [SerializeField] private Transform firePoint;
    private float shootTimer;

    private float timer;
    [SerializeField] private LayerMask enemyLayer;
    private Collider currentTarget;
    
    //--------Détection--------//

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer >= 0.1f)
        {
            Collider[] targets = Physics.OverlapSphere(
                transform.position, 
                towerData.DetectionRange,
                enemyLayer);
            
            if (targets.Length > 0)
            {
                currentTarget = targets[0];
            }
            else
            {
                currentTarget = null;
            }
            
            if (currentTarget != null)
            {
                Debug.Log("Cible : " + currentTarget.name);
                
                shootTimer += 0.1f;

                if (shootTimer >= 1f / towerData.FireRate)
                {
                    Shoot();
                    shootTimer = 0f;
                }
                else
                {
                    shootTimer = 0f;
                }
            }
            
            Debug.Log(targets.Length);

            foreach (Collider target in targets)
            {
                
            }
            
            timer = 0f;
        }
    }
    
    private void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(
            transform.position,
            towerData.DetectionRange
        );
    }

    private void Shoot()
    {
        GameObject arrow = Instantiate(
            towerData.ProjectilePrefab,
            firePoint.position,
            firePoint.rotation
        );

        arrow.GetComponent<TowerProjectil>().SetTarget(currentTarget.transform);
        
        Debug.Log("La tour tire sur : " + currentTarget.name);
    }
    
    //--------Niveau--------//

    public void Awake()
    {
        currentLevel = towerData.MinLevel;
        UpdateModel();
    }
    
    public void LevelUp()
    {
        currentLevel = Mathf.Clamp(currentLevel + 1, towerData.MinLevel, towerData.MaxLevel);
    }

    private void UpdateModel()
    {
        int modelIndex = currentLevel - 1;
        
        GameObject model = towerData.models[modelIndex];

        if (currentModel != null)
        {
            Destroy(currentModel);
        }
        
        currentModel = Instantiate(model, modelParent);
        currentModel.transform.localPosition = Vector3.zero;
        firePoint = currentModel.transform.Find("FirePoint");
    }
}
