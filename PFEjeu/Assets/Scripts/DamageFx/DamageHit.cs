using UnityEngine;

namespace DamageFx
{
    /// <summary>Un coup à afficher (struct : aucune allocation).</summary>
    public readonly struct DamageHit
    {
        public readonly float Amount;
        public readonly Vector3 Point;
        public readonly DamageFxKind Kind;
        public readonly bool IsCrit;
        public readonly bool IsBoss;
        public readonly Transform Target;
        public readonly long TargetKey;         // 0 = pas de cible : jamais de cumul
        public readonly float TargetMaxHealth;  // 0 = inconnu

        public DamageHit(float amount, Vector3 point, DamageFxKind kind, bool isCrit, bool isBoss, Transform target, float targetMaxHealth)
        {
            Amount = amount;
            Point = point;
            Kind = kind;
            IsCrit = isCrit;
            IsBoss = isBoss;
            Target = target;
            TargetKey = target != null ? unchecked((long)EntityId.ToULong(target.GetEntityId())) : 0L;
            TargetMaxHealth = targetMaxHealth;
        }
    }

    /// <summary>Vue d'un chiffre : le gestionnaire ne connaît que cette interface (testable, remplaçable).</summary>
    public interface IDamageNumberView
    {
        bool IsOpen { get; }
        bool IsHeal { get; }
        long TargetKey { get; }
        int Priority { get; }
        float SpawnTime { get; }
        float LastHitTime { get; }
        float Distance(Vector3 cameraPosition);
        Vector3 AnchorPosition { get; }
        void Begin(in DamageHit hit, Vector3 stackOffset);
        void AddHit(in DamageHit hit);
        /// <summary>Avance l'animation. Retourne false quand le chiffre est terminé (à rendre au pool).</summary>
        bool Tick(float dt, float now, Camera cam);
        void Hide();
    }
}
