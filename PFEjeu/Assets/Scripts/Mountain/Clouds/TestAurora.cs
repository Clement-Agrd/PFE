using UnityEngine;

namespace Defense.Mountain
{
    /// <summary>
    /// TEST : ruban d'aurore boreale dans le ciel. Le ruban (maillage genere une fois, non sauvegarde dans la scene) est
    /// pose a distance fixe et recentre sur la camera avant chaque rendu : comme le skybox, pas de parallaxe.
    /// Zero allocation par frame.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class TestAurora : MonoBehaviour
    {
        [Header("Etendue dans le ciel (degres)")]
        [SerializeField] private Vector2 azimuthRange = new Vector2(25f, 115f);
        [SerializeField] private float baseElevation = 15f;
        [SerializeField] private float curtainHeight = 24f;

        [Header("Forme")]
        [SerializeField, Range(8, 200)] private int segments = 72;
        [SerializeField] private float waves = 3f;
        [SerializeField] private float waveAmplitude = 4.5f;
        [SerializeField] private float phase;
        [SerializeField] private float distance = 1400f;

        private Mesh _mesh;
        private Camera _camera;

        private void OnEnable()
        {
            BuildMesh();
            Application.onBeforeRender += Reposition;
            Reposition();
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= Reposition;
            if (_mesh != null)
            {
                DestroyImmediate(_mesh);
                _mesh = null;
            }
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled) BuildMesh();
        }

        #region Maillage

        private void BuildMesh()
        {
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "TestAuroraRibbon", hideFlags = HideFlags.HideAndDontSave };
            }
            _mesh.Clear();

            int columns = segments + 1;
            var vertices = new Vector3[columns * 2];
            var uvs = new Vector2[columns * 2];
            var triangles = new int[segments * 6];

            for (int i = 0; i < columns; i++)
            {
                float t = i / (float)segments;
                float azimuth = Mathf.Lerp(azimuthRange.x, azimuthRange.y, t);
                float wave = Mathf.Sin((t * waves + phase) * Mathf.PI * 2f) * waveAmplitude
                           + Mathf.Sin((t * waves * 2.3f + phase * 1.7f) * Mathf.PI * 2f) * waveAmplitude * 0.35f;
                float bottom = baseElevation + wave;
                float top = bottom + curtainHeight * (0.75f + 0.25f * Mathf.Sin((t * waves * 1.4f + phase) * Mathf.PI * 2f));
                // Le ruban s'attenue aux extremites pour ne pas se terminer par un bord net.
                float edge = Mathf.Sin(t * Mathf.PI);
                top = Mathf.Lerp(bottom + 1f, top, edge);

                vertices[i] = Direction(azimuth, bottom) * distance;
                vertices[columns + i] = Direction(azimuth, top) * distance;
                uvs[i] = new Vector2(t, 0f);
                uvs[columns + i] = new Vector2(t, 1f);
            }

            for (int i = 0; i < segments; i++)
            {
                int k = i * 6;
                triangles[k] = i;
                triangles[k + 1] = columns + i;
                triangles[k + 2] = i + 1;
                triangles[k + 3] = i + 1;
                triangles[k + 4] = columns + i;
                triangles[k + 5] = columns + i + 1;
            }

            _mesh.vertices = vertices;
            _mesh.uv = uvs;
            _mesh.triangles = triangles;
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * distance * 3f);
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        private static Vector3 Direction(float azimuth, float elevation) =>
            Quaternion.Euler(-elevation, azimuth, 0f) * Vector3.forward;

        #endregion

        #region Position

        private void Reposition()
        {
            transform.SetPositionAndRotation(ResolveOrigin(), Quaternion.identity);
            transform.localScale = Vector3.one;
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

        #endregion
    }
}
