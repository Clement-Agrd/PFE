using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace IceVFX
{
    /// Tir au clic gauche depuis ce Transform (mets-le sur la caméra pour tester).
    /// Compatible ancien et nouveau Input System.
    public class IceShooter : MonoBehaviour
    {
        public GameObject projectile;
        public float cooldown = 0.35f;
        [Tooltip("Instances pré-créées dans le pool au démarrage (évite le hitch du premier tir)")]
        [Range(0, 16)] public int prewarm = 4;
        float next;

#if UNITY_EDITOR
        void Reset() { projectile = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VFX/Ice/Prefabs/IceProjectile.prefab"); }
#endif

        void Start()
        {
            if (!projectile || prewarm <= 0) return;
            IcePool.Prewarm(projectile, prewarm);
            var ip = projectile.GetComponent<IceProjectile>();
            if (ip && ip.impactPrefab) IcePool.Prewarm(ip.impactPrefab, Mathf.Max(2, prewarm / 2));
        }

        void Update()
        {
            bool click;
#if ENABLE_INPUT_SYSTEM
            click = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            click = Input.GetMouseButtonDown(0);
#endif
            if (click) Fire();
        }

        /// Tire un projectile (aussi appelable depuis du code / tests).
        public void Fire()
        {
            if (!projectile || Time.time < next) return;
            next = Time.time + cooldown;
            IcePool.Get(projectile, transform.position + transform.forward * 0.8f, transform.rotation);
        }
    }
}
