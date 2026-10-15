using System;
using System.Collections.Generic;
using UnityEngine;
using Core.InventorySystem;
using Core.Village;

namespace Core.VillageEvents
{
    public enum EventAxis { Ressources, Batiments, Heros, Rituel, Corruption }

    public enum EffectType
    {
        Resource,           // item + amount (signé)
        WallRepair,         // amount = % d'intégrité des remparts (signé)
        BuildingDowntime,   // building + amount = jours d'indisponibilité (coût)
        BuildingLevelUp,    // building : +1 niveau gratuit (gain)
        HeroUnavailable,    // amount = cycles d'absence d'un héros (coût)
        RitualAdvance,      // amount = nuits de rituel avancées (gain)
        NextNightEnemies,   // amount = % de PV ennemis la nuit suivante (coût si > 0)
        CorruptionTier      // amount = paliers de corruption (signé)
    }

    /// <summary>Un effet unitaire. Le sens (gain/coût) se déduit du type et du signe.</summary>
    [Serializable]
    public struct EventEffect
    {
        public EffectType type;
        public ItemDefinition item;
        public BuildingDefinition building;
        public int amount;

        public EventAxis Axis => type switch
        {
            EffectType.Resource => EventAxis.Ressources,
            EffectType.WallRepair => EventAxis.Batiments,
            EffectType.BuildingDowntime => EventAxis.Batiments,
            EffectType.BuildingLevelUp => EventAxis.Batiments,
            EffectType.HeroUnavailable => EventAxis.Heros,
            EffectType.RitualAdvance => EventAxis.Rituel,
            EffectType.NextNightEnemies => EventAxis.Rituel,
            _ => EventAxis.Corruption
        };

        public bool IsCost => type switch
        {
            EffectType.Resource => amount < 0,
            EffectType.WallRepair => amount < 0,
            EffectType.BuildingDowntime => true,
            EffectType.BuildingLevelUp => false,
            EffectType.HeroUnavailable => true,
            EffectType.RitualAdvance => amount < 0,
            EffectType.NextNightEnemies => amount > 0,
            _ => amount > 0 // la corruption qui monte est un coût
        };

        /// <summary>Montant tel qu'affiché/appliqué après escalade (les montants fixes ne scalent pas).</summary>
        public int Scaled(float multiplier) => type switch
        {
            EffectType.Resource or EffectType.NextNightEnemies or EffectType.WallRepair
                => Mathf.RoundToInt(amount * multiplier),
            _ => amount
        };

        public string Describe(float multiplier)
        {
            int a = Scaled(multiplier);
            string sign = a >= 0 ? "+" : "";
            string bName = building != null ? building.DisplayName : "bâtiment";
            return type switch
            {
                EffectType.Resource => $"{sign}{a} {(item != null ? item.DisplayName : "ressource")}",
                EffectType.WallRepair => $"Remparts {sign}{a} %",
                EffectType.BuildingDowntime => $"{bName} indisponible {a} j",
                EffectType.BuildingLevelUp => $"{bName} +1 niveau",
                EffectType.HeroUnavailable => $"Un héros absent {a} cycle(s)",
                EffectType.RitualAdvance => a >= 0 ? $"Rituel avancé de {a} nuit(s)" : $"Rituel reculé de {-a} nuit(s)",
                EffectType.NextNightEnemies => $"Nuit suivante : ennemis {sign}{a} %",
                _ => a >= 0 ? $"Corruption +{a} palier" : $"Corruption {a} palier"
            };
        }
    }

    [Serializable]
    public sealed class EventChoice
    {
        public string label = "Choix";
        [Tooltip("Sortie sans dépense immédiate : exemptée de la règle « au moins un gain ».")]
        public bool isFreeOption;
        public EventEffect[] effects;

        [Header("Suite différée (optionnel)")]
        public VillageEventDefinition followUp;
        [Min(1)] public int followUpDelayNights = 2;
    }

    [Serializable]
    public sealed class EventConditions
    {
        [Min(0)] public int minTownHallLevel;
        [Tooltip("0 = ignoré. Sinon l'événement n'apparaît que si l'intégrité des remparts est sous ce %.")]
        [Range(0, 100)] public int wallsBelowPercent;
        [Tooltip("Optionnel : n'apparaît que si ce stock est sous le seuil.")]
        public ItemDefinition stockItem;
        [Min(0)] public int stockBelow;
        [Tooltip("Bâtiments qui doivent exister dans le village.")]
        public BuildingDefinition[] requiredBuildings;
        [Tooltip("Niveau minimum du premier bâtiment requis (0 = ignoré).")]
        [Min(0)] public int requiredBuildingMinLevel;
        [Min(0)] public int minNight;
        [Tooltip("0 = pas de plafond.")]
        [Min(0)] public int maxNight;
        [Min(0)] public int minCorruptionTier;
    }

    /// <summary>
    /// Une fiche d'événement post-défense (données uniquement).
    /// Création : Assets > Create > Village > Village Event
    /// </summary>
    [CreateAssetMenu(fileName = "NewVillageEvent", menuName = "Village/Village Event")]
    public sealed class VillageEventDefinition : ScriptableObject
    {
        [Header("Identité")]
        [SerializeField] private string title = "Événement";
        [SerializeField, TextArea(2, 5)] private string flavorText;

        [Header("Conditions")]
        [SerializeField] private EventConditions conditions = new();

        [Header("Tirage")]
        [SerializeField, Min(0.01f)] private float weight = 1f;
        [Tooltip("Nuits avant de pouvoir revenir. Au minimum 1 : jamais deux nuits de suite.")]
        [SerializeField, Min(1)] private int cooldownNights = 3;
        [SerializeField] private bool unique;

        [Header("Choix (2 à 4)")]
        [SerializeField] private EventChoice[] choices;

        public string Title => title;
        public string FlavorText => flavorText;
        public EventConditions Conditions => conditions;
        public float Weight => weight;
        public int CooldownNights => cooldownNights;
        public bool Unique => unique;
        public IReadOnlyList<EventChoice> Choices => choices;

#if UNITY_EDITOR
        /// <summary>Remplit la fiche par code (générateur d'exemples de l'éditeur).</summary>
        public void EditorSetup(string newTitle, string flavor, float newWeight, int cooldown, bool isUnique,
            EventConditions newConditions, EventChoice[] newChoices)
        {
            title = newTitle;
            flavorText = flavor;
            weight = newWeight;
            cooldownNights = cooldown;
            unique = isUnique;
            conditions = newConditions;
            choices = newChoices;
        }

        private void OnValidate()
        {
            if (choices == null) return;
            if (choices.Length < 2 || choices.Length > 4)
                Debug.LogWarning($"[VillageEvent] '{name}' : 2 à 4 choix attendus ({choices.Length}).", this);

            var seenPrimaryCost = new HashSet<EventAxis>();
            foreach (EventChoice c in choices)
            {
                if (c?.effects == null) continue;

                bool hasGain = false, hasCost = false;
                EventAxis? primary = null;
                foreach (EventEffect e in c.effects)
                {
                    if (e.IsCost) { hasCost = true; primary ??= e.Axis; }
                    else hasGain = true;
                }

                if (!hasCost)
                    Debug.LogWarning($"[VillageEvent] '{name}' / '{c.label}' : aucune concession.", this);
                if (!hasGain && !c.isFreeOption)
                    Debug.LogWarning($"[VillageEvent] '{name}' / '{c.label}' : aucun gain (cocher 'isFreeOption' si voulu).", this);
                if (primary.HasValue && !seenPrimaryCost.Add(primary.Value))
                    Debug.LogWarning($"[VillageEvent] '{name}' : deux choix paient sur l'axe {primary.Value}.", this);
            }
        }
#endif
    }
}
