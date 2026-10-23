using System.Collections;
using UnityEngine;

namespace IceVFX
{
    /// Rotation lente (cristal héros).
    public class IceSpin : MonoBehaviour
    {
        public Vector3 axis = Vector3.forward;
        public float speed = 120f;
        void Update() { transform.Rotate(axis, speed * Time.deltaTime, Space.Self); }
    }
}
