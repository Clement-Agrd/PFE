using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace BuildVFX
{
    /// Démo : B = lancer, T = changer la couleur d'équipe.
    public class BuildDemo : MonoBehaviour
    {
        public BuildSite target;
        static readonly Color[] Teams = { new Color(2.2f, 1.1f, 0.25f), new Color(0.3f, 1.2f, 2.6f), new Color(2.4f, 0.25f, 0.3f), new Color(0.5f, 2.2f, 0.6f) };
        int ti;
        void Update()
        {
            if (!target) return;
            bool play, tint;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var k = Keyboard.current; play = k != null && k.bKey.wasPressedThisFrame; tint = k != null && k.tKey.wasPressedThisFrame;
#else
            play = Input.GetKeyDown(KeyCode.B); tint = Input.GetKeyDown(KeyCode.T);
#endif
            if (tint) { ti = (ti + 1) % Teams.Length; target.teamColor = Teams[ti]; }
            if (play) target.Play();
        }
    }
}
