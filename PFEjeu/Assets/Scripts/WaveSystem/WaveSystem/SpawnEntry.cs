using System;
using UnityEngine;

namespace Core.WaveSystem
{
    /// <summary>
    /// Une ligne de spawn dans une vague : quel prefab, combien au total, dans
    /// quelle zone, et par SALVES de combien (façon Dungeon Defenders).
    /// </summary>
    [Serializable]
    public sealed class SpawnEntry
    {
        public GameObject prefab;
        [Min(1)] public int count = 10;

        [Tooltip("Id de la SpawnZone (scène) où ces ennemis apparaissent.")]
        public string zoneId = "Zone";

        [Header("Salve")]
        [Tooltip("Nombre d'ennemis qui apparaissent EN MÊME TEMPS à chaque salve.")]
        [Min(1)] public int burstSize = 4;
        [Tooltip("Temps entre deux salves (secondes).")]
        [Min(0f)] public float burstInterval = 0.3f;
        [Tooltip("Délai avant que cette ligne commence à spawner (pour décaler plusieurs zones entre elles).")]
        [Min(0f)] public float startDelay = 0f;
    }
}