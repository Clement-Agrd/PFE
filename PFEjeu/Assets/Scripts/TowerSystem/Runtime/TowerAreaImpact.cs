using UnityEngine;

public sealed class TowerAreaImpact : MonoBehaviour
{
    [SerializeField] private ParticleSystem[] fullRadiusEffects;
    [SerializeField] private Transform[] groundedEffects;

    public static Vector3 FindGround(Vector3 position, RaycastHit[] hits, out Vector3 normal)
    {
        normal = Vector3.up;
        Vector3 ground = position;
        float nearest = float.PositiveInfinity;
        int groundLayer = LayerMask.NameToLayer("Ground");
        int count = Physics.RaycastNonAlloc(position + Vector3.up * .65f, Vector3.down, hits, 15f,
            LayerMask.GetMask("Ground", "Default"), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.distance >= nearest || hit.normal.y < .35f) continue;
            var collider = hit.collider;
            if (collider.gameObject.layer != groundLayer && collider is not TerrainCollider &&
                (collider.attachedRigidbody != null ||
                 collider.GetComponentInParent<Core.HealthSystem.Health>() != null ||
                 collider.GetComponentInParent<Core.Village.Building>() != null)) continue;
            nearest = hit.distance;
            ground = hit.point;
            normal = hit.normal;
        }
        return ground;
    }

    public void Play(float radius) => Play(radius, transform.position, transform.forward);

    public void Play(float radius, Vector3 ground, Vector3 normal, bool showGroundEffects = true)
    {
        // Restart after positioning and sizing so bursts and randomized spikes use the final settings.
        gameObject.SetActive(false);
        Vector3 offset = ground + normal * .055f - transform.position;
        if (groundedEffects != null)
        {
            foreach (var effect in groundedEffects)
            {
                if (effect == null) continue;
                effect.gameObject.SetActive(showGroundEffects);
                if (!showGroundEffects) continue;
                effect.position += offset;
                effect.rotation = Quaternion.FromToRotation(Vector3.forward, normal);
            }
        }
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
        foreach (var particles in GetComponentsInChildren<ParticleSystem>())
        {
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.Play(false);
        }
    }
}
