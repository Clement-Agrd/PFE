using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Core.TowerDefense;

namespace Core.Minimap
{
    /// <summary>
    /// Cœur de la minimap. Le monde est "photographié" UNE SEULE FOIS par une caméra
    /// orthographique vue du dessus (bake dans une RenderTexture), puis la minimap HUD
    /// et la grande carte ne font qu'afficher une portion (uvRect) de cette image :
    /// plus aucun mesh n'est rendu pendant le jeu. Un nouveau bake est fait à
    /// l'ouverture de la grande carte (ou via Rebake()) pour refléter les constructions.
    /// Suit aussi les ennemis vivants pour les marqueurs.
    /// </summary>
    public sealed class MinimapSystem : MonoBehaviour
    {
        public static MinimapSystem Instance { get; private set; }

        [SerializeField] private Transform player;

        [Header("Minimap (HUD)")]
        [Tooltip("Demi-hauteur (mètres) de la zone du monde visible dans la minimap HUD.")]
        [SerializeField, Min(5f)] private float miniOrthoSize = 32f;
        [SerializeField] private int miniTextureSize = 512; // conservé pour compatibilité des scènes (inutilisé)

        [Header("Grande carte")]
        [SerializeField] private int worldTextureWidth = 1536;
        [SerializeField] private int worldTextureHeight = 864;
        [SerializeField, Min(0f)] private float worldPadding = 25f;
        [Tooltip("Si coché, la grande carte utilise ces limites (monde) au lieu de les déduire des marqueurs : utile quand des zones n'ont pas de marqueur.")]
        [SerializeField] private bool useMapLimits;
        [SerializeField] private Bounds mapLimits = new Bounds(Vector3.zero, new Vector3(100f, 10f, 100f));

        [Header("Performances")]
        [Tooltip("Multiplie la résolution de l'image de la carte (bake unique). 1.5 = net même en zoom x5.")]
        [SerializeField, Range(0.5f, 3f)] private float bakeResolutionScale = 1.5f;
        [Tooltip("Cadence de mise à jour de la minimap HUD (défilement + icônes). Aucun rendu 3D : seulement de l'UI.")]
        [SerializeField, Range(5f, 60f)] private float miniUpdateRate = 30f;
        [Tooltip("Refait un bake à l'ouverture de la grande carte (pour voir les tours construites, etc.).")]
        [SerializeField] private bool rebakeOnWorldMapOpen = true;
        [Tooltip("Rebake automatique périodique (secondes). 0 = jamais. Chaque bake coûte une frame de rendu du monde.")]
        [SerializeField, Min(0f)] private float autoRebakeInterval = 0f;

        [Header("Rendu")]
        [SerializeField] private float cameraHeight = 120f;
        [SerializeField] private Color background = new(0.10f, 0.16f, 0.12f);
        [SerializeField] private LayerMask renderMask = ~(1 << 5); // tout sauf UI

        public Transform Player => player;
        public Camera WorldCamera { get; private set; }
        public RenderTexture WorldTexture { get; private set; }

        public Vector2 WorldCenter { get; private set; }
        public Vector2 WorldSize { get; private set; } = new Vector2(100, 100);
        public IReadOnlyList<Transform> Enemies => _enemies;

        /// <summary>Incrémenté à cadence réduite tant que la minimap HUD est visible.</summary>
        public int MiniStamp { get; private set; }
        /// <summary>Vrai une fois le premier bake terminé.</summary>
        public bool IsBaked { get; private set; }

        private readonly List<Transform> _enemies = new();
        private int _worldUsers;
        private bool _miniVisible = true;
        private float _nextMiniTick;
        private float _nextAutoRebake;
        private bool _baking;
        private int _worldTexW, _worldTexH;

        private void Awake()
        {
            Instance = this;
            if (player == null)
            {
                var go = GameObject.FindGameObjectWithTag("Player");
                if (go != null) player = go.transform;
            }
            _worldTexW = Mathf.Max(256, Mathf.RoundToInt(worldTextureWidth * bakeResolutionScale));
            _worldTexH = Mathf.Max(144, Mathf.RoundToInt(worldTextureHeight * bakeResolutionScale));
            WorldTexture = NewTexture(_worldTexW, _worldTexH, "MinimapWorld", 4);
            WorldCamera = NewCamera("WorldMapCamera", WorldTexture);
            WorldCamera.enabled = false; // activée uniquement le temps d'un bake (1 frame)
        }

        /// <summary>Appelé par le HUD : une minimap masquée ne met plus ses icônes à jour.</summary>
        public void SetMiniVisible(bool visible) => _miniVisible = visible;

        // --- éclairage neutre pour la caméra de bake ---
        // Le cycle jour/nuit teinte le soleil, l'ambiance et le brouillard (jaune
        // au couchant, sombre la nuit). La carte doit rester lisible et sans
        // filtre de couleur : on neutralise ces réglages le temps du bake.
        [Header("Éclairage de la carte")]
        [SerializeField] private Color mapSunColor = Color.white;
        [SerializeField, Min(0f)] private float mapSunIntensity = 1.2f;
        [SerializeField] private Color mapAmbient = new(0.55f, 0.55f, 0.55f);

        private Light _sun;
        private bool _sunSearched;
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

        private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != WorldCamera) return;
            if (_sun == null && !_sunSearched)
            {
                _sunSearched = true; // une seule recherche
                _sun = RenderSettings.sun;
                if (_sun == null)
                    foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                        if (l.type == LightType.Directional) { _sun = l; break; }
            }
            _savedFog = RenderSettings.fog;
            _savedAmbient = RenderSettings.ambientLight;
            RenderSettings.fog = false;
            if (RenderSettings.ambientMode != AmbientMode.Skybox)
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
            if (cam == WorldCamera) RestoreLighting();
        }

        private void RestoreLighting()
        {
            if (!_lightingOverridden) return;
            RenderSettings.fog = _savedFog;
            if (RenderSettings.ambientMode != AmbientMode.Skybox)
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
            FitWorldCamera();
            Rebake();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (WorldTexture != null) WorldTexture.Release();
        }

        private void LateUpdate()
        {
            float now = Time.unscaledTime;

            if (_miniVisible && now >= _nextMiniTick)
            {
                _nextMiniTick = now + 1f / miniUpdateRate;
                MiniStamp++;
            }

            if (autoRebakeInterval > 0f && now >= _nextAutoRebake)
            {
                _nextAutoRebake = now + autoRebakeInterval;
                Rebake();
            }

            // Liste des ennemis : copie du registre, seulement quand une vue va se redessiner.
            if (_miniVisible || _worldUsers > 0)
            {
                _enemies.Clear();
                foreach (var e in EnemyController.Active)
                    if (e != null) _enemies.Add(e.transform);
            }
        }

        // ---------- bake ----------

        /// <summary>Re-photographie le monde (coût = 1 frame de rendu, ponctuel).</summary>
        public void Rebake()
        {
            if (_baking || WorldCamera == null) return;
            StartCoroutine(BakeRoutine());
        }

        private IEnumerator BakeRoutine()
        {
            _baking = true;
            yield return null; // laisse la scène finir son initialisation
            WorldCamera.enabled = true; // rendue à la fin de cette frame
            yield return null;
            WorldCamera.enabled = false;
            IsBaked = true;
            _baking = false;
        }

        /// <summary>Appelé par la vue de la grande carte à l'ouverture / fermeture.</summary>
        public void RequestWorldRender(bool on)
        {
            _worldUsers = Mathf.Max(0, _worldUsers + (on ? 1 : -1));
            if (on && _worldUsers == 1 && rebakeOnWorldMapOpen && IsBaked) Rebake();
        }

        // ---------- géométrie carte <-> uv ----------

        private float TexAspect => (float)worldTextureWidth / worldTextureHeight;

        private float FitHalfHeight()
        {
            return Mathf.Max(WorldSize.y, WorldSize.x / TexAspect) * 0.5f;
        }

        /// <summary>Cadre la caméra de bake sur tout le monde.</summary>
        private void FitWorldCamera()
        {
            if (WorldCamera == null) return;
            WorldCamera.orthographicSize = FitHalfHeight();
            WorldCamera.transform.position = new Vector3(WorldCenter.x, cameraHeight, WorldCenter.y);
        }

        /// <summary>Position monde (x,z) -> coordonnées uv (0..1) dans l'image bakée.</summary>
        public Vector2 WorldToUv(Vector3 pos)
        {
            float halfH = FitHalfHeight();
            float halfW = halfH * TexAspect;
            return new Vector2(
                (pos.x - WorldCenter.x) / (2f * halfW) + 0.5f,
                (pos.z - WorldCenter.y) / (2f * halfH) + 0.5f);
        }

        /// <summary>uvRect (portion de l'image) centré sur un point monde, de hauteur visible donnée (en mètres).</summary>
        public Rect UvRect(Vector2 centerWorldXZ, float visibleHeightMeters, float viewAspect)
        {
            float halfH = FitHalfHeight();
            float halfW = halfH * TexAspect;
            float h = visibleHeightMeters / (2f * halfH);
            float w = visibleHeightMeters * viewAspect / (2f * halfW);
            float cu = (centerWorldXZ.x - WorldCenter.x) / (2f * halfW) + 0.5f;
            float cv = (centerWorldXZ.y - WorldCenter.y) / (2f * halfH) + 0.5f;
            return new Rect(cu - w * 0.5f, cv - h * 0.5f, w, h);
        }

        /// <summary>uvRect de la minimap HUD (suit le joueur).</summary>
        public Rect MiniUvRect(float viewAspect)
        {
            Vector2 c = player != null ? new Vector2(player.position.x, player.position.z) : WorldCenter;
            return UvRect(c, miniOrthoSize * 2f, viewAspect);
        }

        /// <summary>uvRect de la grande carte : zoom 1 = tout le monde visible. pan en mètres monde.</summary>
        public Rect WorldUvRect(float zoom, Vector2 pan)
        {
            float visibleH = WorldMetersPerViewportHeight(zoom);
            return UvRect(WorldCenter + pan, visibleH, TexAspect);
        }

        /// <summary>
        /// Borne le décalage de la carte pour que la fenêtre visible reste dans les limites du monde :
        /// on peut aller jusqu'à voir exactement chaque bord à n'importe quel zoom, sans les dépasser.
        /// </summary>
        public Vector2 ClampPan(Vector2 pan, float zoom)
        {
            float halfH = FitHalfHeight() / Mathf.Max(0.2f, zoom);
            float halfW = halfH * TexAspect;
            float maxX = Mathf.Max(0f, WorldSize.x * 0.5f - halfW);
            float maxY = Mathf.Max(0f, WorldSize.y * 0.5f - halfH);
            return new Vector2(Mathf.Clamp(pan.x, -maxX, maxX), Mathf.Clamp(pan.y, -maxY, maxY));
        }

        /// <summary>Hauteur visible (en mètres) de la grande carte à ce zoom.</summary>
        public float WorldMetersPerViewportHeight(float zoom) => FitHalfHeight() / Mathf.Max(0.2f, zoom) * 2f;

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

        private static RenderTexture NewTexture(int w, int h, string n, int aa)
        {
            var rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32)
            {
                name = n,
                antiAliasing = aa, // un bake unique : on peut se payer du MSAA
                useMipMap = true,
                autoGenerateMips = true,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp
            };
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
            cam.allowMSAA = true;
            cam.useOcclusionCulling = false;
            cam.allowDynamicResolution = false;
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
