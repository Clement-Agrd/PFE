using System.Collections.Generic;
using UnityEngine;

namespace Core.TweenSystem
{
    /// <summary>
    /// Fait tourner tous les tweens actifs (persistant entre scènes). Créé
    /// automatiquement. Les tweens n'ont pas besoin d'un composant sur la cible.
    /// </summary>
    public sealed class TweenManager : MonoBehaviour
    {
        private static TweenManager _instance;

        private readonly List<Tween> _tweens = new();
        private readonly List<Tween> _toAdd = new();

        public static TweenManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[TweenManager]");
                    _instance = go.AddComponent<TweenManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        /// <summary>Enregistre un tween pour qu'il soit joué, et le renvoie (pour chaîner).</summary>
        public static Tween Play(Tween tween)
        {
            if (tween != null) Instance._toAdd.Add(tween);
            return tween;
        }

        /// <summary>Stoppe tous les tweens d'une cible donnée.</summary>
        public static void Kill(object target)
        {
            if (_instance == null || target == null) return;

            foreach (Tween t in _instance._tweens)
                if (ReferenceEquals(t.Target, target)) t.Kill();

            foreach (Tween t in _instance._toAdd)
                if (ReferenceEquals(t.Target, target)) t.Kill();
        }

        private void Update()
        {
            if (_toAdd.Count > 0)
            {
                _tweens.AddRange(_toAdd);
                _toAdd.Clear();
            }

            float dt = Time.deltaTime;
            float unscaledDt = Time.unscaledDeltaTime;

            for (int i = _tweens.Count - 1; i >= 0; i--)
            {
                Tween tween = _tweens[i];
                bool done = tween.Tick(tween.UseUnscaledTime ? unscaledDt : dt);
                if (done) _tweens.RemoveAt(i);
            }
        }
    }
}
