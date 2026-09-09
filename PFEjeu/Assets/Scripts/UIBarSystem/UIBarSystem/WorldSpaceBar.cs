using UnityEngine;

namespace Core.UIBarSystem
{
    /// <summary>
    /// Fait qu'une barre (world-space) suit une cible du monde et fait face à la
    /// caméra. Peut se cacher quand la barre est pleine. À poser sur le GameObject
    /// de la barre (un Canvas world-space, ou un objet enfant d'un UIBar).
    /// </summary>
    public sealed class WorldSpaceBar : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 worldOffset = new Vector3(0f, 2f, 0f);
        [SerializeField] private bool billboard = true;

        [Header("Masquage")]
        [SerializeField] private UIBar bar;
        [Tooltip("Cacher la barre quand elle est pleine (>= ce seuil).")]
        [SerializeField] private bool hideWhenFull = true;
        [SerializeField, Range(0f, 1f)] private float fullThreshold = 0.999f;

        [SerializeField] private Camera worldCamera;

        private void Awake()
        {
            if (worldCamera == null) worldCamera = Camera.main;
        }

        /// <summary>Change la cible suivie (ex. à la réutilisation depuis un pool).</summary>
        public void SetTarget(Transform newTarget) => target = newTarget;

        private void LateUpdate()
        {
            Camera cam = worldCamera != null ? worldCamera : Camera.main;

            if (target != null)
                transform.position = target.position + worldOffset;

            if (billboard && cam != null)
                transform.forward = cam.transform.forward; // face à la caméra

            if (hideWhenFull && bar != null)
            {
                bool full = bar.Normalized >= fullThreshold;
                if (gameObject.activeSelf == full)
                    gameObject.SetActive(!full);
            }
        }
    }
}
