using UnityEngine;

namespace Core.Village.Exploration
{
    public enum ExpeditionOutcome { Success, Failure, CriticalFailure }

    public readonly struct ExpeditionOdds
    {
        public readonly float Success, Failure, Critical;
        public ExpeditionOdds(float success, float critical)
        { Success = success; Critical = critical; Failure = 1f - success - critical; }
        public ExpeditionOutcome Resolve(float roll)
            => roll < Critical ? ExpeditionOutcome.CriticalFailure
             : roll < Critical + Failure ? ExpeditionOutcome.Failure : ExpeditionOutcome.Success;
    }

    public static class ExpeditionRules
    {
        public static ExpeditionOdds GetOdds(int heroLevel, int zoneLevel,
            float equalLevelSuccess = .7f, float perLevel = .1f, float criticalShare = .2f)
        {
            float success = Mathf.Clamp(equalLevelSuccess + perLevel *
                (Mathf.Clamp(heroLevel, 1, 5) - Mathf.Clamp(zoneLevel, 1, 3)), .05f, .95f);
            return new ExpeditionOdds(success, (1f - success) * Mathf.Clamp01(criticalShare));
        }
    }
}
