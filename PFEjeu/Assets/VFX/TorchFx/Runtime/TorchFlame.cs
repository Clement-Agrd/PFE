using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TorchFx
{
    /// <summary>
    /// Orchestrateur de la flamme de torche (style toon, par couches de quads billboard).
    ///
    /// Torche (prefab réaliste, inchangé)
    /// │
    /// └── FlameSocket                 (vide, posé à la base de la flamme, sur la tête de torche)
    ///     ├── TorchFlame              (ce script : état, scintillement, dessin des couches)
    ///     ├── FlameLightFlicker       (module : Light qui pulse avec la flamme)   [optionnel]
    ///     └── FlameEmbers             (module : braises)                          [optionnel]
    ///
    /// Les couches sont dessinées avec Graphics.DrawMesh : aucun MeshRenderer à gérer, aucune allocation par frame.
    /// Update n'est actif que tant que la flamme brûle ou s'éteint.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TorchFlame : MonoBehaviour
    {
        #region Inspector
        [Header("Références")]
        [SerializeField] private FlameProfile profile;
        [Tooltip("Base de la flamme (haut de la tête de torche). Par défaut : ce transform.")]
        [SerializeField] private Transform flameAnchor;

        [Header("Etat")]
        [SerializeField] private bool startLit = true;
        [Tooltip("Vent en espace monde. 0 à 1,5 est une plage lisible.")]
        [SerializeField] private Vector3 wind = Vector3.zero;
        #endregion

        #region Evènements
        public event Action Ignited;
        public event Action Extinguished;
        #endregion

        #region API publique
        public bool IsLit => targetIntensity > 0f;
        public float Intensity01 => currentIntensity;

        public void Ignite()
        {
            targetIntensity = 1f;
            enabled = true;
        }

        public void Extinguish()
        {
            targetIntensity = 0f;
        }

        public void Toggle()
        {
            if (IsLit) Extinguish();
            else Ignite();
        }

        public void SetWind(Vector3 worldWind)
        {
            wind = worldWind;
        }
        #endregion

        #region Etat interne
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int SpeedMulId = Shader.PropertyToID("_SpeedMul");
        private static readonly int FlickerId = Shader.PropertyToID("_Flicker");
        private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        private static readonly int WindId = Shader.PropertyToID("_WindWS");

        private static Mesh sharedQuad;

        // Créé dans Awake : MaterialPropertyBlock ne peut pas être instancié dans un constructeur MonoBehaviour.
        private MaterialPropertyBlock block;
        private IFlameModule[] modules = Array.Empty<IFlameModule>();
        private Material[] layerMaterials;
        private float currentIntensity;
        private float targetIntensity;
        private float noiseSeed;
        private bool reportedLit;
        #endregion

        #region Cycle Unity
        private void Reset()
        {
            flameAnchor = transform;
        }

        private void Awake()
        {
            if (flameAnchor == null) flameAnchor = transform;

            block = new MaterialPropertyBlock();
            noiseSeed = UnityEngine.Random.value * 100f; // chaque torche scintille différemment
            targetIntensity = startLit ? 1f : 0f;
            currentIntensity = targetIntensity;
            reportedLit = startLit;
        }

        private void OnEnable()
        {
            if (profile == null || profile.Material == null)
            {
                Debug.LogError($"[{nameof(TorchFlame)}] Profil ou matériau manquant sur '{name}'.", this);
                enabled = false;
                return;
            }

            BuildLayerMaterials();
            modules = GetComponentsInChildren<IFlameModule>(true);
        }

        private void OnDisable()
        {
            ReleaseLayerMaterials();
        }

        private void Update()
        {
            AdvanceIntensity(Time.deltaTime);

            float time = Time.time;
            FlameState state = new FlameState(currentIntensity, ComputeFlicker(time), time, flameAnchor.position, wind);

            for (int i = 0; i < modules.Length; i++)
            {
                modules[i].Tick(in state);
            }

            if (currentIntensity > 0f)
            {
                DrawLayers(in state);
            }

            ReportTransitions();
        }
        #endregion

        #region Simulation
        private void AdvanceIntensity(float deltaTime)
        {
            float duration = targetIntensity > currentIntensity ? profile.IgniteDuration : profile.ExtinguishDuration;
            currentIntensity = Mathf.MoveTowards(currentIntensity, targetIntensity, deltaTime / duration);
        }

        // Deux octaves de bruit de Perlin : un souffle lent + de petits à-coups.
        private float ComputeFlicker(float time)
        {
            float slow = Mathf.PerlinNoise(noiseSeed, time * profile.FlickerFrequency) * 2f - 1f;
            float fast = Mathf.PerlinNoise(noiseSeed + 17.3f, time * profile.FlickerFrequency * 2.7f) * 2f - 1f;
            return Mathf.Clamp(slow * 0.75f + fast * 0.25f, -1f, 1f);
        }

        private void ReportTransitions()
        {
            if (currentIntensity >= 1f && !reportedLit)
            {
                reportedLit = true;
                Ignited?.Invoke();
            }
            else if (currentIntensity <= 0f && targetIntensity <= 0f)
            {
                if (reportedLit)
                {
                    reportedLit = false;
                    Extinguished?.Invoke();
                }

                // Flamme éteinte : plus d'Update tant qu'on ne rallume pas (les modules ont déjà reçu intensité 0).
                enabled = false;
            }
        }
        #endregion

        #region Rendu
        private void DrawLayers(in FlameState state)
        {
            float grow = Mathf.SmoothStep(0f, 1f, state.Intensity01);
            float heightMultiplier = grow * (1f + state.Flicker * profile.HeightFlicker);
            float widthMultiplier = Mathf.Lerp(0.45f, 1f, grow) * (1f - state.Flicker * profile.WidthFlicker);
            float brightness = grow * (1f + state.Flicker * profile.BrightnessFlicker);

            FlameProfile.Layer[] layers = profile.Layers;
            Mesh quad = SharedQuad;

            for (int i = 0; i < layers.Length; i++)
            {
                FlameProfile.Layer layer = layers[i];

                Vector3 scale = new Vector3(
                    profile.Width * layer.widthScale * widthMultiplier,
                    profile.Height * layer.heightScale * heightMultiplier,
                    1f);
                Matrix4x4 matrix = Matrix4x4.TRS(state.Position, Quaternion.identity, scale);

                block.Clear();
                block.SetFloat(SeedId, noiseSeed + layer.seedOffset);
                block.SetColor(TintId, layer.tint);
                block.SetFloat(SpeedMulId, layer.speedMultiplier);
                block.SetFloat(FlickerId, state.Flicker);
                block.SetFloat(BrightnessId, brightness);
                block.SetVector(WindId, state.Wind);

                Graphics.DrawMesh(quad, matrix, layerMaterials[i], gameObject.layer, null, 0, block, ShadowCastingMode.Off, false);
            }
        }

        // Couche 0 = matériau du profil. Les suivantes = copies avec une file de rendu +i :
        // l'ordre d'affichage des couches est ainsi déterministe (pas de scintillement de tri).
        private void BuildLayerMaterials()
        {
            ReleaseLayerMaterials();

            FlameProfile.Layer[] layers = profile.Layers;
            Material source = profile.Material;
            layerMaterials = new Material[layers.Length];

            for (int i = 0; i < layers.Length; i++)
            {
                if (i == 0)
                {
                    layerMaterials[i] = source;
                    continue;
                }

                Material copy = new Material(source)
                {
                    name = $"{source.name} (couche {i})",
                    hideFlags = HideFlags.HideAndDontSave
                };
                copy.renderQueue = source.renderQueue + i;
                layerMaterials[i] = copy;
            }
        }

        private void ReleaseLayerMaterials()
        {
            if (layerMaterials == null) return;

            for (int i = 1; i < layerMaterials.Length; i++)
            {
                if (layerMaterials[i] != null) Destroy(layerMaterials[i]);
            }

            layerMaterials = null;
        }

        // Quad dont le pivot est à la BASE (y de 0 à 1) : la flamme pousse vers le haut depuis le socket.
        private static Mesh SharedQuad
        {
            get
            {
                if (sharedQuad == null) sharedQuad = CreateQuad();
                return sharedQuad;
            }
        }

        private static Mesh CreateQuad()
        {
            Mesh mesh = new Mesh { name = "TorchFx_FlameQuad", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f),
                new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            // Bornes généreuses : le shader tourne le quad vers la caméra, l'AABB d'origine ne le couvre pas toujours.
            mesh.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(1.6f, 1.2f, 1.6f));
            return mesh;
        }
        #endregion

#if UNITY_EDITOR
        // Boîte orange = volume réel de la flamme : aide à placer le socket sur la tête de torche.
        private void OnDrawGizmosSelected()
        {
            if (profile == null) return;

            Transform anchor = flameAnchor != null ? flameAnchor : transform;
            Vector3 size = new Vector3(profile.Width * 0.74f, profile.Height, profile.Width * 0.74f);

            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
            Gizmos.DrawWireCube(anchor.position + Vector3.up * (size.y * 0.5f), size);
        }
#endif
    }
}
