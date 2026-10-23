using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FireVFX
{
    /// Caméra libre minimale pour la démo : ZQSD/WASD + maintien du clic droit pour regarder.
    public class FireFlyCam : MonoBehaviour
    {
        public float speed = 6f, look = 0.15f;
        float yaw, pitch;
        void Start() { var e = transform.eulerAngles; yaw = e.y; pitch = e.x; }
        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current; var m = Mouse.current; if (k == null || m == null) return;
            Vector3 d = Vector3.zero;
            if (k.wKey.isPressed || k.zKey.isPressed) d += Vector3.forward;
            if (k.sKey.isPressed) d += Vector3.back;
            if (k.dKey.isPressed) d += Vector3.right;
            if (k.aKey.isPressed) d += Vector3.left;
            if (m.rightButton.isPressed) { var md = m.delta.ReadValue(); yaw += md.x * look; pitch = Mathf.Clamp(pitch - md.y * look, -89f, 89f); transform.rotation = Quaternion.Euler(pitch, yaw, 0f); }
            transform.position += transform.rotation * d * speed * Time.deltaTime;
#endif
        }
    }
}
