using System;
using UnityEngine;

namespace Core.VillageEvents
{
    /// <summary>
    /// État de campagne minimal que les événements lisent et modifient : avancée du rituel,
    /// palier de corruption et malus d'ennemis de la nuit suivante.
    /// </summary>
    public sealed class VillageState : MonoBehaviour
    {
        [Header("Rituel")]
        [SerializeField, Min(1)] private int ritualNight = 1;
        [SerializeField, Min(1)] private int ritualTotalNights = 10;

        [Header("Corruption")]
        [SerializeField, Min(1)] private int maxCorruptionTier = 3;
        [SerializeField, Min(0)] private int corruptionTier;

        /// <summary>Nuit courante du rituel (1 = première nuit).</summary>
        public int RitualNight => ritualNight;
        public int RitualTotalNights => ritualTotalNights;
        public int CorruptionTier => corruptionTier;
        public int MaxCorruptionTier => maxCorruptionTier;
        public bool CorruptionAtMax => corruptionTier >= maxCorruptionTier;

        /// <summary>Bonus de PV ennemis (en %) accumulé pour la prochaine nuit.</summary>
        public int PendingEnemyBonusPercent { get; private set; }

        /// <summary>Bonus de PV ennemis (en %) actif pendant la nuit en cours.</summary>
        public int ActiveEnemyBonusPercent { get; private set; }

        public event Action OnChanged;

        public void AdvanceRitual(int nights)
        {
            ritualNight = Mathf.Clamp(ritualNight + nights, 1, ritualTotalNights);
            OnChanged?.Invoke();
        }

        public void AddCorruption(int tiers)
        {
            corruptionTier = Mathf.Clamp(corruptionTier + tiers, 0, maxCorruptionTier);
            OnChanged?.Invoke();
        }

        public void AddNextNightEnemyBonus(int percent)
        {
            PendingEnemyBonusPercent = Mathf.Max(0, PendingEnemyBonusPercent + percent);
            OnChanged?.Invoke();
        }

        /// <summary>Au début d'une nuit : le bonus en attente devient actif.</summary>
        public void BeginNight()
        {
            ActiveEnemyBonusPercent = PendingEnemyBonusPercent;
            PendingEnemyBonusPercent = 0;
            OnChanged?.Invoke();
        }

        /// <summary>À la fin d'une nuit : la nuit de rituel suivante arrive.</summary>
        public void EndNight()
        {
            ActiveEnemyBonusPercent = 0;
            AdvanceRitual(1);
        }
    }
}
