using System;
using UnityEngine;
using Core.TowerDefense;

namespace Core
{
    /// <summary>
    /// Compteur de kills par type d'ennemi pour le bestiaire, sauvegardé dans PlayerPrefs
    /// (clé = nom de l'asset EnemyDefinition). Une fiche est « découverte » au premier kill.
    /// </summary>
    public static class BestiaryData
    {
        public static event Action Changed;

        public static int Kills(EnemyDefinition enemy)
            => enemy != null ? PlayerPrefs.GetInt(Key(enemy), 0) : 0;

        public static bool IsDiscovered(EnemyDefinition enemy) => Kills(enemy) > 0;

        public static void RegisterKill(EnemyDefinition enemy)
        {
            if (enemy == null) return;
            PlayerPrefs.SetInt(Key(enemy), Kills(enemy) + 1);
            Changed?.Invoke();
        }

        public static void ResetAll(System.Collections.Generic.IEnumerable<EnemyDefinition> enemies)
        {
            foreach (EnemyDefinition e in enemies)
                if (e != null) PlayerPrefs.DeleteKey(Key(e));
            Changed?.Invoke();
        }

        private static string Key(EnemyDefinition enemy) => "bestiary_kills_" + enemy.name;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Hook()
        {
            EnemyController.Killed -= RegisterKill;
            EnemyController.Killed += RegisterKill;
        }
    }
}
