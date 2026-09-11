using UnityEngine;

[CreateAssetMenu(fileName = "SOTower", menuName = "Scriptable Objects/SOTower")]
public class SOTower : ScriptableObject
{
    [Header("Base Information")]
    public string towerName;

    [Header("Tower Mesh")] 
    [SerializeField] private Mesh model1;
    [SerializeField] private Mesh model2;
    [SerializeField] private Mesh model3;
    
    
}
