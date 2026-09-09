using System;
using UnityEngine;

[CreateAssetMenu(fileName = "StatsSO", menuName = "Scriptable Objects/Stats1")]
public class StatsSO : ScriptableObject
{
    [Serializable]
    public struct StatEntry
    {
        public Stats.StatTypes type;
        public float value;
    }
    
    [Tooltip("Ne renseigne que les stats utiles pour ce preset.")]
    public StatEntry[] entries;
 
    // Cherche la valeur d'une stat précise dans ce preset.
    public float GetValue(Stats.StatTypes type)
    {
        foreach (var entry in entries)
        {
            if (entry.type == type)
                return entry.value;
        }
        return 0f; // pas trouvé -> valeur par défaut
    }
}