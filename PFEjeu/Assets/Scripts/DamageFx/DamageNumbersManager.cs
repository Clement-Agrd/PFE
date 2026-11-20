using System.Collections.Generic;
using UnityEngine;

namespace DamageFx
{
    /// <summary>
    /// Gestionnaire unique des chiffres : pool, cumul par cible, plafond global, distance, anti-superposition.
    /// Un seul LateUpdate fait avancer tous les chiffres actifs (aucun Update par chiffre, aucune allocation par frame).
    /// À poser UNE fois dans la scène (Tools > Damage FX > Ajouter le gestionnaire à la scène). Les appelants passent par DamageNumbers.Show.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DamageNumbersManager : MonoBehaviour
    {
        #region Références
        [SerializeField] private DamageNumbersSettings settings;
        [SerializeField, Tooltip("Un style par DamageFxKind, dans l'ordre de l'enum (Physical, Fire, Ice, Poison, Magic, Heal, TowerHit).")]
        private DamageTypeStyle[] styles = new DamageTypeStyle[DamageFxKinds.Count];
        [SerializeField, Tooltip("Style des coups critiques (doré, HDR).")] private DamageTypeStyle critStyle;
        [SerializeField] private DamageNumberView viewPrefab;
        [SerializeField, Min(0)] private int prewarm = 32;
        [SerializeField] private DamageFxKindMap kindMap;
        #endregion

        #region État
        private readonly Stack<IDamageNumberView> pool = new Stack<IDamageNumberView>(64);
        private readonly List<IDamageNumberView> active = new List<IDamageNumberView>(64);
        // Chiffre ouvert par (cible, soin ou non) : clé = id * 2 + (soin ? 1 : 0).
        private readonly Dictionary<long, IDamageNumberView> openByKey = new Dictionary<long, IDamageNumberView>(64);
        private Camera cam;
        #endregion

        public DamageFxKindMap KindMap => kindMap;
        public int ActiveCount => active.Count;

        #region Cycle de vie Unity
        private void Awake()
        {
            if (settings == null || viewPrefab == null)
            {
                Debug.LogError("[DamageFx] Réglages ou prefab manquants : lance Tools > Damage FX > 1 Créer les assets par défaut.", this);
                enabled = false;
                return;
            }
            for (int i = 0; i < prewarm; i++) pool.Push(CreateView());
        }

        private void OnEnable() { DamageNumbers.Register(this); }
        private void OnDisable() { DamageNumbers.Unregister(this); }

        private void LateUpdate()
        {
            if (cam == null) cam = Camera.main;
            float dt = Time.deltaTime, now = Time.time;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                IDamageNumberView v = active[i];
                if (v.Tick(dt, now, cam)) continue;
                Release(i);
            }
        }
        #endregion

        #region API
        public void Show(in DamageHit hit)
        {
            if (hit.Amount <= 0f) return;
            if (cam == null) cam = Camera.main;
            DamageTypeStyle style = styles[(int)hit.Kind];
            if (style == null) return;

            bool isHeal = hit.Kind == DamageFxKind.Heal;
            bool tower = hit.Kind == DamageFxKind.TowerHit;
            bool big = hit.IsCrit || hit.IsBoss || tower || hit.Amount >= settings.BigHit;

            // 1) Cumul : un coup (non crit) sur une cible qui a déjà un chiffre ouvert s'additionne dedans.
            long key = hit.TargetKey * 2L + (isHeal ? 1 : 0);
            if (!hit.IsCrit && hit.TargetKey != 0 && openByKey.TryGetValue(key, out IDamageNumberView open) && open.IsOpen)
            {
                open.AddHit(in hit);
                return;
            }

            // 2) Distance : les chiffres lointains disparaissent, seuls les gros coups restent.
            if (cam != null)
            {
                float dist = (hit.Point - cam.transform.position).magnitude;
                if (dist > settings.FarDistance) return;
                if (!big && dist > settings.HideDistance) return;
            }

            // 3) Plafond : on recycle le moins important / le plus ancien, ou on abandonne si le nouveau est moins important.
            int newPriority = hit.IsCrit ? 40 : hit.IsBoss ? 35 : style.Priority;
            if (active.Count >= settings.MaxActiveNumbers)
            {
                int victim = FindLowestPriority();
                if (victim < 0 || active[victim].Priority > newPriority) return;
                Release(victim);
            }

            // 4) Création, repoussée hors des chiffres déjà présents sur la même cible.
            IDamageNumberView view = pool.Count > 0 ? pool.Pop() : CreateView();
            Vector3 jitter = new Vector3(Random.Range(-1f, 1f), Random.Range(-0.4f, 0.4f), Random.Range(-1f, 1f)) * settings.SpawnJitter;
            Vector3 stack = jitter + Vector3.up * (CountOverlapping(hit.Point, hit.TargetKey) * settings.StackStep);
            view.Begin(in hit, stack);
            active.Add(view);
            if (hit.TargetKey != 0) openByKey[key] = view;
        }
        #endregion

        #region Interne
        private IDamageNumberView CreateView()
        {
            DamageNumberView v = Instantiate(viewPrefab, transform);
            v.Configure(settings, styles, critStyle);
            v.gameObject.SetActive(false);
            return v;
        }

        private void Release(int index)
        {
            IDamageNumberView v = active[index];
            int last = active.Count - 1;
            active[index] = active[last];
            active.RemoveAt(last);
            if (v.TargetKey != 0)
            {
                long key = v.TargetKey * 2L + (v.IsHeal ? 1 : 0);
                if (openByKey.TryGetValue(key, out IDamageNumberView mapped) && ReferenceEquals(mapped, v)) openByKey.Remove(key);
            }
            v.Hide();
            pool.Push(v);
        }

        /// <summary>Index du chiffre à sacrifier : priorité la plus basse, à égalité le plus ancien.</summary>
        private int FindLowestPriority()
        {
            int best = -1, bestPriority = int.MaxValue;
            float bestTime = float.MaxValue;
            for (int i = 0; i < active.Count; i++)
            {
                int p = active[i].Priority;
                float t = active[i].SpawnTime;
                if (p < bestPriority || (p == bestPriority && t < bestTime))
                {
                    best = i; bestPriority = p; bestTime = t;
                }
            }
            return best;
        }

        /// <summary>Nombre de chiffres déjà présents sur la même cible (ou au même endroit si pas de cible) : sert à décaler le nouveau.</summary>
        private int CountOverlapping(Vector3 point, long targetKey)
        {
            int n = 0;
            for (int i = 0; i < active.Count; i++)
            {
                IDamageNumberView v = active[i];
                bool same = targetKey != 0 ? v.TargetKey == targetKey : (v.AnchorPosition - point).sqrMagnitude < 0.5f;
                if (same) n++;
            }
            return Mathf.Min(n, 4);
        }
        #endregion
    }
}
