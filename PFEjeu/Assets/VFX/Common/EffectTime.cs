using System.Collections;
using UnityEngine;

namespace VFXCycle
{
    internal static class EffectTime
    {
        public static void ConfigureParticles(GameObject effect, bool unscaled)
        {
            if (!unscaled) return;
            foreach (var system in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                main.useUnscaledTime = true;
            }
        }

        public static IEnumerator DestroyAfter(GameObject effect, float seconds, bool unscaled)
        {
            if (unscaled) yield return new WaitForSecondsRealtime(seconds);
            else yield return new WaitForSeconds(seconds);
            Object.Destroy(effect);
        }
    }
}
