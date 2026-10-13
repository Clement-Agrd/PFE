using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Core.HealthSystem;
using Core.TowerDefense;
using Core.Village;

namespace Core.TrapSystem
{
    /// <summary>
    /// Base commune des pièges : détection des ennemis, dégâts, bonus de Forge.
    /// Le niveau d'amélioration de l'objet posé s'applique via ApplyForgeUpgrade (comme Tower).
    /// </summary>
    public abstract class TrapBase : MonoBehaviour
    {
        [Header("Dégâts")]
        [SerializeField, Min(0)] protected int baseDamage = 10;
        [SerializeField] protected DamageType damageType;

        [Header("Ciblage")]
        [Tooltip("Layers des ennemis.")]
        [SerializeField] protected LayerMask enemyMask = ~0;

        [Header("Placement")]
        [Tooltip("Rayon occupé au sol : deux pièges ne peuvent pas se chevaucher.")]
        [SerializeField, Min(0.1f)] private float footprintRadius = 0.6f;

        private static readonly List<TrapBase> Active = new();

        protected GameObject owner;
        protected int upgradeLevel;
        protected int bonusDamage;

        public float FootprintRadius => footprintRadius;

        protected virtual void OnEnable()
        {
            if (!Active.Contains(this)) Active.Add(this);
        }

        protected virtual void OnDisable() => Active.Remove(this);

        /// <summary>Vrai si un piège posé chevauche déjà ce disque (distance horizontale).</summary>
        public static bool IsOccupied(Vector3 position, float radius)
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                TrapBase t = Active[i];
                if (t == null) { Active.RemoveAt(i); continue; }

                Vector3 d = t.transform.position - position;
                d.y = 0f;
                float min = t.footprintRadius + radius;
                if (d.sqrMagnitude < min * min) return true;
            }
            return false;
        }

        protected int Damage => baseDamage + bonusDamage;

        /// <summary>Appelé au placement avec le niveau d'amélioration de l'objet consommé.</summary>
        public virtual void ApplyForgeUpgrade(ForgeDefinition forge, int itemLevel, GameObject placedBy = null)
        {
            owner = placedBy;
            upgradeLevel = Mathf.Max(0, itemLevel);
            bonusDamage = 0;

            ForgeCategoryRules rules = forge != null
                ? forge.GetRules(Core.InventorySystem.ItemCategory.Trap)
                : null;
            if (rules?.bonuses == null || upgradeLevel <= 0) return;

            foreach (UpgradeBonus bonus in rules.bonuses)
                if (bonus.stat == Core.StatsSystem.EnumStats.StatTypes.PhysicDamage)
                    bonusDamage += Mathf.RoundToInt(bonus.valuePerLevel * upgradeLevel);
        }

        protected void Hit(Health target)
        {
            if (target == null || target.IsDead) return;
            target.TakeDamage(new DamageInfo(Damage, damageType, owner != null ? owner : gameObject));
        }

        /// <summary>Ennemis (EnemyController) dans une sphère, sans doublon.</summary>
        protected List<EnemyController> FindEnemies(Vector3 center, float radius)
        {
            var result = new List<EnemyController>();
            Collider[] hits = Physics.OverlapSphere(center, radius, enemyMask, QueryTriggerInteraction.Ignore);
            foreach (Collider c in hits)
            {
                EnemyController e = c.GetComponentInParent<EnemyController>();
                if (e != null && !e.IsFinished && !e.Health.IsDead && !result.Contains(e))
                    result.Add(e);
            }
            return result;
        }

        protected static EnemyController EnemyOf(Collider other)
        {
            EnemyController e = other.GetComponentInParent<EnemyController>();
            return e != null && !e.IsFinished && !e.Health.IsDead ? e : null;
        }

        protected static void SetAgentSpeedFactor(EnemyController e, Dictionary<EnemyController, float> originals, float factor)
        {
            NavMeshAgent agent = e.Agent;
            if (agent == null) return;
            if (!originals.ContainsKey(e)) originals[e] = agent.speed;
            agent.speed = originals[e] * factor;
        }

        protected static void RestoreAgentSpeed(EnemyController e, Dictionary<EnemyController, float> originals)
        {
            if (e != null && e.Agent != null && originals.TryGetValue(e, out float speed))
                e.Agent.speed = speed;
            originals.Remove(e);
        }

        protected virtual void OnDrawGizmosSelected() { }
    }
}
