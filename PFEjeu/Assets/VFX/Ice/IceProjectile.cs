using UnityEngine;

namespace IceVFX
{
    /// Projectile de glace. Dégâts : SendMessage("TakeDamage", float) et SendMessage("OnFrozen", float).
    /// Recyclé via IcePool : tout le reset est dans OnEnable.
    [RequireComponent(typeof(Rigidbody))]
    public class IceProjectile : MonoBehaviour
    {
        static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        static readonly int RadialMixId = Shader.PropertyToID("_RadialMix");
        static readonly int RadialOriginId = Shader.PropertyToID("_RadialOrigin");
        static readonly int RadialExtentId = Shader.PropertyToID("_RadialExtent");

        [Header("Mouvement")]
        public float speed = 14f;
        public float lifetime = 6f;
        [Tooltip("rad/s, 0 = tir droit")] public float homing = 0.6f;
        public Transform target;

        [Header("Impact")]
        public float damage = 15f;
        public float frozenTime = 2f;
        public LayerMask hitMask = ~0;
        [Tooltip("Laisser vide si le tag n'existe pas")] public string ignoreTag = "";
        public GameObject impactPrefab;
        [Tooltip("Durée pendant laquelle la traînée finit de se dissiper avant recyclage")]
        [Range(0.5f, 6f)] public float trailLinger = 2.5f;

        [Header("Apparition")]
        [Tooltip("Durée de l'apparition du cristal, du noyau vers l'extérieur")]
        [Range(0.05f, 1f)] public float spawnTime = 0.3f;
        [Tooltip("Origine du dissolve radial (espace objet du cristal)")]
        public Vector3 dissolveOrigin = new Vector3(0f, 0f, 0.1f);
        [Tooltip("Rayon du dissolve radial (espace objet)")]
        public float dissolveExtent = 1.1f;

        [Header("Qualité")]
        public IceQuality quality = IceQuality.High;
        [Tooltip("Plafond de particules actives en Low (projectile + impact ≈ 150)")]
        [Range(10, 150)] public int lowParticleBudget = 60;

        [Header("Debug")]
        public bool logHits;

        [Header("Références (auto)")]
        public Transform trails;
        public Light glow;
        public Renderer crystal;

        ParticleSystem[] systems;
        Collider col;
        MaterialPropertyBlock block;
        float seed, age, deadTime;
        bool dead;

        void Awake()
        {
            systems = GetComponentsInChildren<ParticleSystem>(true);
            col = GetComponent<Collider>();
            block = new MaterialPropertyBlock();
            IceVFXUtil.ApplyQuality(systems, quality, lowParticleBudget);
        }

        void OnEnable() { ResetState(); }

        void ResetState()
        {
            seed = Random.value * 10f;
            age = 0f; deadTime = 0f; dead = false;
            if (col) col.enabled = true;
            if (glow) glow.enabled = true;
            if (crystal) { crystal.enabled = true; SetDissolve(1f); }
            IceVFXUtil.Restart(systems);
        }

        void SetDissolve(float d)
        {
            crystal.GetPropertyBlock(block);
            block.SetFloat(DissolveId, d);
            block.SetFloat(RadialMixId, 1f);
            block.SetVector(RadialOriginId, dissolveOrigin);
            block.SetFloat(RadialExtentId, dissolveExtent);
            crystal.SetPropertyBlock(block);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dead)
            {
                deadTime += dt;
                if (deadTime >= trailLinger) IcePool.Release(gameObject);
                return;
            }

            age += dt;
            if (age >= lifetime) { Die(); return; }

            if (target && homing > 0f)
            {
                Vector3 d = (target.position - transform.position).normalized;
                transform.forward = Vector3.RotateTowards(transform.forward, d, homing * dt, 0f);
            }
            transform.position += transform.forward * speed * dt;
            if (glow) glow.intensity = 3f + Mathf.PerlinNoise(Time.time * 18f, seed) * 2.2f;

            // apparition : le cristal se forme du noyau vers l'extérieur
            if (crystal && age <= spawnTime + dt)
            {
                float k = Mathf.Clamp01(age / spawnTime);
                SetDissolve(1f - k * k * (3f - 2f * k));
            }
        }

        /// Fin de vie sans impact.
        void Die() { Stop(); }

        void OnTriggerEnter(Collider c)
        {
            if (dead || c.isTrigger) return;
            if (logHits) Debug.Log($"[IceVFX] hit {c.name} (layer {c.gameObject.layer}) age={age:F2}", c);
            if (!string.IsNullOrEmpty(ignoreTag) && c.CompareTag(ignoreTag)) return;
            if (((1 << c.gameObject.layer) & hitMask.value) == 0) return;

            Vector3 n = -transform.forward;
            if (Physics.Raycast(transform.position - transform.forward * 0.6f, transform.forward, out RaycastHit h, 1.5f, hitMask, QueryTriggerInteraction.Ignore))
                n = h.normal;
            if (impactPrefab) IcePool.Get(impactPrefab, transform.position, Quaternion.LookRotation(n));

            c.gameObject.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
            c.gameObject.SendMessage("OnFrozen", frozenTime, SendMessageOptions.DontRequireReceiver);
            Stop();
        }

        /// Coupe le projectile, laisse la traînée (espace monde) se dissiper, puis recycle.
        void Stop()
        {
            dead = true; deadTime = 0f;
            if (col) col.enabled = false;
            if (crystal) crystal.enabled = false;
            if (glow) glow.enabled = false;
            for (int i = 0; i < systems.Length; i++)
            {
                bool local = systems[i].main.simulationSpace == ParticleSystemSimulationSpace.Local;
                systems[i].Stop(false, local ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }
}
