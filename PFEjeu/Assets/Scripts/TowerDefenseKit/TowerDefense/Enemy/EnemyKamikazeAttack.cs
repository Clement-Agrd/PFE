using System.Collections.Generic;
using UnityEngine;
using Core.HealthSystem;

namespace Core.TowerDefense
{
    /// <summary>
    /// Attaque kamikaze : une fois à portée de sa cible, l'ennemi amorce une courte mèche,
    /// explose (dégâts de zone sur tout ce qui possède un Health, sauf les autres ennemis),
    /// puis meurt. Se branche comme n'importe quel EnemyAttackModule (EnemyController.attackModule).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyKamikazeAttack :
        EnemyAttackModule
    {
        [Header("Explosion")]

        [SerializeField, Min(0f)]
        private float explosionDamage = 25f;

        [SerializeField, Min(0.5f)]
        private float explosionRadius = 3.5f;

        [Tooltip("Délai entre l'amorçage (animation) et l'explosion.")]
        [SerializeField, Min(0f)]
        private float fuseTime = 0.6f;

        [SerializeField]
        private LayerMask damageLayers = ~0;

        [Tooltip("Type de dégâts de l'explosion (ex. Feu).")]
        [SerializeField]
        private DamageType explosionDamageType;

        [SerializeField]
        private string fuseTrigger = "Attack";

        [Tooltip("Optionnel : effet instancié à l'explosion.")]
        [SerializeField]
        private GameObject explosionVfx;


        private float _explodeTime;

        private bool _fuseLit;

        private bool _exploded;

        private readonly HashSet<Health> _hit = new HashSet<Health>();


        public override void Tick(
            float deltaTime,
            Health target)
        {
            if (target == null || _exploded)
                return;

            if (_fuseLit)
            {
                if (Time.time >= _explodeTime)
                    Explode();

                return;
            }

            if (!CanHit(target))
                return;

            _fuseLit = true;

            _explodeTime =
                Time.time + ScaleTime(fuseTime);

            IsBusy = true;

            TriggerAnimation(fuseTrigger);
        }


        private void Explode()
        {
            _exploded = true;

            IsBusy = false;

            Vector3 center = transform.position;

            _hit.Clear();

            Collider[] colliders =
                Physics.OverlapSphere(
                    center,
                    explosionRadius,
                    damageLayers,
                    QueryTriggerInteraction.Ignore
                );

            foreach (Collider col in colliders)
            {
                if (col.GetComponentInParent<EnemyController>() != null)
                    continue;

                Health health = col.GetComponentInParent<Health>();

                if (health == null || health.IsDead || !_hit.Add(health))
                    continue;

                DamageInfo info =
                    new DamageInfo(
                        Mathf.Max(1, Mathf.RoundToInt(explosionDamage)),
                        explosionDamageType,
                        gameObject,
                        DamageSourceKind.None
                    );

                health.TakeDamage(in info);
            }

            if (explosionVfx != null)
                Instantiate(explosionVfx, center, Quaternion.identity);

            // Le kamikaze se sacrifie : passe par Health pour que la vague le compte comme mort.
            if (Owner != null && Owner.Health != null)
                Owner.Health.Kill();
        }


        public override void Cancel()
        {
            // Une fois la mèche allumée, rien ne l'arrête.
            if (_fuseLit && !_exploded)
                return;

            base.Cancel();
        }


        public override void ResetRuntime()
        {
            base.ResetRuntime();

            _fuseLit = false;

            _exploded = false;

            _explodeTime = 0f;

            _hit.Clear();
        }
    }
}
