using UnityEngine;

namespace TorchFx
{
    /// <summary>
    /// Instantané de l'état de la flamme, calculé une fois par frame par <see cref="TorchFlame"/>
    /// et partagé avec tous les modules (lumière, braises, futurs modules : son, fumée...).
    /// </summary>
    public readonly struct FlameState
    {
        /// <summary>0 = éteinte, 1 = pleine puissance. Lissé pendant l'allumage / l'extinction.</summary>
        public readonly float Intensity01;

        /// <summary>Bruit lissé entre -1 et 1, identique pour tous les modules (lumière et flamme pulsent ensemble).</summary>
        public readonly float Flicker;

        public readonly float Time;

        /// <summary>Position monde de la base de la flamme.</summary>
        public readonly Vector3 Position;

        /// <summary>Vent en espace monde.</summary>
        public readonly Vector3 Wind;

        public FlameState(float intensity01, float flicker, float time, Vector3 position, Vector3 wind)
        {
            Intensity01 = intensity01;
            Flicker = flicker;
            Time = time;
            Position = position;
            Wind = wind;
        }
    }

    /// <summary>
    /// Module piloté par <see cref="TorchFlame"/>. Il suffit d'ajouter un composant qui l'implémente
    /// sur l'objet de la torche (ou un enfant) : il est détecté automatiquement.
    /// </summary>
    public interface IFlameModule
    {
        void Tick(in FlameState state);
    }
}
