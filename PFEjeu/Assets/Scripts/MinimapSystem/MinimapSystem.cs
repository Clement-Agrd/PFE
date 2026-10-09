using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Core.TowerDefense;

namespace Core.Minimap
{
    /// <summary>
    /// Cœur de la minimap : deux caméras orthographiques vues du dessus qui
    /// rendent dans des RenderTextures — l'une suit le joueur (HUD), l'autre
    /// cadre tout le monde (grande carte, active seulement quand elle est
    /// visible). Suit aussi les ennemis vivants pour les marqueurs.
    /// </summary>
    public sealed class MinimapSystem : MonoBehaviour
    {
        public static MinimapSystem Instance { get; private set; }

        [SerializeField] private Transform player;

        [Header("Minimap (HUD)")]
        [SerializeField, Min(5f)] private float miniOrthoSize = 32f;
        [SerializeField] private int miniTextureSize = 512;

        [Header("Grande carte")]
        [SerializeField] private int worldTextureWidth = 1536;
        [SerializeField] private int worldTextureHeight = 864;
        [SerializeField, Min(0f)] private float worldPadding = 25f;
        [Tooltip("Si coché, la grande carte utilise ces limites (monde) au lieu de les déduire des marqueurs : utile quand des zones n'ont pas de marqueur.")]
        [SerializeField] private bool useMapLimits;
        [SerializeField] private Bounds mapLimits = new Bounds(Vector3.zero, new Vector3(100f, 10f, 100f));

        [Header("Rendu")]
        [SerializeField] private float cameraHeight = 120f;
        [SerializeField] private Color background = new(0.10f, 0.16f, 0.12f);
        [SerializeField] private LayerMask renderMask = ~(1 << 5); // tout sauf UI

        public Transform Player => player;
        public Camera MiniCamera { get; private set; }
        public Camera WorldCamera { get; private set; }
        public RenderTexture MiniTexture { get; private set; }
        public RenderTexture WorldTexture { get; private set; }

        public Vector2 WorldCenter { get; private set; }
        public Vector2 WorldSize { get; private set; } = new Vector2(100, 100);
        public IReadOnlyList<Transform> Enemies => _enemies;

        private readonly List<Transform> _enemies = new();
        private float _nextEnemyScan;
        private int _worldUsers;

        private void Awake()
        {
            Instance = this;
            if (player == null)
            {
                var go = GameObject.FindGameObjectWithTag("Player");
                if (go != null) player = go.transform;
            }
            MiniTexture = NewTexture(miniTextureSize, miniTextureSize, "MinimapMini");
            WorldTexture = NewTexture(worldTextureWidth, worldTextureHeight, "MinimapWorld");
            MiniCamera = NewCamera("MiniMapCamera", MiniTexture);
            WorldCamera = NewCamera("WorldMapCamera", WorldTexture);
            WorldCamera.enabled = false;
        }

        // --- éclairage neutre pour les caméras de carte ---
        // Le cycle jour/nuit teinte le soleil, l'ambiance et le brouillard (jaune
        // au couchant, sombre la nuit). Les cartes doivent rester lisibles et sans
        // filtre de couleur : on neutralise ces réglages le temps de leur rendu.
        [Header("Éclairage de la carte")]
        [SerializeField] private Color mapSunColor = Color.white;
        [SerializeField, Min(0f)] private float mapSunIntensity = 1.2f;
        [SerializeField] private Color mapAmbient = new(0.55f, 0.55f, 0.55f);

        private Light _sun;
        private Color _savedSunColor, _savedAmbient;
        private float _savedSunIntensity;
        private bool _savedFog;
        private bool _lightingOverridden;

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            RestoreLighting();
        }

        private bool IsMapCamera(Camera c) => c != null && (c == MiniCamera || c == WorldCamera);

        private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (!IsMapCamera(cam)) return;
            if (_sun == null)
            {
                _sun = RenderSettings.sun;
                if (_sun == null)
                    foreach (var l in FindObjectsByType<Light>())
                        if (l.type == LightType.Directional) { _sun = l; break; }
            }
            _savedFog = RenderSettings.fog;
            _savedAmbient = RenderSettings.ambientLight;
            RenderSettings.fog = false;
            if (RenderSettings.ambientMode != UnityEngine.Rendering.AmbientMode.Skybox)
                RenderSettings.ambientLight = mapAmbient;
            if (_sun != null)
            {
                _savedSunColor = _sun.color;
                _savedSunIntensity = _sun.intensity;
                _sun.color = mapSunColor;
                _sun.intensity = mapSunIntensity;
            }
            _lightingOverridden = true;
        }

        private void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (IsMapCamera(cam)) RestoreLighting();
        }

        private void RestoreLighting()
        {
            if (!_lightingOverridden) return;
            RenderSettings.fog = _savedFog;
            if (RenderSettings.ambientMode != UnityEngine.Rendering.AmbientMode.Skybox)
                RenderSettings.ambientLight = _savedAmbient;
            if (_sun != null)
            {
                _sun.color = _savedSunColor;
                _sun.intensity = _savedSunIntensity;
            }
            _lightingOverridden = false;
        }

        private void Start()
        {
            ComputeWorldBounds();
            FitWorldCamera(1f, Vector2.zero);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (MiniTexture != null) MiniTexture.Release();
            if (WorldTexture != null) WorldTexture.Release();
        }

        private void LateUpdate()
        {
            if (player != null && MiniCamera != null)
            {
                Vector3 p = player.position;
                MiniCamera.transform.position = new Vector3(p.x, cameraHeight, p.z);
                MiniCamera.orthographicSize = miniOrthoSize;
            }

            if (Time.unscaledTime >= _nextEnemyScan)
            {
                _nextEnemyScan = Time.unscaledTime + 0.25f;
                _enemies.Clear();
                foreach (var e in FindObjectsByType<EnemyController>())
                    if (e != null && e.isActiveAndEnabled) _enemies.Add(e.transform);
            }
        }

        /// <summary>Appelé par la vue de la grande carte pour allumer/éteindre le rendu.</summary>
        public void RequestWorldRender(bool on)
        {
            _worldUsers = Mathf.Max(0, _worldUsers + (on ? 1 : -1));
            if (WorldCamera != null) WorldCamera.enabled = _worldUsers > 0;
        }

        /// <summary>zoom 1 = tout le monde visible. pan en mètres monde.</summary>
        public void FitWorldCamera(float zoom, Vector2 pan)
        {
            if (WorldCamera == null) return;
            WorldCamera.orthographicSize = FitHalfHeight() / Mathf.Max(0.2f, zoom);
            WorldCamera.transform.position = new Vector3(WorldCenter.x + pan.x, cameraHeight, WorldCenter.y + pan.y);
        }

        /// <summary>
        /// Borne le décalage de la carte pour que la fenêtre visible reste dans les limites du monde :
        /// on peut aller jusqu'à voir exactement chaque bord à n'importe quel zoom, sans les dépasser.
        /// </summary>
        public Vector2 ClampPan(Vector2 pan, float zoom)
        {
            float aspect = (float)worldTextureWidth / worldTextureHeight;
            float halfH = FitHalfHeight() / Mathf.Max(0.2f, zoom);
            float halfW = halfH * aspect;
            float maxX = Mathf.Max(0f, WorldSize.x * 0.5f - halfW);
            float maxY = Mathf.Max(0f, WorldSize.y * 0.5f - halfH);
            return new Vector2(Mathf.Clamp(pan.x, -maxX, maxX), Mathf.Clamp(pan.y, -maxY, maxY));
        }

        /// <summary>Hauteur visible (en mètres) de la grande carte à ce zoom.</summary>
        public float WorldMetersPerViewportHeight(float zoom) => FitHalfHeight() / Mathf.Max(0.2f, zoom) * 2f;

        private float FitHalfHeight()
        {
            float aspect = (float)worldTextureWidth / worldTextureHeight;
            return Mathf.Max(WorldSize.y, WorldSize.x / aspect) * 0.5f;
        }

        private void ComputeWorldBounds()
        {
            if (useMapLimits)
            {
                WorldCenter = new Vector2(mapLimits.center.x, mapLimits.center.z);
                WorldSize = new Vector2(mapLimits.size.x + worldPadding * 2f, mapLimits.size.z + worldPadding * 2f);
                return;
            }

            bool any = false;
            Bounds b = new Bounds();
            foreach (var m in MinimapMarker.All)
            {
                if (!any) { b = new Bounds(m.transform.position, Vector3.zero); any = true; }
                else b.Encapsulate(m.transform.position);
            }
            if (player != null)
            {
                if (!any) { b = new Bounds(player.position, Vector3.zero); any = true; }
                else b.Encapsulate(player.position);
            }
            if (!any) b = new Bounds(transform.position, new Vector3(100, 0, 100));
            WorldCenter = new Vector2(b.center.x, b.center.z);
            WorldSize = new Vector2(b.size.x + worldPadding * 2f, b.size.z + worldPadding * 2f);
        }

        private static RenderTexture NewTexture(int w, int h, string n)
        {
            var rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32) { name = n, antiAliasing = 2 };
            rt.Create();
            return rt;
        }

        private Camera NewCamera(string n, RenderTexture target)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // regarde vers le bas, nord = +Z
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.cullingMask = renderMask;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = cameraHeight + 80f;
            cam.targetTexture = target;
            cam.depth = -20;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderShadows = false;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            return cam;
        }
    }
}
