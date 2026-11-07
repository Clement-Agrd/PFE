namespace Core.Vfx
{
    /// <summary>Contrat commun a tous les effets d'ambiance / gameplay pilotes par le VfxService.</summary>
    public interface IVfxPlayer
    {
        void Play();
        void Stop();

        /// <summary>Intensite globale de l'effet, 0 (invisible) a 1 (reglage du profil).</summary>
        void SetIntensity(float intensity);
    }
}
