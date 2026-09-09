using System;
using System.Collections.Generic;
using UnityEngine;
using Core.HealthSystem;

namespace ProfessionalTPS
{
    [Serializable]
    public sealed class MeleeComboStep
    {
        [Min(0)] public int damage = 20;

        [Tooltip("Temps avant que le coup touche, si les Animation Events sont désactivés.")]
        [Min(0f)] public float startup = 0.18f;

        [Tooltip("Temps de récupération après l'impact.")]
        [Min(0f)] public float recovery = 0.28f;

        [Tooltip("À partir de quand le joueur peut mettre l'attaque suivante en file d'attente.")]
        [Min(0f)] public float queueOpensAt = 0.16f;

        [Min(0.05f)] public float hitRadius = 0.85f;
        [Min(0f)] public float forwardOffset = 1.0f;
    }

    public sealed class MeleeCombatModule : CombatModule
    {
        [Header("Prototype / Animation")]
        [Tooltip(
            "OFF = timings gérés par les valeurs Startup/Recovery.\n" +
            "ON = les animations doivent appeler les Animation Events."
        )]
        [SerializeField] private bool useAnimationEvents = false;

        [Header("Dégâts")]
        [Tooltip("Type de dégâts de l'arme : Slash, Physical, etc.")]
        [SerializeField] private DamageType damageType;

        [Header("Hit Detection")]
        [SerializeField] private Transform hitOrigin;

        [Tooltip("Layers pouvant recevoir les attaques.")]
        [SerializeField] private LayerMask targetMask = ~0;

        [SerializeField]
        private QueryTriggerInteraction triggerInteraction =
            QueryTriggerInteraction.Ignore;

        [Header("Combo 3 coups")]
        [SerializeField]
        private MeleeComboStep[] combo =
        {
            new MeleeComboStep
            {
                damage = 20,
                startup = 0.16f,
                recovery = 0.24f,
                queueOpensAt = 0.12f,
                hitRadius = 0.8f,
                forwardOffset = 0.95f
            },

            new MeleeComboStep
            {
                damage = 24,
                startup = 0.18f,
                recovery = 0.26f,
                queueOpensAt = 0.13f,
                hitRadius = 0.9f,
                forwardOffset = 1.0f
            },

            new MeleeComboStep
            {
                damage = 34,
                startup = 0.24f,
                recovery = 0.38f,
                queueOpensAt = 0.17f,
                hitRadius = 1.0f,
                forwardOffset = 1.05f
            }
        };

        [Header("Déplacement pendant l'attaque")]
        [SerializeField, Range(0f, 1f)]
        private float attackMovementMultiplier = 0.35f;

        // Buffer non-alloué pour éviter de générer du garbage à chaque attaque.
        private readonly Collider[] _hits = new Collider[24];

        // Empêche un même coup de toucher plusieurs fois la même cible
        // si elle possède plusieurs colliders.
        private readonly HashSet<IDamageable> _damagedThisSwing =
            new HashSet<IDamageable>();

        private int _stepIndex;
        private float _timer;

        private bool _impactDone;
        private bool _queuedNext;

        public override CombatMode Mode => CombatMode.Melee;

        public override float MovementMultiplier =>
            IsBusy ? attackMovementMultiplier : 1f;

        public override void AttackPressed()
        {
            if (combo == null || combo.Length == 0)
                return;

            // Première attaque
            if (!IsBusy)
            {
                StartStep(0);
                return;
            }

            MeleeComboStep step =
                combo[Mathf.Clamp(_stepIndex, 0, combo.Length - 1)];

            // Le joueur appuie pendant le bon timing :
            // on mémorise l'attaque suivante.
            if (_stepIndex < combo.Length - 1 &&
                _timer >= step.queueOpensAt)
            {
                _queuedNext = true;
            }
        }

        public override void Tick(float deltaTime)
        {
            if (!IsBusy)
                return;

            _timer += deltaTime;

            // Quand les vraies animations seront disponibles,
            // les Animation Events prendront le contrôle du timing.
            if (useAnimationEvents)
                return;

            MeleeComboStep step = combo[_stepIndex];

            if (!_impactDone &&
                _timer >= step.startup)
            {
                AnimationImpact();
            }

            if (_timer >= step.startup + step.recovery)
            {
                CompleteCurrentStep();
            }
        }

        public override void AnimationImpact()
        {
            if (!IsBusy || _impactDone)
                return;

            _impactDone = true;

            DoHit(combo[_stepIndex]);

            Owner?.Audio?.PlayMeleeSwing(_stepIndex);
        }

        public override void AnimationFinished()
        {
            if (!IsBusy)
                return;

            CompleteCurrentStep();
        }

        public override void Cancel()
        {
            base.Cancel();

            _timer = 0f;
            _stepIndex = 0;
            _queuedNext = false;
            _impactDone = false;

            _damagedThisSwing.Clear();
        }

        private void StartStep(int index)
        {
            _stepIndex =
                Mathf.Clamp(index, 0, combo.Length - 1);

            _timer = 0f;
            _impactDone = false;
            _queuedNext = false;

            _damagedThisSwing.Clear();

            IsBusy = true;

            Owner?.Animation?.PlayMeleeAttack(_stepIndex);
        }

        private void CompleteCurrentStep()
        {
            // Une attaque suivante a été mise en file.
            if (_queuedNext &&
                _stepIndex < combo.Length - 1)
            {
                StartStep(_stepIndex + 1);
                return;
            }

            // Fin du combo.
            IsBusy = false;

            _timer = 0f;
            _stepIndex = 0;

            _queuedNext = false;
            _impactDone = false;

            _damagedThisSwing.Clear();
        }

        private void DoHit(MeleeComboStep step)
        {
            if (Owner == null)
                return;

            Transform originTransform =
                hitOrigin != null
                    ? hitOrigin
                    : transform;

            Vector3 center =
                originTransform.position +
                Owner.transform.forward * step.forwardOffset;

            int count = Physics.OverlapSphereNonAlloc(
                center,
                step.hitRadius,
                _hits,
                targetMask,
                triggerInteraction
            );

            for (int i = 0; i < count; i++)
            {
                Collider hit = _hits[i];

                if (hit == null)
                    continue;

                // On ne peut jamais se frapper soi-même.
                if (hit.transform.IsChildOf(Owner.transform))
                    continue;

                IDamageable damageable =
                    FindDamageable(hit);

                if (damageable == null)
                    continue;

                if (damageable.IsDead)
                    continue;

                // Une cible avec plusieurs colliders ne reçoit
                // les dégâts qu'une seule fois pour ce coup.
                if (!_damagedThisSwing.Add(damageable))
                    continue;

                DamageInfo damageInfo =
                    new DamageInfo(
                        step.damage,
                        damageType,
                        Owner.gameObject
                    );

                damageable.TakeDamage(in damageInfo);
            }
        }

        /// <summary>
        /// Recherche un IDamageable sur le collider
        /// ou n'importe lequel de ses parents.
        ///
        /// Cela fonctionne avec Health, mais aussi avec
        /// n'importe quel autre composant implémentant IDamageable.
        /// </summary>
        private static IDamageable FindDamageable(Collider collider)
        {
            if (collider == null)
                return null;

            MonoBehaviour[] behaviours =
                collider.GetComponentsInParent<MonoBehaviour>(true);

            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IDamageable damageable)
                    return damageable;
            }

            return null;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (combo == null || combo.Length == 0)
                return;

            int index =
                Mathf.Clamp(_stepIndex, 0, combo.Length - 1);

            MeleeComboStep step =
                combo[index];

            Transform ownerTransform =
                Owner != null
                    ? Owner.transform
                    : transform;

            Transform originTransform =
                hitOrigin != null
                    ? hitOrigin
                    : transform;

            Vector3 center =
                originTransform.position +
                ownerTransform.forward * step.forwardOffset;

            Gizmos.DrawWireSphere(
                center,
                step.hitRadius
            );
        }
#endif
    }
}