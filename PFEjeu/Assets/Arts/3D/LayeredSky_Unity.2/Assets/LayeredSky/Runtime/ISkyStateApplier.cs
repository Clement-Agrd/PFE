namespace LayeredSky
{
    /// <summary>
    /// Applique un SkyState à UNE cible (matériau, lumière, environnement...).
    /// Principe ouvert/fermé : ajouter une cible (Volume URP, audio d'ambiance...) = ajouter une classe, sans toucher au contrôleur.
    /// </summary>
    public interface ISkyStateApplier
    {
        /// <summary>Appelé à chaque frame de transition.</summary>
        void Apply(in SkyState state);

        /// <summary>Appelé une fois quand la transition est terminée (ou après un changement immédiat).</summary>
        void Finish();
    }
}
