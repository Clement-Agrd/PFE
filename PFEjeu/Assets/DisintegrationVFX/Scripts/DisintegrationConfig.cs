using System.Collections.Generic;
using UnityEngine;

namespace Core.DisintegrationVFX
{
    /// <summary>
    /// Config globale, chargée depuis Resources : permet à n'importe quel ennemi de se désintégrer
    /// sans câblage de scène.
    /// </summary>
    public sealed class DisintegrationConfig : ScriptableObject
    {
        public const string ResourcePath = "DisintegrationConfig";

        [Header("Assets")]
        public DisintegrationFX fxPrefab;
        public Material template;
        public DisintegrationStyle defaultStyle;

        [Header("Limites")]
        [Range(1, 12)] public int maxActiveDisintegrations = 6;
        public DisintegrationQuality quality = DisintegrationQuality.High;

        private static DisintegrationConfig _instance;

        public static DisintegrationConfig Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Resources.Load<DisintegrationConfig>(ResourcePath);
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            DisintegrationManager.ResetStatics();
        }
    }

    /// <summary>Pool du prefab FX + limite globale de désintégrations simultanées.</summary>
    public static class DisintegrationManager
    {
        private static readonly Stack<DisintegrationFX> Pool = new Stack<DisintegrationFX>();
        private static readonly List<DisintegrationController> Active = new List<DisintegrationController>();
        private static Transform _root;

        public static int ActiveCount => Active.Count;

        public static void ResetStatics()
        {
            Pool.Clear();
            Active.Clear();
            _root = null;
        }

        public static DisintegrationFX Rent()
        {
            DisintegrationConfig cfg = DisintegrationConfig.Instance;
            if (cfg == null || cfg.fxPrefab == null)
                return null;

            if (_root == null)
                _root = new GameObject("DisintegrationFX_Pool").transform;

            DisintegrationFX fx = null;
            while (Pool.Count > 0 && fx == null)
                fx = Pool.Pop();

            if (fx == null)
                fx = Object.Instantiate(cfg.fxPrefab, _root);

            fx.gameObject.SetActive(true);
            return fx;
        }

        public static void Return(DisintegrationFX fx)
        {
            if (fx == null)
                return;
            fx.gameObject.SetActive(false);
            Pool.Push(fx);
        }

        public static void Register(DisintegrationController c)
        {
            Active.Add(c);
            DisintegrationConfig cfg = DisintegrationConfig.Instance;
            int max = cfg != null ? cfg.maxActiveDisintegrations : 6;
            // coupe le plus ancien : il termine immédiatement (le reste de son FX s'éteint seul)
            while (Active.Count > max)
            {
                DisintegrationController oldest = Active[0];
                Active.RemoveAt(0);
                if (oldest != null && oldest != c)
                    oldest.ForceFinish();
            }
        }

        public static void Unregister(DisintegrationController c) => Active.Remove(c);
    }
}
