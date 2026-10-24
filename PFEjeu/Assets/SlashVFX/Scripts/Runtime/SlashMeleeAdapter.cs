using ProfessionalTPS;
using UnityEngine;

namespace SlashVFX
{
    /// <summary>Connects MeleeCombatModule.StepStarted to the slash VFX of this character.</summary>
    [AddComponentMenu("Slash VFX/Slash Melee Adapter")]
    public sealed class SlashMeleeAdapter : MonoBehaviour
    {
        [Tooltip("Your melee module. Found automatically on this object or its parents.")]
        public MeleeCombatModule melee;
        public SlashMeleeBridge bridge;
        [Tooltip("Play the Finisher slash on the last hit of your combo (your combo has 3 hits, the VFX has 4 steps).")]
        public bool finisherOnLastHit;
        [Min(1)] public int comboLength = 3;

        void Awake()
        {
            if (melee == null) melee = GetComponentInParent<MeleeCombatModule>();
            if (melee == null) melee = GetComponentInChildren<MeleeCombatModule>();
            if (bridge == null) bridge = GetComponent<SlashMeleeBridge>();
            if (melee == null) Debug.LogWarning("[SlashVFX] SlashMeleeAdapter: no MeleeCombatModule found.", this);
            if (bridge == null) Debug.LogWarning("[SlashVFX] SlashMeleeAdapter: no SlashMeleeBridge found.", this);
        }

        void OnEnable()
        {
            if (melee != null) melee.StepStarted += OnStep;
        }

        void OnDisable()
        {
            if (melee != null) melee.StepStarted -= OnStep;
        }

        void OnStep(int index)
        {
            if (bridge == null) return;
            if (finisherOnLastHit && index >= comboLength - 1 && bridge.controller != null && bridge.controller.steps != null)
                index = bridge.controller.steps.Length - 1;
            bridge.OnStep(index);
        }
    }
}
