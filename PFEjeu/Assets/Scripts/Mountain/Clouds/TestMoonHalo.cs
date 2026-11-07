using UnityEngine;

namespace Defense.Mountain
{
    /// <summary>
    /// TEST : halo de lune. Un quad additif place a distance fixe dans la direction de la lune du skybox, recentre sur la
    /// camera a chaque frame (le ciel est a l'infini : pas de parallaxe). Zero allocation par frame.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class TestMoonHalo : MonoBehaviour
    {
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        [Header("Direction de la lune dans le skybox (mesuree)")]
        [SerializeField] private float azimuth = 343.87f;
        [SerializeField] private float elevation = 27.85f;

        [Header("Quad")]
        [SerializeField] private float distance = 1200f;
        [Tooltip("Demi-largeur angulaire du quad (deg).")]
        [SerializeField] private float angularHalfWidth = 20f;

        [Header("Scintillement lent (nuages qui passent)")]
        [SerializeField] private float baseIntensity = 0.45f;
        [SerializeField, Range(0f, 0.5f)] private float shimmer = 0.12f;
        [SerializeField] private float shimmerSpeed = 0.15f;

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;
        private Camera _camera;

        private void OnEnable()
        {
            _renderer = GetComponent<MeshRenderer>();
            _block = new MaterialPropertyBlock();
            // Appele avant chaque rendu de camera, en jeu comme en edition (LateUpdate ne tourne pas hors Play).
            Application.onBeforeRender += Reposition;
            Reposition();
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= Reposition;
        }

        private void Reposition()
        {
            if (_renderer == null) return;

            Vector3 origin = ResolveOrigin();
            Vector3 direction = Quaternion.Euler(-elevation, azimuth, 0f) * Vector3.forward;
            Vector3 position = origin + direction * distance;

            Transform t = transform;
            t.position = position;
            t.rotation = Quaternion.LookRotation(direction);
            t.localScale = Vector3.one * (2f * distance * Mathf.Tan(angularHalfWidth * Mathf.Deg2Rad));

            float wave = 0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * shimmerSpeed * Mathf.PI * 2f + azimuth);
            float intensity = baseIntensity * (1f - shimmer + shimmer * 2f * wave);
            _renderer.GetPropertyBlock(_block);
            _block.SetFloat(IntensityId, intensity);
            _renderer.SetPropertyBlock(_block);
        }

        private Vector3 ResolveOrigin()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.SceneView view = UnityEditor.SceneView.lastActiveSceneView;
                if (view != null && view.camera != null) return view.camera.transform.position;
            }
#endif
            if (_camera == null) _camera = Camera.main;
            return _camera != null ? _camera.transform.position : Vector3.zero;
        }
    }
}
