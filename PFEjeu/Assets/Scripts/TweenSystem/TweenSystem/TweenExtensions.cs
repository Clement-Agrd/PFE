using System;
using UnityEngine;

namespace Core.TweenSystem
{
    /// <summary>
    /// API ergonomique : lance des tweens sur les composants Unity courants.
    /// Toutes renvoient le Tween pour chaîner (.SetEase, .OnComplete...).
    /// Les valeurs de départ sont capturées au moment de l'appel.
    /// </summary>
    public static class TweenExtensions
    {
        // --- Transform ---

        public static Tween TweenMove(this Transform tr, Vector3 to, float duration)
        {
            Vector3 from = tr.position;
            return TweenManager.Play(new Tween(tr, duration, t => tr.position = Vector3.LerpUnclamped(from, to, t)));
        }

        public static Tween TweenLocalMove(this Transform tr, Vector3 to, float duration)
        {
            Vector3 from = tr.localPosition;
            return TweenManager.Play(new Tween(tr, duration, t => tr.localPosition = Vector3.LerpUnclamped(from, to, t)));
        }

        public static Tween TweenScale(this Transform tr, Vector3 to, float duration)
        {
            Vector3 from = tr.localScale;
            return TweenManager.Play(new Tween(tr, duration, t => tr.localScale = Vector3.LerpUnclamped(from, to, t)));
        }

        public static Tween TweenScale(this Transform tr, float to, float duration)
            => tr.TweenScale(Vector3.one * to, duration);

        public static Tween TweenRotate(this Transform tr, Quaternion to, float duration)
        {
            Quaternion from = tr.rotation;
            return TweenManager.Play(new Tween(tr, duration, t => tr.rotation = Quaternion.SlerpUnclamped(from, to, t)));
        }

        /// <summary>Stoppe tous les tweens de ce transform.</summary>
        public static void KillTweens(this Transform tr) => TweenManager.Kill(tr);

        // --- UI / 2D ---

        public static Tween TweenFade(this CanvasGroup cg, float to, float duration)
        {
            float from = cg.alpha;
            return TweenManager.Play(new Tween(cg, duration, t => cg.alpha = Mathf.LerpUnclamped(from, to, t)));
        }

        public static Tween TweenColor(this SpriteRenderer sr, Color to, float duration)
        {
            Color from = sr.color;
            return TweenManager.Play(new Tween(sr, duration, t => sr.color = Color.LerpUnclamped(from, to, t)));
        }
    }

    /// <summary>Tween d'une valeur arbitraire (float), sans cible visuelle.</summary>
    public static class Tweener
    {
        /// <summary>Interpole 'from' → 'to' sur 'duration', en appelant onValue à chaque frame.</summary>
        public static Tween Value(float from, float to, float duration, Action<float> onValue)
            => TweenManager.Play(new Tween(null, duration, t => onValue(Mathf.LerpUnclamped(from, to, t))));
    }
}
