using System;
using UnityEngine;

namespace Core.TweenSystem
{
    /// <summary>
    /// Un tween : interpole une progression 0→1 sur une durée, avec un easing,
    /// et appelle onUpdate(valeurEasée) chaque frame. Chaînable
    /// (SetEase/SetDelay/SetLoops/OnComplete). Piloté par le TweenManager.
    /// </summary>
    public sealed class Tween
    {
        private readonly Action<float> _onUpdate;
        private readonly float _duration;

        private Ease _ease = Ease.Linear;
        private float _delayRemaining;
        private int _loops = 1;      // 1 = une fois ; -1 = infini
        private bool _pingpong;
        private Action _onComplete;

        private float _elapsed;
        private int _loopsDone;
        private bool _forward = true;

        /// <summary>La cible (pour Kill par cible). Peut être null pour un tween de valeur.</summary>
        public object Target { get; }
        public bool IsComplete { get; private set; }
        public bool UseUnscaledTime { get; private set; }

        public Tween(object target, float duration, Action<float> onUpdate)
        {
            Target = target;
            _duration = Mathf.Max(0.0001f, duration);
            _onUpdate = onUpdate;
        }

        #region Chaînage

        public Tween SetEase(Ease ease) { _ease = ease; return this; }
        public Tween SetDelay(float seconds) { _delayRemaining = Mathf.Max(0f, seconds); return this; }
        public Tween SetLoops(int count, bool pingpong = false) { _loops = count; _pingpong = pingpong; return this; }
        public Tween OnComplete(Action action) { _onComplete = action; return this; }
        public Tween SetUnscaledTime(bool value) { UseUnscaledTime = value; return this; }

        #endregion

        /// <summary>Avance le tween. Appelé par le TweenManager. Renvoie true quand terminé.</summary>
        public bool Tick(float deltaTime)
        {
            if (IsComplete) return true;

            if (_delayRemaining > 0f)
            {
                _delayRemaining -= deltaTime;
                if (_delayRemaining > 0f) return false;
                deltaTime = -_delayRemaining; // reporte le surplus sur cette frame
            }

            _elapsed += deltaTime;
            float raw = Mathf.Clamp01(_elapsed / _duration);
            float p = _forward ? raw : 1f - raw;

            _onUpdate?.Invoke(Easing.Evaluate(_ease, p));

            if (raw >= 1f)
            {
                _loopsDone++;
                bool infinite = _loops < 0;

                if (!infinite && _loopsDone >= _loops)
                {
                    IsComplete = true;
                    _onComplete?.Invoke();
                }
                else
                {
                    _elapsed = 0f;
                    if (_pingpong) _forward = !_forward;
                }
            }

            return IsComplete;
        }

        /// <summary>Stoppe le tween immédiatement (sans appeler OnComplete).</summary>
        public void Kill() => IsComplete = true;

        /// <summary>Termine le tween d'un coup (valeur finale + OnComplete).</summary>
        public void Complete()
        {
            _onUpdate?.Invoke(Easing.Evaluate(_ease, _forward ? 1f : 0f));
            IsComplete = true;
            _onComplete?.Invoke();
        }
    }
}
