namespace Core.TweenSystem
{
    /// <summary>
    /// Courbes d'accélération. « In » = démarre lentement, « Out » = finit
    /// lentement, « InOut » = les deux. Back/Elastic dépassent la cible (overshoot).
    /// </summary>
    public enum Ease
    {
        Linear,

        InQuad, OutQuad, InOutQuad,
        InCubic, OutCubic, InOutCubic,
        InSine, OutSine, InOutSine,
        InBack, OutBack, InOutBack,

        OutBounce,
        OutElastic
    }
}
