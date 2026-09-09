using UnityEngine;

namespace ProfessionalTPS
{
    public enum CombatMode
    {
        Melee = 0,
        Bow = 1,
        Magic = 2
    }

    public abstract class CombatModule : MonoBehaviour
    {
        protected PlayerCombatController Owner { get; private set; }

        public abstract CombatMode Mode { get; }
        public bool IsBusy { get; protected set; }

        public virtual float MovementMultiplier => IsBusy ? 0.45f : 1f;
        public virtual bool FaceCameraWhileBusy => false;
        public virtual bool CanRoll => !IsBusy;
        public virtual bool CanJump => !IsBusy;

        public virtual void Initialize(PlayerCombatController owner)
        {
            Owner = owner;
        }

        public abstract void AttackPressed();
        public abstract void Tick(float deltaTime);

        public virtual void AnimationImpact() { }
        public virtual void AnimationFinished()
        {
            IsBusy = false;
        }

        public virtual void Cancel()
        {
            IsBusy = false;
        }
    }
}
