using System.Collections;
using BuildVFX;
using UnityEngine;
using UpgradeVFX;

namespace Core.Vfx
{
    [RequireComponent(typeof(BuildSite), typeof(UpgradeSite))]
    public sealed class BuildingEffects : MonoBehaviour
    {
        private BuildSite _build;
        private UpgradeSite _upgrade;
        private Coroutine _pending;

        private void Awake()
        {
            _build = GetComponent<BuildSite>();
            _upgrade = GetComponent<UpgradeSite>();
        }

        public void PlayBuild() => Schedule(0);
        public void PlayUpgrade(int level) => Schedule(level);

        private void Schedule(int level)
        {
            if (_pending != null) StopCoroutine(_pending);
            _build.Cancel();
            _upgrade.Cancel();
            _pending = StartCoroutine(PlayAfterVisualUpdate(level));
        }

        private IEnumerator PlayAfterVisualUpdate(int level)
        {
            yield return null;
            _pending = null;

            var building = transform.parent;
            if (building == null) yield break;
            if (level == 0)
            {
                _build.PlayFor(building);
                yield break;
            }

            _upgrade.stateA = building.gameObject;
            _upgrade.SetTier(level);
            _upgrade.Play();
        }

        private void OnDisable()
        {
            if (_pending != null) StopCoroutine(_pending);
            _pending = null;
        }
    }
}
