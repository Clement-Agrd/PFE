using System;
using UnityEngine;

namespace Core.WaveSystem
{
    /// <summary>
    /// Une ligne de spawn dans une vague : quel prefab, combien, et le délai entre
    /// deux apparitions de ce prefab.
    /// </summary>
    [Serializable]
    public struct SpawnEntry
    {
        public GameObject prefab;

        [Min(1)] public int count;

        [Tooltip("Délai (s) entre deux spawns de cette ligne. 0 = tous d'un coup.")]
        [Min(0f)] public float interval;
    }
}
