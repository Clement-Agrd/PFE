using UnityEngine;

namespace Core.Vfx
{
    /// <summary>Recoit les demandes d'effets ponctuels d'un ennemi (impact, mort) sans connaitre le service.</summary>
    public interface IEnemyVfxSink
    {
        void PlayHit(Vector3 position);
        void PlayDeath(Vector3 position);
    }
}
