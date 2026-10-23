using UnityEngine;

namespace FireVFX
{
    /// Tire en boucle une boule de feu depuis ce Transform (axe +Z). Décor de zone FX : pas de dégâts, pas de hit stop ni de shake.
    public class FireLoopShooter : MonoBehaviour
    {
        [SerializeField] private GameObject projectile;
        [Min(0.5f)] [SerializeField] private float interval = 4f;
        [Min(0f)] [SerializeField] private float startDelay = 1f;
        [SerializeField] private float speed = 14f;
        [Tooltip("Layers que le projectile peut toucher (le sol de la zone).")]
        [SerializeField] private LayerMask hitMask = ~0;
        [SerializeField] private bool silentFeedback = true;

        private float _next;

        private void OnEnable() { _next = Time.time + startDelay; if (silentFeedback) FireSettings.feedback = false; }
        private void OnDisable() { if (silentFeedback) FireSettings.feedback = true; }

        private void Update()
        {
            if (projectile == null || Time.time < _next) return;
            _next = Time.time + interval;

            GameObject go = Instantiate(projectile, transform.position, transform.rotation);
            var fp = go.GetComponent<FireProjectile>();
            if (fp == null) return;
            fp.speed = speed; fp.homing = 0f; fp.damage = 0f; fp.pushForce = 0f; fp.burnTime = 0f;
            fp.hitMask = hitMask;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0.1f);
            Gizmos.DrawRay(transform.position, transform.forward * 8f);
        }
    }
}
