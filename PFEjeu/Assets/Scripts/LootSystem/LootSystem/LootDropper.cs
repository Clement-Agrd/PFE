using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.LootSystem
{
    /// <summary>
    /// Composant pratique : porte une table de butin et la roule à la demande.
    /// Le résultat part dans un event → à toi de décider (inventaire, pickups au sol).
    /// </summary>
    public sealed class LootDropper : MonoBehaviour
    {
        [SerializeField] private LootTable table;

        /// <summary>Émis après un Drop avec la liste des objets obtenus (peut être vide).</summary>
        public event Action<IReadOnlyList<LootResult>> OnLootDropped;

        /// <summary>Roule la table et renvoie (et diffuse) les objets obtenus.</summary>
        public IReadOnlyList<LootResult> Drop()
        {
            if (table == null)
            {
                Debug.LogWarning("[LootDropper] Aucune LootTable assignée.");
                return Array.Empty<LootResult>();
            }

            List<LootResult> results = table.Roll();
            OnLootDropped?.Invoke(results);
            return results;
        }
    }
}
