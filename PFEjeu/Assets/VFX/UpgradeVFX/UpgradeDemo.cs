using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace UpgradeVFX
{
    /// Démo : U = lancer, T = équipe suivante, 1/2/3 = niveau, R = revenir à l'état 1.
    public class UpgradeDemo : MonoBehaviour
    {
        public UpgradeSite target;
        static readonly Color[] Teams = { new Color(2.2f, 1.1f, 0.25f), new Color(0.3f, 1.2f, 2.6f), new Color(2.4f, 0.25f, 0.3f), new Color(0.5f, 2.2f, 0.6f) };
        int ti;
        void Update()
        {
            if (!target) return;
            bool play, tint, reset; int tier = 0;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var k = Keyboard.current; if (k == null) return;
            play = k.uKey.wasPressedThisFrame; tint = k.tKey.wasPressedThisFrame; reset = k.rKey.wasPressedThisFrame;
            if (k.digit1Key.wasPressedThisFrame) tier = 1; if (k.digit2Key.wasPressedThisFrame) tier = 2; if (k.digit3Key.wasPressedThisFrame) tier = 3;
#else
            play = Input.GetKeyDown(KeyCode.U); tint = Input.GetKeyDown(KeyCode.T); reset = Input.GetKeyDown(KeyCode.R);
            if (Input.GetKeyDown(KeyCode.Alpha1)) tier = 1; if (Input.GetKeyDown(KeyCode.Alpha2)) tier = 2; if (Input.GetKeyDown(KeyCode.Alpha3)) tier = 3;
#endif
            if (tint) { ti = (ti + 1) % Teams.Length; target.teamColor = Teams[ti]; }
            if (tier > 0) target.SetTier(tier);
            if (reset) { target.Cancel(); target.ResetToStateA(); }
            if (play) { target.ResetToStateA(); target.Play(); }
        }
    }
}
