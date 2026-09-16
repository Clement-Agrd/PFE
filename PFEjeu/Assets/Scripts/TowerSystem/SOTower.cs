using Core.HealthSystem;
using UnityEngine;

[CreateAssetMenu(fileName = "SOTower", menuName = "Scriptable Objects/SOTower")]
public class SOTower : ScriptableObject
{
    [Header("Base Information")]
    [SerializeField] private string towerName;
    [SerializeField] private float detectionRange;
    public float DetectionRange => detectionRange;
    
    [Header("Projectile")]
    [SerializeField] private GameObject projectilePrefab;
    public GameObject ProjectilePrefab => projectilePrefab;
    
    [SerializeField] private float fireRate;
    public float FireRate => fireRate;
    
    [SerializeField] private DamageType damageType;
    public DamageType DamageType => damageType;
    
    
    [Header("Level Information")]
    [SerializeField] private int minLevel;
    public int MinLevel => minLevel;
    [SerializeField] private int maxLevel;
    public int MaxLevel => maxLevel;

    [Header("Model")] 
    public GameObject[] models;
}
