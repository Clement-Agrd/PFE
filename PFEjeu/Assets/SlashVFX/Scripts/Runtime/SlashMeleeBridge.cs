using UnityEngine;

namespace SlashVFX
{
    /// <summary>
    /// Plays the slash VFX of a SlashComboController when your own combo starts a step.
    /// Hook it to any "step started" event: see ProfessionalTPS.MeleeCombatModule.StepStarted.
    /// </summary>
    [AddComponentMenu("Slash VFX/Slash Melee Bridge")]
    public sealed class SlashMeleeBridge : MonoBehaviour
    {
        [Tooltip("Controller that owns the slash prefabs.")]
        public SlashComboController controller;
        [Tooltip("Height above the character pivot where slashes start (chest).")]
        [Range(0f, 2f)] public float chestHeight = 1.1f;
        [Tooltip("Tip of the sword, optional (the slash follows it for 0.05 s).")]
        public Transform bladeTip;

        void Awake()
        {
            if (controller == null) controller = GetComponentInChildren<SlashComboController>();
            if (controller == null)
                Debug.LogWarning("[SlashVFX] SlashMeleeBridge: no SlashComboController found.", this);
        }

        /// <summary>Call from the combat code when step <paramref name="index"/> starts.</summary>
        public void OnStep(int index)
        {
            if (controller == null) return;
            controller.readMouse = false;
            if (controller.origin == null || controller.origin == controller.transform)
                controller.origin = transform;
            controller.aim = transform.forward;
            controller.bladeTip = bladeTip;
            controller.followBlade = bladeTip != null;
            Vector3 p = transform.position;
            // the controller reads origin.position: lift it to chest height through a temporary offset
            controller.PlayStepAt(index, p + Vector3.up * chestHeight, transform.forward);
        }
    }
}
