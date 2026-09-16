using Core.HealthSystem;
using UnityEngine;

public class TowerProjectil : MonoBehaviour
{
    private Transform target;
    private DamageType damageType;

    [Min(0f)]
    [SerializeField] private float speed = 10f;

    public void SetTarget(Transform newTarget, DamageType newDamageType)
    {
        if (newTarget == null)
            return;
        
        target = newTarget;
        damageType = newDamageType;
    }

    private void Update()
    {
        if (target == null)
            return;

        transform.position = Vector3.MoveTowards(
            transform.position,
            target.position,
            speed * Time.deltaTime
        );
    }
}
