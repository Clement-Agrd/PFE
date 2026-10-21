using UnityEngine;
using IceVFX;

/// <summary>
/// Tire en boucle le projectile de glace (IceVFX) depuis ce Transform, dans son axe Z.
/// Les dégâts / gel sont forcés à 0 : pur décor, il ne blesse rien dans la scène.
/// </summary>
public class IceLoopFX : MonoBehaviour
{
    [SerializeField] private GameObject projectilePrefab;
    [Tooltip("Cible facultative : le projectile la vise (homing). Sinon tir droit.")]
    [SerializeField] private Transform target;
    [Min(0.3f)] [SerializeField] private float interval = 2.5f;
    [Min(0f)] [SerializeField] private float startDelay = 0.5f;
    [Range(0f, 8f)] [SerializeField] private float randomYawDegrees = 0f;
    [Tooltip("Portée du projectile : durée de vie = distance / vitesse")]
    [Min(1f)] [SerializeField] private float range = 14f;

    private float _next;

    private void OnEnable() { _next = Time.time + startDelay; }

    private void Start()
    {
        if (projectilePrefab != null) IcePool.Prewarm(projectilePrefab, 2);
    }

    private void Update()
    {
        if (projectilePrefab == null || Time.time < _next) return;
        _next = Time.time + interval;

        Quaternion rot = transform.rotation * Quaternion.Euler(0f, Random.Range(-randomYawDegrees, randomYawDegrees), 0f);
        GameObject go = IcePool.Get(projectilePrefab, transform.position, rot);
        if (go == null) return;

        var p = go.GetComponent<IceProjectile>();
        if (p == null) return;
        p.damage = 0f;
        p.frozenTime = 0f;
        p.target = target;
        p.lifetime = range / Mathf.Max(0.1f, p.speed);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position, transform.forward * range);
    }
}
