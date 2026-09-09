using UnityEngine;

namespace ProfessionalTPS
{
    /// <summary>
    /// Put this component on the same animated hierarchy as the Animator.
    /// Later, add Animation Events to the attack clips:
    /// - AE_AttackImpact at the exact sword-hit / arrow-release / spell-release frame
    /// - AE_AttackFinished near the end of the action
    /// - AE_Footstep on foot contacts
    /// </summary>
    public sealed class PlayerAnimationEvents : MonoBehaviour
    {
        [SerializeField] private PlayerCombatController combat;
        [SerializeField] private PlayerAudioBridge audioBridge;

        public void AE_AttackImpact()
        {
            combat?.AnimationEvent_Impact();
        }

        public void AE_AttackFinished()
        {
            combat?.AnimationEvent_ActionFinished();
        }

        public void AE_Footstep()
        {
            audioBridge?.PlayFootstep();
        }
    }
}
