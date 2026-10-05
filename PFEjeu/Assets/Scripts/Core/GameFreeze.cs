using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Central authority for pausing gameplay and freeing the cursor. Several
    /// systems (inventory, exploration panel, village view...) can each hold a
    /// pause and/or cursor-unlock request at the same time; Time.timeScale and
    /// Cursor only change back once every requester has released, so two
    /// overlapping panels no longer un-pause or re-lock the cursor out from
    /// under each other. Always pair a Request with a Release using the same
    /// owner (typically "this").
    /// </summary>
    public static class GameFreeze
    {
        private static readonly HashSet<object> _pauseRequesters = new();
        private static readonly HashSet<object> _cursorUnlockRequesters = new();

        public static bool IsPaused => _pauseRequesters.Count > 0;
        public static bool IsCursorUnlocked => _cursorUnlockRequesters.Count > 0;

        // Sans ce reset, un requester jamais relâché (session interrompue,
        // objet détruit sans passer par OnDisable...) reste bloqué dans les
        // sets d'une entrée à l'autre en Play Mode si le domain reload est
        // désactivé, gelant la caméra/le temps au démarrage suivant.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnPlayModeStart()
        {
            _pauseRequesters.Clear();
            _cursorUnlockRequesters.Clear();
            Time.timeScale = 1f;
        }

        public static void RequestPause(object owner)
        {
            if (_pauseRequesters.Add(owner) && _pauseRequesters.Count == 1)
                Time.timeScale = 0f;
        }

        public static void ReleasePause(object owner)
        {
            if (_pauseRequesters.Remove(owner) && _pauseRequesters.Count == 0)
                Time.timeScale = 1f;
        }

        public static void RequestCursorUnlock(object owner)
        {
            if (_cursorUnlockRequesters.Add(owner) && _cursorUnlockRequesters.Count == 1)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        public static void ReleaseCursorUnlock(object owner)
        {
            if (_cursorUnlockRequesters.Remove(owner) && _cursorUnlockRequesters.Count == 0)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }
}
