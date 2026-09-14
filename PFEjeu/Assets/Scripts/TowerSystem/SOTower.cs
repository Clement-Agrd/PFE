using UnityEngine;

[CreateAssetMenu(fileName = "SOTower", menuName = "Scriptable Objects/SOTower")]
public class SOTower : ScriptableObject
{
    [Header("Base Information")]
    [SerializeField] private string towerName;
    [SerializeField] private float detectionRange;
    
    
    [SerializeField] private int minLevel;
    [SerializeField] private int maxLevel;
    
    public int MinLevel => minLevel;
    public int MaxLevel => maxLevel;
    public float DetectionRange => detectionRange;
    
    [Header("Model")] 
    public GameObject[] models;
}
