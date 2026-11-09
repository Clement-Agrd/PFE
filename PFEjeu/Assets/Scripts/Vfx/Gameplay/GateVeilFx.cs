using Core.HealthSystem;
using Core.TowerDefense;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Voile magique teal de la herse + braises. Reagit a PlayerBase.OnHealthChanged : flash a chaque degat,
    /// teinte qui vire au rouge-orange quand la base est faible. Evenement uniquement, Update seulement
    /// pendant le flash.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GateVeilFx : MonoBehaviour, IVfxPlayer
    {
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int TintId = Shader.PropertyToID("_Tint");

        [SerializeField] private VfxProfile profile;
        [SerializeField] private PlayerBase playerBase;
        [SerializeField] private Health gateHealth;
        [SerializeField] private ParticleSystem[] systems = System.Array.Empty<ParticleSystem>();
        [SerializeField, Range(0.05f, 1f)] private float baseIntensity = 0.6f;
        [SerializeField, Range(0f, 3f)] private float flashBoost = 1.6f;
        [SerializeField, Min(0.1f)] private float flashDuration = 0.5f;
        [Tooltip("Sous ce ratio de vie la teinte passe vers la couleur ennemie.")]
        [SerializeField, Range(0.05f, 1f)] private float lowHealthRatio = 0.35f;

        private ParticleSystemRenderer[] _renderers;
        private MaterialPropertyBlock _block;
        private float _flash;
        private float _intensity = 1f;
        private float _ratio = 1f;
        private int _lastHealth = -1;

        private void Awake()
        {
            _renderers = new ParticleSystemRenderer[systems.Length];
            for (int i = 0; i < systems.Length; i++) _renderers[i] = systems[i].GetComponent<ParticleSystemRenderer>();
            _block = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            if (gateHealth != null)
            {
                gateHealth.OnHealthChanged += OnHealthChanged;
                OnHealthChanged(gateHealth.CurrentHealth, gateHealth.MaxHealth);
            }
            else if (playerBase != null) playerBase.OnHealthChanged += OnHealthChanged;
            Play();
            Apply();
        }

        private void OnDisable()
        {
            if (gateHealth != null) gateHealth.OnHealthChanged -= OnHealthChanged;
            else if (playerBase != null) playerBase.OnHealthChanged -= OnHealthChanged;
        }

        public void Play()
        {
            for (int i = 0; i < systems.Length; i++) if (!systems[i].isPlaying) systems[i].Play(true);
        }

        public void Stop()
        {
            for (int i = 0; i < systems.Length; i++) systems[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        public void SetIntensity(float intensity)
        {
            _intensity = Mathf.Clamp01(intensity);
            Apply();
        }

        private void OnHealthChanged(int current, int max)
        {
            _ratio = max > 0 ? Mathf.Clamp01(current / (float)max) : 1f;
            if (_lastHealth >= 0 && current < _lastHealth) _flash = 1f;
            _lastHealth = current;
            Apply();
            enabled = true;
        }

        private void Update()
        {
            if (_flash <= 0f) return;
            _flash = Mathf.Max(0f, _flash - Time.deltaTime / flashDuration);
            Apply();
        }

        private void Apply()
        {
            if (_renderers == null || profile == null) return;
            float danger = 1f - Mathf.Clamp01(_ratio / lowHealthRatio);
            Color tint = Color.Lerp(profile.DefenseColor, profile.EnemyColor, danger);
            float value = (baseIntensity + _flash * flashBoost) * _intensity;
            for (int i = 0; i < _renderers.Length; i++)
            {
                _renderers[i].GetPropertyBlock(_block);
                _block.SetFloat(IntensityId, value);
                _block.SetColor(TintId, tint);
                _renderers[i].SetPropertyBlock(_block);
            }
        }
    }
}
