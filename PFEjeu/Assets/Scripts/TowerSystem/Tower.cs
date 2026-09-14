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

    private float timer;
    [SerializeField] private LayerMask enemyLayer;

    public void Awake()
    {
        currentLevel = towerData.MinLevel;
        UpdateModel();
    }

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer >= 0.1f)
        {
            Collider[] targets = Physics.OverlapSphere(
                transform.position, 
                towerData.DetectionRange,
                enemyLayer);
            
            Debug.Log(targets.Length);

            foreach (Collider target in targets)
            {
                
            }
            
            timer = 0f;
        }
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
    }
}
