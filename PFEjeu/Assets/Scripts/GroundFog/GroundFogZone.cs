using Core.DayNightSystem;
using UnityEngine;

namespace Core.GroundFog
{
    /// <summary>Qualité du raymarch : nombre de pas par pixel (coût d'overdraw proportionnel).</summary>
    public enum GroundFogQuality { Low = 6, Medium = 12, High = 20 }

    /// <summary>
    /// Zone de brouillard au sol : à poser sur un cube avec le material Custom/GroundFog.
    /// Pilote les paramètres via MaterialPropertyBlock, et suit le cycle jour/nuit
    /// (DayNightCycle.NightBlend01) ou une valeur manuelle.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class GroundFogZone : MonoBehaviour
    {
        #region Inspector
        [SerializeField] private GroundFogPreset preset;
        [SerializeField] private GroundFogQuality quality = GroundFogQuality.Medium;

        [Tooltip("Optionnel : la nuit est lue sur DayNightCycle.NightBlend01.")]
        [SerializeField] private DayNightCycle dayNight;

        [Tooltip("Utilisé si aucun DayNightCycle n'est assigné (0 = jour, 1 = nuit).")]
        [SerializeField, Range(0f, 1f)] private float manualNight = 1f;
        #endregion

        #region Ids
        private static readonly int ColorId = Shader.PropertyToID("_FogColor");
        private static readonly int DensityId = Shader.PropertyToID("_Density");
        private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
        private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
        private static readonly int NoiseStrengthId = Shader.PropertyToID("_NoiseStrength");
        private static readonly int WindId = Shader.PropertyToID("_WindDir");
        private static readonly int StepsId = Shader.PropertyToID("_Steps");
        private static readonly int WaveAmpId = Shader.PropertyToID("_WaveAmp");
        private static readonly int WaveLengthId = Shader.PropertyToID("_WaveLength");
        private static readonly int WaveSpeedId = Shader.PropertyToID("_WaveSpeed");
        private static readonly int SelfLightId = Shader.PropertyToID("_SelfLight");
        private static readonly int LightInfluenceId = Shader.PropertyToID("_LightInfluence");
        private static readonly int EdgeFadeId = Shader.PropertyToID("_EdgeFade");
        #endregion

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;
        private float _lastNight = -1f;

        private void Awake() => Cache();

        private void OnEnable()
        {
            Cache();
            _lastNight = -1f;
            Apply(CurrentNight());
        }

        private void OnDisable()
        {
            if (_renderer != null) _renderer.SetPropertyBlock(null);
        }

        private void OnValidate()
        {
            _lastNight = -1f;
            if (isActiveAndEnabled) Apply(CurrentNight());
        }

        private void LateUpdate()
        {
            float night = CurrentNight();
            // On ne réécrit le block que si la valeur change (pas d'allocation, peu de travail).
            if (Mathf.Abs(night - _lastNight) > 0.001f) Apply(night);
        }

        /// <summary>Force le niveau de nuit (0..1) quand aucun DayNightCycle n'est branché.</summary>
        public void SetNight(float night01)
        {
            manualNight = Mathf.Clamp01(night01);
        }

        public void SetPreset(GroundFogPreset newPreset)
        {
            preset = newPreset;
            _lastNight = -1f;
        }

        private void Cache()
        {
            if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
            if (_block == null) _block = new MaterialPropertyBlock();
        }

        private float CurrentNight() => dayNight != null ? dayNight.NightBlend01 : manualNight;

        private void Apply(float night)
        {
            if (preset == null || _renderer == null) return;
            _lastNight = night;

            GroundFogPreset.State d = preset.Day;
            GroundFogPreset.State n = preset.Night;

            _renderer.GetPropertyBlock(_block);
            _block.SetColor(ColorId, Color.Lerp(d.color, n.color, night));
            _block.SetFloat(DensityId, Mathf.Lerp(d.density, n.density, night));
            _block.SetFloat(ThicknessId, Mathf.Lerp(d.thickness, n.thickness, night));
            _block.SetFloat(NoiseStrengthId, Mathf.Lerp(d.noiseStrength, n.noiseStrength, night));
            _block.SetFloat(NoiseScaleId, preset.NoiseScale);
            _block.SetVector(WindId, preset.Wind);
            _block.SetFloat(StepsId, (int)quality);
            _block.SetFloat(WaveAmpId, preset.WaveAmplitude);
            _block.SetFloat(WaveLengthId, preset.WaveLength);
            _block.SetFloat(WaveSpeedId, preset.WaveSpeed);
            _block.SetFloat(SelfLightId, preset.SelfLight);
            _block.SetFloat(LightInfluenceId, preset.LightInfluence);
            _block.SetFloat(EdgeFadeId, preset.EdgeFade);
            _renderer.SetPropertyBlock(_block);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.6f, 1f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        }
    }
}
