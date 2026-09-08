using System.Collections.Generic;
using UnityEngine;

namespace Core.LootSystem
{
    /// <summary>
    /// Table de butin (asset). Un roll combine : les entrées GARANTIES (chacune
    /// avec sa probabilité) et des tirages PONDÉRÉS depuis le reste du pool.
    /// Les entrées peuvent pointer vers des sous-tables (composition récursive).
    /// Création : Assets > Create > Loot > Loot Table
    /// </summary>
    [CreateAssetMenu(fileName = "LootTable", menuName = "Loot/Loot Table")]
    public sealed class LootTable : ScriptableObject
    {
        [SerializeField] private List<LootEntry> entries = new();

        [Header("Tirages pondérés")]
        [Tooltip("Nombre de tirages dans le pool des entrées non garanties.")]
        [SerializeField, Min(0)] private int rolls = 1;

        [Tooltip("Poids d'un tirage « rien » (aucun objet). 0 = un objet sort à chaque tirage.")]
        [SerializeField, Min(0f)] private float nothingWeight = 0f;

        private const int MaxDepth = 8; // garde-fou contre les sous-tables récursives

        /// <summary>Effectue un roll complet et renvoie la liste des objets obtenus.</summary>
        public List<LootResult> Roll()
        {
            var results = new List<LootResult>();
            RollInto(results, 0);
            return results;
        }

        /// <summary>Ajoute les résultats d'un roll à une liste existante (utilisé par les sous-tables).</summary>
        public void RollInto(List<LootResult> results, int depth)
        {
            if (results == null || depth > MaxDepth) return;

            // 1) Entrées garanties.
            for (int i = 0; i < entries.Count; i++)
            {
                LootEntry entry = entries[i];
                if (!entry.Guaranteed) continue;

                if (Random.value <= entry.DropChance)
                    Resolve(entry, results, depth);
            }

            // 2) Tirages pondérés parmi les entrées non garanties.
            if (rolls <= 0) return;

            var pool = new List<LootEntry>();
            for (int i = 0; i < entries.Count; i++)
                if (!entries[i].Guaranteed) pool.Add(entries[i]);

            for (int r = 0; r < rolls; r++)
            {
                LootEntry picked = PickWeighted(pool);
                if (picked != null) Resolve(picked, results, depth);
            }
        }

        private void Resolve(LootEntry entry, List<LootResult> results, int depth)
        {
            // Sous-table → on roule récursivement dedans.
            if (entry.SubTable != null)
            {
                entry.SubTable.RollInto(results, depth + 1);
                return;
            }

            if (entry.Item == null) return;

            int amount = Random.Range(entry.MinAmount, entry.MaxAmount + 1);
            if (amount > 0)
                results.Add(new LootResult(entry.Item, amount));
        }

        // Choisit une entrée du pool selon son poids ; peut renvoyer null (« rien »).
        private LootEntry PickWeighted(List<LootEntry> pool)
        {
            float total = nothingWeight;
            for (int i = 0; i < pool.Count; i++)
                total += Mathf.Max(0f, pool[i].Weight);

            if (total <= 0f) return null;

            float roll = Random.value * total;

            if (roll < nothingWeight) return null; // tirage « rien »
            roll -= nothingWeight;

            for (int i = 0; i < pool.Count; i++)
            {
                float w = Mathf.Max(0f, pool[i].Weight);
                if (roll < w) return pool[i];
                roll -= w;
            }

            return null;
        }
    }
}
