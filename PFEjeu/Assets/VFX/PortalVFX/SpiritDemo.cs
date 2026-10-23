using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace SpiritVFX
{
    /// Démo : P = ouvrir / fermer.
    public class SpiritDemo : MonoBehaviour
    {
        public SpiritGate target;
        void Update()
        {
            if (!target) return;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var k = Keyboard.current; if (k != null && k.pKey.wasPressedThisFrame) target.Toggle();
#else
            if (Input.GetKeyDown(KeyCode.P)) target.Toggle();
#endif
        }
    }
}
