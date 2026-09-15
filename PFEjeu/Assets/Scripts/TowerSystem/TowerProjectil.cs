using UnityEngine;

public class TowerProjectil : MonoBehaviour
{
    private Transform target;

    [SerializeField] private float speed = 10f;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
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
