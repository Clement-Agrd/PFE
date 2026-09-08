using System.Collections.Generic;
using UnityEngine;

namespace Core.WaveSystem
{
    /// <summary>
    /// Une vague : un délai avant de commencer, puis une ou plusieurs lignes de
    /// spawn. Asset partagé, composé dans l'Inspector.
    /// Création : Assets > Create > Waves > Wave
    /// </summary>
    [CreateAssetMenu(fileName = "Wave", menuName = "Waves/Wave")]
    public sealed class WaveDefinition : ScriptableObject
    {
        [SerializeField] private string waveName;

        [Tooltip("Délai avant le début de cette vague (s).")]
        [SerializeField, Min(0f)] private float startDelay = 1f;

        [SerializeField] private List<SpawnEntry> spawns = new();

        public string WaveName => waveName;
        public float StartDelay => startDelay;
        public IReadOnlyList<SpawnEntry> Spawns => spawns;

        /// <summary>Nombre total d'ennemis de la vague (avant scaling endless).</summary>
        public int TotalCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < spawns.Count; i++)
                    total += Mathf.Max(1, spawns[i].count);
                return total;
            }
        }
    }
}
