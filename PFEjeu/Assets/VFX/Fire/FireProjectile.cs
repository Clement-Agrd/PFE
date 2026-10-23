using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FireVFX
{
    /// Boule de feu. Dégâts de zone : SendMessage("TakeDamage", float) et SendMessage("OnBurn", float).
    public class FireProjectile : MonoBehaviour
    {
        [Header("Mouvement")]
        public float speed = 16f;
        public float lifetime = 6f;
        [Tooltip("rad/s, 0 = tir droit")] public float homing = 0.7f;
        public Transform target;
        [Tooltip("0 = tir tendu, >0 = trajectoire en arc")] public float gravity = 0f;
        [Tooltip("Rayon du sphere-cast (évite de traverser les murs fins)")] public float radius = 0.35f;
        [Header("Explosion")]
        public float explosionRadius = 3.5f;
        public float damage = 40f;
        public float burnTime = 3f;
        public float pushForce = 600f;
        public LayerMask hitMask = ~0;
        [Tooltip("Laisser vide si le tag n'existe pas")] public string ignoreTag = "";
        public GameObject impactPrefab;
        [Header("Références (auto)")]
        public Transform trails;
        public Light glow;

        static readonly RaycastHit[] hitBuf = new RaycastHit[8];
        static readonly Collider[] colBuf = new Collider[48];
        Vector3 vel; float seed, born; bool dead;

        void Start() { vel = transform.forward * speed; seed = Random.value * 10f; born = Time.time; }

        void Update()
        {
            if (dead) return;
            if (Time.time - born > lifetime) { Explode(transform.position, -transform.forward); return; }

            if (target && homing > 0f)
            {
                Vector3 d = (target.position - transform.position).normalized;
                vel = Vector3.RotateTowards(vel, d * speed, homing * Time.deltaTime, 0f);
            }
            if (gravity > 0f) vel += Vector3.down * gravity * Time.deltaTime;

            Vector3 step = vel * Time.deltaTime; float dist = step.magnitude;
            if (dist > 0f && Sweep(step / dist, dist, out RaycastHit h))
            {
                Vector3 n = h.distance <= 0.0001f || h.normal == Vector3.zero ? -vel.normalized : h.normal;
                Vector3 p = h.distance <= 0.0001f ? transform.position : h.point + n * 0.1f;
                Explode(p, n);
                return;
            }
            transform.position += step;
            if (vel.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(vel);
            if (glow) { glow.intensity = 30f + Mathf.PerlinNoise(Time.time * 22f, seed) * 22f; FireLights.Report(glow.transform.position, glow.color, glow.intensity, glow.range); }
        }

        bool Sweep(Vector3 dir, float dist, out RaycastHit best)
        {
            int n = Physics.SphereCastNonAlloc(transform.position, radius, dir, hitBuf, dist, hitMask, QueryTriggerInteraction.Ignore);
            best = default; float bd = float.MaxValue; bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = hitBuf[i];
                if (!string.IsNullOrEmpty(ignoreTag) && h.collider.CompareTag(ignoreTag)) continue;
                if (h.distance < bd) { bd = h.distance; best = h; found = true; }
            }
            return found;
        }

        void Explode(Vector3 pos, Vector3 normal)
        {
            if (dead) return;
            dead = true;
            if (impactPrefab) FirePool.Get(impactPrefab, pos, Quaternion.LookRotation(normal));

            int n = Physics.OverlapSphereNonAlloc(pos, explosionRadius, colBuf, hitMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = colBuf[i];
                if (!string.IsNullOrEmpty(ignoreTag) && c.CompareTag(ignoreTag)) continue;
                Vector3 cp = c.ClosestPoint(pos);
                float fall = 1f - Mathf.Clamp01(Vector3.Distance(cp, pos) / explosionRadius);
                if (fall <= 0f) continue;
                c.gameObject.SendMessage("TakeDamage", damage * fall, SendMessageOptions.DontRequireReceiver);
                c.gameObject.SendMessage("OnBurn", burnTime, SendMessageOptions.DontRequireReceiver);
                var rb = c.attachedRigidbody;
                if (rb && !rb.isKinematic) rb.AddExplosionForce(pushForce * fall, pos, explosionRadius, 0.4f);
            }

            if (trails)
            {
                trails.SetParent(null);
                foreach (var p in trails.GetComponentsInChildren<ParticleSystem>())
                    p.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(trails.gameObject, 4f);
            }
            Destroy(gameObject);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected() { Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.5f); Gizmos.DrawWireSphere(transform.position, explosionRadius); }
#endif
    }
}
