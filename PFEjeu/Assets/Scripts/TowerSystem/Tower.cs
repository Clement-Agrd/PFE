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
            }
            
            Debug.Log(targets.Length);
            
            timer = 0f;
        }
    }
    
    private void OnDrawGizmosSelected()
    {
        if (towerData == null)
            return;
        
        Gizmos.DrawWireSphere(
            transform.position,
            towerData.DetectionRange
        );
    }

    private void Shoot()
    {
        if (currentTarget == null)
            return;
        if (towerData.ProjectilePrefab == null)
            return;
        if (firePoint == null)
            return;
        
        GameObject arrow = Instantiate(
            towerData.ProjectilePrefab,
            firePoint.position,
            firePoint.rotation
        );
        
        Debug.Log("Projectile créé : " + arrow.name);

        TowerProjectil projectile = arrow.GetComponent<TowerProjectil>();

        if (projectile == null)
            return;

        projectile.SetTarget(currentTarget.transform,
            towerData.DamageType);
        
        Debug.Log("La tour tire sur : " + currentTarget.name);
    }
    
    //--------Niveau--------//

    public void Awake()
    {
        if (towerData == null)
        {
            Debug.LogError("SOTower manquant sur " + gameObject.name);
            return;
        }
        
        currentLevel = towerData.MinLevel;
        UpdateModel();
    }
    
    public void LevelUp()
    {
        currentLevel = Mathf.Clamp(currentLevel + 1, towerData.MinLevel, towerData.MaxLevel);
    }

    private void UpdateModel()
    {
        
        if (towerData.models == null || towerData.models.Length == 0)
        {
            Debug.LogError("Aucun modèle n'est configuré dans le SOTower.");
            return;
        }
        
        int modelIndex = currentLevel - 1;
        
        if (modelIndex < 0 || modelIndex >= towerData.models.Length)
        {
            Debug.LogError("Le niveau " + currentLevel + " ne possède pas de modèle.");
            return;
        }
        
        GameObject model = towerData.models[modelIndex];
        
        if (model == null)
        {
            Debug.LogError("Le modèle du niveau " + currentLevel + " est vide.");
            return;
        }

        if (currentModel != null)
        {
            Destroy(currentModel);
        }
        
        currentModel = Instantiate(model, modelParent);
        currentModel.transform.localPosition = Vector3.zero;
        firePoint = currentModel.transform.Find("FirePoint");
        
        if (firePoint == null)
        {
            Debug.LogError(
                "FirePoint introuvable dans le modèle du niveau " + currentLevel);
        }
        
        if (modelParent == null)
        {
            Debug.LogError("Model Parent manquant sur " + gameObject.name);
            return;
        }
        
    }
}
