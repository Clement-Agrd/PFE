using UnityEngine;

/// <summary>Test helper: loops the Attack trigger on a soldier to iterate on attack FX.</summary>
public class AttackLoopTester : MonoBehaviour
{
    [SerializeField] Animator animator;
    [SerializeField] float interval = 2.5f;
    [SerializeField] float attackSpeed = 1f;
    float next;

    void Reset() { animator = GetComponentInChildren<Animator>(); }

    void Update()
    {
        if (animator == null) return;
        animator.SetFloat("AttackSpeed", attackSpeed);
        if (Time.time >= next)
        {
            animator.SetTrigger("Attack");
            next = Time.time + interval;
        }
    }
}
