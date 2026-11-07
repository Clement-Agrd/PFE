using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Limite les lights reelles des torches : seules les N plus proches de la camera restent activees
    /// (N = VfxProfile.Quality.maxRealLights), sans ombre. Les autres sont remplacees par la lueur de
    /// TorchGlowField. Recalcul periodique, tableaux pre-alloues : zero allocation par frame.
    /// </summary>
    public sealed class TorchLightBudget : MonoBehaviour
    {
        [SerializeField] private VfxProfile profile;
        [Tooltip("Lights ponctuelles des torches existantes (instances de scene, prefab inchange).")]
        [SerializeField] private Light[] torchLights = System.Array.Empty<Light>();

        private Camera _camera;
        private float[] _sqrDistance;
        private int[] _order;
        private float _nextRefresh;

        private void Awake()
        {
            _sqrDistance = new float[torchLights.Length];
            _order = new int[torchLights.Length];
            for (int i = 0; i < torchLights.Length; i++)
            {
                if (torchLights[i] != null) torchLights[i].shadows = LightShadows.None;
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < torchLights.Length; i++)
            {
                if (torchLights[i] != null) torchLights[i].gameObject.SetActive(true);
            }
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < _nextRefresh || profile == null) return;
            _nextRefresh = Time.unscaledTime + profile.TorchLightRefresh;

            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            Vector3 eye = _camera.transform.position;
            int n = torchLights.Length;
            for (int i = 0; i < n; i++)
            {
                _order[i] = i;
                _sqrDistance[i] = torchLights[i] != null ? (torchLights[i].transform.position - eye).sqrMagnitude : float.MaxValue;
            }

            int budget = Mathf.Min(profile.Quality.maxRealLights, n);
            // FlameLightFlicker pilote Light.enabled : on pilote donc le GameObject de la light.
            // Selection partielle : les `budget` plus proches (budget petit, n ~ 40).
            for (int k = 0; k < budget; k++)
            {
                int best = k;
                for (int j = k + 1; j < n; j++)
                {
                    if (_sqrDistance[_order[j]] < _sqrDistance[_order[best]]) best = j;
                }
                (_order[k], _order[best]) = (_order[best], _order[k]);
            }

            for (int k = 0; k < n; k++)
            {
                Light light = torchLights[_order[k]];
                if (light != null && light.gameObject.activeSelf != (k < budget)) light.gameObject.SetActive(k < budget);
            }
        }
    }
}
