using UnityEngine;

[CreateAssetMenu(fileName = "SOTower", menuName = "Scriptable Objects/SOTower")]
public class SOTower : ScriptableObject
{
    [Header("Base Information")]
    [SerializeField] private string towerName;
    [SerializeField] private float detectionRange;
    
    [Header("Projectile")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private float fireRate;
    
    [Header("Level Information")]
    [SerializeField] private int minLevel;
    [SerializeField] private int maxLevel;
    
    public int MinLevel => minLevel;
    public int MaxLevel => maxLevel;
    public float DetectionRange => detectionRange;
    public GameObject ProjectilePrefab => projectilePrefab;
    public float FireRate => fireRate;
    
    [Header("Model")] 
    public GameObject[] models;
}
