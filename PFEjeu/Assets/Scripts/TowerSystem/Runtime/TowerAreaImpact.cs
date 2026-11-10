using UnityEngine;

public sealed class TowerAreaImpact : MonoBehaviour
{
    [SerializeField] private ParticleSystem[] fullRadiusEffects;

    public void Play(float radius)
    {
        foreach (var effect in fullRadiusEffects)
        {
            if (effect == null) continue;
            var main = effect.main;
            main.startSize = radius * 2f;
        }

        if (TryGetComponent<IceVFX.IceImpact>(out var ice))
        {
            ice.spikeRadius = new Vector2(radius * .16f, radius * .83f);
            if (ice.frostDecal != null)
            {
                var size = ice.frostDecal.size;
                ice.frostDecal.size = new Vector3(radius * 2f, radius * 2f, size.z);
            }
        }

        gameObject.SetActive(true);
    }
}
