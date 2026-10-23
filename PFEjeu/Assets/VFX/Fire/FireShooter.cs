using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FireVFX
{
    /// Tir au clic gauche depuis ce Transform (mets-le sur la caméra pour tester). Touche Q : alterne la qualité.
    public class FireShooter : MonoBehaviour
    {
        public GameObject projectile;
        public float cooldown = 0.4f;
        float next;

#if UNITY_EDITOR
        void Reset() { projectile = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VFX/Fire/Prefabs/FireProjectile.prefab"); }
#endif
        void Update()
        {
            bool click, q;
#if ENABLE_INPUT_SYSTEM
            click = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            q = Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame;
#else
            click = Input.GetMouseButtonDown(0); q = Input.GetKeyDown(KeyCode.Q);
#endif
            if (q) { FireSettings.quality = (FireQuality)(((int)FireSettings.quality + 1) % 3); Debug.Log("[FireVFX] Quality: " + FireSettings.quality + " (s'applique aux nouvelles instances)"); }
            if (click && projectile && Time.time >= next)
            {
                next = Time.time + cooldown;
                Instantiate(projectile, transform.position + transform.forward * 0.8f, transform.rotation);
            }
        }
    }
}
