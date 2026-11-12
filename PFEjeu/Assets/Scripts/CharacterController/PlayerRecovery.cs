using Core.HealthSystem;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ProfessionalTPS
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health))]
    public sealed class PlayerRecovery : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float regenerationDelay = 5f;
        [SerializeField, Range(0f, 1f)] private float regenerationFractionPerSecond = .04f;
        [SerializeField, Min(.1f)] private float recoveryDelay = 35f;
        [SerializeField, Min(0f)] private float extraCameraDistance = 5f;
        [SerializeField] private ThirdPersonCamera cameraRig;
        [SerializeField] private Behaviour[] controls = System.Array.Empty<Behaviour>();

        private Health _health;
        private float _lastDamageTime;
        private float _healingRemainder;
        private float _recoverAt;
        private float _cameraZoom;
        private bool _down;
        private bool[] _enabledControls;
        private Volume _grayscale;
        private VolumeProfile _grayscaleProfile;
        private ColorAdjustments _colorAdjustments;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _enabledControls = new bool[controls.Length];
        }

        private void OnEnable()
        {
            _lastDamageTime = Time.time;
            _health.OnDamageResolved += HandleDamage;
            _health.OnDeath += EnterDownState;
            if (_health.IsDead) EnterDownState();
        }

        private void OnDisable()
        {
            _health.OnDamageResolved -= HandleDamage;
            _health.OnDeath -= EnterDownState;
            if (_down) RestoreControls();
            if (cameraRig != null) cameraRig.SetRecoveryCamera(false, 0f);
            if (_grayscale != null) _grayscale.weight = 0f;
            _down = false;
            _cameraZoom = 0f;
        }

        private void OnDestroy()
        {
            if (_colorAdjustments != null) Destroy(_colorAdjustments);
            if (_grayscaleProfile != null) Destroy(_grayscaleProfile);
        }

        private void HandleDamage(DamageInfo info, int amount)
        {
            _lastDamageTime = Time.time;
            _healingRemainder = 0f;
        }

        private void EnterDownState()
        {
            if (_down) return;
            _down = true;
            _recoverAt = Time.time + recoveryDelay;
            _healingRemainder = 0f;

            for (int i = 0; i < controls.Length; i++)
            {
                var control = controls[i];
                _enabledControls[i] = control != null && control.enabled;
                if (_enabledControls[i]) control.enabled = false;
            }

            if (_grayscale != null) return;
            var effect = new GameObject("Désaturation joueur à terre");
            effect.transform.SetParent(transform, false);
            _grayscale = effect.AddComponent<Volume>();
            _grayscale.isGlobal = true;
            _grayscale.priority = 100f;
            _grayscale.weight = 0f;
            _grayscaleProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _colorAdjustments = _grayscaleProfile.Add<ColorAdjustments>();
            _colorAdjustments.saturation.Override(-100f);
            _grayscale.sharedProfile = _grayscaleProfile;
        }

        private void RestoreControls()
        {
            for (int i = 0; i < controls.Length; i++)
                if (_enabledControls[i] && controls[i] != null) controls[i].enabled = true;
        }

        private void Update()
        {
            if (_down && (!_health.IsDead || Time.time >= _recoverAt))
            {
                if (_health.IsDead) _health.Revive();
                RestoreControls();
                _down = false;
                _lastDamageTime = Time.time;
            }

            UpdatePresentation();
            if (_down || _health.IsDead || _health.CurrentHealth >= _health.MaxHealth ||
                Time.time - _lastDamageTime < regenerationDelay) return;

            _healingRemainder += _health.MaxHealth * regenerationFractionPerSecond * Time.deltaTime;
            int points = Mathf.FloorToInt(_healingRemainder);
            if (points <= 0) return;
            _healingRemainder -= points;
            _health.Heal(points, showNumber: false);
            if (_health.CurrentHealth >= _health.MaxHealth) _healingRemainder = 0f;
        }

        private void UpdatePresentation()
        {
            if (_grayscale != null)
                _grayscale.weight = Mathf.MoveTowards(_grayscale.weight, _down ? 1f : 0f, 2f * Time.deltaTime);

            if (cameraRig == null || (!_down && _cameraZoom <= 0f)) return;
            _cameraZoom = Mathf.MoveTowards(_cameraZoom, _down ? extraCameraDistance : 0f, 7f * Time.deltaTime);
            cameraRig.SetRecoveryCamera(_down, _cameraZoom);
        }
    }
}
